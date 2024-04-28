using FrostySdk.Managers;
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
