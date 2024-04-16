using BulkExporterPlugin.Models;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin;
using MeshSetPlugin.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkExporterPlugin.Exporters
{
    public static class Exporter
    {
        public static IEnumerable<AssetEntry> EnumerateMeshAssets(string path)
        {
            var assets = EnumerateAssets(path, "CompositeMeshAsset").ToList();
            assets.AddRange(EnumerateAssets(path, "RigidMeshAsset"));
            return assets;
        }

        public static IEnumerable<AssetEntry> EnumerateSkinnedMeshAssets(string path) => EnumerateAssets(path, "SkinnedMeshAsset");

        public static IEnumerable<AssetEntry> EnumerateTextureAssets(string path) => EnumerateAssets(path, "TextureAsset");

        public static IEnumerable<AssetEntry> EnumerateAudioAssets(string path) => EnumerateAssets(path, "NewWaveAsset");

        public static IEnumerable<AssetEntry> EnumerateAssets(string path, string type)
        {
            path = path.Trim('/');
            List<AssetEntry> items = new List<AssetEntry>();

            foreach (AssetEntry entry in App.AssetManager.EnumerateEbx(type: type))
            {
                if (entry.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    items.Add(entry);
            }

            return items;
        }

        public static AssetCollection EnumerateAllAssets(string path)
        {
            return new AssetCollection
            {
                Meshes = EnumerateMeshAssets(path),
                SkinnedMeshes = EnumerateSkinnedMeshAssets(path),
                Textures = EnumerateTextureAssets(path),
                Audios = EnumerateAudioAssets(path),
            };
        }

        public static void ExportAssets(this AssetCollection assetCollection, string path, BulkExportSetting settings)
        {
            var commonPath = GetRootPath(ref assetCollection, settings);

            var assetCounts = assetCollection.GetCounts();

            int total = (settings.ExportMeshes ? assetCounts.MeshCount : 0) +
                        (settings.ExportSkinnedMeshes ? assetCounts.SkinnedMeshCount : 0) +
                        (settings.ExportTextures ? assetCounts.TextureCount : 0) +
                        (settings.ExportAudio ? assetCounts.AudioCount : 0);

            var exported = new AssetCount
            {
                MeshCount = 0,
                SkinnedMeshCount = 0,
                TextureCount = 0,
                AudioCount = 0
            };

            FrostyTaskWindow.Show("Bulk Exporting Assets", "", (task) =>
            {
                App.AssetManager.SendManagerCommand("legacy", "SetCacheModeEnabled", true);

                int progress = 0;
                if (settings.ExportMeshes)
                {
                    FBXExporter exporter = new FBXExporter(task);
                    foreach (AssetEntry asset in assetCollection.Meshes)
                    {
                        var ebxAssetEntry = asset as EbxAssetEntry;
                        if (ebxAssetEntry == null)
                            continue;

                        EbxAsset ebxAsset = App.AssetManager.GetEbx(ebxAssetEntry);
                        dynamic meshAsset = (dynamic)ebxAsset.RootObject;

                        ulong resRid = meshAsset.MeshSetResource;
                        ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
                        MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);

                        var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);
                        if (filePath.EndsWith("_mesh"))
                            filePath = filePath.Substring(0, filePath.Length - 5);
                        filePath += '.' + settings.MeshFormat.ToString().ToLower();
                        EnsureDirectoryExists(filePath);

                        exporter.ExportFBX(meshAsset, filePath, settings.MeshVersion.ToString().Replace("FBX_", ""), settings.MeshScale.ToString(), settings.FlattenMesh, settings.SingleLOD, string.Empty, settings.MeshFormat.ToString().ToLower(), meshSet);

                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                        exported.MeshCount++;
                    }
                    App.Logger.Log("Bulk Exported {0} meshAssets to {1}", exported.MeshCount, path);
                }
                if (settings.ExportSkinnedMeshes)
                {
                    FBXExporter exporter = new FBXExporter(task);
                    foreach (AssetEntry asset in assetCollection.SkinnedMeshes)
                    {
                        var ebxAssetEntry = asset as EbxAssetEntry;
                        if (ebxAssetEntry == null)
                            continue;

                        EbxAsset ebxAsset = App.AssetManager.GetEbx(ebxAssetEntry);
                        dynamic meshAsset = (dynamic)ebxAsset.RootObject;

                        ulong resRid = meshAsset.MeshSetResource;
                        ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
                        MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);

                        var filePath = CreateAssetFilePath(path, commonPath, asset.Name, settings.Flatten);
                        if (filePath.EndsWith("_mesh"))
                            filePath = filePath.Substring(0, filePath.Length - 5);
                        filePath += '.' + settings.MeshFormat.ToString().ToLower();
                        EnsureDirectoryExists(filePath);

                        try
                        {
                            exporter.ExportFBX(meshAsset, filePath, settings.MeshVersion.ToString().Replace("FBX_", ""), settings.MeshScale.ToString(), settings.FlattenMesh, settings.SingleLOD, settings.SkeletonPath, settings.MeshFormat.ToString().ToLower(), meshSet);
                            exported.SkinnedMeshCount++;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            App.Logger.LogError("[Skipping] Skeleton miss match for skinnedMeshAsset : {1}", asset.Name);
                        }

                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                    }
                    App.Logger.Log("Bulk Exported {0} skinnedMeshAssets to {1}", exported.SkinnedMeshCount, path);
                }
                if (settings.ExportTextures)
                {
                    foreach (AssetEntry asset in assetCollection.Textures)
                    {
                        //TODO: export texture asset
                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                    }
                    App.Logger.Log("Bulk Exported {0} textureAssets to {1}", exported.TextureCount, path);
                }
                if (settings.ExportAudio)
                {
                    foreach (AssetEntry asset in assetCollection.Audios)
                    {
                        //TODO: export audio asset
                        task.Update(asset.Name, (progress / (double)total) * 100.0);
                        progress++;
                    }
                    App.Logger.Log("Bulk Exported {0} audioAssets to {1}", exported.AudioCount, path);
                }

                App.AssetManager.SendManagerCommand("legacy", "FlushCache");
                App.Logger.Log("Bulk Exported total of {0} assets to {1}", exported.Total, path);
            });

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
            var directory = Path.GetDirectoryName(filePath);
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
