using FrostySdk.Interfaces;
using FrostySdk.Managers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Frosty.Core.Controls;
using Frosty.Core;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;
using BulkExporterPlugin.Exporters;
using BulkExporterPlugin.Models;
using BulkExporterPlugin.Windows;
using Microsoft.Win32;

namespace BulkExporterPlugin.Editors
{
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

        private const string PART_SelectedDataExplorer = "PART_SelectedDataExplorer";
        private const string PART_DataExplorer = "PART_DataExplorer";
        private const string PART_Texture = "PART_Texture";
        private const string PART_Mesh = "PART_Mesh";
        private const string PART_SkinnedMesh = "PART_SkinnedMesh";
        private const string PART_Audio = "PART_Audio";
        private const string PART_ExportButton = "PART_ExportButton";

        private List<string> _included_paths = new List<string>();
        private List<string> _excluded_paths = new List<string>();

        private string[] _assetTypefilter;
        private IEnumerable<EbxAssetEntry> _allAssets = Array.Empty<EbxAssetEntry>();
        private IEnumerable<EbxAssetEntry> _selectedAssets = Array.Empty<EbxAssetEntry>();

        private FrostyDataExplorer _selectedDataExplorer;
        private FrostyDataExplorer _allDataExplorer;
        private CheckBox _meshCheck;
        private CheckBox _textureCheck;
        private CheckBox _audioCheck;
        private CheckBox _skinnedMeshCheck;
        private Button _exportButton;
        private ILogger _logger;

