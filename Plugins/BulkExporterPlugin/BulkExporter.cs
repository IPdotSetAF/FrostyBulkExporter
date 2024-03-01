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

namespace BulkExporterPlugin
{
    public class BulkExportCommand : ICommand
    {
        public bool CanExecute(object parameter)
        {
            return true;
        }

#pragma warning disable 67
        public event EventHandler CanExecuteChanged;
#pragma warning restore 67

        public void Execute(object parameter)
        {
            if (parameter is FrameworkElement param && param.Tag is BulkExporter explorer)
            {
                explorer.ExportChunk();
            }
        }
    }
    public class RightClickCommand : ICommand
    {
        public bool CanExecute(object parameter)
        {
            return true;
        }

#pragma warning disable 67
        public event EventHandler CanExecuteChanged;
#pragma warning restore 67

        public void Execute(object parameter)
        {
            if (parameter is ListBoxItem lbi)
                lbi.IsSelected = true;
        }
    }

    [TemplatePart(Name = PART_ChunksListBox, Type = typeof(ListBox))]
    [TemplatePart(Name = PART_ResExplorer, Type = typeof(FrostyDataExplorer))]
    [TemplatePart(Name = PART_ResExportMenuItem, Type = typeof(MenuItem))]
    [TemplatePart(Name = PART_ResImportMenuItem, Type = typeof(MenuItem))]
    [TemplatePart(Name = PART_RevertMenuItem, Type = typeof(MenuItem))]
    [TemplatePart(Name = PART_ChunkFilter, Type = typeof(TextBox))]
    [TemplatePart(Name = PART_Flatten, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Mesh, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_Texture, Type = typeof(CheckBox))]
    [TemplatePart(Name = PART_ExportButton, Type = typeof(Button))]
    public class BulkExporter : FrostyBaseEditor
    {
        public override ImageSource Icon => BulkExporterMenuExtension.imageSource;

        private const string PART_ChunksListBox = "PART_ChunksListBox";
        private const string PART_ResExplorer = "PART_ResExplorer";
        private const string PART_ResExportMenuItem = "PART_ResExportMenuItem";
        private const string PART_ResImportMenuItem = "PART_ResImportMenuItem";
        private const string PART_RevertMenuItem = "PART_RevertMenuItem";
        private const string PART_ChunkFilter = "PART_ChunkFilter";
        private const string PART_Flatten = "PART_Flatten";
        private const string PART_Texture = "PART_Texture";
        private const string PART_Mesh = "PART_Mesh";
        private const string PART_ExportButton = "PART_ExportButton";

        private ListBox chunksListBox;
        private FrostyDataExplorer resExplorer;
        private TextBox chunkFilterTextBox;
        //private CheckBox chunkModifiedBox;
        private CheckBox flattenCheck;
        private CheckBox meshCheck;
        private CheckBox textureCheck;
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

            chunksListBox = GetTemplateChild(PART_ChunksListBox) as ListBox;
            resExplorer = GetTemplateChild(PART_ResExplorer) as FrostyDataExplorer;
            chunkFilterTextBox = GetTemplateChild(PART_ChunkFilter) as TextBox;
            //chunkModifiedBox = GetTemplateChild(PART_Mesh) as CheckBox;

            flattenCheck = GetTemplateChild(PART_Flatten) as CheckBox;
            meshCheck = GetTemplateChild(PART_Mesh) as CheckBox;
            textureCheck = GetTemplateChild(PART_Texture) as CheckBox;
            exportButton = GetTemplateChild(PART_ExportButton) as Button;
            exportButton.Click += ExportButton_Click;

            resExplorer.SelectionChanged += ResExplorer_SelectionChanged;
            MenuItem mi = GetTemplateChild(PART_ResExportMenuItem) as MenuItem;
            mi.Click += ResExportMenuItem_Click;

            Loaded += FrostyChunkResEditor_Loaded;
            chunksListBox.SelectionChanged += ChunksListBox_SelectionChanged;
            chunkFilterTextBox.LostFocus += ChunkFilterTextBox_LostFocus;
            chunkFilterTextBox.KeyUp += ChunkFilterTextBox_KeyUp;
            //chunkModifiedBox.Checked += ChunkFilterTextBox_LostFocus;
            //chunkModifiedBox.Unchecked += ChunkFilterTextBox_LostFocus;
        }

