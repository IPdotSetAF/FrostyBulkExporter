using System.IO;
using System.Linq;
using System.Windows.Media;
using BulkExporterPlugin.Exporters;
using BulkExporterPlugin.Models;
using BulkExporterPlugin.Windows;
using Frosty.Core;
using Microsoft.Win32;

namespace BulkExporterPlugin
{
    public class BulkExportContextMenuItem : DataExplorerContextMenuExtension
    {
        public override string ContextItemName => "Bulk Export";
        public override ImageSource Icon => new ImageSourceConverter().ConvertFromString("pack://application:,,,/BulkExporterPlugin;component/Images/BulkExporter.png") as ImageSource;
        public override DataExplorerTargetContextMenu TargetContextMenu => DataExplorerTargetContextMenu.EXPLORER;

        public override RelayCommand ContextItemClicked => new RelayCommand((o) =>
        {
            string selectedPath = App.EditorWindow.DataExplorer.SelectedPath;

            AssetCollection assets = Exporter.EnumerateAllAssets(selectedPath);
            AssetCount assetCounts = assets.GetCounts();

            BulkExportWindow win = new BulkExportWindow(assetCounts);
            if (win.ShowDialog() == false)
                return;

            var exportConfig = win.ExportSetting;

            var dialog = new SaveFileDialog
            {
                Title = "Select Export Directory",
                Filter = "Directory|*.this.directory",
                FileName = "select"
            };

            string exportPath = "";

            if (dialog.ShowDialog() == true)
            {
                exportPath = dialog.FileName;
                exportPath = exportPath.Replace("\\select.this.directory", "");
                exportPath = exportPath.Replace(".this.directory", "");
                if (!Directory.Exists(exportPath))
                    Directory.CreateDirectory(exportPath);
            }
            else
                return;

            assets.ExportAssets(exportPath + '\\' + selectedPath.Split('/').Last(), exportConfig);

            App.EditorWindow.DataExplorer.RefreshAll();

        });
    }
}