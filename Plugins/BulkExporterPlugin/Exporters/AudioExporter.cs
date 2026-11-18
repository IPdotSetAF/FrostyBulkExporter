using Frosty.Core;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using SoundEditorPlugin;
using System.Collections.Generic;
using System.IO;
using WAV = SoundEditorPlugin.WAV;

namespace BulkExporterPlugin.Exporters
{
    public class AudioExporter
    {
        public static string[] assetTypes = { "SoundWaveAsset", "NewWaveAsset", "LocalizedWaveAsset", "HarmonySampleBankAsset" };

        public IEnumerable<SoundDataTrack> EnumerateTracks(EbxAssetEntry asset)
        {
            EbxAsset ebxAsset = App.AssetManager.GetEbx(asset);
            var soundAsset =  (dynamic)ebxAsset.RootObject;

            switch (asset.Type)
            {
                case "SoundWaveAsset":
                    return EnumerateSoundWaveAssetTracks(soundAsset);
                case "NewWaveAsset":
                case "LocalizedWaveAsset":
                    return EnumerateNewWavAssetTracks(soundAsset);
                case "HarmonySampleBankAsset":
                    return EnumerateHarmonySampleBankAssetTracks(soundAsset);
                default:
                    return new List<SoundDataTrack>();
            }
        }

        private IEnumerable<SoundDataTrack> EnumerateSoundWaveAssetTracks(dynamic soundWave)
        {
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            int index = 0;

            foreach (dynamic runtimeVariation in soundWave.RuntimeVariations)
            {
                SoundDataTrack track = new SoundDataTrack { Name = "Track #" + ((index++) + 1) };

                dynamic soundDataChunk = soundWave.Chunks[runtimeVariation.ChunkIndex];
                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(soundDataChunk.ChunkId);

                if (chunkEntry == null)
                {
                    App.Logger.LogWarning($"SoundChunk {soundDataChunk.ChunkId} doesn't exist. This could be because its a LocalizedChunk that is not loaded by your game.");
                }
                else
                {
                    using (NativeReader reader = new NativeReader(App.AssetManager.GetChunk(chunkEntry)))
                    {
                        List<short> decodedSoundBuf = new List<short>();
                        double startLoopingTime = 0.0;
                        double loopingDuration = 0.0;

                        for (int i = 0; i < runtimeVariation.SegmentCount; i++)
                        {
                            var segment = soundWave.Segments[runtimeVariation.FirstSegmentIndex + i];
                            reader.Position = segment.SamplesOffset;

                            uint headerSize = reader.ReadUInt(Endian.Big) & 0x00ffffff;
                            byte codec = reader.ReadByte();
                            int channels = (reader.ReadByte() >> 2) + 1;
                            ushort sampleRate = reader.ReadUShort(Endian.Big);
                            uint sampleCount = reader.ReadUInt(Endian.Big) & 0x00ffffff;

                            switch (codec)
                            {
                                case 0x12: track.Codec = "Pcm16Big"; break;
                                case 0x14: track.Codec = "Xas1"; break;
                                case 0x15: track.Codec = "EaLayer31"; break;
                                case 0x16: track.Codec = "EaLayer32Pcm"; break;
                                default: track.Codec = "Unknown (" + codec.ToString("x2") + ")"; break;
                            }

                            reader.Position = segment.SamplesOffset;
                            long size = reader.Length - reader.Position;
                            if ((runtimeVariation.FirstSegmentIndex + i + 1 < soundWave.Segments.Count) && ((soundWave.Segments[runtimeVariation.FirstSegmentIndex + i + 1].SamplesOffset > segment.SamplesOffset)))
                            {
                                size = soundWave.Segments[runtimeVariation.FirstSegmentIndex + i + 1].SamplesOffset - reader.Position;
                            }
                            byte[] soundBuf = reader.ReadBytes((int)size);
                            double duration = 0.0;

                            if (codec == 0x12)
                            {
                                short[] data = Pcm16b.Decode(soundBuf);
                                decodedSoundBuf.AddRange(data);
                                duration += (data.Length / channels) / (double)sampleRate;
                                sampleCount = (uint)data.Length;
                            }
                            else if (codec == 0x14)
                            {
                                short[] data = XAS.Decode(soundBuf);
                                decodedSoundBuf.AddRange(data);
                                duration += (data.Length / channels) / (double)sampleRate;
                                sampleCount = (uint)data.Length;
                            }
                            else if (codec == 0x15 || codec == 0x16)
                            {
                                sampleCount = 0;
                                EALayer3.Decode(soundBuf, soundBuf.Length, (short[] data, int count, EALayer3.StreamInfo info) =>
                                {
                                    if (info.streamIndex == -1)
                                        return;
                                    sampleCount += (uint)data.Length;
                                    decodedSoundBuf.AddRange(data);
                                });
                                duration += (sampleCount / channels) / (double)sampleRate;
                            }

                            if (runtimeVariation.SegmentCount > 1)
                            {
                                if (i < runtimeVariation.FirstLoopSegmentIndex)
                                {
                                    startLoopingTime += duration;
                                    track.LoopStart += sampleCount;
                                }
                                if (i >= runtimeVariation.FirstLoopSegmentIndex && i <= runtimeVariation.LastLoopSegmentIndex)
                                {
                                    loopingDuration += duration;
                                    track.LoopEnd += sampleCount;
                                }
                            }

                            track.SampleRate = sampleRate;
                            track.ChannelCount = channels;
                            track.Duration += duration;
                        }

                        track.LoopEnd += track.LoopStart;
                        track.Samples = decodedSoundBuf.ToArray();

                        track.SegmentCount = runtimeVariation.SegmentCount;
                    }
                }

                tracks.Add(track);
            }

            foreach (dynamic localization in soundWave.Localization)
            {
                for (int i = 0; i < localization.VariationCount; i++)
                {
                    SoundDataTrack track = tracks[i + localization.FirstVariationIndex];

                    PointerRef pr = localization.Language;
                    EbxAsset asset2 = App.AssetManager.GetEbx(App.AssetManager.GetEbxEntry(pr.External.FileGuid));
                    dynamic obj = asset2.GetObject(pr.External.ClassGuid);

                    track.Language = obj.__Id;
                }
            }

            return tracks;
        }

