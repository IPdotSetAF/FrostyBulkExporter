using BulkExporterPlugin.Models;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using MeshSetPlugin;
using MeshSetPlugin.Resources;
using SoundEditorPlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TexturePlugin;

namespace BulkExporterPlugin.Exporters
{
    public static class Exporter
    {
        public static string[] MeshAssetTypes = { "CompositeMeshAsset", "RigidMeshAsset" };
        public static string[] SkinnedMeshAssetTypes = { "SkinnedMeshAsset" };
        public static string[] TextureAssetTypes = { "TextureAsset" };
        public static string[] AudioAssetTypes = AudioExporter.assetTypes;

        public static AssetCollection EnumerateAllAssets(string[] paths, string[] excludePaths)
        {
            for (int i = 0; i < paths.Length; i++)
                paths[i] = paths[i].Trim('/');

            for (int i = 0; i < excludePaths.Length; i++)
                excludePaths[i] = excludePaths[i].Trim('/');

            IEnumerable<EbxAssetEntry> meshes = new List<EbxAssetEntry>();
            IEnumerable<EbxAssetEntry> skinnedMeshes = new List<EbxAssetEntry>();
            IEnumerable<EbxAssetEntry> textures = new List<EbxAssetEntry>();
            IEnumerable<EbxAssetEntry> audios = new List<EbxAssetEntry>();

            FrostyTaskWindow.Show("Enumerating Assets", "Initializing...", (task) =>
            {
                task.Update("Enumerating Meshes...", 0);
                meshes = EnumerateAssets(paths, excludePaths, MeshAssetTypes);
                task.Update("Enumerating Skinned Meshes...", 25);
                skinnedMeshes = EnumerateAssets(paths, excludePaths, SkinnedMeshAssetTypes);
                task.Update("Enumerating Textures...", 50);
                textures = EnumerateAssets(paths, excludePaths, TextureAssetTypes);
                task.Update("Enumerating Audios...", 75);
                audios = EnumerateAssets(paths, excludePaths, AudioAssetTypes);
                task.Update("Done", 100);
            });

            return new AssetCollection
            {
                Meshes = meshes,
                SkinnedMeshes = skinnedMeshes,
                Textures = textures,
                Audios = audios,
            };
        }

        public static AssetCollection EnumerateAllAssets(string path) => EnumerateAllAssets(new[] { path }, Array.Empty<string>());

        public static IEnumerable<EbxAssetEntry> EnumerateAssets(string[] paths, string[] excludePaths, params string[] types)
        {
            List<EbxAssetEntry> items = new List<EbxAssetEntry>();

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

                    if (includes.Length > 0)
                        items.Add(entry);
                }

            return items;
        }

