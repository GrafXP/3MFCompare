using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ThreeMfCompare.App;

public partial class ToolpathPane : UserControl
{
    public ToolpathPane()
    {
        InitializeComponent();
    }

    ToolpathPaneViewModel? Pane => DataContext as ToolpathPaneViewModel;

    void Viewport_CameraChanged(object sender, RoutedEventArgs e) => Pane?.OnCameraMoved();

    /// <summary>A double click frames the prints again, from where the camera looks now.</summary>
    void Viewport_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel main) main.ViewFromCommand.Execute("fit");
        e.Handled = true;
    }
}
