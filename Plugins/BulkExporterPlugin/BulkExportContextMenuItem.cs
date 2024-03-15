using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using BulkExporterPlugin.Windows;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Legacy;
using Frosty.Core.Windows;
using FrostySdk.IO;
using FrostySdk.Managers;
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
            string selectedPath = App.SelectedPath;

            BulkExportWindow win = new BulkExportWindow();
            if (win.ShowDialog() == false)
                return;

            var exportConfig = win.ExportSetting;

            var dialog = new SaveFileDialog
            {
                Title = "Select Export Directory",
                Filter = "Directory|*.this.directory",
                FileName = "select"
            };

            if (dialog.ShowDialog() == true)
            {
                string exportPath = dialog.FileName;
                exportPath = exportPath.Replace("\\select.this.directory", "");
                exportPath = exportPath.Replace(".this.directory", "");
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
            }

            //EbxAsset asset = App.AssetManager.GetEbx(entry);
            //LegacyFileEntry selectedAsset = legacyExplorer.SelectedAssets[0] as LegacyFileEntry;


            //if (ofd.ShowDialog() == true)
            //{
            //    IList<AssetEntry> assets = legacyExplorer.SelectedAssets;
            //    FrostyTaskWindow.Show("Exporting Legacy Assets", "", (task) =>
            //    {
            //        App.AssetManager.SendManagerCommand("legacy", "SetCacheModeEnabled", true);
            //        FileInfo fi = new FileInfo(sfd.FileName);

            //        int progress = 0;
            //        foreach (LegacyFileEntry asset in assets)
            //        {
            //            task.Update(asset.Name, (progress / (double)assets.Count) * 100.0);
            //            progress++;

            //            string outFileName = fi.Directory.FullName + "\\" + asset.Filename + "." + asset.Type;
            //            using (NativeWriter writer = new NativeWriter(new FileStream(outFileName, FileMode.Create)))
            //                writer.Write(new NativeReader(App.AssetManager.GetCustomAsset("legacy", asset)).ReadToEnd());
            //        }

            //        App.Logger.Log("Legacy files saved to {0}", fi.Directory.FullName);
            //        App.AssetManager.SendManagerCommand("legacy", "FlushCache");
            //    });
            //}


            //string newName = win.SelectedPath + "/" + win.SelectedName;
            //newName = newName.Trim('/');

            //Type newType = win.SelectedType;
            //FrostyTaskWindow.Show("Duplicating asset", "", (task) =>
            //{
            //    if (!MeshVariationDb.IsLoaded)
            //        MeshVariationDb.LoadVariations(task);

            //    try
            //    {
            //        string key = "null";
            //        foreach (string typekey in extensions.Keys)
            //        {
            //            if (TypeLibrary.IsSubClassOf(entry.Type, typekey))
            //            {
            //                key = typekey;
            //                break;
            //            }
            //        }

            //        task.Update("Duplicating asset...");
            //        extensions[key].DuplicateAsset(entry, newName, newType != null, newType);
            //    }
            //    catch (Exception e)
            //    {
            //        App.Logger.Log($"Failed to duplicate {entry.Name}");
            //    }
            //});

            App.EditorWindow.DataExplorer.RefreshAll();
        });

        //public void DuplicateContextMenuItem()
        //{
        //    foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        //    {
        //        if (type.IsSubclassOf(typeof(DuplicateAssetExtension)))
        //        {
        //            var extension = (DuplicateAssetExtension)Activator.CreateInstance(type);
        //            extensions.Add(extension.AssetType, extension);
        //        }
        //    }
        //    extensions.Add("null", new DuplicateAssetExtension());
        //}

    }
}