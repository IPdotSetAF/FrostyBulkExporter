using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using BulkExporterPlugin.Windows;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.IO;
using FrostySdk.Managers;

namespace BulkExporterPlugin
{
    public class BulkExportContextMenuItem : DataExplorerContextMenuExtension
    {
        public override string ContextItemName => "Bulk Export";
        public override ImageSource Icon => new ImageSourceConverter().ConvertFromString("pack://application:,,,/BulkExporterPlugin;component/Images/BulkExporter.png") as ImageSource;
        public override DataExplorerTargetContextMenu TargetContextMenu => DataExplorerTargetContextMenu.EXPLORER;

        public override RelayCommand ContextItemClicked => new RelayCommand((o) =>
        {
            string path = App.SelectedPath;
            //EbxAsset asset = App.AssetManager.GetEbx(entry);

            BulkExportWindow win = new BulkExportWindow();
            if (win.ShowDialog() == false)
                return;

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