        public static void ExportAssets(this AssetCollection assetCollection, string path, BulkExportSetting settings)
        {
            var commonPath = GetRootPath(ref assetCollection, settings);

            var assetCounts = assetCollection.GetCounts();

            int total = (settings.ExportMeshes ? assetCounts.MeshCount : 0) +
                        (settings.ExportSkinnedMeshes ? assetCounts.SkinnedMeshCount : 0) +
                        (settings.ExportTextures ? assetCounts.TextureCount : 0) +
                        (settings.ExportAudio ? assetCounts.AudioCount : 0);

            var exported = new AssetCount();

            FrostyTaskWindow.Show("Bulk Exporting Assets", "", (task) =>
            {
                App.AssetManager.SendManagerCommand("legacy", "SetCacheModeEnabled", true);

                int progress = 0;
                if (settings.ExportMeshes)
                {
                    FBXExporter exporter = new FBXExporter(task);
                    foreach (EbxAssetEntry asset in assetCollection.Meshes)
                    {
                        dynamic meshAsset = GetAsset(asset);
                        if (meshAsset == null)
                            continue;

                        ulong resRid = meshAsset.MeshSetResource;
                        ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
                        MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);

                        var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);
                        if (filePath.EndsWith("_mesh"))
                            filePath = filePath.Substring(0, filePath.Length - 5);
                        var format = settings.MeshFormat.ToString().ToLower();
                        filePath += '.' + format;
                        EnsureDirectoryExists(filePath);

                        try
                        {
                            exporter.ExportFBX(meshAsset, filePath, settings.MeshVersion.ToString().Replace("FBX_", ""), settings.MeshScale.ToString(), settings.FlattenMesh, settings.SingleLOD, string.Empty, format, meshSet);
                        }
                        catch (Exception)
                        {
                            App.Logger.Log("[Skipping] Failed to export meshAsset: {0}", asset.Name);
                        }

                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                        exported.MeshCount++;
                    }
                    App.Logger.Log("Bulk Exported {0} meshAssets to {1}", exported.MeshCount, path);
                }
                if (settings.ExportSkinnedMeshes)
                {
                    FBXExporter exporter = new FBXExporter(task);
                    foreach (EbxAssetEntry asset in assetCollection.SkinnedMeshes)
                    {
                        dynamic meshAsset = GetAsset(asset);
                        if (meshAsset == null)
                            continue;

                        ulong resRid = meshAsset.MeshSetResource;
                        ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
                        MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);

                        var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);
                        if (filePath.EndsWith("_mesh"))
                            filePath = filePath.Substring(0, filePath.Length - 5);
                        var format = settings.MeshFormat.ToString().ToLower();
                        filePath += '.' + format;
                        EnsureDirectoryExists(filePath);

                        try
                        {
                            exporter.ExportFBX(meshAsset, filePath, settings.MeshVersion.ToString().Replace("FBX_", ""), settings.MeshScale.ToString(), settings.FlattenMesh, settings.SingleLOD, settings.SkeletonPath, format, meshSet);
                            exported.SkinnedMeshCount++;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            App.Logger.LogError("[Skipping] Skeleton miss match for skinnedMeshAsset : {0}", asset.Name);
                        }
                        catch (Exception)
                        {
                            App.Logger.Log("[Skipping] Failed to export skinnedMeshAsset: {0}", asset.Name);
                        }

                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                    }
                    App.Logger.Log("Bulk Exported {0} skinnedMeshAssets to {1}", exported.SkinnedMeshCount, path);
                }
                if (settings.ExportTextures)
                {
                    var textureExporter = new TextureExporter();
                    foreach (EbxAssetEntry asset in assetCollection.Textures)
                    {
                        dynamic textureAsset = GetAsset(asset);
                        if (textureAsset == null)
                            continue;

                        ResAssetEntry resEntry = App.AssetManager.GetResEntry(textureAsset.Resource);
                        Texture texture = App.AssetManager.GetResAs<Texture>(resEntry);

                        var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);
                        var format = settings.TextureFormat.ToString().ToLower();
                        filePath += '.' + format;
                        EnsureDirectoryExists(filePath);

                        try
                        {
                            textureExporter.Export(texture, filePath, "*." + format);
                        }
                        catch (Exception)
                        {
                            App.Logger.Log("[Skipping] Failed to export textureAsset: {0}", asset.Name);
                        }

                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                        exported.TextureCount++;
                    }
                    App.Logger.Log("Bulk Exported {0} textureAssets to {1}", exported.TextureCount, path);
                }
                if (settings.ExportAudio)
                {
                    var audioExporter = new AudioExporter();
                    foreach (EbxAssetEntry asset in assetCollection.Audios)
                    {
                        try
                        {
                            IEnumerable<SoundDataTrack> tracks = audioExporter.EnumerateTracks(asset);

                            var trackCounts = tracks.Count();
                            if (trackCounts == 0)
                                continue;

                            var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);

                            foreach (var track in tracks)
                            {
                                var trackPath = filePath + (trackCounts > 1 ? '_' + track.Name : "") + ".wav";
                                EnsureDirectoryExists(trackPath);
                                audioExporter.Export(track, trackPath);
                                task.Update(asset.Name + '_' + track.Name, (progress / (double)total) * 100.0);
                            }
                        }
                        catch (Exception)
                        {
                            App.Logger.Log("[Skipping] Failed to export audioAsset: {0}", asset.Name);
                        }

                        progress++;
                        exported.AudioCount++;
                    }
                    App.Logger.Log("Bulk Exported {0} audioAssets to {1}", exported.AudioCount, path);
                }

                App.AssetManager.SendManagerCommand("legacy", "FlushCache");
                App.Logger.Log("Bulk Exported total of {0} assets to {1}", exported.Total, path);
            });

        }

        private static dynamic GetAsset(EbxAssetEntry asset)
        {
            EbxAsset ebxAsset = App.AssetManager.GetEbx(asset);
            return (dynamic)ebxAsset.RootObject;
        }

        private static string CreateAssetFilePath(string rootPath, string commonPath, string assetName, bool flatten)
        {
            if (flatten)
                assetName = assetName.Split('/').Last();
            else
                assetName = assetName.Substring(commonPath.Length, assetName.Length - commonPath.Length);

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
    }
}
