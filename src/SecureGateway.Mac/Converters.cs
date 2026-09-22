using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SecureGateway.Mac
{
    /// <summary>RadioButton.IsChecked ⇄ enum value named in ConverterParameter.</summary>
    public class EnumBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter != null && value.ToString() == parameter.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return Enum.Parse(targetType, parameter.ToString()!);
            return BindingOperations.DoNothing;
        }
    }

    public class BoolToConnectTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "Disconnect" : "Connect";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Green when connected, otherwise the brush named in ConverterParameter ("gray" or "red").</summary>
    public class BoolToStatusBrushConverter : IValueConverter
    {
        private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#A6E3A1"));
        private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#585B70"));
        private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F38BA8"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return Green;
            return parameter?.ToString() == "red" ? Red : Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class StringNotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !string.IsNullOrEmpty(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
