using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using Frosty.Core;
using System.Windows.Media;
using FrostySdk;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using BulkExporterPlugin.Exporters;
using BulkExporterPlugin.Models;
using BulkExporterPlugin.Windows;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Collections;

namespace BulkExporterPlugin.Editors
{
    [TemplatePart(Name = PART_IncludedPathsList, Type = typeof(ListBox))]
    [TemplatePart(Name = PART_ExcludedPathsList, Type = typeof(ListBox))]
    [TemplatePart(Name = PART_SelectedDataExplorer, Type = typeof(FrostyDataExplorer))]
    [TemplatePart(Name = PART_DataExplorer, Type = typeof(FrostyDataExplorer))]
    [TemplatePart(Name = PART_Mesh, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_SkinnedMesh, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Audio, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Texture, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_ExportButton, Type = typeof(Button))]
    public class BulkExporter : FrostyBaseEditor
    {
        public override ImageSource Icon => BulkExporterMenuExtension.imageSource;

        private const string PART_IncludedPathsList = "PART_IncludedPathsList";
        private const string PART_ExcludedPathsList = "PART_ExcludedPathsList";
        private const string PART_SelectedDataExplorer = "PART_SelectedDataExplorer";
        private const string PART_DataExplorer = "PART_DataExplorer";
        private const string PART_Texture = "PART_Texture";
        private const string PART_Mesh = "PART_Mesh";
        private const string PART_SkinnedMesh = "PART_SkinnedMesh";
        private const string PART_Audio = "PART_Audio";
        private const string PART_ExportButton = "PART_ExportButton";


        private List<string> _included_paths = new List<string>();
        private List<string> _excluded_paths = new List<string>();

        private ListBox includedPathsListBox;
        private ListBox excludedPathsListBox;
        private FrostyDataExplorer selectedDataExplorer;
        private FrostyDataExplorer dataExplorer;
        private CheckBox meshCheck;
        private CheckBox textureCheck;
        private CheckBox audioCheck;
        private CheckBox skinnedMeshCheck;
        private Button exportButton;
        private ILogger logger;

