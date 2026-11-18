using FrostySdk.Managers.Entries;
using SoundEditorPlugin;
using System.Collections.Generic;
using System.IO;
using WAV = SoundEditorPlugin.WAV;

namespace BulkExporterPlugin.Exporters
{
    /// <summary>
    /// Audio side of the bulk exporter. Decoding is delegated to <see cref="SoundDecoder"/> (SoundEditorPlugin),
    /// so bulk export follows the same codec support as the sound editor (EA-Opus, multistream Opus, ...).
    /// </summary>
    public class AudioExporter
    {
        /// <summary>
        /// Base sound types. Enumeration and filtering match them with inheritance, so derived types
        /// such as LocalizedWaveAsset (a SoundWaveAsset) are included without being listed.
        /// </summary>
        public static string[] assetTypes = SoundDecoder.SupportedAssetTypes;

        private readonly SoundDecoder.Options m_options;

        public AudioExporter(SoundDecoder.Options options)
        {
            m_options = options;
        }

        public List<SoundDataTrack> EnumerateTracks(EbxAssetEntry asset) => SoundDecoder.Decode(asset, m_options);

        /// <summary>
        /// Writes the track as a 16-bit PCM WAV. Samples are streamed straight to disk (no per-frame objects,
        /// so long sounds do not exhaust memory). The file is written under a temporary name and renamed at the
        /// end, so a failure never leaves a truncated WAV behind.
        /// </summary>
        public void Export(SoundDataTrack track, string fileName)
        {
            int channels = track.ChannelCount;
            int sampleRate = track.SampleRate;
            if (channels <= 0 || sampleRate <= 0)
                throw new InvalidDataException($"{track.Name}: invalid format ({channels} channels, {sampleRate} Hz)");

            // only whole frames are written
            long frameCount = track.Samples.Length / channels;
            long dataSize = frameCount * channels * 2;
            if (dataSize > uint.MaxValue - 60)
                throw new InvalidDataException($"{track.Name}: too long for a WAV file");

            string tempFileName = fileName + ".partial";
            try
            {
                using (FileStream stream = new FileStream(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    // more than 2 channels: WAVE_FORMAT_EXTENSIBLE with a speaker mask, as the WAV spec requires
                    // (plain PCM is rejected or read as stereo by some Windows players and tools)
                    bool extensible = channels > 2;
                    uint fmtSize = extensible ? 40u : 16u;

                    writer.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // RIFF
                    writer.Write((uint)(4 + 8 + fmtSize + 8 + dataSize));
                    writer.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 }); // WAVE

                    writer.Write(new byte[] { 0x66, 0x6d, 0x74, 0x20 }); // "fmt "
                    writer.Write(fmtSize);
                    writer.Write(extensible ? (ushort)0xFFFE : (ushort)1); // EXTENSIBLE or PCM
                    writer.Write((ushort)channels);
                    writer.Write((uint)sampleRate);
                    writer.Write((uint)(sampleRate * channels * 2));
                    writer.Write((ushort)(channels * 2));
                    writer.Write((ushort)16);
                    if (extensible)
                    {
                        writer.Write((ushort)22);         // cbSize
                        writer.Write((ushort)16);         // valid bits per sample
                        writer.Write(SpeakerMask(channels));
                        // KSDATAFORMAT_SUBTYPE_PCM {00000001-0000-0010-8000-00AA00389B71}
                        writer.Write(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
                    }

                    writer.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 }); // data
                    writer.Write((uint)dataSize);

                    long sampleCount = frameCount * channels;
                    for (long i = 0; i < sampleCount; i++)
                        writer.Write(track.Samples[i]);
                }

                if (File.Exists(fileName))
                    File.Delete(fileName);
                File.Move(tempFileName, fileName);
            }
            finally
            {
                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);
            }
        }

        /// <summary>
        /// Speaker positions for the channel order of EA multichannel sounds (stream pairs FL FR, FC LFE, BL BR;
        /// same table as the sound editor). Other counts get 0 (no assigned positions).
        /// </summary>
        private static uint SpeakerMask(int channels)
        {
            switch (channels)
            {
                case 4: return 0x1 | 0x2 | 0x10 | 0x20;                    // FL FR BL BR
                case 6: return 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20;        // FL FR FC LFE BL BR
                case 8: return 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20 | 0x200 | 0x400; // + SL SR
                default: return 0;
            }
        }
    }
}
