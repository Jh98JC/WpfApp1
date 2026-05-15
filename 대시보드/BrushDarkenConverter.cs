using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfApp2
{
    public class BrushDarkenConverter : IValueConverter
    {
        // 0.8 => 20% darker (multiply channels by factor)
        public double Factor { get; set; } = 0.8;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush scb)
            {
                var f = Factor;
                if (double.IsNaN(f) || double.IsInfinity(f)) f = 0.8;
                if (f < 0) f = 0; if (f > 1) f = 1;
                System.Windows.Media.Color c = scb.Color;
                byte r = (byte)Math.Clamp((int)(c.R * f), 0, 255);
                byte g = (byte)Math.Clamp((int)(c.G * f), 0, 255);
                byte b = (byte)Math.Clamp((int)(c.B * f), 0, 255);
                System.Windows.Media.Color darker = System.Windows.Media.Color.FromArgb(c.A, r, g, b);
                var brush = new SolidColorBrush(darker);
                if (brush.CanFreeze) brush.Freeze();
                return brush;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not intended for two-way binding
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
