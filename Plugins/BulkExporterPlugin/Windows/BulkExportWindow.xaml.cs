using Frosty.Controls;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
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
    public partial class BulkExportWindow : FrostyMessageBox
    {
        private bool _exportMesh = false;
        public bool ExportMesh
        {
            get => _exportMesh;
            set => _exportMesh = value;
        }

        private bool _exportSkinnedMesh = false;
        public bool ExportSkinnedMesh
        {
            get => _exportSkinnedMesh;
            set => _exportSkinnedMesh = value;
        }

        private bool _exportTexture = false;
        public bool ExportTexture
        {
            get => _exportTexture;
            set => _exportTexture = value;
        }

        private bool _exportAudio = false;
        public bool ExportAudio
        {
            get => _exportAudio;
            set => _exportAudio = value;
        }

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
