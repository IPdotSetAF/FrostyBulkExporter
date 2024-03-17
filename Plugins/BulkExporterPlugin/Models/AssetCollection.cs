using FrostySdk.Managers;
using System.Collections.Generic;
using System.Linq;

namespace BulkExporterPlugin.Models
{
    public class AssetCollection
    {
        public IEnumerable<AssetEntry> Meshes { get; set; }
        public IEnumerable<AssetEntry> SkinnedMeshes { get; set; }
        public IEnumerable<AssetEntry> Textures { get; set; }
        public IEnumerable<AssetEntry> Audios { get; set; }

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
