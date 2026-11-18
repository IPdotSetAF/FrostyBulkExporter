using BulkExporterPlugin.Models;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;
using MeshSetPlugin;
using MeshSetPlugin.Resources;
using SoundEditorPlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TexturePlugin;

namespace BulkExporterPlugin.Exporters
{
    public static class Exporter
    {
        public static string[] MeshAssetTypes = { "CompositeMeshAsset", "RigidMeshAsset" };
        public static string[] SkinnedMeshAssetTypes = { "SkinnedMeshAsset" };
        public static string[] TextureAssetTypes = { "TextureAsset" };
        public static string[] AudioAssetTypes = AudioExporter.assetTypes;

        /// <summary>Maximum time spent decoding one sound segment (vgmstream run or native EALayer3 worker); the decoder process is killed after it.</summary>
        public const int AudioDecodeTimeoutMs = 60_000;

        /// <summary>Written in the export folder when some assets were not exported, or only partially.</summary>
        public const string SkippedListFileName = "bulk_export_skipped.txt";

        // prefixes in the skipped list for lines that are not failures, so they can be filtered out
        private const string InfoPrefix = "info: ";
        private const string WarningPrefix = "warning: ";
        public const string InProgressFileName = "bulk_export_in_progress.txt";

        /// <summary>True when <paramref name="type"/> is one of <paramref name="baseTypes"/> or derives from one.</summary>
        public static bool IsOfType(string type, IEnumerable<string> baseTypes) =>
            !string.IsNullOrEmpty(type) && baseTypes.Any(baseType => TypeLibrary.IsSubClassOf(type, baseType));

        #region -- Enumeration --

        public static AssetCollection EnumerateAllAssets(string[] paths, string[] excludePaths)
        {
            for (int i = 0; i < paths.Length; i++)
                paths[i] = paths[i].Trim('/');

            for (int i = 0; i < excludePaths.Length; i++)
                excludePaths[i] = excludePaths[i].Trim('/');

            AssetCollection result = AssetCollection.Empty();

            FrostyTaskWindow.Show("Enumerating Assets", "Initializing...", (task) =>
            {
                try
                {
                    task.Update("Enumerating Meshes...", 0);
                    var meshes = EnumerateAssets(paths, excludePaths, MeshAssetTypes);
                    task.Update("Enumerating Skinned Meshes...", 25);
                    var skinnedMeshes = EnumerateAssets(paths, excludePaths, SkinnedMeshAssetTypes);
                    task.Update("Enumerating Textures...", 50);
                    var textures = EnumerateAssets(paths, excludePaths, TextureAssetTypes);
                    task.Update("Enumerating Audios...", 75);
                    var audios = EnumerateAssets(paths, excludePaths, AudioAssetTypes);
                    task.Update("Done", 100);

                    result = new AssetCollection
                    {
                        Meshes = meshes,
                        SkinnedMeshes = skinnedMeshes,
                        Textures = textures,
                        Audios = audios,
                    };
                }
                catch (Exception e)
                {
                    // all or nothing: a partial list would silently export a subset.
                    // Never let the exception escape the task window callback, it would take the editor down.
                    result = AssetCollection.Empty();
                    App.Logger.LogError("Bulk Exporter: asset enumeration failed, nothing will be exported: {0}", e.ToString());
                }
            });

            return result;
        }

        public static AssetCollection EnumerateAllAssets(string path) => EnumerateAllAssets(new[] { path }, Array.Empty<string>());

