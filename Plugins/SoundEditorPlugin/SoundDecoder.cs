using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using SoundEditorPlugin.Helpers;
using SoundEditorPlugin.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SoundEditorPlugin
{
    /// <summary>
    /// UI-free decoding of sound assets into PCM tracks, used for batch/bulk export.
    /// <para>
    /// The parsing mirrors the <c>InitialLoad</c> methods of the editors in FrostySoundWaveEditor.cs
    /// (SoundWave, NewWave, HarmonySampleBank) without the waveform rendering. Keep both in sync when
    /// codec handling changes there.
    /// </para>
    /// Differences on purpose, to make unattended export robust:
    /// <list type="bullet">
    /// <item>EALayer3 (0x5/0x6) is decoded by the same native ealayer3.dll as the editor, so the output is
    /// identical, but in a worker process (<see cref="NativeEALayer3Worker"/>): a crash (access violation) or an
    /// endless loop in the native code only ends the worker. If the native decoder fails on a sound, vgmstream
    /// is tried instead.</item>
    /// <item>Other compressed codecs (EA-Opus, multistream Opus, EALayer3 spike, EA-MP3, Speex, ATRAC9, ...) go
    /// to vgmstream. The channel count and sample rate vgmstream reports win over the stream header.</item>
    /// <item>Every external decoder has a timeout and is killed when it runs out.</item>
    /// <item>A track that could not be decoded completely gets <see cref="SoundDataTrack.DecodeProblem"/> set,
    /// so callers can tell partial output from complete output.</item>
    /// </list>
    /// </summary>
    public static class SoundDecoder
    {
        public sealed class Options
        {
            /// <summary>Maximum time for one vgmstream run, in milliseconds.</summary>
            public int VgmStreamTimeoutMs { get; set; } = 60_000;

            /// <summary>Maximum time for one native EALayer3 decode (worker process), in milliseconds.</summary>
            public int NativeDecodeTimeoutMs { get; set; } = 60_000;

            public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

            /// <summary>Receives non fatal problems (missing chunk, unsupported codec, ...).</summary>
            public Action<string> Warn { get; set; }
        }


        /// <summary>Asset types this decoder understands (derived types such as LocalizedWaveAsset included).</summary>
        public static readonly string[] SupportedAssetTypes = { "SoundWaveAsset", "NewWaveAsset", "HarmonySampleBankAsset" };

        public static bool IsSupported(string assetType)
        {
            if (string.IsNullOrEmpty(assetType))
                return false;

            foreach (string type in SupportedAssetTypes)
            {
                if (TypeLibrary.IsSubClassOf(assetType, type))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Decodes every track of a sound asset. The asset type is matched with inheritance, so
        /// LocalizedWaveAsset (a SoundWaveAsset) uses the SoundWave layout.
        /// Problems in one track or segment (decoder errors, timeouts, doubtful lengths) do not throw: they are
        /// recorded in <see cref="SoundDataTrack.DecodeProblem"/> and the other tracks are still decoded.
        /// </summary>
        /// <exception cref="OperationCanceledException">The cancellation token was triggered.</exception>
        /// <exception cref="InvalidDataException">The asset itself could not be loaded or parsed (unsupported type, missing resource or RAM chunk).</exception>
        public static List<SoundDataTrack> Decode(EbxAssetEntry entry, Options options = null)
        {
            options = options ?? new Options();

            // one fresh native decoder per asset, as in the editor (see NativeEALayer3Worker.Recycle)
            NativeEALayer3Worker.Recycle();

            string diagFilter = Environment.GetEnvironmentVariable("FBE_AUDIO_DIAG");
            s_diagAsset = !string.IsNullOrEmpty(diagFilter) && entry.Name.IndexOf(diagFilter, StringComparison.OrdinalIgnoreCase) >= 0
                ? entry.Name
                : null;

            EbxAsset ebx = App.AssetManager.GetEbx(entry);
            dynamic root = ebx?.RootObject;
            if (root == null)
                throw new InvalidDataException("asset could not be loaded");

            // NewWaveAsset first: test the most specific layouts before the SoundWave base class
            if (TypeLibrary.IsSubClassOf(entry.Type, "NewWaveAsset"))
                return DecodeNewWave(root, options);
            if (TypeLibrary.IsSubClassOf(entry.Type, "HarmonySampleBankAsset"))
                return DecodeHarmonySampleBank(root, options);
            if (TypeLibrary.IsSubClassOf(entry.Type, "SoundWaveAsset"))
                return DecodeSoundWave(root, options);

            throw new InvalidDataException($"unsupported sound asset type {entry.Type}");
        }

        #region -- SoundWaveAsset (and LocalizedWaveAsset) --

        public static List<SoundDataTrack> DecodeSoundWave(dynamic soundWave, Options options)
        {
            options = options ?? new Options();
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            int index = 0;
            foreach (dynamic runtimeVariation in soundWave.RuntimeVariations)
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                SoundDataTrack track = new SoundDataTrack { Name = "Track #" + ((index++) + 1) };
                // always add the track, so localization indices below still line up
                tracks.Add(track);

                List<short> decodedSoundBuf = new List<short>();
                int segmentCount = 0;
                try
                {
                    // EBX field types come from the game's type info: convert explicitly instead of relying on dynamic conversions
                    int chunkIndex = ToInt(runtimeVariation.ChunkIndex);
                    int firstSegment = ToInt(runtimeVariation.FirstSegmentIndex);
                    segmentCount = ToInt(runtimeVariation.SegmentCount);
                    int firstLoopSegment = ToInt(runtimeVariation.FirstLoopSegmentIndex);
                    int lastLoopSegment = ToInt(runtimeVariation.LastLoopSegmentIndex);

                    dynamic soundDataChunk = soundWave.Chunks[chunkIndex];
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(soundDataChunk.ChunkId);
                    if (chunkEntry == null)
                        MarkProblem(track, $"sound chunk {soundDataChunk.ChunkId} not found (localized chunk not installed?)", options);
                    else
                        DecodeSoundWaveSegments(soundWave, track, chunkEntry, firstSegment, segmentCount, firstLoopSegment, lastLoopSegment, decodedSoundBuf, options);
                }
                catch (Exception e) when (!IsCancellation(e, options))
                {
                    MarkProblem(track, $"track could not be decoded completely ({Describe(e)}), what was decoded before it is kept", options);
                }

                FinishTrack(track, decodedSoundBuf, segmentCount, options);
            }

            try
            {
                ResolveLanguages(soundWave, tracks, options);
            }
            catch (Exception e) when (!IsCancellation(e, options))
            {
                // the language only decorates the file names, the audio is still valid
                options.Warn?.Invoke($"localization could not be read ({Describe(e)}), file names have no language");
            }

            return tracks;
        }

        private static void DecodeSoundWaveSegments(dynamic soundWave, SoundDataTrack track, ChunkAssetEntry chunkEntry, int firstSegment, int segmentCount,
            int firstLoopSegment, int lastLoopSegment, List<short> decodedSoundBuf, Options options)
        {
            using (NativeReader reader = new NativeReader(App.AssetManager.GetChunk(chunkEntry)))
            {
                for (int i = 0; i < segmentCount; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var segment = soundWave.Segments[firstSegment + i];
                    long samplesOffset = Convert.ToInt64((object)segment.SamplesOffset);

                    if (!TryReadHeader(reader, samplesOffset, out byte codec, out int channels, out int sampleRate, out long declaredFrames))
                    {
                        MarkProblem(track, $"wrong sample offset at segment {i + 1} of {segmentCount}, the rest of the track is missing", options);
                        break;
                    }

                    track.Codec = CodecName(codec);
                    track.CodecUnformatted = codec;

                    if (i == firstLoopSegment && segmentCount > 1)
                        track.LoopStart = (uint)decodedSoundBuf.Count;

                    reader.Position = samplesOffset;
                    byte[] soundBuf = reader.ReadToEnd();

                    SegmentResult result = DecodeAndCheck(track, i, segmentCount, codec, channels, sampleRate, declaredFrames, soundBuf, options, out bool failed);
                    if (failed)
                    {
                        // already flagged, the segment is left out
                    }
                    else if (result == null)
                        MarkProblem(track, $"segment {i + 1} of {segmentCount} uses unsupported codec {track.Codec}", options);
                    else
                        AppendSegment(track, decodedSoundBuf, result, channels, sampleRate, i, segmentCount, options);

                    if (i == lastLoopSegment && segmentCount > 1)
                        track.LoopEnd = (uint)decodedSoundBuf.Count;
                }
            }
        }

        private static void ResolveLanguages(dynamic soundWave, List<SoundDataTrack> tracks, Options options)
        {
            foreach (dynamic localization in soundWave.Localization)
            {
                int variationCount = ToInt(localization.VariationCount);
                int firstVariation = ToInt(localization.FirstVariationIndex);

                for (int i = 0; i < variationCount; i++)
                {
                    int trackIndex = i + firstVariation;
                    if (trackIndex < 0 || trackIndex >= tracks.Count)
                    {
                        options.Warn?.Invoke($"localization entry points to track #{trackIndex + 1}, which does not exist");
                        continue;
                    }

                    try
                    {
                        PointerRef pr = localization.Language;
                        EbxAssetEntry languageEntry = App.AssetManager.GetEbxEntry(pr.External.FileGuid);
                        if (languageEntry == null)
                        {
                            options.Warn?.Invoke($"{tracks[trackIndex].Name}: language asset {pr.External.FileGuid} not found, file name has no language");
                            continue;
                        }

                        dynamic obj = App.AssetManager.GetEbx(languageEntry).GetObject(pr.External.ClassGuid);
                        tracks[trackIndex].Language = obj.__Id;
                    }
                    catch (Exception e)
                    {
                        // the language only decorates the file name, the audio is still valid
                        options.Warn?.Invoke($"{tracks[trackIndex].Name}: could not resolve language ({e.GetType().Name}: {e.Message})");
                    }
                }
            }
        }

        #endregion

        #region -- NewWaveAsset --

        public static List<SoundDataTrack> DecodeNewWave(dynamic root, Options options)
        {
            options = options ?? new Options();
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            string name = (string)root.Name;
            ResAssetEntry resEntry = App.AssetManager.GetResEntry(name.ToLower());
            if (resEntry == null)
                throw new InvalidDataException($"NewWaveResource {name} not found");

            NewWaveResource newWave = App.AssetManager.GetResAs<NewWaveResource>(resEntry);

            // chunks are read once and shared by every variation
            Dictionary<int, byte[]> chunkData = new Dictionary<int, byte[]>();

            int index = 0;
            foreach (Variation variation in newWave.Variations)
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                SoundDataTrack track = new SoundDataTrack { Name = "Track #" + ((index++) + 1), VariationIndex = index - 1 };
                tracks.Add(track);

                List<short> decodedSoundBuf = new List<short>();
                int segmentCount = (int)variation.SegmentCount;
                try
                {
                    DecodeNewWaveTrack(newWave, variation, track, chunkData, decodedSoundBuf, options);
                }
                catch (Exception e) when (!IsCancellation(e, options))
                {
                    MarkProblem(track, $"track could not be decoded completely ({Describe(e)}), what was decoded before it is kept", options);
                }

                FinishTrack(track, decodedSoundBuf, segmentCount, options);
            }

            return tracks;
        }

        /// <summary>Decodes one NewWave variation into <paramref name="decodedSoundBuf"/>; returns its segment count.</summary>
        private static int DecodeNewWaveTrack(NewWaveResource newWave, Variation variation, SoundDataTrack track, Dictionary<int, byte[]> chunkData,
            List<short> decodedSoundBuf, Options options)
        {
            int firstSegment = (int)variation.FirstSegmentIndex;
            int segmentCount = (int)variation.SegmentCount;
            if (firstSegment < 0 || segmentCount <= 0 || firstSegment + segmentCount > newWave.Segments.Count)
            {
                MarkProblem(track, $"segment range {firstSegment}+{segmentCount} out of range ({newWave.Segments.Count})", options);
                return segmentCount;
            }

            int chunkIndex = newWave.Segments[firstSegment].SamplesOffsetFlag == 1
                ? (int)variation.MemoryChunkIndex
                : (int)variation.StreamChunkIndex;

            if (chunkIndex < 0 || chunkIndex >= newWave.Chunks.Count)
            {
                MarkProblem(track, $"chunk index {chunkIndex} out of range ({newWave.Chunks.Count})", options);
                return segmentCount;
            }

            if (!chunkData.TryGetValue(chunkIndex, out byte[] data))
            {
                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(newWave.Chunks[chunkIndex].ChunkId);
                if (chunkEntry == null)
                {
                    MarkProblem(track, $"sound chunk {newWave.Chunks[chunkIndex].ChunkId} not found (localized chunk not installed?)", options);
                    return segmentCount;
                }

                using (NativeReader chunkReader = new NativeReader(App.AssetManager.GetChunk(chunkEntry)))
                {
                    data = chunkReader.ReadToEnd();
                }
                chunkData[chunkIndex] = data;
            }

            track.ChunkIndex = chunkIndex;
            track.ChunkId = newWave.Chunks[chunkIndex].ChunkId;
            track.SegmentIndex = firstSegment;

            uint loopStart = 0;
            uint loopLength = 0;
            using (NativeReader reader = new NativeReader(new MemoryStream(data)))
            {
                for (int i = 0; i < segmentCount; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    Segment segment = newWave.Segments[firstSegment + i];
                    long samplesOffset = segment.SamplesOffset & 0xFFFFFFFC;

                    if (!TryReadHeader(reader, samplesOffset, out byte codec, out int channels, out int sampleRate, out long declaredFrames))
                    {
                        MarkProblem(track, $"wrong sample offset at segment {i + 1} of {segmentCount}, the rest of the track is missing", options);
                        break;
                    }

                    track.Codec = CodecName(codec);
                    track.CodecUnformatted = codec;

                    reader.Position = samplesOffset;
                    byte[] soundBuf = reader.ReadToEnd();

                    SegmentResult result = DecodeAndCheck(track, i, segmentCount, codec, channels, sampleRate, declaredFrames, soundBuf, options, out bool failed);
                    uint sampleCount = 0;
                    if (failed)
                    {
                        // already flagged, the segment is left out (sampleCount stays 0 for the loop points)
                    }
                    else if (result == null)
                    {
                        MarkProblem(track, $"segment {i + 1} of {segmentCount} uses unsupported codec {track.Codec}", options);
                    }
                    else
                    {
                        int before = decodedSoundBuf.Count;
                        AppendSegment(track, decodedSoundBuf, result, channels, sampleRate, i, segmentCount, options);
                        sampleCount = (uint)(decodedSoundBuf.Count - before);
                    }

                    // same loop bookkeeping as FrostyNewWaveEditor.LoadTrackFromNewWave
                    if (segmentCount > 1)
                    {
                        if (i < variation.FirstLoopSegmentIndex)
                            loopStart += sampleCount;
                        if (i >= variation.FirstLoopSegmentIndex && i <= variation.LastLoopSegmentIndex)
                            loopLength += sampleCount;
                    }

                    // kept up to date so a later failure leaves the loop points of what was decoded
                    track.LoopStart = loopStart;
                    track.LoopEnd = loopStart + loopLength;
                }
            }

            track.LoopStart = loopStart;
            track.LoopEnd = loopStart + loopLength;
            return segmentCount;
        }

        #endregion

        #region -- HarmonySampleBankAsset --

        public static List<SoundDataTrack> DecodeHarmonySampleBank(dynamic soundWave, Options options)
        {
            options = options ?? new Options();
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            int ramChunkIndex = ToInt(soundWave.RamChunkIndex);
            int streamChunkIndex = ToInt(soundWave.StreamChunkIndex);

            dynamic ramChunk = soundWave.Chunks[ramChunkIndex];
            ChunkAssetEntry ramChunkEntry = App.AssetManager.GetChunkEntry(ramChunk.ChunkId);
            if (ramChunkEntry == null)
                throw new InvalidDataException($"RAM chunk {ramChunk.ChunkId} not found");

            NativeReader streamChunkReader = null;
            string streamProblem = null;
            try
            {
                if (streamChunkIndex != 255)
                {
                    dynamic streamChunk = soundWave.Chunks[streamChunkIndex];
                    ChunkAssetEntry streamChunkEntry = App.AssetManager.GetChunkEntry(streamChunk.ChunkId);
                    if (streamChunkEntry != null)
                        streamChunkReader = new NativeReader(App.AssetManager.GetChunk(streamChunkEntry));
                    else
                        streamProblem = $"stream chunk {streamChunk.ChunkId} not found";
                }

                using (NativeReader reader = new NativeReader(App.AssetManager.GetChunk(ramChunkEntry)))
                {
                    reader.Position = 0x0a;
                    int datasetCount = reader.ReadUShort();

                    reader.Position = 0x20;
                    int dataOffset = reader.ReadInt();

                    reader.Position = 0x50;
                    List<int> offsets = new List<int>();
                    for (int i = 0; i < datasetCount; i++)
                    {
                        offsets.Add(reader.ReadInt());
                        reader.Position += 4;
                    }

                    // same numbering as FrostyHarmonySampleBankEditor (starts at #0)
                    int index = 0;
                    for (int dataset = 0; dataset < offsets.Count; dataset++)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();

                        int fileOffset = -1;
                        bool streaming = false;

                        try
                        {
                            reader.Position = offsets[dataset] + 0x3c;
                            int blockCount = reader.ReadUShort();
                            reader.Position += 0x0a;

                            for (int i = 0; i < blockCount; i++)
                            {
                                uint blockType = reader.ReadUInt();
                                if (blockType == 0x2e4f4646)
                                {
                                    reader.Position += 4;
                                    fileOffset = reader.ReadInt();
                                    reader.Position += 0x0c;
                                    streaming = true;
                                }
                                else if (blockType == 0x2e52414d)
                                {
                                    reader.Position += 4;
                                    fileOffset = reader.ReadInt() + dataOffset;
                                    reader.Position += 0x0c;
                                }
                                else
                                {
                                    reader.Position += 0x14;
                                }
                            }
                        }
                        catch (Exception e) when (!IsCancellation(e, options))
                        {
                            SoundDataTrack broken = new SoundDataTrack { Name = "Track #" + (index++) };
                            tracks.Add(broken);
                            MarkProblem(broken, $"dataset {dataset} could not be parsed ({Describe(e)})", options);
                            FinishTrack(broken, new List<short>(), 1, options);
                            continue;
                        }

                        if (fileOffset == -1)
                        {
                            // datasets without sample data (e.g. parameters only) are not tracks
                            continue;
                        }

                        SoundDataTrack track = new SoundDataTrack { Name = "Track #" + (index++) };
                        tracks.Add(track);

                        NativeReader actualReader = streaming ? streamChunkReader : reader;
                        if (actualReader == null || fileOffset < 0 || fileOffset >= actualReader.Length)
                        {
                            MarkProblem(track, streaming && streamProblem != null ? streamProblem : $"sample data offset {fileOffset} is outside the chunk", options);
                            FinishTrack(track, new List<short>(), 1, options);
                            continue;
                        }

                        List<short> decoded = new List<short>();
                        try
                        {
                            // Harmony banks use the older header: full codec byte (0x14/0x15/0x16), no 0x48 marker
                            actualReader.Position = fileOffset;
                            actualReader.ReadUInt(Endian.Big);
                            byte codec = actualReader.ReadByte();
                            int channels = (actualReader.ReadByte() >> 2) + 1;
                            ushort sampleRate = actualReader.ReadUShort(Endian.Big);
                            // frame count at the same place as in the 0x48 header (bytes 8-11); 0 when missing
                            long declaredFrames = fileOffset + 12 <= actualReader.Length
                                ? actualReader.ReadUInt(Endian.Big) & 0xFFFFFFF : 0;

                            switch (codec)
                            {
                                case 0x14: track.Codec = "XAS"; break;
                                case 0x15: track.Codec = "EALayer3 v5"; break;
                                case 0x16: track.Codec = "EALayer3 v6"; break;
                                default: track.Codec = "Unknown (" + codec.ToString("x2") + ")"; break;
                            }
                            track.CodecUnformatted = codec;

                            actualReader.Position = fileOffset;
                            byte[] soundBuf = actualReader.ReadToEnd();

                            SegmentResult result = null;
                            bool failed = false;
                            if (codec == 0x14)
                                result = new SegmentResult { Samples = XAS.Decode(soundBuf) };
                            else if (codec == 0x15 || codec == 0x16)
                                result = DecodeAndCheck(track, 0, 1, (byte)(codec & 0xF), channels, sampleRate, declaredFrames, soundBuf, options, out failed, mayCut: false);
                            else
                                MarkProblem(track, $"codec {track.Codec} is not supported", options);

                            if (result != null && !failed)
                                AppendSegment(track, decoded, result, channels, sampleRate, 0, 1, options);
                        }
                        catch (Exception e) when (!IsCancellation(e, options))
                        {
                            MarkProblem(track, $"track could not be decoded ({Describe(e)})", options);
                        }

                        FinishTrack(track, decoded, 1, options);
                    }
                }
            }
            finally
            {
                streamChunkReader?.Dispose();
            }

            return tracks;
        }

        #endregion

        #region -- Shared helpers --

        private sealed class SegmentResult
        {
            public short[] Samples;
            /// <summary>Format reported by the decoder itself (vgmstream), 0 when unknown.</summary>
            public int Channels;
            public int SampleRate;
            /// <summary>Set when the preferred decoder failed and another one produced these samples.</summary>
            public string FallbackReason;
            /// <summary>Channel count reported by the native EALayer3 worker (total of all its streams), 0 for other decoders.</summary>
            public int DecoderChannels;
            /// <summary>Where the EALayer3 stream was found to end (StreamLength), shown in the length messages.</summary>
            public string StreamEnd;
            /// <summary>Warning from the decoder about this output (e.g. streams of different lengths), or null.</summary>
            public string Note;
        }

        private static int ToInt(object value) => Convert.ToInt32(value);

        private static void MarkProblem(SoundDataTrack track, string problem, Options options)
        {
            track.DecodeProblem = track.DecodeProblem == null ? problem : track.DecodeProblem + "; " + problem;
            options.Warn?.Invoke($"{track.Name}: {problem}");
        }

        /// <summary>Reads the 0x48 segment header used by SoundWave and NewWave chunks.</summary>
        private static bool TryReadHeader(NativeReader reader, long samplesOffset, out byte codec, out int channels, out int sampleRate, out long declaredFrames)
        {
            codec = 0;
            channels = 0;
            sampleRate = 0;
            declaredFrames = 0;

            if (samplesOffset < 0 || samplesOffset + 12 > reader.Length)
                return false;

            reader.Position = samplesOffset;
            if (reader.ReadUShort() != 0x48)
                return false;

            reader.ReadUShort(Endian.Big); // header size
            codec = (byte)(reader.ReadByte() & 0xF);
            channels = (reader.ReadByte() >> 2) + 1;
            sampleRate = reader.ReadUShort(Endian.Big);
            declaredFrames = reader.ReadUInt(Endian.Big) & 0xFFFFFFF; // samples per channel, same mask as the editor

            // no 2-channel clamp: the EALayer3 worker interleaves the streams of a multichannel sound and reports
            // the real channel count, which AppendSegment uses (the editor clamps to 2 and plays the streams chained)

            return true;
        }

        /// <summary>
        /// Appends one decoded segment to the track. The format reported by the decoder wins over the
        /// stream header; segments that disagree on the format are flagged instead of being mixed silently.
        /// </summary>
        private static void AppendSegment(SoundDataTrack track, List<short> buffer, SegmentResult result, int headerChannels, int headerSampleRate,
            int segment, int segmentCount, Options options)
        {
            int channels = result.Channels > 0 ? result.Channels : headerChannels;
            int sampleRate = result.SampleRate > 0 ? result.SampleRate : headerSampleRate;

            if (result.FallbackReason != null)
                MarkProblem(track, $"segment {segment + 1} of {segmentCount}: {result.FallbackReason}; decoded with vgmstream instead, listen to check it", options);

            if (result.Samples == null || result.Samples.Length == 0)
            {
                MarkProblem(track, $"segment {segment + 1} of {segmentCount} decoded to no audio ({track.Codec})", options);
                return;
            }

            if (channels <= 0 || sampleRate <= 0)
            {
                // the exporter cannot write it and would drop the track without a word
                MarkProblem(track, $"segment {segment + 1} of {segmentCount} has no usable format ({channels} ch, {sampleRate} Hz), it was left out", options);
                return;
            }

            if (buffer.Count > 0 && (channels != track.ChannelCount || sampleRate != track.SampleRate))
            {
                MarkProblem(track, $"segment {segment + 1} of {segmentCount} has a different format ({channels} ch, {sampleRate} Hz) than the previous ones ({track.ChannelCount} ch, {track.SampleRate} Hz), it was left out", options);
                return;
            }

            if (channels > 0 && result.Samples.Length % channels != 0)
                MarkProblem(track, $"segment {segment + 1} of {segmentCount} ends with an incomplete sample frame", options);

            track.ChannelCount = channels;
            track.SampleRate = sampleRate;
            buffer.AddRange(result.Samples);
        }

        public static string CodecName(int codec)
        {
            switch (codec)
            {
                case 0x2: return "PCM 16 Big";
                case 0x3: return "EA-XMA";
                case 0x4: return "XAS Interleaved v1";
                case 0x5: return "EALayer3 Interleaved v1";
                case 0x6: return "EALayer3 Interleaved v2 PCM";
                case 0x7: return "EALayer3 Interleaved v2 Spike";
                case 0x9: return "EASpeex";
                case 0xB: return "EA-MP3";
                case 0xC: return "EAOpus";
                case 0xD: return "EAAtrac9";
                case 0xE: return "MultiStream Opus";
                case 0xF: return "MultiStream Opus (Uncoupled)";
                default: return "Unknown (" + codec.ToString("x") + ")";
            }
        }

        /// <summary>Decodes one segment. Returns null when the codec is not supported at all.</summary>
        private static SegmentResult DecodeSegment(byte codec, byte[] soundBuf, Options options)
        {
            switch (codec)
            {
                case 0x2:
                    return new SegmentResult { Samples = Pcm16b.Decode(soundBuf) };
                case 0x4:
                    return new SegmentResult { Samples = XAS.Decode(soundBuf) };
                case 0x5:
                case 0x6:
                    return DecodeEALayer3(soundBuf, options);
                case 0x3: // EA-XMA: not handled by vgmstream as a raw SPS stream
                case 0x1:
                case 0x8:
                case 0xA:
                case 0x0:
                    return null;
                default:
                    // EA-Opus (0xC), multistream Opus (0xE/0xF), EALayer3 spike, EA-MP3, Speex, ATRAC9
                    return DecodeVgmStream(soundBuf, options);
            }
        }

        private static SegmentResult DecodeVgmStream(byte[] soundBuf, Options options)
        {
            // GetAwaiter().GetResult() keeps the original exception (TimeoutException, ...) instead of an AggregateException
            VgmStreamHelper.DecodeResult result = VgmStreamHelper.Instance
                .DecodeWithFormat(soundBuf, options.VgmStreamTimeoutMs, options.CancellationToken)
                .GetAwaiter().GetResult();

            return new SegmentResult { Samples = result.Samples, Channels = result.Channels, SampleRate = result.SampleRate };
        }

        /// <summary>
        /// EALayer3 through the native decoder, like the editor, but in a worker process. vgmstream is the
        /// fallback when the native decoder fails or crashes on this sound (vgmstream output can differ on some
        /// EALayer3 streams, so it is not used first).
        /// </summary>
        private static SegmentResult DecodeEALayer3(byte[] soundBuf, Options options)
        {
            try
            {
                // channels/sample rate come from the decoder: a sound with more than 2 channels is decoded as several
                // streams that the worker interleaves into one multichannel output (the editor chains them instead).
                // ealayer3.dll can read past the end of the buffer when the sound is the last one of its chunk; it
                // then decodes whatever memory follows the buffer, which is zeros in a fresh process but leftovers
                // after thousands of sounds (heard as a saturated burst at the end). An EA end block and a zero
                // margin after the stream make that read deterministic.
                byte[] padded = WithEndPadding(soundBuf, StreamLength(soundBuf, out string streamEnd));
                NativeEALayer3Worker.Result result;
                try
                {
                    result = NativeEALayer3Worker.Decode(padded, options.NativeDecodeTimeoutMs, options.CancellationToken);
                }
                catch (NativeDecoderCrashedException e)
                {
                    // once more in a fresh worker, in case the crash came from state left by earlier sounds
                    options.Warn?.Invoke($"native decoder crashed ({e.Message}), decoding again in a fresh worker");
                    result = NativeEALayer3Worker.Decode(padded, options.NativeDecodeTimeoutMs, options.CancellationToken);
                }
                return new SegmentResult { Samples = result.Samples, Channels = result.Channels, SampleRate = result.SampleRate, DecoderChannels = result.Channels, StreamEnd = streamEnd, Note = result.Note };
            }
            catch (OperationCanceledException) when (options.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is InvalidDataException || e is TimeoutException || e is IOException || e is System.ComponentModel.Win32Exception)
            {
                SegmentResult fallback;
                try
                {
                    fallback = DecodeVgmStream(soundBuf, options);
                }
                catch (Exception vgm) when (!(vgm is OperationCanceledException && options.CancellationToken.IsCancellationRequested))
                {
                    // keep both errors: the report must say why the native decoder was left too
                    throw new InvalidDataException($"native decoder: {e.Message}; vgmstream fallback: {vgm.GetType().Name}: {vgm.Message}", vgm);
                }
                fallback.FallbackReason = e.Message;
                return fallback;
            }
        }

        /// <summary>
        /// Largest gap between the decoded length and the header length still treated as normal: a clean
        /// ealayer3.dll decode is 577 to 1675 frames shorter than the header, and its random junk tail is about
        /// 1536 frames. Anything further off is not the junk and is never cut.
        /// </summary>
        private const long MaxJunkFrames = 4096;

        /// <summary>Channel count written in the 0x48 header (total of all streams).</summary>
        private static int RawHeaderChannels(byte[] soundBuf) => soundBuf.Length > 5 ? (soundBuf[5] >> 2) + 1 : 0;

        /// <summary>
        /// Checks the EALayer3 output length against the segment header.
        /// <list type="bullet">
        /// <item>a little longer (junk tail): decoded again in fresh workers, cut to the header length if it stays too long</item>
        /// <item>much longer (multichannel or multi-part sound): kept whole and flagged</item>
        /// <item>much shorter (sound cut early): flagged</item>
        /// </list>
        /// Every cut and every doubtful length is flagged in the export report; a junk tail replaced by a confirmed
        /// clean new decode is only logged, since the written audio is then the clean decode.
        /// </summary>
        private static void TrimToDeclaredLength(SegmentResult result, byte codec, int headerChannels, long declaredFrames, byte[] soundBuf,
            bool mayCut, SoundDataTrack track, Options options)
        {
            if (result?.Samples == null || (codec != 0x5 && codec != 0x6))
                return;

            // the decoder's own channel count measures the length (all streams interleaved)
            int channels = result.Channels > 0 ? result.Channels : result.DecoderChannels > 0 ? result.DecoderChannels : headerChannels;
            if (channels <= 0 || declaredFrames <= 0)
            {
                MarkProblem(track, $"length could not be checked (header declares {declaredFrames} frames, {channels} channel(s))", options);
                return;
            }

            int rawHeaderChannels = RawHeaderChannels(soundBuf);
            if (channels != rawHeaderChannels && rawHeaderChannels > 0)
                MarkProblem(track, $"the decoder outputs {channels} channel(s) but the header declares {rawHeaderChannels}; listen to it", options);
            int headerRate = soundBuf.Length > 7 ? (soundBuf[6] << 8) | soundBuf[7] : 0;
            if (result.SampleRate > 0 && headerRate > 0 && result.SampleRate != headerRate)
                MarkProblem(track, $"the decoder outputs {result.SampleRate} Hz but the header declares {headerRate} Hz; listen to it", options);

            string header = $"header: {declaredFrames} frames, {RawHeaderChannels(soundBuf)} channel(s); decoder: {channels} channel(s)" +
                (result.StreamEnd != null ? $"; stream: {result.StreamEnd}" : "");
            long declaredSamples = declaredFrames * channels;
            long frames = result.Samples.Length / channels;

            if (frames < declaredFrames - MaxJunkFrames)
            {
                MarkProblem(track, $"decoded audio is {declaredFrames - frames} frame(s) shorter than the header length ({header}), the end may be missing; listen to it", options);
                return;
            }

            if (result.FallbackReason == null && frames < declaredFrames - MaxCleanShortfall)
            {
                // a clean native decode is 577..1675 frames short (measured); further than that, part of the end may be missing
                MarkProblem(track, $"decoded audio is {declaredFrames - frames} frame(s) shorter than the header length, more than a clean decode ({MinCleanShortfall}-{MaxCleanShortfall}) ({header}); listen to the end", options);
                return;
            }

            if (result.Samples.Length <= declaredSamples)
            {
                if (result.FallbackReason == null)
                    VerifyEnd(result, channels, declaredFrames, header, soundBuf, track, options);
                return;
            }

            long extra = frames - declaredFrames;
            if (extra <= 0)
                return; // only an incomplete last frame, flagged by AppendSegment
            if (extra > MaxJunkFrames)
            {
                MarkProblem(track, $"decoded audio is {extra} frame(s) longer than the header length ({header}), kept uncut; listen to it", options);
                return;
            }

            if (result.FallbackReason != null)
            {
                // vgmstream does not add the native junk: the extra frames may be real audio, keep them
                MarkProblem(track, $"vgmstream output is {extra} frame(s) longer than the header length ({header}), kept uncut; listen to the end", options);
                return;
            }

            // ealayer3.dll sometimes (randomly, same input) appends ~1500 frames of saturated junk: decode again in a
            // fresh worker. A new decode replaces the first one only when it is a clean decode (same test as VerifyEnd).
            string retryError = null;
            int done = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                NativeEALayer3Worker.Result again;
                try
                {
                    again = DecodeInFreshWorker(soundBuf, options);
                }
                catch (Exception e) when (IsRetryFailure(e))
                {
                    retryError = Describe(e);
                    break;
                }
                done++;

                if (IsCleanRetry(again, frames, channels, declaredFrames, requireDrop: false))
                {
                    result.Samples = again.Samples;
                    options.Warn?.Invoke($"{track.Name}: decoder output was {extra} frame(s) too long (junk), decoded again cleanly");
                    return;
                }

                // the same output again: the extra frames are not the random junk, more decodes will not change it
                if (again.Samples != null && SameSamples(again.Samples, result.Samples))
                    break;
            }

            if (!mayCut)
            {
                MarkProblem(track, $"decoded audio is {extra} frame(s) longer than the header length ({header}), {done} new decode(s) did not give a clean one; " +
                    "kept uncut because the header of this sound type is not verified, listen to the end", options);
                return;
            }

            Array.Resize(ref result.Samples, (int)declaredSamples);
            MarkProblem(track, $"decoder output was {extra} frame(s) longer than the header length ({header}) " +
                (retryError != null ? $"and decoding again failed ({retryError})" : $"after {done} new decode(s)") +
                ", cut to the header length; listen to the end to check it", options);
        }

        #region -- Junk tail below the header length --

        // The junk tail is ~1536 frames; a clean decode is 577..1675 frames shorter than the header. When the clean
        // shortfall is above 1536 the junk does not make the output longer than the header, so it is looked for
        // in two ways: the length (0..511 frames short can never be a clean decode) and the tail itself (a burst
        // of full-scale samples at the very end, much denser than in the rest of the sound).
        private const int TailWindowFrames = 1024;     // fully inside the ~1536-frame junk
        private const int TailBodyGuardFrames = 1024;  // keeps the start of the junk out of the body
        private const int TailBodyFrames = 8192;       // body measured before the guard
        private const int TailMinFrames = 3072;        // shorter sounds: length test only
        private const int SaturationLevel = 32000;     // ~97.7 % of full scale
        private const double TailSaturatedShare = 0.40;
        private const double TailBodyRatio = 4.0;
        private const long MinCleanShortfall = 512;    // measured minimum 577
        private const long MaxCleanShortfall = 1800;   // measured maximum 1675
        private const long RetryDropMin = 1000;        // a junk-free retry is ~1536 frames shorter
        private const long RetryDropMax = 2000;
        private const int TailRetries = 2;

        private static bool IsCancellation(Exception e, Options options)
            => e is OperationCanceledException && options.CancellationToken.IsCancellationRequested;

        /// <summary>Failures of a new native decode that end the retries (the first decode is kept).</summary>
        private static bool IsRetryFailure(Exception e)
            => e is InvalidDataException || e is TimeoutException || e is IOException || e is System.ComponentModel.Win32Exception;

        private static string Describe(Exception e)
            => (e is InvalidDataException || e is TimeoutException) ? e.Message : $"{e.GetType().Name}: {e.Message}";

        private static NativeEALayer3Worker.Result DecodeInFreshWorker(byte[] soundBuf, Options options)
        {
            NativeEALayer3Worker.Recycle();
            return NativeEALayer3Worker.Decode(WithEndPadding(soundBuf, StreamLength(soundBuf, out _)), options.NativeDecodeTimeoutMs, options.CancellationToken);
        }

        /// <summary>Share of samples at full scale in the last <see cref="TailWindowFrames"/> frames and in the body before them.</summary>
        private static void MeasureSaturation(short[] samples, int channels, out double tailShare, out double bodyShare)
        {
            tailShare = 0;
            bodyShare = 0;
            int frames = samples.Length / channels;
            if (frames < TailMinFrames)
                return;

            int tailStart = (frames - TailWindowFrames) * channels;
            int tailEnd = frames * channels;
            int bodyEnd = Math.Max(0, frames - TailWindowFrames - TailBodyGuardFrames) * channels;
            int bodyStart = Math.Max(0, bodyEnd - TailBodyFrames * channels);

            tailShare = SaturatedShare(samples, tailStart, tailEnd);
            bodyShare = bodyEnd - bodyStart >= TailWindowFrames * channels ? SaturatedShare(samples, bodyStart, bodyEnd) : 0;
        }

        private static double SaturatedShare(short[] samples, int start, int end)
        {
            if (end <= start)
                return 0;
            int count = 0;
            for (int i = start; i < end; i++)
            {
                int v = samples[i];
                if ((v < 0 ? -v : v) >= SaturationLevel)
                    count++;
            }
            return count / (double)(end - start);
        }

        private static bool HasJunkLength(long frames, long declaredFrames)
            => frames > declaredFrames - MinCleanShortfall && frames <= declaredFrames;

        private static bool HasJunkTail(short[] samples, int channels, out double tailShare)
        {
            MeasureSaturation(samples, channels, out tailShare, out double bodyShare);
            return tailShare >= TailSaturatedShare && tailShare >= TailBodyRatio * bodyShare;
        }

        /// <summary>
        /// A new decode is a clean one when its length is in the measured clean window below the header, it has the
        /// same channel count, a whole number of frames and no junk tail. With <paramref name="requireDrop"/>, it
        /// must also be about one junk tail (1000..2000 frames) shorter than the first decode.
        /// </summary>
        private static bool IsCleanRetry(NativeEALayer3Worker.Result again, long firstFrames, int channels, long declaredFrames, bool requireDrop)
        {
            // a new decode with a warning (padded streams, incomplete frame...) is never "clean"
            if (again?.Samples == null || again.Samples.Length == 0 || again.Samples.Length % channels != 0 || again.Note != null)
                return false;
            if (again.Channels > 0 && again.Channels != channels)
                return false;

            long againFrames = again.Samples.Length / channels;
            long drop = firstFrames - againFrames;
            if (requireDrop && (drop < RetryDropMin || drop > RetryDropMax))
                return false;

            return againFrames >= declaredFrames - MaxCleanShortfall && againFrames <= declaredFrames - MinCleanShortfall
                && !HasJunkTail(again.Samples, channels, out _);
        }

        private static bool SameSamples(short[] a, short[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        /// <summary>
        /// For a native output that is not longer than the header: looks for the junk tail. When the length or the
        /// end looks like junk, the sound is decoded again in fresh workers; the new decode replaces the first one
        /// only when it is a clean decode of the expected length (about 1536 frames shorter, clean end). The junk
        /// is random, so when the new decodes are identical to the first one the end is genuine. Anything that
        /// stays doubtful is flagged, the audio is never changed without a confirmed clean decode.
        /// </summary>
        private static void VerifyEnd(SegmentResult result, int channels, long declaredFrames, string header, byte[] soundBuf, SoundDataTrack track, Options options)
        {
            long frames = result.Samples.Length / channels;
            bool lengthSuspicious = HasJunkLength(frames, declaredFrames);
            bool tailSuspicious = HasJunkTail(result.Samples, channels, out double tailShare);
            if (!lengthSuspicious && !tailSuspicious)
                return;

            List<string> reasons = new List<string>();
            if (tailSuspicious)
                reasons.Add($"{tailShare * 100:0} % of the last {TailWindowFrames} frames are at full scale");
            if (lengthSuspicious)
                reasons.Add($"only {declaredFrames - frames} frame(s) shorter than the header length, a clean decode is at least {MinCleanShortfall} shorter");
            string reason = string.Join(" and ", reasons);

            int identical = 0;
            int done = 0;
            string retryError = null;
            for (int attempt = 0; attempt < TailRetries; attempt++)
            {
                NativeEALayer3Worker.Result again;
                try
                {
                    again = DecodeInFreshWorker(soundBuf, options);
                }
                catch (Exception e) when (IsRetryFailure(e))
                {
                    retryError = Describe(e);
                    break;
                }
                done++;

                if (again.Samples != null && SameSamples(again.Samples, result.Samples))
                {
                    identical++;
                    continue;
                }

                if (IsCleanRetry(again, frames, channels, declaredFrames, requireDrop: true))
                {
                    result.Samples = again.Samples;
                    options.Warn?.Invoke($"{track.Name}: decoded audio ended with decoder junk ({reason}), decoded again cleanly");
                    return;
                }
            }

            if (retryError == null && done > 0 && identical == done)
            {
                // the same output every time: not the random junk
                MarkProblem(track, $"end of the decoded audio looks unusual ({reason}; {header}) but is identical in {done + 1} decodes, so it is probably the real sound; kept as is, listen to the end to check it", options);
                return;
            }

            MarkProblem(track, retryError != null
                ? $"end of the decoded audio looks like decoder junk ({reason}; {header}) and decoding again failed ({retryError}), kept as is; listen to the end to check it"
                : $"end of the decoded audio looks like decoder junk ({reason}; {header}), {done} new decode(s) did not give a clean one, kept as is; listen to the end to check it", options);
        }

        /// <summary>
        /// Decodes one segment and checks its length. A segment whose decode throws is flagged and left out
        /// (<paramref name="failed"/> true) instead of failing the whole asset; cancellation still goes through.
        /// </summary>
        private static SegmentResult DecodeAndCheck(SoundDataTrack track, int segment, int segmentCount, byte codec, int channels, int sampleRate,
            long declaredFrames, byte[] soundBuf, Options options, out bool failed, bool mayCut = true)
        {
            failed = false;
            SegmentResult result;
            try
            {
                result = DecodeSegment(codec, soundBuf, options);
            }
            catch (Exception e) when (!IsCancellation(e, options))
            {
                MarkProblem(track, $"segment {segment + 1} of {segmentCount} could not be decoded ({Describe(e)}), it was left out", options);
                failed = true;
                return null;
            }

            Diagnose(track, segment, codec, channels, sampleRate, declaredFrames, soundBuf, result);

            if (result?.Note != null)
                MarkProblem(track, $"segment {segment + 1} of {segmentCount}: {result.Note}; listen to it", options);

            try
            {
                TrimToDeclaredLength(result, codec, channels, declaredFrames, soundBuf, mayCut, track, options);
            }
            catch (Exception e) when (!IsCancellation(e, options))
            {
                // the decode itself is valid, only the check failed: keep the audio
                MarkProblem(track, $"segment {segment + 1} of {segmentCount}: length check failed ({Describe(e)}), kept as decoded; listen to it", options);
            }
            return result;
        }

        #endregion

        #region -- Diagnostic mode (FBE_AUDIO_DIAG) --

        /// <summary>Asset being decoded when it matches FBE_AUDIO_DIAG, otherwise null.</summary>
        private static string s_diagAsset;

        /// <summary>
        /// When the FBE_AUDIO_DIAG environment variable is set and the asset name contains it, every EALayer3
        /// segment is written to %TEMP%\FrostyAudioDiag: the raw stream (.bin), the worker output (_worker.wav),
        /// and the same stream decoded in this process exactly as the editor does (_inproc.wav), plus a log line.
        /// </summary>
        private static void Diagnose(SoundDataTrack track, int segment, byte codec, int channels, int sampleRate, long declaredFrames, byte[] soundBuf, SegmentResult result)
        {
            if (s_diagAsset == null || (codec != 0x5 && codec != 0x6) || channels <= 0 || sampleRate <= 0)
                return;

            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "FrostyAudioDiag");
                Directory.CreateDirectory(folder);
                string baseName = Path.Combine(folder, $"{Path.GetFileName(s_diagAsset)}_{track.Name.Replace("#", "").Replace(" ", "")}_seg{segment}");

                File.WriteAllBytes(baseName + ".bin", soundBuf);

                List<short> inProcess = new List<short>();
                SortedDictionary<int, (int Channels, int Rate, long Samples, int Calls)> streams = new SortedDictionary<int, (int, int, long, int)>();
                string inProcessError = "";
                try
                {
                    EALayer3.Decode(soundBuf, soundBuf.Length, (short[] data, int count, EALayer3.StreamInfo info) =>
                    {
                        // called from native code: an exception must not cross back into it
                        try
                        {
                            streams.TryGetValue(info.streamIndex, out var s);
                            streams[info.streamIndex] = (info.numChannels, info.sampleRate, s.Samples + (data?.Length ?? 0), s.Calls + 1);
                            if (info.streamIndex == -1 || data == null)
                                return;
                            inProcess.AddRange(data);
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
                catch (Exception e)
                {
                    inProcessError = e.GetType().Name;
                }
                // the editor-style output chains the streams: label it with one stream's channel count
                int inProcessChannels = streams.Where(kv => kv.Key != -1 && kv.Value.Channels > 0).Select(kv => kv.Value.Channels).FirstOrDefault();
                if (inProcessChannels <= 0)
                    inProcessChannels = channels;
                WriteDiagWav(baseName + "_inproc.wav", inProcess.ToArray(), inProcessChannels, sampleRate);

                short[] worker = result?.Samples ?? new short[0];
                int workerChannels = result != null && result.Channels > 0 ? result.Channels : channels;
                WriteDiagWav(baseName + "_worker.wav", worker, workerChannels, result != null && result.SampleRate > 0 ? result.SampleRate : sampleRate);

                int cut = StreamLength(soundBuf, out string cutReason);
                File.AppendAllText(Path.Combine(folder, "diag.txt"),
                    $"{s_diagAsset} {track.Name} seg{segment}: codec {codec}, {channels} ch, {sampleRate} Hz, declared {declaredFrames} frames, " +
                    $"stream {soundBuf.Length} bytes, sound {cut} bytes ({cutReason}), inproc(old) {inProcess.Count / inProcessChannels} frames x {inProcessChannels} ch {inProcessError}, worker(new) {worker.Length / workerChannels} frames x {workerChannels} ch" +
                    (result?.FallbackReason != null ? $" (vgmstream fallback: {result.FallbackReason})" : "") +
                    $", header channels {RawHeaderChannels(soundBuf)}, {TailInfo(worker, result?.DecoderChannels > 0 ? result.DecoderChannels : channels)}, streams: " +
                    string.Join(" ", streams.Select(kv => $"[{kv.Key}: {kv.Value.Channels} ch {kv.Value.Rate} Hz {kv.Value.Samples} samples in {kv.Value.Calls} calls]")) +
                    Environment.NewLine);
            }
            catch (Exception)
            {
                // diagnostics must never break the export
            }
        }

        private static string TailInfo(short[] samples, int channels)
        {
            if (samples == null || channels <= 0)
                return "tail n/a";
            MeasureSaturation(samples, channels, out double tail, out double body);
            return $"full scale: tail {tail * 100:0.0} % body {body * 100:0.0} %";
        }

        private static void WriteDiagWav(string path, short[] samples, int channels, int sampleRate)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                int dataSize = samples.Length * 2;
                writer.Write(0x46464952); // RIFF
                writer.Write(36 + dataSize);
                writer.Write(0x45564157); // WAVE
                writer.Write(0x20746D66); // "fmt "
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(0x61746164); // data
                writer.Write(dataSize);
                foreach (short s in samples)
                    writer.Write(s);
            }
        }

        #endregion

        /// <summary>The stream followed by an EA end block (0x45, size 4) and 64 KB of zeros.</summary>
        private static byte[] WithEndPadding(byte[] soundBuf, int length)
        {
            byte[] padded = new byte[length + 4 + 0x10000];
            Buffer.BlockCopy(soundBuf, 0, padded, 0, length);
            padded[length] = 0x45;
            padded[length + 3] = 0x04;
            return padded;
        }

        /// <summary>
        /// Length of this sound inside the buffer, which runs to the end of the chunk. EA streams are a chain of
        /// blocks (1-byte type, 3-byte big-endian size including the 4-byte block header): one 0x48 header, 0x44
        /// data blocks (0xC4 = last data block) and an optional 0x45 end block. The walk stops at the end block,
        /// after the last data block, or at anything else (typically the next sound's 0x48 header): ealayer3.dll
        /// would otherwise go on decoding the following bytes, which adds a burst of noise at the end.
        /// When the chain cannot be followed at all, the whole buffer is kept (previous behaviour).
        /// </summary>
        private static int StreamLength(byte[] buf, out string reason)
        {
            int pos = 0;
            while (pos + 4 <= buf.Length)
            {
                byte type = buf[pos];
                int size = (buf[pos + 1] << 16) | (buf[pos + 2] << 8) | buf[pos + 3];

                if (pos == 0)
                {
                    if (type != 0x48 || size < 4 || size > buf.Length)
                    {
                        reason = "no 0x48 header, whole buffer kept";
                        return buf.Length;
                    }
                    pos += size;
                    continue;
                }

                if (type == 0x45)
                {
                    reason = $"end block at {pos}";
                    return pos; // the end block is added again by WithEndPadding
                }

                if ((type & 0x7F) != 0x44 || size < 4 || pos + size > buf.Length)
                {
                    reason = $"block type 0x{type:X2} (size {size}) at {pos} is not part of this sound";
                    return pos;
                }

                pos += size;
                if ((type & 0x80) != 0)
                {
                    reason = $"last data block ends at {pos}";
                    return pos;
                }
            }

            reason = $"chunk ends at {buf.Length}";
            return Math.Min(pos, buf.Length);
        }

        private static void FinishTrack(SoundDataTrack track, List<short> decodedSoundBuf, int segmentCount, Options options)
        {
            track.Samples = decodedSoundBuf.ToArray();
            track.SegmentCount = segmentCount;
            // a segment left out or a track stopped early must not leave loop points past the audio
            uint loopStart = track.LoopStart, loopEnd = track.LoopEnd;
            if (track.LoopEnd > track.Samples.Length)
                track.LoopEnd = (uint)track.Samples.Length;
            if (track.LoopStart > track.LoopEnd)
                track.LoopStart = track.LoopEnd;
            if (loopStart != track.LoopStart || loopEnd != track.LoopEnd)
                options.Warn?.Invoke($"{track.Name}: loop points {loopStart}-{loopEnd} were outside the decoded audio, set to {track.LoopStart}-{track.LoopEnd}");
            track.Duration = (track.ChannelCount > 0 && track.SampleRate > 0)
                ? (track.Samples.Length / track.ChannelCount) / (double)track.SampleRate
                : 0.0;
            track.IsLoaded = true;

            if (track.Samples.Length == 0 && track.DecodeProblem == null)
                MarkProblem(track, "no audio could be decoded", options);
        }

        #endregion
    }
}
