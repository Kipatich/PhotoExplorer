using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhotoExplorer.Core;
using PhotoExplorer.Desktop;
using PhotoExplorer.Storage;

namespace PhotoExplorer.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts");
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PhotoExplorer;component/Themes/Controls.xaml", UriKind.Relative) });
        var bindings = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindings));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var exitCode = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                ThemeManager.Apply("Dark");
                var folder = DemoImages.Ensure(Path.Combine(output, "Демонстрация"));
                var decoded = WpfThumbnailGenerator.Decode(Directory.GetFiles(folder, "*.png")[0], 480);
                Check(decoded.PixelWidth == 480, "WIC decodes demo image");
                CheckCodecs(decoded, output);
                var cache = new SqliteThumbnailCache(Path.Combine(output, "smoke.db"));
                await cache.ClearAsync();
                var settings = new UserSettings();
                var vm = new MainViewModel(new PhotoCatalog(), cache, new ThumbnailService(cache, new WpfThumbnailGenerator()), new SettingsStore(Path.Combine(output, "settings.json")), settings);
                var window = new MainWindow(vm) { Width = 1440, Height = 920, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                window.Show();
                await vm.NavigateAsync(folder);
                await WaitUntil(() => vm.Preview is not null && vm.Photos.Count(p => p.Image is not null) >= 9);
                Check(vm.Photos.Count == 12, "Demo contains 12 images");
                Check(vm.Selected is not null && vm.Preview is not null, "Selection and full preview load");
                Check(window.WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(window) is not null, "Custom title bar retains native resize and caption behavior");
                var quickPhoto = vm.Photos[1];
                vm.Selected = quickPhoto;
                Check(ReferenceEquals(vm.Preview, quickPhoto.Image), "Inspector immediately reuses the grid thumbnail");
                await WaitUntil(() => quickPhoto.DetailPreview is not null);
                vm.Selected = vm.Photos[0]; vm.Selected = quickPhoto;
                Check(ReferenceEquals(vm.Preview, quickPhoto.DetailPreview), "Inspector reuses a recently decoded preview");
                vm.Selected = vm.Photos[0];
                await WaitUntil(() => vm.SelectedFolder?.Path == folder);
                Check(vm.SelectedFolder!.IsSelected, "Opened folder is selected in the expanded tree");
                Capture(window, Path.Combine(output, "desktop-dark.png"));
                var stats = await cache.GetStatisticsAsync();
                Check(stats.Entries >= 9, "Visible thumbnails persisted to SQLite");
                vm.MoveSelection(1); Check(vm.Selected?.Name.StartsWith("02_") == true, "Keyboard selection model");
                vm.Query = "северный";
                await WaitUntil(() => vm.Photos.Count == 1 && !vm.IsBusy);
                Check(vm.Photos[0].Name.Contains("Северный"), "Debounced search");
                vm.Query = "";
                await WaitUntil(() => vm.Photos.Count == 12 && !vm.IsBusy);
                await vm.NavigateAsync(Path.Combine(folder, "missing"));
                Check(vm.Photos.Count == 12 && vm.Status.Contains("Не удалось"), "Unavailable directory preserves prior view");
                await vm.NavigateAsync(folder);
                vm.FavoriteCommand.Execute(null); Check(vm.Favorites.Count == 1, "Favorite add");
                vm.FavoriteCommand.Execute(null); Check(vm.Favorites.Count == 0, "Favorite remove");
                ThemeManager.Apply("Light");
                await Task.Delay(250);
                Capture(window, Path.Combine(output, "desktop-light.png"));
                window.Width = 1060; window.Height = 700;
                await Task.Delay(250);
                Capture(window, Path.Combine(output, "desktop-compact.png"));
                ThemeManager.Apply("Dark");
                var settingsWindow = new SettingsWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                settingsWindow.Show(); await Task.Delay(150);
                var depthBox = (TextBox)settingsWindow.FindName("DepthBox");
                depthBox.Text = "2"; Check(vm.SearchDepth == 2, "Settings depth changes update the view model");
                depthBox.Text = "0"; Check(vm.SearchDepth == 0, "Zero depth selects unlimited recursion");
                Capture(settingsWindow, Path.Combine(output, "settings.png")); settingsWindow.Close();
                var viewer = new ViewerWindow(vm.Photos.Select(p => p.File).ToArray(), 0, 2) { WindowState = WindowState.Normal, Width = 1200, Height = 800, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
                viewer.Show();
                await WaitUntil(() => ((Image)viewer.FindName("Photo")).Source is not null);
                viewer.Move(1);
                await WaitUntil(() => ((Image)viewer.FindName("Photo")).Source is not null);
                Check(viewer.CurrentFile.Name.StartsWith("02_"), "Viewer advances");
                var viewerPhoto = (Image)viewer.FindName("Photo");
                var oldWidth = viewerPhoto.Width;
                var zoomEvent = Wheel(viewerPhoto, 120);
                Check(zoomEvent.Handled && viewerPhoto.Width > oldWidth, "Wheel over the fullscreen image zooms before ScrollViewer consumes it");
                Wheel(viewerPhoto, -120);
                Capture(viewer, Path.Combine(output, "viewer.png"));
                viewer.Close();
                var corrupt = Path.Combine(folder, "broken.png");
                await File.WriteAllTextAsync(corrupt, "not an image");
                await vm.NavigateAsync(folder); vm.SelectPath(corrupt);
                await WaitUntil(() => vm.PreviewError.Length > 0);
                Check(vm.Preview is null, "Corrupt image has an error state");
                File.Delete(corrupt);
                vm.Query = "no-match-xyz";
                await WaitUntil(() => vm.IsEmpty);
                Capture(window, Path.Combine(output, "empty-state.png"));
                vm.Query = "";
                await WaitUntil(() => vm.Photos.Count == 12 && !vm.IsBusy);
                var large = Path.Combine(output, "large"); Directory.CreateDirectory(large);
                var original = Directory.GetFiles(folder, "*.png")[0];
                for (var i = 0; i < 240; i++) File.Copy(original, Path.Combine(large, $"{i:0000}.png"), true);
                await vm.NavigateAsync(large);
                await WaitUntil(() => vm.Photos.Count(p => p.Image is not null) >= 4);
                Check(vm.Photos.Count == 240 && vm.Photos.Count(p => p.Image is not null) < 60, "Large folder virtualizes thumbnails");
                vm.Selected = vm.Photos[^1];
                await WaitUntil(() => vm.Photos[^1].Image is not null);
                Check(vm.Photos.Count(p => p.Image is not null) < 60, "Scrolling releases offscreen thumbnails");
                await vm.NavigateAsync(folder);
                var first = vm.NavigateAsync(large);
                var second = vm.NavigateAsync(folder);
                await Task.WhenAll(first, second);
                Check(vm.Photos.Count == 12 && vm.Folder == folder, "Latest navigation wins");
                await CheckExplorerAsync(window, vm, output);
                window.Close();
                await CheckProgressAsync(output, cache);
                var bindingOutput = bindings.ToString();
                await File.WriteAllTextAsync(Path.Combine(output, "bindings.log"), bindingOutput);
                Check(string.IsNullOrWhiteSpace(bindingOutput), "No WPF binding errors");
                Console.WriteLine($"PASS: WPF smoke checks. Screenshots: {output}");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exitCode;
    }
    private static async Task CheckProgressAsync(string output, IThumbnailCache cache)
    {
        var catalog = new PausedCatalog();
        var vm = new MainViewModel(catalog, cache, new ThumbnailService(cache, new WpfThumbnailGenerator()), new SettingsStore(Path.Combine(output, "progress-settings.json")), new());
        var window = new MainWindow(vm) { Width = 1200, Height = 800, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        window.Show();
        var scan = vm.NavigateAsync(output);
        await WaitUntil(() => vm.ScanProgressText.Contains("1250") || vm.ScanProgressText.Contains("1 250") || vm.ScanProgressText.Contains("1,250"));
        Check(vm.IsBusy && vm.Status.StartsWith("Сканируем директории") && vm.ScanProgressText.Contains("38"), "Scan progress displays live file and directory counts");
        Capture(window, Path.Combine(output, "scanning.png"));
        catalog.Completion.SetResult(new([], 0)); await scan;
        Check(!vm.IsBusy, "Scan progress closes when enumeration completes");
        window.Close();
    }
    private sealed class PausedCatalog : IPhotoCatalog
    {
        public TaskCompletionSource<BrowseResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken cancellationToken = default, IProgress<BrowseProgress>? progress = null)
        {
            progress?.Report(new(1250, 38, 214, 12));
            return Completion.Task.WaitAsync(cancellationToken);
        }
        public IReadOnlyList<string> GetDirectories(string folder) => [];
        public bool HasSubdirectories(string folder) => false;
    }
    private static MouseWheelEventArgs Wheel(UIElement source, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(args);
        return args;
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Visuals<T>(child)) yield return descendant;
        }
    }
    private static async Task CheckExplorerAsync(MainWindow window, MainViewModel vm, string output)
    {
        var fixture = Path.Combine(output, "tree-fixture");
        for (var i = 0; i < 45; i++) Directory.CreateDirectory(Path.Combine(fixture, $"folder-{i:00}"));
        Directory.CreateDirectory(Path.Combine(fixture, "folder-01", "nested"));
        await vm.NavigateAsync(fixture);
        await WaitUntil(() => vm.SelectedFolder?.Path == fixture);
        var node = vm.SelectedFolder!;
        await node.LoadAsync(); node.IsExpanded = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(node.Children.Single(n => n.Name == "folder-00").Children.Count == 0, "Empty folders have no expand arrow");
        Check(node.Children.Single(n => n.Name == "folder-01").Children.Count == 1, "Folders with children have an expand arrow");
        var tree = (TreeView)window.FindName("FolderTree");
        var scroll = (ScrollViewer)window.FindName("NavigationScroll");
        scroll.ScrollToTop(); window.UpdateLayout();
        var treeItem = Visuals<TreeViewItem>(tree).First(item => item.DataContext == node);
        var wheelEvent = Wheel(treeItem, -120);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(wheelEvent.Handled && scroll.VerticalOffset > 0, "Wheel over a folder scrolls the left panel");
        window.Activate(); treeItem.Focus();
        var selectedBorder = (Border)treeItem.Template.FindName("SelectionBorder", treeItem);
        var activeColor = ((SolidColorBrush)selectedBorder.Background).Color;
        ((TextBox)window.FindName("SearchBox")).Focus();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(((SolidColorBrush)selectedBorder.Background).Color == activeColor && activeColor == ((SolidColorBrush)Application.Current.Resources["Selection"]).Color,
            "Tree selection keeps its theme color after losing focus");
        var left = (ColumnDefinition)window.FindName("NavigationColumn");
        var before = left.ActualWidth;
        var splitter = (GridSplitter)window.FindName("LeftSplitter");
        splitter.Focus();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent };
        splitter.RaiseEvent(key); window.UpdateLayout();
        Check(left.ActualWidth > before, "Panel splitter resizes the navigation column");
        splitter.RaiseEvent(new DragCompletedEventArgs(10, 0, false));
        Check(vm.Settings.LeftPanelWidth == left.ActualWidth, "Navigation width is saved after resizing");
        var right = (ColumnDefinition)window.FindName("InspectorColumn");
        before = right.ActualWidth;
        var rightSplitter = (GridSplitter)window.FindName("RightSplitter");
        rightSplitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
        window.UpdateLayout();
        Check(right.ActualWidth > before, "Panel splitter resizes the inspector column");
        rightSplitter.RaiseEvent(new DragCompletedEventArgs(-10, 0, false));
        Check(vm.Settings.RightPanelWidth == right.ActualWidth, "Inspector width is saved after resizing");
        var nested = Path.Combine(fixture, "folder-01", "nested");
        vm.Address = nested;
        var addressBox = (TextBox)window.FindName("AddressBox");
        addressBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        await WaitUntil(() => vm.SelectedFolder?.Path == nested);
        Check(vm.SelectedFolder!.IsSelected && node.Children.Single(n => n.Name == "folder-01").IsExpanded, "Address entry expands ancestors and selects the requested folder");
        vm.FavoriteCommand.Execute(null);
        await vm.NavigateAsync(fixture);
        var location = vm.Favorites.First(f => f.Path == nested);
        var favoriteButton = Visuals<Button>(window).First(button => ReferenceEquals(button.DataContext, location));
        favoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => vm.SelectedFolder?.Path == nested);
        Check(vm.SelectedFolder!.IsSelected, "Favorite navigation synchronizes tree selection");
        vm.FavoriteCommand.Execute(null);
        Capture(window, Path.Combine(output, "tree-navigation.png"));
    }
    private static void CheckCodecs(BitmapSource source, string output)
    {
        BitmapEncoder[] encoders = [new JpegBitmapEncoder(), new BmpBitmapEncoder(), new GifBitmapEncoder(), new TiffBitmapEncoder()];
        foreach (var encoder in encoders)
        {
            encoder.Frames.Add(BitmapFrame.Create(source));
            var path = Path.Combine(output, encoder.GetType().Name + ".image");
            using (var stream = File.Create(path)) encoder.Save(stream);
            Check(WpfThumbnailGenerator.Decode(path, 240).PixelWidth == 240, encoder.GetType().Name + " roundtrip");
            File.Delete(path);
        }
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var jpeg = new JpegBitmapEncoder();
        jpeg.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        var rotated = Path.Combine(output, "orientation.jpg");
        using (var stream = File.Create(rotated)) jpeg.Save(stream);
        var result = WpfThumbnailGenerator.Decode(rotated, 480);
        Check(result.PixelWidth == source.PixelHeight && result.PixelHeight == source.PixelWidth, "EXIF orientation applied");
        File.Move(rotated, rotated + ".moved", true);
        File.Delete(rotated + ".moved");
        Check(true, "Decoder releases source file handle");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("UI condition timed out.");
            await Task.Delay(60);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var target = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
