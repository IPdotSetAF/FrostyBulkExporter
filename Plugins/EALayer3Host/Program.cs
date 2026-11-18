using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;

namespace EALayer3Host
{
    /// <summary>
    /// Worker process for the native EALayer3 decoder (ealayer3.dll).
    /// <para>
    /// Usage: EALayer3Host.exe &lt;path to ealayer3.dll&gt; &lt;request pipe handle&gt; &lt;response pipe handle&gt;
    /// </para>
    /// Protocol (little endian), repeated until the request pipe is closed:
    /// <list type="bullet">
    /// <item>request: int32 length, then the EA sound stream (from its 0x48 header to the end of the chunk)</item>
    /// <item>response: int32 status, int32 channels, int32 sample rate, int32 sample count, the interleaved int16 samples,
    /// then a text (BinaryWriter string): the error when the status is not 0, otherwise a warning about the
    /// output (empty when there is none)</item>
    /// </list>
    /// Channels: total of all decoded streams; a sound with more than 2 channels is decoded as several streams,
    /// which are interleaved into one multichannel output.
    /// Status: 0 = decoded, 4 = memory fault in the decoder (the process exits afterwards), 5 = other error.
    /// Nothing is written to stdout, which the native decoder may use.
    /// </summary>
    internal static class Program
    {
        private const int StatusOk = 0;
        private const int StatusMemoryFault = 4;
        private const int StatusError = 5;

        private const int ExitBadArguments = 2;
        private const int ExitLibraryNotLoaded = 3;
        private const int ExitAfterMemoryFault = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct StreamInfo
        {
            public int streamIndex;
            public int numChannels;
            public int sampleRate;
        }

        // same signature as SoundEditorPlugin.EALayer3
        private delegate void AudioCallback([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] short[] data, int count, StreamInfo info);