        static BulkExporter()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BulkExporter), new FrameworkPropertyMetadata(typeof(BulkExporter)));
        }

        public BulkExporter(ILogger logger = null)
        {
            this.logger = logger;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            includedPathsListBox = GetTemplateChild(PART_IncludedPathsList) as ListBox;
            excludedPathsListBox = GetTemplateChild(PART_ExcludedPathsList) as ListBox;
            selectedDataExplorer = GetTemplateChild(PART_SelectedDataExplorer) as FrostyDataExplorer;
            dataExplorer = GetTemplateChild(PART_DataExplorer) as FrostyDataExplorer;
            meshCheck = GetTemplateChild(PART_Mesh) as CheckBox;
            textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            skinnedMeshCheck = GetTemplateChild(PART_SkinnedMesh) as CheckBox;
            audioCheck = GetTemplateChild(PART_Audio) as CheckBox;
            textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            exportButton = GetTemplateChild(PART_ExportButton) as Button;

            meshCheck.Click += filterChecks_Checked;
            skinnedMeshCheck.Click += filterChecks_Checked;
            textureCheck.Click += filterChecks_Checked;
            audioCheck.Click += filterChecks_Checked;
            exportButton.Click += ExportButton_Click;

            selectedDataExplorer.ExplorerContextMenu = new ContextMenu();
            selectedDataExplorer.ExplorerContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("ExcludeRecursive.png"),
                Header = "Exclude",
                Command = ExcludeClick
            });
            selectedDataExplorer.AssetContextMenu = new ContextMenu();
            selectedDataExplorer.AssetContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("Exclude.png"),
                Header = "Exclude",
                Command = ExcludeAssetClick
            });

            dataExplorer.ExplorerContextMenu = new ContextMenu();
            dataExplorer.ExplorerContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("IncludeRecursive.png"),
                Header = "Include",
                Command = IncludeClick
            });
            dataExplorer.AssetContextMenu = new ContextMenu();
            dataExplorer.AssetContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("Include.png"),
                Header = "Include",
                Command = IncludeAssetClick
            });

            Loaded += BulkExporter_Loaded;
        }

        private void BulkExporter_Loaded(object sender, RoutedEventArgs e)
        {
            if (dataExplorer.ItemsSource != null)
                return;

            dataExplorer.ItemsSource = loadAllAssets();
            selectedDataExplorer.ItemsSource = Array.Empty<EbxAssetEntry>();
        }

        private RelayCommand ExcludeClick => new RelayCommand((o) =>
        {
            var path = selectedDataExplorer.SelectedPath;

            _included_paths.RemoveAll(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (!_excluded_paths.Contains(path))
                _excluded_paths.Add(path);

            UpdatePathLists();
            selectedDataExplorer.ItemsSource = LoadSelectedAssets();
        });

        private RelayCommand ExcludeAssetClick => new RelayCommand((o) =>
        {
            var asset = selectedDataExplorer.SelectedAsset as EbxAssetEntry;
            string path = asset.Name;

            if (_excluded_paths.Contains(path))
                return;
            _included_paths.Remove(path);
            _excluded_paths.Add(path);

            UpdatePathLists();
            selectedDataExplorer.ItemsSource = LoadSelectedAssets();
        });

        private RelayCommand IncludeClick => new RelayCommand((o) =>
        {
            var path = dataExplorer.SelectedPath;

            _excluded_paths.RemoveAll(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (!_included_paths.Contains(path))
                _included_paths.Add(path);

            UpdatePathLists();
            selectedDataExplorer.ItemsSource = LoadSelectedAssets();
        });

        private RelayCommand IncludeAssetClick => new RelayCommand((o) =>
        {
            var asset = dataExplorer.SelectedAsset as EbxAssetEntry;
            string path = asset.Name;

            if (_included_paths.Contains(path))
                return;
            _excluded_paths.Remove(path);
            _included_paths.Add(path);

            UpdatePathLists();
            selectedDataExplorer.ItemsSource = LoadSelectedAssets();
        });

        private void filterChecks_Checked(object sender, RoutedEventArgs e)
        {
            dataExplorer.ItemsSource = loadAllAssets();
            selectedDataExplorer.ItemsSource = LoadSelectedAssets();
        }

        public void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            AssetCollection assets = Exporter.EnumerateAllAssets(_included_paths.ToArray(), _excluded_paths.ToArray());
            AssetCount assetCounts = assets.GetCounts();

            BulkExportWindow win = new BulkExportWindow(assetCounts);
            win.ExportSetting.ExportMeshes = meshCheck.IsChecked == true;
            win.ExportSetting.ExportSkinnedMeshes = skinnedMeshCheck.IsChecked == true;
            win.ExportSetting.ExportTextures = textureCheck.IsChecked == true;
            win.ExportSetting.ExportAudio = audioCheck.IsChecked == true;
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

            assets.ExportAssets(exportPath, exportConfig);

            App.EditorWindow.DataExplorer.RefreshAll();
        }

        private Image GetImage(string name)
        {
            return new Image()
            {
                Source = new ImageSourceConverter().ConvertFromString($"pack://application:,,,/BulkExporterPlugin;component/Images/{name}") as ImageSource
            };
        }

        private IEnumerable<EbxAssetEntry> loadAllAssets()
        {
            var collection = Exporter.EnumerateAllAssets("/");
            IEnumerable<EbxAssetEntry> assets = new List<EbxAssetEntry>();

            if (meshCheck.IsChecked == true)
                assets = assets.Concat(collection.Meshes);
            if (skinnedMeshCheck.IsChecked == true)
                assets = assets.Concat(collection.SkinnedMeshes);
            if (textureCheck.IsChecked == true)
                assets = assets.Concat(collection.Textures);
            if (audioCheck.IsChecked == true)
                assets = assets.Concat(collection.Audios);

            return assets;
        }

        private IEnumerable<EbxAssetEntry> LoadSelectedAssets()
        {
            var collection = Exporter.EnumerateAllAssets(_included_paths.ToArray(), _excluded_paths.ToArray());
            IEnumerable<EbxAssetEntry> assets = new List<EbxAssetEntry>();

            if (meshCheck.IsChecked == true)
                assets = assets.Concat(collection.Meshes);
            if (skinnedMeshCheck.IsChecked == true)
                assets = assets.Concat(collection.SkinnedMeshes);
            if (textureCheck.IsChecked == true)
                assets = assets.Concat(collection.Textures);
            if (audioCheck.IsChecked == true)
                assets = assets.Concat(collection.Audios);

            return assets;
        }

        private void UpdatePathLists()
        {
            includedPathsListBox.Items.Clear();
            foreach (var path in _included_paths)
                includedPathsListBox.Items.Add(path);

            excludedPathsListBox.Items.Clear();
            foreach (var path in _excluded_paths)
                excludedPathsListBox.Items.Add(path);
        }
    }
}