        static BulkExporter()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BulkExporter), new FrameworkPropertyMetadata(typeof(BulkExporter)));
        }

        public BulkExporter(ILogger logger = null)
        {
            this._logger = logger;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _selectedDataExplorer = GetTemplateChild(PART_SelectedDataExplorer) as FrostyDataExplorer;
            _allDataExplorer = GetTemplateChild(PART_DataExplorer) as FrostyDataExplorer;
            _meshCheck = GetTemplateChild(PART_Mesh) as CheckBox;
            _textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            _skinnedMeshCheck = GetTemplateChild(PART_SkinnedMesh) as CheckBox;
            _audioCheck = GetTemplateChild(PART_Audio) as CheckBox;
            _textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            _exportButton = GetTemplateChild(PART_ExportButton) as Button;

            _meshCheck.Click += filterChecks_Checked;
            _skinnedMeshCheck.Click += filterChecks_Checked;
            _textureCheck.Click += filterChecks_Checked;
            _audioCheck.Click += filterChecks_Checked;
            _exportButton.Click += ExportButton_Click;

            _selectedDataExplorer.ExplorerContextMenu = new ContextMenu();
            _selectedDataExplorer.ExplorerContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("ExcludeRecursive.png"),
                Header = "Exclude",
                Command = ExcludeClick
            });
            _selectedDataExplorer.AssetContextMenu = new ContextMenu();
            _selectedDataExplorer.AssetContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("Exclude.png"),
                Header = "Exclude",
                Command = ExcludeAssetClick
            });

            _allDataExplorer.ExplorerContextMenu = new ContextMenu();
            _allDataExplorer.ExplorerContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("IncludeRecursive.png"),
                Header = "Include",
                Command = IncludeClick
            });
            _allDataExplorer.AssetContextMenu = new ContextMenu();
            _allDataExplorer.AssetContextMenu.Items.Add(new MenuItem
            {
                Icon = GetImage("Include.png"),
                Header = "Include",
                Command = IncludeAssetClick
            });

            Loaded += BulkExporter_Loaded;
        }

        private void BulkExporter_Loaded(object sender, RoutedEventArgs e)
        {
            if (_allDataExplorer.ItemsSource != null)
                return;

            FilterUpdated();

            ReloadAllAssets();
            _selectedDataExplorer.ItemsSource = _selectedAssets;
        }

        private RelayCommand ExcludeClick => new RelayCommand((o) =>
        {
            var path = _selectedDataExplorer.SelectedPath;

            _included_paths.RemoveAll(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (!_excluded_paths.Contains(path))
                _excluded_paths.Add(path);

            ReloadSelectedAssets();
        });

        private RelayCommand ExcludeAssetClick => new RelayCommand((o) =>
        {
            var asset = _selectedDataExplorer.SelectedAsset as EbxAssetEntry;
            string path = asset.Name;

            if (_excluded_paths.Contains(path))
                return;
            _included_paths.Remove(path);
            _excluded_paths.Add(path);

            ReloadSelectedAssets();
        });

        private RelayCommand IncludeClick => new RelayCommand((o) =>
        {
            var path = _allDataExplorer.SelectedPath;

            _excluded_paths.RemoveAll(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            if (!_included_paths.Contains(path))
                _included_paths.Add(path);

            ReloadSelectedAssets();
        });

        private RelayCommand IncludeAssetClick => new RelayCommand((o) =>
        {
            var asset = _allDataExplorer.SelectedAsset as EbxAssetEntry;
            string path = asset.Name;

            if (_included_paths.Contains(path))
                return;
            _excluded_paths.Remove(path);
            _included_paths.Add(path);

            ReloadSelectedAssets();
        });

        private void filterChecks_Checked(object sender, RoutedEventArgs e) => FilterUpdated();

        public void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            AssetCollection assets = Exporter.EnumerateAllAssets(_included_paths.ToArray(), _excluded_paths.ToArray());
            AssetCount assetCounts = assets.GetCounts();

            BulkExportWindow win = new BulkExportWindow(assetCounts);
            win.ExportSetting.ExportMeshes = _meshCheck.IsChecked == true;
            win.ExportSetting.ExportSkinnedMeshes = _skinnedMeshCheck.IsChecked == true;
            win.ExportSetting.ExportTextures = _textureCheck.IsChecked == true;
            win.ExportSetting.ExportAudio = _audioCheck.IsChecked == true;
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

        private Image GetImage(string name) => new Image()
        {
            Source = new ImageSourceConverter().ConvertFromString($"pack://application:,,,/BulkExporterPlugin;component/Images/{name}") as ImageSource
        };

        private void ReloadAllAssets()
        {
            var collection = Exporter.EnumerateAllAssets("/");

            _allAssets = collection.Meshes
                .Concat(collection.SkinnedMeshes)
                .Concat(collection.Textures)
                .Concat(collection.Audios);

            _allDataExplorer.ItemsSource = FilterAssets(_allAssets, _assetTypefilter);
        }

        private void ReloadSelectedAssets()
        {
            var collection = Exporter.EnumerateAllAssets(_included_paths.ToArray(), _excluded_paths.ToArray());

            _selectedAssets = collection.Meshes
                .Concat(collection.SkinnedMeshes)
                .Concat(collection.Textures)
                .Concat(collection.Audios);

            _selectedDataExplorer.ItemsSource = FilterAssets(_selectedAssets, _assetTypefilter);
        }

        private void FilterUpdated()
        {
            IEnumerable<string> types = Array.Empty<string>();

            if (_meshCheck.IsChecked == true)
                types = types.Concat(Exporter.MeshAssetTypes);
            if (_skinnedMeshCheck.IsChecked == true)
                types = types.Concat(Exporter.SkinnedMeshAssetTypes);
            if (_textureCheck.IsChecked == true)
                types = types.Concat(Exporter.TextureAssetTypes);
            if (_audioCheck.IsChecked == true)
                types = types.Concat(Exporter.AudioAssetTypes);

            _assetTypefilter = types.ToArray();

            _allDataExplorer.ItemsSource = FilterAssets(_allAssets, _assetTypefilter);
            _selectedDataExplorer.ItemsSource = FilterAssets(_selectedAssets, _assetTypefilter);
        }

        private IEnumerable<EbxAssetEntry> FilterAssets(IEnumerable<EbxAssetEntry> assets, string[] assetTypes) =>
            assets.Where(asset => assetTypes.Contains(asset.Type));
    }
}
