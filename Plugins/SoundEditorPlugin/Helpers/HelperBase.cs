using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace SoundEditorPlugin.Helpers
{
    public abstract class HelperBase<T>
        where T : HelperBase<T>, new()
    {
        public enum InitializedState
        {
            NotInitialized,
            Initializing,
            Initialized
        };

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public InitializedState State = InitializedState.NotInitialized;
        public string BasePath { get; protected set; } = "Base";                 // The base directory to extract contents to
        public string ResourceName { get; protected set; } = "Resource";         // The resource name in Resources/ folder
        public string ToolName { get; protected set; } = "Tool";                 // Name of the extracted tool executable
        public string ResourcePath                                               // Full path to the extracted tool
        { 
            get 
            { 
                return Path.Combine(BasePath, ToolName); 
            } 
        }

        // Lazy Singleton pattern: https://csharpindepth.com/Articles/Singleton
        private static readonly Lazy<T> lazy = new Lazy<T>(() => new T());

        public static T Instance
        {
            get
            {
                return lazy.Value;
            }
        }

        public async Task InitializeAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (State == InitializedState.Initialized)
                    return;

                State = InitializedState.Initializing;
                Directory.CreateDirectory(BasePath);

                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string outputPath = Path.Combine(BasePath, entry.Name);
                        if (File.Exists(outputPath))
                            continue;

                        // extract to a temp name first, so a failed extraction never leaves a truncated exe behind
                        string partialPath = outputPath + ".partial";
                        using (Stream entryStream = entry.Open())
                        using (FileStream outputStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            await entryStream.CopyToAsync(outputStream).ConfigureAwait(continueOnCapturedContext: false);
                        }
                        File.Move(partialPath, outputPath);
                    }
                }

                State = InitializedState.Initialized;
            }
            catch
            {
                // allow a later retry instead of leaving every caller waiting on a dead initialization
                State = InitializedState.NotInitialized;
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private readonly object _initLock = new object();
        private Task _initTask;

        /// <summary>
        /// Starts the initialization once (thread-safe) and returns it. A failed initialization is retried
        /// on the next call. Callers that must not block forever should wait on the returned task with a timeout.
        /// </summary>
        public Task EnsureInitializedAsync()
        {
            lock (_initLock)
            {
                if (_initTask == null || _initTask.IsFaulted || _initTask.IsCanceled)
                    _initTask = Task.Run(() => InitializeAsync());
                return _initTask;
            }
        }
        public async Task WaitForSemaphore()
        {
            await _semaphore.WaitAsync();
            _semaphore.Release();
        }
    }
}
