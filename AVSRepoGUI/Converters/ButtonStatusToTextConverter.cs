using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AVSRepoGUI.Converters
{
    public class ButtonStatusToTextConverter : IValueConverter
    {
        //public static Style style = "MaterialDesignRaisedButton";

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {

            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.Installed)
            {
                return "Uninstall";

            }
            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.InstalledUnknown)
            {
                return "Force Upgrade";
            }
            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.NotInstalled)
            {
                return "Install";
            }
            return "Update";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
}
