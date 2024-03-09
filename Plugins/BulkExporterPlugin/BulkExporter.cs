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

namespace BulkExporterPlugin
{
    [TemplatePart(Name = PART_AssetTreeView, Type = typeof(TreeView))]
    [TemplatePart(Name = PART_DataExplorer, Type = typeof(FrostyDataExplorer))]
    [TemplatePart(Name = PART_AssetFilter, Type = typeof(TextBox))]
    [TemplatePart(Name = PART_Flatten, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Mesh, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_SkinnedMesh, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Audio, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Texture, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_ExportButton, Type = typeof(Button))]
    public class BulkExporter : FrostyBaseEditor
    {
        public override ImageSource Icon => BulkExporterMenuExtension.imageSource;

        private const string PART_AssetTreeView = "PART_AssetTreeView";
        private const string PART_DataExplorer = "PART_DataExplorer";
        private const string PART_AssetFilter = "PART_AssetFilter";
        private const string PART_Flatten = "PART_Flatten";
        private const string PART_Texture = "PART_Texture";
        private const string PART_Mesh = "PART_Mesh";
        private const string PART_SkinnedMesh = "PART_SkinnedMesh";
        private const string PART_Audio = "PART_Audio";
        private const string PART_ExportButton = "PART_ExportButton";

        private TreeView AssetTreeView;
        private FrostyDataExplorer dataExplorer;
        private TextBox assetFilterTextBox;
        private CheckBox flattenCheck;
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

        public BulkExporter(ILogger inLogger = null)
        {
            logger = inLogger;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            AssetTreeView = GetTemplateChild(PART_AssetTreeView) as TreeView;
            dataExplorer = GetTemplateChild(PART_DataExplorer) as FrostyDataExplorer;
            assetFilterTextBox = GetTemplateChild(PART_AssetFilter) as TextBox;
            flattenCheck = GetTemplateChild(PART_Flatten) as CheckBox;
            meshCheck = GetTemplateChild(PART_Mesh) as CheckBox;
            textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            skinnedMeshCheck = GetTemplateChild(PART_SkinnedMesh) as CheckBox;
            audioCheck = GetTemplateChild(PART_Audio) as CheckBox;
            textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            exportButton = GetTemplateChild(PART_ExportButton) as Button;
            exportButton.Click += ExportButton_Click;

            dataExplorer.SelectionChanged += ResExplorer_SelectionChanged;

            Loaded += BulkExporter_Loaded;
            assetFilterTextBox.LostFocus += AssetFilterTextBox_LostFocus;
            assetFilterTextBox.KeyUp += AssetFilterTextBox_KeyUp;
        }
        private void BulkExporter_Loaded(object sender, RoutedEventArgs e)
        {
            if (dataExplorer.ItemsSource != null)
                return;

            dataExplorer.ItemsSource = App.AssetManager.EnumerateEbx();
            UpdateTreeView();
        }

        public void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            logger.Log("Export Clicked");


            //ResAssetEntry selectedAsset = resExplorer.SelectedAsset as ResAssetEntry;
            //FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save Resource", "*.res (Resource Files)|*.res", "Res", selectedAsset.Filename);

            //Stream resStream = App.AssetManager.GetRes(selectedAsset);
            //if (resStream == null)
            //    return;

            //if (sfd.ShowDialog())
            //{
            //    FrostyTaskWindow.Show("Exporting Asset", "", (task) =>
            //    {
            //        using (NativeWriter writer = new NativeWriter(new FileStream(sfd.FileName, FileMode.Create)))
            //        {
            //            // write res meta first
            //            writer.Write(selectedAsset.ResMeta);

            //            // followed by remaining data
            //            using (NativeReader reader = new NativeReader(resStream))
            //                writer.Write(reader.ReadToEnd());
            //        }
            //    });
            //    logger?.Log("Resource saved to {0}", sfd.FileName);
            //}
        }

        private void ResExplorer_SelectionChanged(object sender, RoutedEventArgs e)
        {
            logger.Log("Selection Changed");
            //if (resExplorer.SelectedAsset != null)
            //{
            //    resBundleBox.Items.Clear();
            //    ResAssetEntry SelectedRes = (ResAssetEntry)resExplorer.SelectedAsset;
            //    resBundleBox.Items.Add("Selected resource is in Bundles: ");
            //    foreach (int bundle in SelectedRes.Bundles)
            //    {
            //        resBundleBox.Items.Add(App.AssetManager.GetBundleEntry(bundle).Name);
            //    }
            //    if (SelectedRes.AddedBundles.Count != 0)
            //    {
            //        resBundleBox.Items.Add("Added to Bundles:");
            //        foreach (int bundle in SelectedRes.AddedBundles)
            //        {
            //            resBundleBox.Items.Add(App.AssetManager.GetBundleEntry(bundle).Name);
            //        }
            //    }
            //}
            //else
            //{
            //    resBundleBox.Items.Clear();
            //    resBundleBox.Items.Add("No res selected");
            //}
        }

