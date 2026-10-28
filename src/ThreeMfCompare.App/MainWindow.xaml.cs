using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Visual;

namespace ThreeMfCompare.App;

public partial class MainWindow : Window
{
    readonly MainViewModel viewModel = new();
    readonly StartupOptions options = StartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1));
    TextDiffWindow? diffWindow;
    bool failed;

    public MainWindow()
    {
        InitializeComponent();
        Look.DarkTitleBar(this);
        DataContext = viewModel;
        viewModel.Failed += (title, message) =>
        {
            failed = true;
            if (options.Screenshot is null) MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        viewModel.TextDiffRequested += ShowText;
        if (options.Size("size") is { } size) (Width, Height) = size;
        Loaded += async (_, _) => await FollowCommandLineAsync();
        Closed += (_, _) => viewModel.Dispose();
    }

    void Open_Click(object sender, RoutedEventArgs e) => Open();

    void Open()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Bambu Studio projects",
            Filter = "Bambu Studio projects (*.3mf)|*.3mf|All files|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true) _ = viewModel.AddFilesAsync(dialog.FileNames);
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        // A dropped folder brings the projects in it.
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.3mf") : [p]);
        _ = viewModel.AddFilesAsync(files);
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            Page? page = e.Key switch { Key.D1 => Page.Files, Key.D2 => Page.Settings, Key.D3 => Page.Slices, Key.D4 => Page.Gcode, _ => null };
            if (page is { } p)
            {
                viewModel.Page = p;
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                Open();
                e.Handled = true;
            }
            else if (e.Key == Key.F && viewModel.Page == Page.Settings)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5 && viewModel.SliceAllCommand.CanExecute(null))
        {
            viewModel.SliceAllCommand.Execute(null);
            e.Handled = true;
        }
    }

    void ShowText(TextDiffRequest request)
    {
        diffWindow?.Close();
        diffWindow = new TextDiffWindow(request) { Owner = this };
        diffWindow.Show();
    }

    async Task FollowCommandLineAsync()
    {
        try
        {
            await viewModel.AddFilesAsync(options.Files.Select(Path.GetFullPath));
            if (options.Number("baseline") is { } baseline && baseline >= 1 && baseline <= viewModel.Projects.Count)
                viewModel.SetBaselineCommand.Execute(viewModel.Projects[(int)baseline - 1]);
            if (options.Text("category") is { } category) viewModel.SelectCategory(category);
            if (options.Text("search") is { } search) viewModel.Search = search;
            if (options.Has("all")) viewModel.OnlyDifferences = false;
            if (options.Has("customized")) viewModel.OnlyCustomized = true;
            if (options.Has("slice")) await viewModel.SliceAsync([.. viewModel.Projects.Where(p => !p.IsSliced)]);
            if (options.Number("plate") is { } plate) viewModel.SelectedPlate = (int)plate;
            if (options.Has("lines")) viewModel.UseBeads = false;
            if (options.Enum<Page>("page") is { } page) viewModel.Page = page;

            if (viewModel.Page == Page.Gcode)
            {
                await viewModel.RefreshPanesAsync();
                if (options.Text("hide") is { } hide)
                    foreach (var name in hide.Split(',', StringSplitOptions.TrimEntries))
                        foreach (var item in viewModel.LegendItems.Where(l => l.Feature.DisplayName().Equals(name, StringComparison.OrdinalIgnoreCase)))
                            item.IsShown = false;
                if (options.Text("z") is { } z)
                {
                    // Drawn whole first and then narrowed, as the slider would: the panes keep their meshes and only take new triangles.
                    await viewModel.SceneReady;
                    await Task.Delay(500);
                    viewModel.ShowHeights(z);
                }
                if (options.Text("view") is { } view) viewModel.ViewFromCommand.Execute(view);
                // Recoloured once drawn, as the drop-down would: the meshes take new colours in place.
                if (options.Enum<ToolpathColouring>("colour") is { } colouring) viewModel.Colouring = colouring;
                if (options.Number("turn") is { } degrees)
                {
                    // After the panes have drawn once, as a drag would come.
                    await Task.Delay(500);
                    viewModel.TurnFirstPane(degrees);
                }
                // Twice: a redraw asked for while one runs is drawn by the loop that is running.
                await viewModel.SceneReady;
                await viewModel.SceneReady;
            }
            if (viewModel.Page == Page.Slices) await viewModel.RefreshMetricsAsync();
            if (options.Text("diff") is { } key && viewModel.Projects.Count > 1)
            {
                viewModel.OnlyDifferences = false;
                viewModel.Search = "";
                viewModel.SelectCategory("All settings");
                if (viewModel.SettingItems.OfType<SettingRowViewModel>().FirstOrDefault(r => r.Key == key) is { } row)
                    viewModel.ShowTextCommand.Execute(row.Cells[viewModel.BaselineIndex == 0 ? 1 : 0]);
            }
        }
        catch (Exception ex) when (options.Screenshot is not null)
        {
            failed = true;
            Console.Error.WriteLine(ex);
        }

        if (options.Screenshot is not { } file) return;
        await Task.Delay(TimeSpan.FromSeconds(options.Number("wait") ?? 1.5));
        SaveScreenshot(file, diffWindow is { } window ? (FrameworkElement)window.Content : (FrameworkElement)Content);
        Application.Current.Shutdown(failed ? 1 : 0);
    }

    /// <summary>A window's content as a PNG, the 3D views included.</summary>
    void SaveScreenshot(string file, FrameworkElement content)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