        public static IEnumerable<EbxAssetEntry> EnumerateAssets(string[] paths, string[] excludePaths, params string[] types)
        {
            List<EbxAssetEntry> items = new List<EbxAssetEntry>();
            // EnumerateEbx(type) also returns derived types: an asset can match several requested types
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in types)
                foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx(type: type))
                {
                    var includes = paths.Where(path => entry.Name.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var excludes = excludePaths.Where(path => entry.Name.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (excludes.Length > 0)
                    {
                        if (includes.Length == 0)
                            continue;

                        if (includes.Max(s => s.Length) < excludes.Max(s => s.Length))
                            continue;
                    }

                    if (includes.Length > 0 && seen.Add(entry.Name))
                        items.Add(entry);
                }

            return items;
        }

        #endregion

        #region -- Export --

        /// <summary>Shared state of one bulk export run.</summary>
        private sealed class ExportRun
        {
            public string Path;
            public string CommonPath;
            public BulkExportSetting Settings;
            public CancellationToken Token;
            public readonly List<string> Skipped = new List<string>();
            public readonly AssetCount Exported = new AssetCount();
            /// <summary>Every file written in this run, to detect two assets mapping to the same file.</summary>
            public readonly HashSet<string> WrittenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class WorkItem
        {
            public EbxAssetEntry Asset;
            public string Kind;
        }

        public static void ExportAssets(this AssetCollection assetCollection, string path, BulkExportSetting settings)
        {
            List<WorkItem> work = new List<WorkItem>();
            if (settings.ExportMeshes)
                work.AddRange(assetCollection.Meshes.Select(a => new WorkItem { Asset = a, Kind = "meshAsset" }));
            if (settings.ExportSkinnedMeshes)
                work.AddRange(assetCollection.SkinnedMeshes.Select(a => new WorkItem { Asset = a, Kind = "skinnedMeshAsset" }));
            if (settings.ExportTextures)
                work.AddRange(assetCollection.Textures.Select(a => new WorkItem { Asset = a, Kind = "textureAsset" }));
            if (settings.ExportAudio)
                work.AddRange(assetCollection.Audios.Select(a => new WorkItem { Asset = a, Kind = "audioAsset" }));

            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                ExportRun run = new ExportRun
                {
                    Path = path,
                    CommonPath = GetRootPath(ref assetCollection, settings),
                    Settings = settings,
                    Token = cancellation.Token,
                };

                FrostyTaskWindow.Show("Bulk Exporting Assets", "", (task) => RunExport(task, work, run),
                    showCancelButton: true,
                    cancelCallback: (task) =>
                    {
                        task.Update("Cancelling after the current asset...");
                        cancellation.Cancel();
                    });
            }
        }

        private static void RunExport(FrostyTaskWindow task, List<WorkItem> work, ExportRun run)
        {
            int index = 0;
            try
            {
                // a result list from a previous run in the same folder must not be mistaken for this one
                DeleteStaleSkippedList(run.Path);

                App.AssetManager.SendManagerCommand("legacy", "SetCacheModeEnabled", true);

                FBXExporter meshExporter = null;
                TextureExporter textureExporter = null;
                AudioExporter audioExporter = null;
                SoundDecoder.Options audioOptions = null;

                for (; index < work.Count; index++)
                {
                    if (run.Token.IsCancellationRequested)
                        break;

                    WorkItem item = work[index];
                    task.Update(item.Asset.Name, (index / (double)work.Count) * 100.0);
                    MarkInProgress(run.Path, item);

                    switch (item.Kind)
                    {
                        case "meshAsset":
                            meshExporter = meshExporter ?? new FBXExporter(task);
                            if (TryExport(item, run, written => ExportMesh(meshExporter, item.Asset, run, string.Empty, written)))
                                run.Exported.MeshCount++;
                            break;

                        case "skinnedMeshAsset":
                            meshExporter = meshExporter ?? new FBXExporter(task);
                            if (TryExport(item, run, written => ExportMesh(meshExporter, item.Asset, run, run.Settings.SkeletonPath, written)))
                                run.Exported.SkinnedMeshCount++;
                            break;

                        case "textureAsset":
                            textureExporter = textureExporter ?? new TextureExporter();
                            if (TryExport(item, run, written => ExportTexture(textureExporter, item.Asset, run, written)))
                                run.Exported.TextureCount++;
                            break;

                        case "audioAsset":
                            if (audioExporter == null)
                            {
                                audioOptions = new SoundDecoder.Options
                                {
                                    VgmStreamTimeoutMs = AudioDecodeTimeoutMs,
                                    NativeDecodeTimeoutMs = AudioDecodeTimeoutMs,
                                    CancellationToken = run.Token,
                                };
                                audioExporter = new AudioExporter(audioOptions);
                            }

                            string assetName = item.Asset.Name;
                            audioOptions.Warn = message => App.Logger.LogWarning("[{0}] {1}", assetName, message);

                            bool hasAudio = false;
                            if (TryExport(item, run, written => hasAudio = ExportAudio(audioExporter, item.Asset, run, written)) && hasAudio)
                                run.Exported.AudioCount++;
                            break;
                    }
                }

                if (index >= work.Count)
                    task.Update("Done", 100.0);
            }
            catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
            {
                // the asset in progress is reported below with the other remaining ones
            }
            catch (Exception e)
            {
                // never let an exception escape the task window callback: it would crash the editor
                App.Logger.LogError("Bulk export stopped by an unexpected error: {0}", e.ToString());
                if (index < work.Count)
                    run.Skipped.Add($"{work[index].Asset.Name} ({work[index].Kind}): export stopped by an unexpected error: {e.GetType().Name}: {e.Message}");
                index++;
            }
            finally
            {
                // everything that was never attempted is listed, so a cancelled or aborted run is not mistaken for a complete one
                string reason = run.Token.IsCancellationRequested ? "not attempted, export cancelled" : "not attempted, export stopped early";
                for (int i = index; i < work.Count; i++)
                    run.Skipped.Add($"{work[i].Asset.Name} ({work[i].Kind}): {reason}");

                try
                {
                    App.AssetManager.SendManagerCommand("legacy", "FlushCache");
                }
                catch (Exception e)
                {
                    App.Logger.LogWarning("Could not flush the legacy cache: {0}", e.Message);
                }

                if (run.Token.IsCancellationRequested)
                    App.Logger.LogWarning("Bulk export cancelled by the user.");

                App.Logger.Log("Bulk Exported {0} meshAssets, {1} skinnedMeshAssets, {2} textureAssets, {3} audioAssets to {4}",
                    run.Exported.MeshCount, run.Exported.SkinnedMeshCount, run.Exported.TextureCount, run.Exported.AudioCount, run.Path);
                ReportSkipped(run);
                App.Logger.Log("Bulk Exported total of {0} of {1} assets to {2}", run.Exported.Total, work.Count, run.Path);

                ClearInProgress(run.Path);
            }
        }

        /// <summary>
        /// Names the asset being exported in a small file. If a native decoder kills the whole editor
        /// (nothing else gets written then), this file tells which asset caused it.
        /// </summary>
        private static void MarkInProgress(string folder, WorkItem item)
        {
            try
            {
                File.WriteAllText(Path.Combine(folder, InProgressFileName),
                    $"Exporting {item.Kind} {item.Asset.Name}{Environment.NewLine}If the editor closed on its own, this asset caused it.{Environment.NewLine}");
            }
            catch (Exception)
            {
                // diagnostic only
            }
        }

        private static void ClearInProgress(string folder)
        {
            try
            {
                File.Delete(Path.Combine(folder, InProgressFileName));
            }
            catch (Exception)
            {
                // diagnostic only
            }
        }

        /// <summary>
        /// Runs one export. On failure the files already written for this asset are removed and the asset is
        /// added to the skipped list; the batch goes on. Cancellation is let through to stop the batch.
        /// </summary>
        private static bool TryExport(WorkItem item, ExportRun run, Action<List<string>> export)
        {
            List<string> written = new List<string>();
            try
            {
                export(written);
                return true;
            }
            catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
            {
                DeleteFiles(written);
                throw;
            }
            catch (Exception e)
            {
                DeleteFiles(written);

                string reason = e is IndexOutOfRangeException && item.Kind == "skinnedMeshAsset"
                    ? $"probably a skeleton mismatch ({e.Message})"
                    : $"{e.GetType().Name}: {e.Message}";

                run.Skipped.Add($"{item.Asset.Name} ({item.Kind}): {reason}");
                App.Logger.LogWarning("[Skipping] Failed to export {0} {1}: {2}", item.Kind, item.Asset.Name, reason);
                return false;
            }
        }

        /// <returns>false when the asset holds no audio at all (listed as info, not counted as exported)</returns>
        private static bool ExportAudio(AudioExporter audioExporter, EbxAssetEntry asset, ExportRun run, List<string> written)
        {
            List<SoundDataTrack> allTracks = audioExporter.EnumerateTracks(asset);
            if (allTracks.Count == 0)
            {
                // nothing to decode (e.g. music master/controller assets that only reference other assets): not a failure
                run.Skipped.Add($"{InfoPrefix}{asset.Name} (audioAsset): no audio in this asset (0 tracks), nothing to export");
                return false;
            }

            List<SoundDataTrack> tracks = allTracks
                .Where(track => track.Samples != null && track.Samples.Length > 0 && track.ChannelCount > 0 && track.SampleRate > 0)
                .ToList();

            List<string> problems = allTracks
                .Where(track => track.DecodeProblem != null)
                .Select(track => $"{track.Name}: {track.DecodeProblem}")
                .ToList();

            if (tracks.Count == 0)
            {
                string reason = problems.Count > 0 ? string.Join(" | ", problems) : "every track is empty";
                throw new InvalidDataException("no decodable track (" + reason + ")");
            }

            // the suffix is decided from the asset's full track list, so file names do not depend on which tracks failed
            string filePath = CreateAssetFilePath(run.Path, run.CommonPath, asset.Name, run.Settings.Flatten);
            foreach (SoundDataTrack track in tracks)
            {
                run.Token.ThrowIfCancellationRequested();

                string suffix = "";
                if (allTracks.Count > 1)
                    suffix += "_" + track.Name;
                if (!string.IsNullOrEmpty(track.Language))
                    suffix += "_" + track.Language;

                string trackPath = filePath + SanitizeFileName(suffix) + ".wav";
                ReserveOutputPath(run, trackPath);
                EnsureDirectoryExists(trackPath);
                audioExporter.Export(track, trackPath);
                // recorded only once complete: Export writes through a .partial file, so on failure an older WAV is left untouched
                written.Add(trackPath);
            }

            if (problems.Count > 0)
            {
                // listed so it is not mistaken for a clean export; "partial" only when tracks are actually missing
                string line = tracks.Count < allTracks.Count
                    ? $"{asset.Name} (audioAsset): partial, {tracks.Count} of {allTracks.Count} track(s) written, "
                    : $"{WarningPrefix}{asset.Name} (audioAsset): all {tracks.Count} track(s) written, ";
                run.Skipped.Add(line + string.Join(" | ", problems));
            }

            return true;
        }

        private static void ExportMesh(FBXExporter exporter, EbxAssetEntry asset, ExportRun run, string skeletonPath, List<string> written)
        {
            dynamic meshAsset = GetAsset(asset);

            ulong resRid = meshAsset.MeshSetResource;
            ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
            if (rEntry == null)
                throw new InvalidDataException("MeshSet resource not found");

            MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);

            var filePath = CreateAssetFilePath(run.Path, run.CommonPath, asset.Name, run.Settings.Flatten);
            if (filePath.EndsWith("_mesh"))
                filePath = filePath.Substring(0, filePath.Length - 5);
            var format = run.Settings.MeshFormat.ToString().ToLower();
            filePath += '.' + format;
            ReserveOutputPath(run, filePath);
            EnsureDirectoryExists(filePath);
            written.Add(filePath);

            exporter.ExportFBX(meshAsset, filePath, run.Settings.MeshVersion.ToString().Replace("FBX_", ""), run.Settings.MeshScale.ToString(),
                run.Settings.FlattenMesh, run.Settings.SingleLOD, false /* exportNonRenderable */, skeletonPath, format, meshSet);
        }

