using BulkExporterPlugin.Enums;
using BulkExporterPlugin.Models;
using Frosty.Controls;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BulkExporterPlugin.Windows
{
    /// <summary>
    /// Interaction logic for BulkExportWindow.xaml
    /// </summary>
    public partial class BulkExportWindow : FrostyMessageBox, INotifyPropertyChanged
    {
        private BulkExportSetting _setting;

        #region --Flatten--
        public bool Flatten
        {
            get => _setting.Flatten;
            set => _setting.Flatten = value;
        }
        #endregion

        #region --ExportMesh--
        public bool ExportMesh
        {
            get => _setting.ExportMeshes;
            set
            {
                _setting.ExportMeshes = value;
                OnPropertyChanged(nameof(IsMeshSettingVisible));
            }
        }
        #endregion

        #region --ExportSkinnedMesh--
        public bool ExportSkinnedMesh
        {
            get => _setting.ExportSkinnedMeshes;
            set
            {
                _setting.ExportSkinnedMeshes = value;
                OnPropertyChanged(nameof(IsMeshSettingVisible));
            }
        }
        #endregion

        #region --ExportTexture--
        public bool ExportTexture
        {
            get => _setting.ExportTextures;
            set => _setting.ExportTextures = value;
        }
        #endregion

        #region --ExportAudio--
        public bool ExportAudio
        {
            get => _setting.ExportAudio;
            set => _setting.ExportAudio = value;
        }
        #endregion

        #region --TextureExportFormat--
        public TextureFormat TextureExportFormat
        {
            get => _setting.TextureFormat;
            set => _setting.TextureFormat = value;
        }
        #endregion

        #region --IsMeshSettingVisible--
        public bool IsMeshSettingVisible
        {
            get => _setting.ExportMeshes || _setting.ExportSkinnedMeshes;
        }
        #endregion

        #region --MeshExportFormat--
        public MeshFormat MeshExportFormat
        {
            get => _setting.MeshFormat;
            set => _setting.MeshFormat = value;
        }
        #endregion

        #region --FbxVersion--
        public MeshVersion FbxVersion
        {
            get => _setting.MeshVersion;
            set => _setting.MeshVersion = value;
        }
        #endregion

        #region --MeshScale--
        public MeshScale MeshScale
        {
            get => _setting.MeshScale;
            set => _setting.MeshScale = value;
        }
        #endregion

        #region --FlattenMesh--
        public bool FlattenMesh
        {
            get => _setting.FlattenMesh;
            set => _setting.FlattenMesh = value;
        }
        #endregion

        #region --SingleLOD--
        public bool SingleLOD
        {
            get => _setting.SingleLOD;
            set => _setting.SingleLOD = value;
        }
        #endregion

        #region --AdditionalMeshes--
        public bool AdditionalMeshes
        {
            get => _setting.AdditionalMeshes;
            set => _setting.AdditionalMeshes = value;
        }
        #endregion

        #region NotifyChanges
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

        public BulkExportWindow()
        {
            _setting = BulkExportSetting.GetConfig();

            InitializeComponent();
            DataContext = this;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            BulkExportSetting.StoreConfig(_setting);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
