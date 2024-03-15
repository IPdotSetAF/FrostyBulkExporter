using BulkExporterPlugin.Enums;
using Frosty.Core;
using System;

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

        public string SkeletonPath { get; set; } = "";


        public static BulkExportSetting GetConfig()
        {
            var config = new BulkExportSetting();

            config.Flatten = Config.Get<bool>("BulkExport_Flatten", config.Flatten, ConfigScope.Game);
            config.ExportMeshes = Config.Get<bool>("BulkExport_ExportMeshes", config.ExportMeshes, ConfigScope.Game);
            config.ExportSkinnedMeshes = Config.Get<bool>("BulkExport_ExportSkinnedMeshes", config.ExportSkinnedMeshes, ConfigScope.Game);
            config.ExportAudio = Config.Get<bool>("BulkExport_ExportAudio", config.ExportAudio, ConfigScope.Game);
            config.ExportTextures = Config.Get<bool>("BulkExport_ExportTextures", config.ExportTextures, ConfigScope.Game);

            config.TextureFormat = (TextureFormat)Enum.Parse(typeof(TextureFormat), Config.Get<string>("BulkExport_TextureFormat", config.TextureFormat.ToString(), ConfigScope.Game));

            config.MeshFormat = (MeshFormat)Enum.Parse(typeof(MeshFormat), Config.Get<string>("BulkExport_MeshFormat", config.MeshFormat.ToString(), ConfigScope.Game));
            config.MeshVersion = (MeshVersion)Enum.Parse(typeof(MeshVersion), Config.Get<string>("BulkExport_MeshVersion", config.MeshVersion.ToString(), ConfigScope.Game));
            config.MeshScale = (MeshScale)Enum.Parse(typeof(MeshScale), Config.Get<string>("BulkExport_MeshScale", config.MeshScale.ToString(), ConfigScope.Game));
            config.FlattenMesh = Config.Get<bool>("BulkExport_FlattenMesh", config.FlattenMesh, ConfigScope.Game);
            config.SingleLOD = Config.Get<bool>("BulkExport_SingleLOD", config.SingleLOD, ConfigScope.Game);
            config.AdditionalMeshes = Config.Get<bool>("BulkExport-AdditionalMeshes", config.AdditionalMeshes, ConfigScope.Game);

            config.SkeletonPath = Config.Get<string>("BulkExport_SkeletonPath", config.SkeletonPath, ConfigScope.Game);

            return config;
        }

        public static void StoreConfig(BulkExportSetting config)
        {
            Config.Add("BulkExport_Flatten", config.Flatten, ConfigScope.Game);
            Config.Add("BulkExport_ExportMeshes", config.ExportMeshes, ConfigScope.Game);
            Config.Add("BulkExport_ExportSkinnedMeshes", config.ExportSkinnedMeshes, ConfigScope.Game);
            Config.Add("BulkExport_ExportAudio", config.ExportAudio, ConfigScope.Game);
            Config.Add("BulkExport_ExportTextures", config.ExportTextures, ConfigScope.Game);

            Config.Add("BulkExport_TextureFormat", config.TextureFormat.ToString(), ConfigScope.Game);

            Config.Add("BulkExport_MeshFormat", config.MeshFormat.ToString(), ConfigScope.Game);
            Config.Add("BulkExport_MeshVersion", config.MeshVersion.ToString(), ConfigScope.Game);
            Config.Add("BulkExport_MeshScale", config.MeshScale.ToString(), ConfigScope.Game);
            Config.Add("BulkExport_FlattenMesh", config.FlattenMesh, ConfigScope.Game);
            Config.Add("BulkExport_SingleLOD", config.SingleLOD, ConfigScope.Game);
            Config.Add("BulkExport-AdditionalMeshes", config.AdditionalMeshes, ConfigScope.Game);

            Config.Add("BulkExport_SkeletonPath", config.SkeletonPath, ConfigScope.Game);
        }
    }
}
