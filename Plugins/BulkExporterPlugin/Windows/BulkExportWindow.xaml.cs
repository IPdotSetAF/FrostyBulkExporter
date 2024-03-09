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
        private bool ExportMesh = false;
        private bool ExportSkinnedMesh = false;
        private bool ExportTexture = false;
        private bool ExportAudio = false;

        public BulkExportWindow()
        {
            InitializeComponent();
        }

        private void FrostyMessageBox_FrostyLoaded(object sender, EventArgs e)
        {

        }
    }
}