        private static void ExportTexture(TextureExporter textureExporter, EbxAssetEntry asset, ExportRun run, List<string> written)
        {
            dynamic textureAsset = GetAsset(asset);

            ResAssetEntry resEntry = App.AssetManager.GetResEntry(textureAsset.Resource);
            if (resEntry == null)
                throw new InvalidDataException("texture resource not found");

            Texture texture = App.AssetManager.GetResAs<Texture>(resEntry);

            var filePath = CreateAssetFilePath(run.Path, run.CommonPath, asset.Name, run.Settings.Flatten);
            var format = run.Settings.TextureFormat.ToString().ToLower();
            filePath += '.' + format;
            ReserveOutputPath(run, filePath);
            EnsureDirectoryExists(filePath);
            written.Add(filePath);

            textureExporter.Export(texture, filePath, "*." + format, false /* exportAsStrip */);
        }

        #endregion

        #region -- Helpers --

        /// <summary>Fails instead of silently overwriting a file another asset of this run already wrote.</summary>
        private static void ReserveOutputPath(ExportRun run, string filePath)
        {
            if (!run.WrittenFiles.Add(filePath))
                throw new IOException($"output file {filePath} was already written by another asset of this export (use a non flattened export)");
        }

        private static void DeleteFiles(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch (Exception e)
                {
                    App.Logger.LogWarning("Could not remove incomplete file {0}: {1}", file, e.Message);
                }
            }
        }

        private static void DeleteStaleSkippedList(string path)
        {
            string file = Path.Combine(path, SkippedListFileName);
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception e)
            {
                App.Logger.LogWarning("Could not remove the previous {0} ({1}), it may not match this export.", file, e.Message);
            }
        }

        private static void ReportSkipped(ExportRun run)
        {
            if (run.Skipped.Count == 0)
                return;

            int info = run.Skipped.Count(line => line.StartsWith(InfoPrefix, StringComparison.Ordinal));
            int warnings = run.Skipped.Count(line => line.StartsWith(WarningPrefix, StringComparison.Ordinal));
            App.Logger.LogWarning("Bulk export: {0} asset(s) skipped or partial, {1} written with a warning, {2} without audio, see {3}",
                run.Skipped.Count - info - warnings, warnings, info, Path.Combine(run.Path, SkippedListFileName));
            try
            {
                Directory.CreateDirectory(run.Path);
                File.WriteAllLines(Path.Combine(run.Path, SkippedListFileName), run.Skipped);
            }
            catch (Exception e)
            {
                App.Logger.LogWarning("Could not write {0}: {1}", SkippedListFileName, e.Message);
                foreach (string line in run.Skipped)
                    App.Logger.LogWarning("  {0}", line);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static dynamic GetAsset(EbxAssetEntry asset)
        {
            EbxAsset ebxAsset = App.AssetManager.GetEbx(asset);
            dynamic root = ebxAsset?.RootObject;
            if (root == null)
                throw new InvalidDataException("asset could not be loaded");
            return root;
        }

        private static string CreateAssetFilePath(string rootPath, string commonPath, string assetName, bool flatten)
        {
            if (flatten)
                assetName = assetName.Split('/').Last();
            else if (commonPath.Length > 0 && assetName.StartsWith(commonPath, StringComparison.OrdinalIgnoreCase))
                assetName = assetName.Substring(commonPath.Length);

            return rootPath + '\\' + assetName.TrimStart('/').Replace('/', '\\');
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private static string GetRootPath(ref AssetCollection assetCollection, BulkExportSetting settings)
        {
            var paths = new List<string>();

            if (settings.ExportMeshes)
                paths.AddRange(assetCollection.Meshes.Select(asset => asset.Path.ToLower()));

            if (settings.ExportSkinnedMeshes)
                paths.AddRange(assetCollection.SkinnedMeshes.Select(asset => asset.Path.ToLower()));

            if (settings.ExportAudio)
                paths.AddRange(assetCollection.Audios.Select(asset => asset.Path.ToLower()));

            if (settings.ExportTextures)
                paths.AddRange(assetCollection.Textures.Select(asset => asset.Path.ToLower()));

            var commonPath = LongestCommonPrefix(paths);

            // keep whole folder names: "sound/vo" and "sound/vox" share "sound/vo" as characters but only "sound/" as folders
            bool allSameFolder = paths.Count > 0 && paths.All(p => p.Length == commonPath.Length || p[commonPath.Length] == '/');
            if (!allSameFolder)
            {
                int lastSlash = commonPath.LastIndexOf('/');
                commonPath = lastSlash >= 0 ? commonPath.Substring(0, lastSlash) : "";
            }

            return commonPath.Trim('/');
        }

        private static string LongestCommonPrefix(IEnumerable<string> strings)
        {
            if (!strings.Any())
                return "";

            string first = strings.First();
            int commonPrefixLength = 0;

            foreach (char c in first)
            {
                if (strings.All(s => s.Length > commonPrefixLength && s[commonPrefixLength] == c))
                    commonPrefixLength++;
                else
                    break;
            }

            return first.Substring(0, commonPrefixLength);
        }

        #endregion
    }
}
