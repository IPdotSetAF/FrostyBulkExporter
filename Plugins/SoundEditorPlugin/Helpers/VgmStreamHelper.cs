using FrostyCore;
using FrostySdk.Ebx;
using FrostySdk.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using NAudio.Wave;
using static SoundEditorPlugin.Resources.NewWaveResource;
using System.Threading;

namespace SoundEditorPlugin.Helpers
{
    public class VgmStreamHelper : HelperBase<VgmStreamHelper>
    {
        public VgmStreamHelper()
        {
            BasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            ResourceName = "SoundEditorPlugin.Resources.vgmstream.zip";
            ToolName = "vgmstream-cli.exe";
        }

        /// <summary>
        /// Converts any vgmstream-supported audio file (e.g. .opus) to a temporary WAV file.
        /// The caller is responsible for deleting the returned file when done.
        /// Returns null if the conversion fails.
        /// </summary>
        public async Task<string> ConvertToWav(string inputFilePath)
        {
            await EnsureInitializedAsync();

            string tempOutputFileName = BasePath + Guid.NewGuid() + ".wav";

            Process process = new Process();
            ProcessStartInfo psi = new ProcessStartInfo(ResourcePath + ".")
            {
                Arguments = $"-o \"{tempOutputFileName}\" \"{inputFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo = psi;
            process.EnableRaisingEvents = true;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0 && !File.Exists(tempOutputFileName))
                return null;

            return tempOutputFileName;
        }

        public Task<short[]> Decode(byte[] soundBuffer)
        {
            return Decode(soundBuffer, Timeout.Infinite, CancellationToken.None);
        }

        /// <summary>
        /// Decodes an EA sound stream (SPS) to 16-bit PCM samples through vgmstream-cli.
        /// The external process is killed if it runs longer than <paramref name="timeoutMs"/>
        /// (<see cref="Timeout.Infinite"/> to wait forever) or if <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        /// <exception cref="TimeoutException">vgmstream did not finish in time.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="FileFormatException">vgmstream failed to decode the data.</exception>
        public async Task<short[]> Decode(byte[] soundBuffer, int timeoutMs, CancellationToken cancellationToken)
        {
            DecodeResult result = await DecodeWithFormat(soundBuffer, timeoutMs, cancellationToken);
            return result.Samples;
        }

        /// <summary>Interleaved 16-bit samples plus the format vgmstream actually produced.</summary>
        public sealed class DecodeResult
        {
            public short[] Samples { get; set; }
            public int Channels { get; set; }
            public int SampleRate { get; set; }
        }

        /// <summary>Same as <see cref="Decode(byte[], int, CancellationToken)"/>, also returning the output channel count and sample rate.</summary>
        public async Task<DecodeResult> DecodeWithFormat(byte[] soundBuffer, int timeoutMs, CancellationToken cancellationToken)
        {
            await WaitForInitialization(timeoutMs, cancellationToken);

            string tempInputFileName = BasePath + Guid.NewGuid() + ".sps";
            string tempOutputFileName = BasePath + Guid.NewGuid() + ".wav";
            File.WriteAllBytes(tempInputFileName, soundBuffer);

            try
            {
                using (Process process = new Process())
                {
                    ProcessStartInfo processStartInfo = new ProcessStartInfo(ResourcePath + ".");

                    // paths are quoted: the temp folder can contain spaces (e.g. user name)
                    processStartInfo.Arguments = $"-o \"{tempOutputFileName}\" \"{tempInputFileName}\"";

                    processStartInfo.UseShellExecute = false;
                    processStartInfo.CreateNoWindow = true;
                    // both pipes are drained asynchronously so a chatty vgmstream can never block on a full pipe
                    processStartInfo.RedirectStandardOutput = true;
                    processStartInfo.RedirectStandardError = true;
                    process.StartInfo = processStartInfo;

                    StringBuilder errorOutput = new StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errorOutput) errorOutput.AppendLine(e.Data); };
                    process.OutputDataReceived += (s, e) => { };

                    cancellationToken.ThrowIfCancellationRequested();
                    process.Start();
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    WaitForExitOrKill(process, timeoutMs, cancellationToken);

                    // same rule as before: a non-zero exit code is tolerated as long as a WAV was written
                    if (!File.Exists(tempOutputFileName))
                    {
                        string details;
                        lock (errorOutput) details = errorOutput.ToString().Trim();
                        throw new FileFormatException($"vgmstream failed to decode the data (exit code {process.ExitCode})" + (details.Length > 0 ? ": " + details : "."));
                    }
                }