        private IEnumerable<SoundDataTrack> EnumerateNewWavAssetTracks(dynamic newWave)
        {
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            int index = 0;
            foreach (dynamic soundDataChunk in newWave.Chunks)
            {
                SoundDataTrack track = new SoundDataTrack { Name = "Track #" + ((index++) + 1) };

                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(soundDataChunk.ChunkId);

                if (chunkEntry == null)
                {
                    App.Logger.LogWarning($"SoundChunk {soundDataChunk.ChunkId} doesn't exist. This could be because its a LocalizedChunk that is not loaded by your game.");
                }
                else
                {
                    using (NativeReader reader = new NativeReader(App.AssetManager.GetChunk(chunkEntry)))
                    {
                        List<short> decodedSoundBuf = new List<short>();
                        reader.Position = 0;

                        uint headerSize = reader.ReadUInt(Endian.Big) & 0x00ffffff;
                        byte codec = reader.ReadByte();
                        int channels = (reader.ReadByte() >> 2) + 1;
                        ushort sampleRate = reader.ReadUShort(Endian.Big);
                        uint sampleCount = reader.ReadUInt(Endian.Big) & 0x00ffffff;

                        switch (codec)
                        {
                            case 0x12: track.Codec = "Pcm16Big"; break;
                            case 0x14: track.Codec = "Xas1"; break;
                            case 0x15: track.Codec = "EaLayer31"; break;
                            case 0x16: track.Codec = "EaLayer32Pcm"; break;
                            case 0x1c: track.Codec = "EaOpus"; break;
                            default: track.Codec = "Unknown (" + codec.ToString("x2") + ")"; break;
                        }

                        reader.Position = 0;
                        byte[] soundBuf = reader.ReadToEnd();
                        double duration = 0.0;

                        if (codec == 0x12)
                        {
                            short[] data = Pcm16b.Decode(soundBuf);
                            decodedSoundBuf.AddRange(data);
                            duration += (data.Length / channels) / (double)sampleRate;
                        }
                        else if (codec == 0x14)
                        {
                            short[] data = XAS.Decode(soundBuf);
                            decodedSoundBuf.AddRange(data);
                            duration += (data.Length / channels) / (double)sampleRate;
                        }
                        else if (codec == 0x15 || codec == 0x16 || codec == 0x1c)
                        {
                            sampleCount = 0;
                            EALayer3.Decode(soundBuf, soundBuf.Length, (short[] data, int count, EALayer3.StreamInfo info) =>
                            {
                                if (info.streamIndex == -1)
                                    return;
                                sampleCount += (uint)data.Length;
                                decodedSoundBuf.AddRange(data);
                            });
                            duration += (sampleCount / channels) / (double)sampleRate;
                        }

                        track.SampleRate = sampleRate;
                        track.ChannelCount = channels;
                        track.Duration += duration;
                        track.Samples = decodedSoundBuf.ToArray();

                        track.SegmentCount = 1;
                    }
                }

                tracks.Add(track);
            }

            return tracks;
        }

