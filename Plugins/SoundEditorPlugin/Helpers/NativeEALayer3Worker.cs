using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SoundEditorPlugin.Helpers
{
    /// <summary>The native decoder crashed (the worker process died) while decoding one stream.</summary>
    internal sealed class NativeDecoderCrashedException : IOException
    {
        public NativeDecoderCrashedException(string message) : base(message) { }
    }

    /// <summary>
    /// Runs the native EALayer3 decoder (ealayer3.dll) in a separate worker process, EALayer3Host.exe,
    /// which is embedded in this plugin and extracted to the temp folder on first use.
    /// <para>
    /// The decoding is the same as calling <see cref="EALayer3.Decode"/> in the editor, except that the streams
    /// of a sound with more than 2 channels are interleaved into one multichannel output. A memory fault or an
    /// endless loop inside the native code only ends the worker: it is killed or replaced, and the editor
    /// gets an exception for that one sound. One worker is kept alive between calls (starting a process per
    /// sound would be too slow for bulk export); calls are serialized.
    /// </para>
    /// </summary>
    internal static class NativeEALayer3Worker
    {
        private const string ResourceName = "SoundEditorPlugin.Resources.EALayer3Host.exe";
        private const int MaxSampleCount = 1 << 29; // the byte count (x2) must still fit in an int

        private static readonly object s_lock = new object();
        private static string s_exePath;
        private static Process s_process;
        private static AnonymousPipeServerStream s_requests;
        private static AnonymousPipeServerStream s_responses;
        private static int s_decodesSinceStart;

        /// <summary>Why the worker could not be started last time; further starts are skipped for a while.</summary>
        private static string s_startFailure;
        private static DateTime s_startFailureTime;
        private static readonly TimeSpan StartRetryDelay = TimeSpan.FromMinutes(1);

        internal sealed class Result
        {
            public short[] Samples;
            public int Channels;
            public int SampleRate;
            /// <summary>Warning from the worker about a successful decode (e.g. streams of different lengths), or null.</summary>
            public string Note;
        }

        private sealed class Response
        {
            public int Status;
            public string Error;
            public Result Result;
        }

        /// <summary>Decodes one EA sound stream (from its 0x48 header to the end of the chunk).</summary>
        /// <exception cref="TimeoutException">The decoder did not finish in time (the worker was killed).</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled (the worker was killed).</exception>
        /// <exception cref="InvalidDataException">The decoder failed or crashed on this stream.</exception>
        /// <exception cref="FileNotFoundException">ealayer3.dll could not be found.</exception>
        public static Result Decode(byte[] soundBuf, int timeoutMs, CancellationToken cancellationToken)
        {
            lock (s_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // a worker that cannot start (antivirus, read-only temp folder...) is not retried for every sound
                if (s_startFailure != null && DateTime.UtcNow - s_startFailureTime < StartRetryDelay)
                    throw new InvalidDataException(s_startFailure);

                try
                {
                    StartIfNeeded();
                    s_startFailure = null;
                }
                catch (Exception e)
                {
                    Stop();
                    s_startFailure = e is InvalidDataException
                        ? e.Message
                        : $"the native EALayer3 worker could not be started ({e.GetType().Name}: {e.Message}).";
                    s_startFailureTime = DateTime.UtcNow;
                    throw new InvalidDataException(s_startFailure, e);
                }

                Process process = s_process;
                Stream requests = s_requests;
                Stream responses = s_responses;

                // the write is inside the task too: if the worker hangs, a large request could block on a full pipe
                Task<Response> exchange = Task.Run(() => Exchange(requests, responses, soundBuf));
                exchange.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!WaitQuietly(exchange, 100))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Stop();
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (timeoutMs != Timeout.Infinite && stopwatch.ElapsedMilliseconds > timeoutMs)
                    {
                        Stop();
                        throw new TimeoutException($"native EALayer3 decoder did not finish within {timeoutMs / 1000} s.");
                    }
                }

                if (exchange.IsFaulted && exchange.Exception?.InnerException is InvalidDataException invalid)
                {
                    // the worker answered something unusable (e.g. too many samples): not a crash, no point retrying
                    Stop();
                    throw new InvalidDataException(invalid.Message, invalid);
                }

                if (exchange.IsFaulted)
                {
                    // the worker died during this sound (or the protocol broke): report how, then start fresh next time
                    string how = DescribeExit(process);
                    Stop();
                    throw new NativeDecoderCrashedException("native EALayer3 decoder crashed (" + how + ").");
                }

                Response response = exchange.Result;
                s_decodesSinceStart++;
                switch (response.Status)
                {
                    case 0:
                        return response.Result;
                    case 4:
                        Stop(); // the worker exits on its own after a memory fault
                        throw new NativeDecoderCrashedException("native EALayer3 decoder crashed (memory access violation).");
                    default:
                        throw new InvalidDataException("native EALayer3 decoder failed" + (string.IsNullOrEmpty(response.Error) ? $" (status {response.Status})." : ": " + response.Error));
                }
            }
        }

        /// <summary>
        /// Ends the current worker if it has decoded anything, so the next sound starts in a fresh one.
        /// ealayer3.dll leaves state behind between calls: after thousands of sounds in the same process,
        /// some sounds end with a burst of saturated junk that a fresh process does not produce (the editor
        /// always decodes in a process that has only seen the current asset). Called once per asset.
        /// </summary>
        public static void Recycle()
        {
            lock (s_lock)
            {
                if (s_decodesSinceStart > 0)
                    Stop();
            }
        }

        private static Response Exchange(Stream requests, Stream responses, byte[] soundBuf)
        {
            BinaryWriter writer = new BinaryWriter(requests, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(soundBuf.Length);
            writer.Write(soundBuf);
            writer.Flush();

            BinaryReader reader = new BinaryReader(responses, System.Text.Encoding.UTF8, leaveOpen: true);
            int status = reader.ReadInt32();
            int channels = reader.ReadInt32();
            int sampleRate = reader.ReadInt32();
            int sampleCount = reader.ReadInt32();
            if (sampleCount < 0 || sampleCount > MaxSampleCount)
                throw new InvalidDataException($"unexpected sample count {sampleCount} from the EALayer3 worker.");

            byte[] raw = new byte[sampleCount * 2];
            int read = 0;
            while (read < raw.Length)
            {
                int n = responses.Read(raw, read, raw.Length - read);
                if (n == 0)
                    throw new EndOfStreamException("the EALayer3 worker stopped while sending samples.");
                read += n;
            }

            short[] samples = new short[sampleCount];
            Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
            string error = reader.ReadString();

            return new Response
            {
                Status = status,
                Error = error,
                Result = new Result { Samples = samples, Channels = channels, SampleRate = sampleRate, Note = status == 0 && error.Length > 0 ? error : null },
            };
        }

        private static bool WaitQuietly(Task task, int milliseconds)
        {
            try
            {
                return task.Wait(milliseconds);
            }
            catch (AggregateException)
            {
                return true; // faulted, handled by the caller
            }
        }

        private static void StartIfNeeded()
        {
            if (s_process != null && !s_process.HasExited)
                return;

            Stop();

            string exePath = ExtractWorker();
            string dllPath = FindNativeLibrary();

            AnonymousPipeServerStream requests = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            AnonymousPipeServerStream responses = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(exePath)
                {
                    Arguments = $"\"{dllPath}\" {requests.GetClientHandleAsString()} {responses.GetClientHandleAsString()}",
                    UseShellExecute = false, // also makes the pipe handles inheritable by the worker
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(dllPath),
                };

                try
                {
                    s_process = Process.Start(startInfo);
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    // typically an antivirus rule blocking freshly built executables ("access denied")
                    throw new InvalidDataException($"the native EALayer3 worker could not be started ({e.Message}). If an antivirus blocks it, exclude {WorkerFolder}.", e);
                }

                // our copies of the worker's ends must be closed, otherwise a dead worker is never noticed (no end of stream)
                requests.DisposeLocalCopyOfClientHandle();
                responses.DisposeLocalCopyOfClientHandle();

                s_requests = requests;
                s_responses = responses;
            }
            catch
            {
                requests.Dispose();
                responses.Dispose();
                throw;
            }
        }

        /// <summary>Ends the worker: a healthy one exits when its request pipe closes, a stuck one is killed.</summary>
        private static void Stop()
        {
            try { s_requests?.Dispose(); } catch (Exception) { }

            if (s_process != null)
            {
                try
                {
                    if (!s_process.WaitForExit(500))
                    {
                        s_process.Kill();
                        s_process.WaitForExit(5000);
                    }
                }
                catch (Exception)
                {
                    // already gone
                }
                s_process.Dispose();
            }

            try { s_responses?.Dispose(); } catch (Exception) { }

            s_process = null;
            s_requests = null;
            s_responses = null;
            s_decodesSinceStart = 0;
        }

        private static string DescribeExit(Process process)
        {
            try
            {
                if (!process.WaitForExit(2000))
                    return "the worker stopped answering";

                uint code = unchecked((uint)process.ExitCode);
                switch (code)
                {
                    case 0xC0000005: return "memory access violation";
                    case 0xC00000FD: return "stack overflow";
                    case 0xC0000409: return "stack buffer overrun";
                    case 3: return "ealayer3.dll could not be loaded";
                    default: return $"exit code 0x{code:X8}";
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>Folder the worker is extracted to. Fixed, so a single antivirus exclusion covers every version.</summary>
        public static string WorkerFolder => Path.Combine(Path.GetTempPath(), "FrostyEALayer3Host");

        /// <summary>
        /// Extracts the worker to %TEMP%\FrostyEALayer3Host\&lt;content hash&gt;\EALayer3Host.exe. The hash keeps
        /// different plugin versions apart; an existing copy with the right content is reused.
        /// </summary>
        private static string ExtractWorker()
        {
            if (s_exePath != null && File.Exists(s_exePath))
                return s_exePath;

            byte[] content;
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (resource == null)
                    throw new FileNotFoundException("EALayer3Host.exe is missing from SoundEditorPlugin.dll.");
                using (MemoryStream buffer = new MemoryStream())
                {
                    resource.CopyTo(buffer);
                    content = buffer.ToArray();
                }
            }

            string hash;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(content), 0, 8).Replace("-", "").ToLowerInvariant();

            string folder = Path.Combine(WorkerFolder, hash);
            string exePath = Path.Combine(folder, "EALayer3Host.exe");

            if (!File.Exists(exePath) || new FileInfo(exePath).Length != content.Length)
            {
                Directory.CreateDirectory(folder);
                string partialPath = exePath + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    File.WriteAllBytes(partialPath, content);
                    try
                    {
                        if (File.Exists(exePath))
                            File.Delete(exePath);
                        File.Move(partialPath, exePath);
                    }
                    catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException)
                        && File.Exists(exePath) && new FileInfo(exePath).Length == content.Length)
                    {
                        // another editor instance extracted (or is running) the same exe
                    }
                }
                finally
                {
                    try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch (Exception) { }
                }
            }

            s_exePath = exePath;
            return exePath;
        }

        /// <summary>Finds the ealayer3.dll the editor itself uses (DllImport "../thirdparty/ealayer3.dll").</summary>
        private static string FindNativeLibrary()
        {
            string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string[] candidates =
            {
                Path.Combine(pluginFolder, "..", "ThirdParty", "ealayer3.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThirdParty", "ealayer3.dll"),
                Path.Combine(Environment.CurrentDirectory, "..", "ThirdParty", "ealayer3.dll"),
            };

            foreach (string candidate in candidates)
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            throw new FileNotFoundException("ealayer3.dll was not found in the editor's ThirdParty folder.");
        }
    }
}
