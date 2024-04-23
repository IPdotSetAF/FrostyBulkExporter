namespace BulkExporterPlugin.Models
{
    public class AssetCount
    {
        public int MeshCount { get; set; } = 0;
        public int SkinnedMeshCount { get; set; } = 0;
        public int TextureCount { get; set; } = 0;
        public int AudioCount { get; set; } = 0;

        public int Total => MeshCount + SkinnedMeshCount + TextureCount + AudioCount;
    }
}
