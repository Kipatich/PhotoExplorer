using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PhotoExplorer.Core;

namespace PhotoExplorer.Desktop;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        NavigationColumn.Width = new GridLength(viewModel.Settings.LeftPanelWidth);
        InspectorColumn.Width = new GridLength(viewModel.Settings.RightPanelWidth);
        SizeChanged += (_, _) => ConstrainPanels();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        FolderTree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is TreeViewItem item)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => item.BringIntoView(new Rect(0, 0, item.ActualWidth, 32)));
        }));
        Closed += (_, _) => { SavePanelWidths(); viewModel.Close(); };
        viewModel.SelectionChanged += (_, _) =>
        {
            var row = viewModel.Rows.FirstOrDefault(r => r.Items.Contains(viewModel.Selected!));
            if (row is not null) PhotoList.ScrollIntoView(row);
        };
    }
    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с изображениями", InitialDirectory = ViewModel.Folder };
        if (dialog.ShowDialog(this) == true) await ViewModel.NavigateAsync(dialog.FolderName);
    }
    private async void OnLocation(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Location location) await ViewModel.NavigateAsync(location.Path);
    }
    private async void OnFolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode { Path.Length: > 0 } node &&
            !string.Equals(Path.TrimEndingDirectorySeparator(node.Path), Path.TrimEndingDirectorySeparator(ViewModel.Folder), StringComparison.OrdinalIgnoreCase))
            await ViewModel.NavigateAsync(node.Path);
    }
    private void OnNavigationWheel(object sender, MouseWheelEventArgs e)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? NavigationScroll.ViewportHeight : lines * 18d;
        NavigationScroll.ScrollToVerticalOffset(NavigationScroll.VerticalOffset - e.Delta / 120d * distance);
        e.Handled = true;
    }
    private void SavePanelWidths()
    {
        ViewModel.Settings.LeftPanelWidth = NavigationColumn.ActualWidth;
        ViewModel.Settings.RightPanelWidth = InspectorColumn.ActualWidth;
    }
    private void OnSplitterCompleted(object sender, DragCompletedEventArgs e) { SavePanelWidths(); ViewModel.SaveSettings(); }
    private void ConstrainPanels()
    {
        var available = Math.Max(390, ActualWidth - 332);
        var left = NavigationColumn.Width.Value;
        var right = InspectorColumn.Width.Value;
        if (left + right <= available) return;
        var ratio = Math.Clamp((available - 390) / Math.Max(1, left + right - 390), 0, 1);
        NavigationColumn.Width = new GridLength(170 + Math.Max(0, left - 170) * ratio);
        InspectorColumn.Width = new GridLength(220 + Math.Max(0, right - 220) * ratio);
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
    }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();
    private async void OnAddressKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await ViewModel.NavigateAsync(ViewModel.Address); }
    }
    private void OnSelectPhoto(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PhotoItem photo) ViewModel.Selected = photo;
    }
    private void OnPhotoDoubleClick(object sender, MouseButtonEventArgs e) { OnSelectPhoto(sender, e); e.Handled = true; ShowViewer(); }
    private void OnPreviewClick(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ShowViewer(); }
    private void OnFullscreen(object sender, RoutedEventArgs e) => ShowViewer();
    public void ShowViewer()
    {
        if (ViewModel.Selected is null) return;
        var index = ViewModel.Photos.ToList().IndexOf(ViewModel.Selected);
        var viewer = new ViewerWindow(ViewModel.Photos.Select(p => p.File).ToArray(), index, ViewModel.Settings.SlideshowSeconds) { Owner = this };
        viewer.ShowDialog();
        ViewModel.SelectPath(viewer.CurrentFile.FullPath);
    }
    private async void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PhotoItem photo) return;
        photo.Loading?.Cancel();
        var cancellation = new CancellationTokenSource();
        photo.Loading = cancellation;
        try
        {
            var thumbnail = await ViewModel.Thumbnails.GetAsync(photo.File, 480, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            photo.Image = WpfThumbnailGenerator.FromBytes(thumbnail.Bytes);
            photo.Placeholder = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!cancellation.IsCancellationRequested) photo.Placeholder = "Не удалось прочитать"; }
        finally { if (photo.Loading == cancellation) photo.Loading = null; cancellation.Dispose(); }
    }
    private void OnTileUnloaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PhotoItem photo) return;
        photo.Loading?.Cancel(); photo.Image = null;
    }
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e) => ViewModel.SetColumns(Math.Max(200, PhotoList.ActualWidth - 12));
    private void OnThumbnailSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PhotoList is not null) ViewModel.SetColumns(Math.Max(200, PhotoList.ActualWidth - 12));
    }
    private async void OnDemo(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.NavigateAsync(DemoImages.Ensure(Path.Combine(AppPaths.DataDirectory, "Демонстрация"))); }
        catch (Exception ex) { ViewModel.Status = $"Не удалось создать демонстрацию: {ex.Message}"; }
    }
    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } photo) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{photo.File.FullPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ViewModel.Status = $"Не удалось открыть проводник: {ex.Message}"; }
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        if (Directory.Exists(paths[0])) await ViewModel.NavigateAsync(paths[0]);
        else if (File.Exists(paths[0])) { ViewModel.Query = ""; await ViewModel.NavigateAsync(Path.GetDirectoryName(paths[0])!); ViewModel.SelectPath(paths[0]); }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (key == Key.F1) { ShowHelp(); e.Handled = true; return; }
        if (ctrl && key == Key.O) { OnOpenFolder(this, e); e.Handled = true; return; }
        if (ctrl && key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (ctrl && key == Key.L) { AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; return; }
        if (alt && key is Key.Left or Key.Right or Key.Up)
        {
            var command = key == Key.Left ? ViewModel.BackCommand : key == Key.Right ? ViewModel.ForwardCommand : ViewModel.UpCommand;
            if (command.CanExecute(null)) command.Execute(null);
            e.Handled = true; return;
        }
        if (key == Key.F5) { ViewModel.RefreshCommand.Execute(null); e.Handled = true; return; }
        if (HasAncestor<TextBox>(e.OriginalSource)) { if (key == Key.Escape) ViewModel.Query = ""; return; }
        if (HasAncestor<ComboBox>(e.OriginalSource) || HasAncestor<Slider>(e.OriginalSource) || HasAncestor<TreeView>(e.OriginalSource)) return;
        if (key == Key.F11 || key == Key.Enter && (Keyboard.FocusedElement is not Button button || button.DataContext is PhotoItem)) { ShowViewer(); e.Handled = true; }
        else if (key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            ViewModel.MoveSelection(key switch { Key.Left => -1, Key.Right => 1, Key.Up => -ViewModel.Columns, _ => ViewModel.Columns });
            e.Handled = true;
        }
    }
    private static bool HasAncestor<T>(object source) where T : DependencyObject
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is T) return true;
        return false;
    }
    private void OnHelp(object sender, RoutedEventArgs e) => ShowHelp();
    public void ShowHelp() => new HelpWindow { Owner = this }.ShowDialog();
    private void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow(ViewModel) { Owner = this }.ShowDialog();
}