                return ReadWav(tempOutputFileName);
            }
            finally
            {
                TryDelete(tempInputFileName);
                TryDelete(tempOutputFileName);
            }
        }

        private async Task WaitForInitialization(int timeoutMs, CancellationToken cancellationToken)
        {
            Task init = EnsureInitializedAsync();
            if (!init.IsCompleted)
            {
                Task delay = Task.Delay(timeoutMs, cancellationToken);
                if (await Task.WhenAny(init, delay) != init)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"vgmstream could not be prepared within {timeoutMs / 1000} s.");
                }
            }

            // rethrows the extraction error (e.g. file blocked by an antivirus); the next call retries it
            await init;
        }

        /// <summary>Reads a 16-bit PCM WAV by walking its RIFF chunks (vgmstream may write chunks such as "smpl" before "data").</summary>
        private static DecodeResult ReadWav(string fileName)
        {
            using (NativeReader reader = new NativeReader(new FileStream(fileName, FileMode.Open, FileAccess.Read)))
            {
                if (reader.Length < 12 || reader.ReadUInt(Endian.Big) != 0x52494646 /* RIFF */)
                    throw new FileFormatException("vgmstream output is not a RIFF file.");
                reader.ReadUInt();
                if (reader.ReadUInt(Endian.Big) != 0x57415645 /* WAVE */)
                    throw new FileFormatException("vgmstream output is not a WAVE file.");

                int channels = 0;
                int sampleRate = 0;
                int bitsPerSample = 0;

                while (reader.Position + 8 <= reader.Length)
                {
                    uint chunkId = reader.ReadUInt(Endian.Big);
                    uint chunkSize = reader.ReadUInt();
                    long chunkStart = reader.Position;

                    if (chunkId == 0x666d7420 /* "fmt " */)
                    {
                        reader.ReadUShort(); // format tag
                        channels = reader.ReadUShort();
                        sampleRate = (int)reader.ReadUInt();
                        reader.ReadUInt(); // byte rate
                        reader.ReadUShort(); // block align
                        bitsPerSample = reader.ReadUShort();
                    }
                    else if (chunkId == 0x64617461 /* "data" */)
                    {
                        if (bitsPerSample != 16 || channels <= 0)
                            throw new FileFormatException($"Unexpected vgmstream output ({bitsPerSample} bits per sample, {channels} channels).");

                        long available = reader.Length - chunkStart;
                        if (chunkSize > available)
                            throw new FileFormatException($"vgmstream output is truncated ({available} of {chunkSize} bytes).");

                        int numSamples = (int)(chunkSize / 2);
                        short[] samples = new short[numSamples];
                        for (int i = 0; i < numSamples; i++)
                            samples[i] = reader.ReadShort();

                        return new DecodeResult { Samples = samples, Channels = channels, SampleRate = sampleRate };
                    }

                    // chunks are word aligned
                    reader.Position = chunkStart + chunkSize + (chunkSize & 1);
                }

                throw new FileFormatException("vgmstream output has no data chunk.");
            }
        }

        private static void WaitForExitOrKill(Process process, int timeoutMs, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!process.WaitForExit(100))
            {
                bool timedOut = timeoutMs != Timeout.Infinite && stopwatch.ElapsedMilliseconds > timeoutMs;
                if (timedOut || cancellationToken.IsCancellationRequested)
                {
                    string killProblem = "";
                    try
                    {
                        process.Kill();
                        if (!process.WaitForExit(5000))
                            killProblem = " The process could not be stopped.";
                    }
                    catch (InvalidOperationException)
                    {
                        // it exited on its own in the meantime
                    }
                    catch (Exception e)
                    {
                        killProblem = " The process could not be stopped: " + e.Message;
                    }

                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException("vgmstream run cancelled." + killProblem, cancellationToken);
                    throw new TimeoutException($"vgmstream did not finish within {timeoutMs / 1000} s." + killProblem);
                }
            }

            // flush the asynchronous stdout/stderr readers (bounded: a leftover pipe handle must not block us)
            process.WaitForExit(5000);
        }

        private static void TryDelete(string fileName)
        {
            try
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }
            catch (Exception)
            {
                // temp file, best effort
            }
        }
    }
}
