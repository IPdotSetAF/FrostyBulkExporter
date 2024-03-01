using Frosty.Core;
using System.Windows.Media;

namespace BulkExporterPlugin
{
    public class BulkExporterMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource = new ImageSourceConverter().ConvertFromString("pack://application:,,,/BulkExporterPlugin;component/Images/BulkExporter.png") as ImageSource;

        public override string TopLevelMenuName => "View";
        public override string SubLevelMenuName => null;

        public override string MenuItemName => "Bulk Exporter";
        public override ImageSource Icon => imageSource;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            App.EditorWindow.OpenEditor("Bulk Exporter", new BulkExporter(App.Logger));
        });
    }
}
