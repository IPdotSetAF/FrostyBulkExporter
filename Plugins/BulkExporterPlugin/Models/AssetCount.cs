namespace BulkExporterPlugin.Models
{
    public class AssetCount
    {
         public int MeshCount { get; set; }
         public int SkinnedMeshCount { get; set; }
         public int TextureCount { get; set; }
         public int AudioCount { get; set; }

        public int Total => MeshCount + SkinnedMeshCount + TextureCount + AudioCount;
    }
}
