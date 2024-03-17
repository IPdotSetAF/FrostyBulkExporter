using BulkExporterPlugin.Models;
using Frosty.Core;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkExporterPlugin
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
    }
}
