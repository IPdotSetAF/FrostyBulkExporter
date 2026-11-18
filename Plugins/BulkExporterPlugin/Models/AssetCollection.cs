using FrostySdk.Managers.Entries;
using System.Collections.Generic;
using System.Linq;

namespace BulkExporterPlugin.Models
{
    public class AssetCollection
    {
        public IEnumerable<EbxAssetEntry> Meshes { get; set; }
        public IEnumerable<EbxAssetEntry> SkinnedMeshes { get; set; }
        public IEnumerable<EbxAssetEntry> Textures { get; set; }
        public IEnumerable<EbxAssetEntry> Audios { get; set; }

        public static AssetCollection Empty() => new AssetCollection
        {
            Meshes = new List<EbxAssetEntry>(),
            SkinnedMeshes = new List<EbxAssetEntry>(),
            Textures = new List<EbxAssetEntry>(),
            Audios = new List<EbxAssetEntry>(),
        };

        public AssetCount GetCounts()
        {
            return new AssetCount
            {
                MeshCount = Meshes.Count(),
                SkinnedMeshCount = SkinnedMeshes.Count(),
                TextureCount = Textures.Count(),
                AudioCount = Audios.Count(),
            };
        }
    }
}
