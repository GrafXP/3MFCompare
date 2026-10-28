using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ThreeMfCompare.App;

/// <summary>
/// What the theme's templates read off a control beyond its own properties: an icon glyph beside the
/// content, and the background on hover, on press and when checked. One template then serves the
/// plain, the primary and the quiet buttons.
/// </summary>
public static class Look
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Look), new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static readonly DependencyProperty HoverProperty =
        DependencyProperty.RegisterAttached("Hover", typeof(Brush), typeof(Look), new PropertyMetadata(null));

    public static Brush? GetHover(DependencyObject element) => (Brush?)element.GetValue(HoverProperty);
    public static void SetHover(DependencyObject element, Brush? value) => element.SetValue(HoverProperty, value);

    public static readonly DependencyProperty PressedProperty =
        DependencyProperty.RegisterAttached("Pressed", typeof(Brush), typeof(Look), new PropertyMetadata(null));

    public static Brush? GetPressed(DependencyObject element) => (Brush?)element.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject element, Brush? value) => element.SetValue(PressedProperty, value);

    public static readonly DependencyProperty CheckedProperty =
        DependencyProperty.RegisterAttached("Checked", typeof(Brush), typeof(Look), new PropertyMetadata(null));

    public static Brush? GetChecked(DependencyObject element) => (Brush?)element.GetValue(CheckedProperty);
    public static void SetChecked(DependencyObject element, Brush? value) => element.SetValue(CheckedProperty, value);

    public static readonly DependencyProperty CheckedForegroundProperty =
        DependencyProperty.RegisterAttached("CheckedForeground", typeof(Brush), typeof(Look), new PropertyMetadata(null));

    public static Brush? GetCheckedForeground(DependencyObject element) => (Brush?)element.GetValue(CheckedForegroundProperty);
    public static void SetCheckedForeground(DependencyObject element, Brush? value) => element.SetValue(CheckedForegroundProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Look), new PropertyMetadata(new CornerRadius(6)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    /// <summary>Asks Windows for a dark title bar in the colour of the top bar; older Windows keeps its own.</summary>
    public static void DarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var on = 1;
            DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref on, sizeof(int));
            var caption = 0x001B1C1C; // 0x00BBGGRR: the panels' #1C1C1B
            DwmSetWindowAttribute(handle, CaptionColour, ref caption, sizeof(int));
        };
    }

    const int ImmersiveDarkMode = 20;
    const int CaptionColour = 35;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