        private void AssetFilterTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                UpdateFilter();
        }

        private void AssetFilterTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateFilter();
        }

        private void UpdateFilter()
        {
            //if (assetFilterTextBox.Text == "" && meshCheck.IsChecked == false && textureCheck.IsChecked == false)
            //{
            //    chunksListBox.Items.Filter = null;
            //    return;
            //}
            //else if (assetFilterTextBox.Text != "" && meshCheck.IsChecked == false && textureCheck.IsChecked == false)
            //{
            //    chunksListBox.Items.Filter = new Predicate<object>((object a) => ((ChunkAssetEntry)a).Id.ToString().Contains(assetFilterTextBox.Text.ToLower()));
            //}
            //else if (assetFilterTextBox.Text == "" && meshCheck.IsChecked == false && textureCheck.IsChecked == false)
            //{
            //    chunksListBox.Items.Filter = new Predicate<object>((object a) => ((ChunkAssetEntry)a).IsModified);
            //}
            //else if (assetFilterTextBox.Text != "" && meshCheck.IsChecked == false && textureCheck.IsChecked == false)
            //{
            //    chunksListBox.Items.Filter = new Predicate<object>((object a) => (((ChunkAssetEntry)a).IsModified) & ((ChunkAssetEntry)a).Id.ToString().Contains(assetFilterTextBox.Text.ToLower()));
            //}
        }

        private Dictionary<string, AssetPath> assetPathMapping = new Dictionary<string, AssetPath>(StringComparer.OrdinalIgnoreCase);

        private void UpdateTreeView()
        {
            var ItemsSource = App.AssetManager.EnumerateEbx();

            AssetPath root = new AssetPath("", "", null);
            foreach (AssetEntry entry in ItemsSource)
            {
                //if (!FilterText(entry.Name, entry))
                //    continue;

                string[] arr = entry.Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                AssetPath next = root;

                foreach (string path in arr)
                {
                    bool bFound = false;
                    foreach (AssetPath child in next.Children)
                    {
                        if (child.PathName.Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            if (path.ToCharArray().Any(char.IsUpper))
                                child.UpdatePathName(path);

                            next = child;
                            bFound = true;
                            break;
                        }
                    }

                    if (!bFound)
                    {
                        string fullPath = next.FullPath + "/" + path;
                        AssetPath newPath = null;

                        if (!assetPathMapping.ContainsKey(fullPath))
                        {
                            newPath = new AssetPath(path, fullPath, next);
                            assetPathMapping.Add(fullPath, newPath);
                        }
                        else
                        {
                            newPath = assetPathMapping[fullPath];
                            newPath.Children.Clear();
                        }

                        next.Children.Add(newPath);
                        next = newPath;
                    }
                }
            }

            if (!assetPathMapping.ContainsKey("/"))
                assetPathMapping.Add("/", new AssetPath("![root]", "", null, true));
            root.Children.Insert(0, assetPathMapping["/"]);

            AssetTreeView.ItemsSource = root.Children;
            AssetTreeView.Items.SortDescriptions.Add(new SortDescription("PathName", ListSortDirection.Ascending));
        }
    }

    internal class AssetPath
    {
        private static readonly ImageSource ClosedImage = new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/CloseFolder.png") as ImageSource;
        private static readonly ImageSource OpenImage = new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/OpenFolder.png") as ImageSource;

        public string DisplayName => PathName.Trim('!');
        public string PathName { get; private set; }
        public string FullPath { get; }
        public AssetPath Parent { get; }
        public List<AssetPath> Children { get; } = new List<AssetPath>();
        public bool IsSelected { get; set; }
        public bool IsRoot { get; }

        public bool IsExpanded
        {
            get => expanded && Children.Count != 0;
            set => expanded = value;
        }
        private bool expanded;

        public AssetPath(string inName, string path, AssetPath inParent, bool bInRoot = false)
        {
            PathName = inName;
            FullPath = path;
            IsRoot = bInRoot;
            Parent = inParent;
        }

        public void UpdatePathName(string newName)
        {
            PathName = newName;
        }
    }
}
