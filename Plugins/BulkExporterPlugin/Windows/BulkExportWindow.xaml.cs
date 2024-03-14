using BulkExporterPlugin.Enums;
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
        #region --Flatten--
        private bool _flatten = false;
        public bool Flatten
        {
            get => _flatten;
            set => _flatten = value;
        }
        #endregion

        #region --ExportMesh--
        private bool _exportMesh = false;
        public bool ExportMesh
        {
            get => _exportMesh;
            set
            {
                _exportMesh = value;
                OnPropertyChanged(nameof(IsMeshSettingVisible));
            }
        }
        #endregion

        #region --ExportSkinnedMesh--
        private bool _exportSkinnedMesh = false;
        public bool ExportSkinnedMesh
        {
            get => _exportSkinnedMesh;
            set
            {
                _exportSkinnedMesh = value;
                OnPropertyChanged(nameof(IsMeshSettingVisible));
            }
        }
        #endregion

        #region --ExportTexture--
        private bool _exportTexture = false;
        public bool ExportTexture
        {
            get => _exportTexture;
            set => _exportTexture = value;
        }
        #endregion

        #region --ExportAudio--
        private bool _exportAudio = false;
        public bool ExportAudio
        {
            get => _exportAudio;
            set => _exportAudio = value;
        }
        #endregion

        #region --IsMeshSettingVisible--
        public bool IsMeshSettingVisible
        {
            get => _exportMesh || _exportSkinnedMesh;
        }
        #endregion

        #region --MeshExportFormat--
        private MeshFormat _meshExportFormat = MeshFormat.FBX;
        public MeshFormat MeshExportFormat
        {
            get => _meshExportFormat;
            set => _meshExportFormat = value;
        }
        #endregion

        #region --FbxVersion--
        private MeshExportVersion _fbxVersion = MeshExportVersion.FBX_2017;
        public MeshExportVersion FbxVersion
        {
            get => _fbxVersion;
            set => _fbxVersion = value;
        }
        #endregion

        #region --MeshScale--
        private MeshExportScale _meshScale = MeshExportScale.Meters;
        public MeshExportScale MeshScale
        {
            get => _meshScale;
            set => _meshScale = value;
        }
        #endregion

        #region --FlattenMesh--
        private bool _flattenMesh = false;
        public bool FlattenMesh
        {
            get => _flattenMesh;
            set => _flattenMesh = value;
        }
        #endregion

        #region --SingleLOD--
        private bool _singleLOD = false;
        public bool SingleLOD
        {
            get => _singleLOD;
            set => _singleLOD = value;
        }
        #endregion

        #region --AdditionalMeshes--
        private bool _additionalMeshes = false;
        public bool AdditionalMeshes
        {
            get => _additionalMeshes;
            set => _additionalMeshes = value;
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
            InitializeComponent();
            DataContext = this;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
