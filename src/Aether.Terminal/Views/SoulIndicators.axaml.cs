using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Aether.Terminal.Views;

public partial class SoulIndicators : UserControl
{
    public SoulIndicators()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter TensionToAngleConverter =
        new FuncValueConverter<int, double>(level => level * 3.6);

    public static readonly IMultiValueConverter TensionToColorConverter =
        new FuncMultiValueConverter<object, IBrush>(values =>
        {
            var list = values.ToList();
            if (list.Count > 0 && list[0] is int level)
            {
                if (level < 40) return Brushes.DeepSkyBlue;
                if (level < 75) return Brushes.Yellow;
                return Brushes.Red;
            }
            return Brushes.Gray;
        });

    public static readonly IValueConverter HiveStatusToColorConverter =
        new FuncValueConverter<bool, IBrush>(active => active ? SolidColorBrush.Parse("#ffb000") : Brushes.Gray);

    public static readonly IValueConverter HiveStatusToGlowColorConverter =
        new FuncValueConverter<bool, Color>(active => active ? Color.Parse("#ffb000") : Colors.Transparent);
}
