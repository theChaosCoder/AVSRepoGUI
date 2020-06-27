using System;
using System.Windows.Data;
using System.Windows.Media;

namespace AVSRepoGUI.Converters
{

    public class ButtonStatusConverter : IValueConverter
    {
        //public static Style style = "MaterialDesignRaisedButton";

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.Installed)
            {
                return new SolidColorBrush(Colors.OrangeRed);
            }
            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.InstalledUnknown)
            {
                return new SolidColorBrush(Colors.Red);
            }
            if ((AvsApi.PluginStatus)value == AvsApi.PluginStatus.NotInstalled)
            {
                return new SolidColorBrush(Colors.Green);
            }
            return new SolidColorBrush(Colors.LimeGreen);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    } 
}