        [DllImport("ealayer3.dll", EntryPoint = "Decode")]
        private static extern void Decode([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer, int length, AudioCallback callback);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        private static int Main(string[] args)
        {
            // no "has stopped working" dialog if the native code crashes: the editor handles the dead process
            SetErrorMode(0x0001 /* SEM_FAILCRITICALERRORS */ | 0x0002 /* SEM_NOGPFAULTERRORBOX */ | 0x8000 /* SEM_NOOPENFILEERRORBOX */);

            if (args.Length != 3)
                return ExitBadArguments;

            // loaded by full path; the DllImport above then binds to this already loaded module
            if (LoadLibrary(args[0]) == IntPtr.Zero)
                return ExitLibraryNotLoaded;

            using (AnonymousPipeClientStream requests = new AnonymousPipeClientStream(PipeDirection.In, args[1]))
            using (AnonymousPipeClientStream responses = new AnonymousPipeClientStream(PipeDirection.Out, args[2]))
            using (BinaryReader reader = new BinaryReader(requests))
            using (BinaryWriter writer = new BinaryWriter(new BufferedStream(responses, 1 << 16)))
            {
                while (true)
                {
                    int length;
                    try
                    {
                        length = reader.ReadInt32();
                    }
                    catch (EndOfStreamException)
                    {
                        return 0; // the editor closed the pipe
                    }

                    byte[] buffer = reader.ReadBytes(length);
                    if (buffer.Length != length)
                        return 0;

                    int status = DecodeBuffer(buffer, out List<short> samples, out int channels, out int sampleRate, out string error);

                    writer.Write(status);
                    writer.Write(channels);
                    writer.Write(sampleRate);
                    writer.Write(samples.Count);
                    foreach (short sample in samples)
                        writer.Write(sample);
                    writer.Write(error ?? "");
                    writer.Flush();

                    // after a memory fault the process state cannot be trusted: the editor starts a new worker
                    if (status == StatusMemoryFault)
                        return ExitAfterMemoryFault;
                }
            }
        }

        /// <summary>
        /// Decodes one stream. A memory fault in the native code is a corrupted state exception that a plain
        /// catch does not see, hence the attributes.
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static int DecodeBuffer(byte[] buffer, out List<short> samples, out int channels, out int sampleRate, out string error)
        {
            // a sound with more than 2 channels comes out as several stereo/mono streams (e.g. 5.1 = 3 x 2 ch),
            // their blocks alternating in the callbacks: each stream is collected on its own, then interleaved
            SortedDictionary<int, List<short>> streams = new SortedDictionary<int, List<short>>();
            SortedDictionary<int, int> streamChannels = new SortedDictionary<int, int>();
            SortedDictionary<int, int> streamRates = new SortedDictionary<int, int>();
            string callbackError = null;

            AudioCallback callback = (short[] data, int count, StreamInfo info) =>
            {
                // called from native code: nothing may be thrown back into it
                try
                {
                    // same filter as the sound editor
                    if (info.streamIndex == -1 || data == null)
                        return;
                    if (!streams.TryGetValue(info.streamIndex, out List<short> list))
                    {
                        list = new List<short>();
                        streams[info.streamIndex] = list;
                        streamChannels[info.streamIndex] = info.numChannels;
                        streamRates[info.streamIndex] = info.sampleRate;
                    }
                    else if (callbackError == null && streamChannels[info.streamIndex] != info.numChannels)
                    {
                        callbackError = $"stream {info.streamIndex} changed from {streamChannels[info.streamIndex]} to {info.numChannels} channel(s)";
                    }
                    else if (callbackError == null && streamRates[info.streamIndex] != info.sampleRate)
                    {
                        callbackError = $"stream {info.streamIndex} changed from {streamRates[info.streamIndex]} to {info.sampleRate} Hz";
                    }
                    list.AddRange(data);
                }
                catch (Exception e)
                {
                    if (callbackError == null)
                        callbackError = $"{e.GetType().Name}: {e.Message}";
                }
            };

            int status;
            error = null;
            List<short> decoded = new List<short>();
            int outChannels = 0;
            int outSampleRate = 0;
            try
            {
                Decode(buffer, buffer.Length, callback);
                if (callbackError != null)
                {
                    status = StatusError;
                    error = "decoder output could not be assembled: " + callbackError;
                }
                else
                {
                    error = Interleave(streams, streamChannels, streamRates, decoded, out outChannels, out outSampleRate, out bool fatal);
                    status = fatal ? StatusError : StatusOk;
                }
            }
            catch (AccessViolationException)
            {
                status = StatusMemoryFault;
                error = "memory access violation";
            }
            catch (Exception e)
            {
                status = StatusError;
                error = $"{e.GetType().Name}: {e.Message}";
            }
            GC.KeepAlive(callback);

            samples = status == StatusOk ? decoded : new List<short>();
            channels = status == StatusOk ? outChannels : 0;
            sampleRate = outSampleRate;
            return status;
        }

        /// <summary>Same limit as the editor side (NativeEALayer3Worker.MaxSampleCount).</summary>
        private const long MaxSampleCount = 1 << 29;

        /// <summary>
        /// Interleaves the streams frame by frame, in stream order (stream 0 channels first; for 5.1 that is
        /// FL FR, FC LFE, BL BR). A single stream is passed through. Returns null, a warning about the output
        /// (padded streams, incomplete frames, unusual stream numbering), or, with <paramref name="fatal"/>, the
        /// reason no usable output could be built.
        /// </summary>
        private static string Interleave(SortedDictionary<int, List<short>> streams, SortedDictionary<int, int> streamChannels,
            SortedDictionary<int, int> streamRates, List<short> output, out int channels, out int sampleRate, out bool fatal)
        {
            channels = 0;
            sampleRate = 0;
            fatal = false;
            if (streams.Count == 0)
                return null;

            List<string> notes = new List<string>();
            List<List<short>> lists = new List<List<short>>();
            List<int> counts = new List<int>();
            long maxFrames = 0, minFrames = long.MaxValue;
            int expectedIndex = 0;
            foreach (KeyValuePair<int, List<short>> stream in streams)
            {
                int ch = streamChannels[stream.Key];
                int rate = streamRates[stream.Key];
                if (ch <= 0 || rate <= 0)
                {
                    fatal = true;
                    return $"stream {stream.Key} reports {ch} channel(s) at {rate} Hz";
                }
                if (sampleRate == 0)
                    sampleRate = rate;
                else if (rate != sampleRate)
                {
                    fatal = true;
                    return $"streams have different sample rates ({sampleRate} and {rate} Hz)";
                }
                if (stream.Key != expectedIndex)
                    notes.Add($"stream numbers are not 0..{streams.Count - 1} (found {stream.Key} at position {expectedIndex}), channel order may be wrong");
                expectedIndex++;

                int remainder = stream.Value.Count % ch;
                if (remainder != 0)
                    notes.Add($"stream {stream.Key} ends with an incomplete frame ({remainder} of {ch} samples), completed with silence");

                lists.Add(stream.Value);
                counts.Add(ch);
                channels += ch;
                long frames = (stream.Value.Count + ch - 1) / ch;
                maxFrames = Math.Max(maxFrames, frames);
                minFrames = Math.Min(minFrames, frames);
            }

            if (maxFrames * channels > MaxSampleCount)
            {
                fatal = true;
                return $"decoded output too large ({maxFrames} frames x {channels} channels)";
            }

            if (lists.Count == 1 && notes.Count == 0)
            {
                output.AddRange(lists[0]);
                return null;
            }

            if (minFrames != maxFrames)
                notes.Add($"the {streams.Count} streams of this sound have different lengths ({minFrames} to {maxFrames} frames), the shorter ones were completed with silence");

            output.Capacity = (int)(maxFrames * channels);
            for (long frame = 0; frame < maxFrames; frame++)
            {
                for (int s = 0; s < lists.Count; s++)
                {
                    List<short> list = lists[s];
                    int ch = counts[s];
                    long index = frame * ch;
                    for (int c = 0; c < ch; c++)
                        output.Add(index + c < list.Count ? list[(int)(index + c)] : (short)0);
                }
            }

            return notes.Count == 0 ? null : string.Join("; ", notes);
        }
    }
}
