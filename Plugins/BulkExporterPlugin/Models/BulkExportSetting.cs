using BulkExporterPlugin.Enums;

namespace BulkExporterPlugin.Models
{
    public class BulkExportSetting
    {
        public bool Flatten { get; set; } = false;
        public bool ExportMeshes { get; set; } = true;
        public bool ExportSkinnedMeshes { get; set; } = false;
        public bool ExportAudio { get; set; } = false;
        public bool ExportTextures { get; set; } = false;
        public TextureFormat TextureFormat { get; set; } = TextureFormat.PNG;
        public MeshFormat MeshFormat { get; set; } = MeshFormat.FBX;
        public MeshVersion MeshVersion { get; set; } = MeshVersion.FBX_2017;
        public MeshScale MeshScale { get; set; } = MeshScale.Meters;
        public bool FlattenMesh { get; set; } = true;
        public bool SingleLOD { get; set; } = true;
        public bool AdditionalMeshes { get; set; } = false;
        public string SkeletonPath { get; set; }
    }
}