        public void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            logger.Log("Export Clicked");
        }

        private void ResExplorer_SelectionChanged(object sender, RoutedEventArgs e)
        {
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

        private void ChunksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //if (chunksListBox.SelectedIndex != -1)
            //{
            //    chunksBundleBox.Items.Clear();
            //    ChunkAssetEntry SelectedChk = (ChunkAssetEntry)chunksListBox.SelectedItem;
            //    string FirstLine = "Selected chunk is in Bundles: ";
            //    if (SelectedChk.FirstMip != -1)
            //        FirstLine += " (FirstMip:" + SelectedChk.FirstMip + ")";
            //    if (App.FileSystem.GetManifestChunk(SelectedChk.Id) != null)
            //    {
            //            chunksBundleBox.Items.Add("Selected chunk is a Manifest chunk.");
            //    }
            //    else if (SelectedChk.Bundles.Count == 0 && SelectedChk.AddedBundles.Count == 0)
            //    {
            //        chunksBundleBox.Items.Add("Selected chunk is only in SuperBundles.");
            //    }
            //    if (SelectedChk.Bundles.Count != 0)
            //    {
            //        chunksBundleBox.Items.Add(FirstLine);
            //        foreach (int bundle in SelectedChk.Bundles)
            //        {
            //            chunksBundleBox.Items.Add(App.AssetManager.GetBundleEntry(bundle).Name);
            //        }
            //    }
            //    if (SelectedChk.AddedBundles.Count != 0)
            //    {
            //        chunksBundleBox.Items.Add("Added to Bundles:");
            //        foreach (int bundle in SelectedChk.AddedBundles)
            //        {
            //            chunksBundleBox.Items.Add(App.AssetManager.GetBundleEntry(bundle).Name);
            //        }
            //    }
            //}
            //else
            //{
            //    chunksBundleBox.Items.Clear();
            //    chunksBundleBox.Items.Add("No chunk selected");
            //}
        }

        private void ChunkFilterTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                UpdateFilter();
        }

        private void ChunkFilterTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateFilter();
        }

        private void UpdateFilter()
        {
            if (chunkFilterTextBox.Text == "" /*& chunkModifiedBox.IsChecked == false*/)
            {
                chunksListBox.Items.Filter = null;
                return;
            }
            else if (chunkFilterTextBox.Text != "" /*& chunkModifiedBox.IsChecked == false*/)
            {
                chunksListBox.Items.Filter = new Predicate<object>((object a) => ((ChunkAssetEntry)a).Id.ToString().Contains(chunkFilterTextBox.Text.ToLower()));
            }
            else if (chunkFilterTextBox.Text == "" /*& chunkModifiedBox.IsChecked == true*/)
            {
                chunksListBox.Items.Filter = new Predicate<object>((object a) => ((ChunkAssetEntry)a).IsModified);
            }
            else if (chunkFilterTextBox.Text != "" /*& chunkModifiedBox.IsChecked == true*/)
            {
                chunksListBox.Items.Filter = new Predicate<object>((object a) => (((ChunkAssetEntry)a).IsModified) & ((ChunkAssetEntry)a).Id.ToString().Contains(chunkFilterTextBox.Text.ToLower()));
            }
        }

        private void ResExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ResAssetEntry selectedAsset = resExplorer.SelectedAsset as ResAssetEntry;
            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save Resource", "*.res (Resource Files)|*.res", "Res", selectedAsset.Filename);

            Stream resStream = App.AssetManager.GetRes(selectedAsset);
            if (resStream == null)
                return;

            if (sfd.ShowDialog())
            {
                FrostyTaskWindow.Show("Exporting Asset", "", (task) =>
                {
                    using (NativeWriter writer = new NativeWriter(new FileStream(sfd.FileName, FileMode.Create)))
                    {
                        // write res meta first
                        writer.Write(selectedAsset.ResMeta);

                        // followed by remaining data
                        using (NativeReader reader = new NativeReader(resStream))
                            writer.Write(reader.ReadToEnd());
                    }
                });
                logger?.Log("Resource saved to {0}", sfd.FileName);
            }
        }

        public void ExportChunk()
        {
            ChunkAssetEntry selectedAsset = chunksListBox.SelectedItem as ChunkAssetEntry;
            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save Chunk", "*.chunk (Chunk Files)|*.chunk", "Chunk", selectedAsset.Filename);

            Stream chunkStream = App.AssetManager.GetChunk(selectedAsset);
            if (chunkStream == null)
                return;

            if (sfd.ShowDialog())
            {
                FrostyTaskWindow.Show("Exporting Chunk", "", (task) =>
                {
                    using (NativeWriter writer = new NativeWriter(new FileStream(sfd.FileName, FileMode.Create)))
                    {
                        using (NativeReader reader = new NativeReader(chunkStream))
                            writer.Write(reader.ReadToEnd());
                    }
                });
                logger?.Log("Chunk saved to {0}", sfd.FileName);
            }
        }

        private void FrostyChunkResEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (resExplorer.ItemsSource != null)
                return;

            resExplorer.ItemsSource = App.AssetManager.EnumerateRes();
            chunksListBox.ItemsSource = App.AssetManager.EnumerateChunks();
            chunksListBox.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("DisplayName", System.ComponentModel.ListSortDirection.Ascending));
        }

        private void RefreshChunksListBox(ChunkAssetEntry selectedAsset)
        {
            chunksListBox.Items.Refresh();
        }
    }
}