        private IEnumerable<SoundDataTrack> EnumerateHarmonySampleBankAssetTracks(dynamic soundWave)
        {
            List<SoundDataTrack> tracks = new List<SoundDataTrack>();

            dynamic ramChunk = soundWave.Chunks[soundWave.RamChunkIndex];


            int index = 0;

            ChunkAssetEntry ramChunkEntry = App.AssetManager.GetChunkEntry(ramChunk.ChunkId);


            NativeReader streamChunkReader = null;
            if (soundWave.StreamChunkIndex != 255)
            {
                dynamic streamChunk = soundWave.Chunks[soundWave.StreamChunkIndex];
                ChunkAssetEntry streamChunkEntry = App.AssetManager.GetChunkEntry(streamChunk.ChunkId);
                streamChunkReader = new NativeReader(App.AssetManager.GetChunk(streamChunkEntry));
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

                foreach (int offset in offsets)
                {
                    reader.Position = offset + 0x3c;
                    int blockCount = reader.ReadUShort();
                    reader.Position += 0x0a;

                    int fileOffset = -1;
                    bool streaming = false;

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

                    if (fileOffset != -1)
                    {
                        NativeReader actualReader = reader;
                        if (streaming)
                            actualReader = streamChunkReader;

                        SoundDataTrack track = new SoundDataTrack { Name = "Track #" + (index++) };

                        actualReader.Position = fileOffset;
                        List<short> decodedSoundBuf = new List<short>();

                        uint headerSize = actualReader.ReadUInt(Endian.Big) & 0x00ffffff;
                        byte codec = actualReader.ReadByte();
                        int channels = (actualReader.ReadByte() >> 2) + 1;
                        ushort sampleRate = actualReader.ReadUShort(Endian.Big);
                        uint sampleCount = actualReader.ReadUInt(Endian.Big) & 0x00ffffff;

                        switch (codec)
                        {
                            case 0x14: track.Codec = "XAS"; break;
                            case 0x15: track.Codec = "EALayer3 v5"; break;
                            case 0x16: track.Codec = "EALayer3 v6"; break;
                            default: track.Codec = "Unknown (" + codec.ToString("x2") + ")"; break;
                        }

                        actualReader.Position = fileOffset;
                        byte[] soundBuf = actualReader.ReadToEnd();
                        double duration = 0.0;

                        if (codec == 0x14)
                        {
                            short[] data = XAS.Decode(soundBuf);
                            decodedSoundBuf.AddRange(data);
                            duration += (data.Length / channels) / (double)sampleRate;
                        }
                        else if (codec == 0x15 || codec == 0x16)
                        {
                            sampleCount = 0;
                            EALayer3.Decode(soundBuf, soundBuf.Length, (short[] data, int count, EALayer3.StreamInfo info) =>
                            {
                                if (info.streamIndex == -1)
                                    return;
                                sampleCount += (uint)data.Length;
                                decodedSoundBuf.AddRange(data);
                            });
                            duration += (sampleCount / channels) / (double)sampleRate;
                        }

                        track.Duration += duration;
                        track.Samples = decodedSoundBuf.ToArray();

                        track.SegmentCount = 1;
                        tracks.Add(track);
                    }
                }
            }

            return tracks;
        }

        public void Export(SoundDataTrack track, string fileName)
        {
            WAV.WAVFormatChunk fmt = new WAV.WAVFormatChunk(WAV.WAVFormatChunk.DataFormats.WAVE_FORMAT_PCM, (ushort)track.ChannelCount, (uint)track.SampleRate, (uint)(track.ChannelCount * 2 * track.SampleRate), (ushort)(2 * track.ChannelCount), 16);
            List<WAV.WAVDataFrame> frames = new List<WAV.WAVDataFrame>();
            for (int i = 0; i < track.Samples.Length / track.ChannelCount; i++)
            {
                // write frame
                WAV.WAV16BitDataFrame frame = new WAV.WAV16BitDataFrame((ushort)track.ChannelCount);
                for (int channel = 0; channel < track.ChannelCount; channel++)
                {
                    frame.Data[channel] = track.Samples[i * track.ChannelCount + channel];
                }
                frames.Add(frame);
            }
            WAV.WAVDataChunk data = new WAV.WAVDataChunk(fmt, frames);
            WAV.RIFFMainChunk main = new WAV.RIFFMainChunk(new WAV.RIFFChunkHeader(0, new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0), new byte[] { 0x57, 0x41, 0x56, 0x45 });

            using (FileStream stream = new FileStream(fileName, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                main.Write(writer, new List<WAV.IRIFFChunk>(new WAV.IRIFFChunk[] { fmt, data }));
            }
        }
    }
}
