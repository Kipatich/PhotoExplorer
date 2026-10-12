using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhotoExplorer.Core;

namespace PhotoExplorer.Desktop;

public partial class ViewerWindow : Window
{
    private readonly IReadOnlyList<PhotoFile> _files;
    private readonly DispatcherTimer _timer;
    private int _index;
    private int _version;
    private bool _closed;
    private bool _fit = true;
    private double _zoom = 1;
    private BitmapSource? _bitmap;
    private Point? _drag;
    private double _offsetX, _offsetY;
    public PhotoFile CurrentFile => _files[_index];
    public ViewerWindow(IReadOnlyList<PhotoFile> files, int index, int interval)
    {
        _files = files; _index = index;
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        _timer.Tick += (_, _) => Move(1);
        Loaded += async (_, _) => await LoadAsync();
        SizeChanged += (_, _) => ApplyZoom();
    }
    private async Task LoadAsync()
    {
        var version = ++_version;
        _bitmap = null; Photo.Source = null;
        _fit = true; ErrorLabel.Text = "Загрузка…";
        FileLabel.Text = CurrentFile.Name;
        CounterLabel.Text = $"{_index + 1} / {_files.Count}  ·  {CurrentFile.Extension}  ·  {PhotoItem.FormatSize(CurrentFile.Length)}";
        var path = CurrentFile.FullPath;
        try
        {
            // A bounded preview opens quickly. 1:1 loads original pixels explicitly.
            var bitmap = await Task.Run(() => WpfThumbnailGenerator.Decode(path, 3840));
            if (version != _version || _closed) return;
            _bitmap = bitmap; Photo.Source = bitmap; ErrorLabel.Text = ""; ApplyZoom();
        }
        catch (Exception) { if (version == _version) ErrorLabel.Text = "Изображение недоступно или формат повреждён.\nМожно перейти к следующему файлу стрелками."; }
    }
    public void Move(int delta) { _index = (_index + delta + _files.Count) % _files.Count; _ = LoadAsync(); }
    private void ApplyZoom()
    {
        if (_bitmap is null || Scroller.ActualWidth <= 0) return;
        var fit = Math.Min(Math.Max(1, Scroller.ActualWidth - 48) / _bitmap.PixelWidth, Math.Max(1, Scroller.ActualHeight - 48) / _bitmap.PixelHeight);
        if (_fit) _zoom = Math.Min(1, fit);
        Photo.Width = _bitmap.PixelWidth * _zoom;
        Photo.Height = _bitmap.PixelHeight * _zoom;
        ZoomLabel.Text = _fit ? "По размеру экрана" : $"{_zoom:P0}";
    }
    private void Zoom(double factor) { _fit = false; _zoom = Math.Clamp(_zoom * factor, .05, 8); ApplyZoom(); }
    private async void OnActualSize(object sender, RoutedEventArgs e)
    {
        var version = ++_version;
        var path = CurrentFile.FullPath;
        try
        {
            var bitmap = await Task.Run(() => WpfThumbnailGenerator.Decode(path, int.MaxValue));
            if (version != _version || _closed) return;
            _bitmap = bitmap; Photo.Source = bitmap; _fit = false; _zoom = 1; ErrorLabel.Text = ""; ApplyZoom();
        }
        catch (Exception) { if (version == _version) ErrorLabel.Text = "Не удалось загрузить полный размер изображения."; }
    }
    private void OnFit(object sender, RoutedEventArgs e) { _fit = true; ApplyZoom(); }
    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(1.25);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(.8);
    private void OnMouseWheel(object sender, MouseWheelEventArgs e) { Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15); e.Handled = true; }
    private void OnPrevious(object sender, RoutedEventArgs e) => Move(-1);
    private void OnNext(object sender, RoutedEventArgs e) => Move(1);
    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled) { _timer.Stop(); PlayButton.Content = "▶  Слайд-шоу"; }
        else { _timer.Start(); PlayButton.Content = "Ⅱ  Пауза"; }
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnClosed(object? sender, EventArgs e) { _closed = true; _version++; _timer.Stop(); Photo.Source = null; _bitmap = null; }
    private void OnImageClick(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) { _fit = true; ApplyZoom(); } }
    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) { _fit = true; ApplyZoom(); return; }
        _drag = e.GetPosition(Scroller); _offsetX = Scroller.HorizontalOffset; _offsetY = Scroller.VerticalOffset;
        Scroller.CaptureMouse(); Scroller.Cursor = Cursors.SizeAll;
    }
    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } origin) return;
        var current = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_offsetX + origin.X - current.X);
        Scroller.ScrollToVerticalOffset(_offsetY + origin.Y - current.Y);
    }
    private void OnPanEnd(object sender, MouseButtonEventArgs e) { _drag = null; Scroller.ReleaseMouseCapture(); Scroller.Cursor = Cursors.Arrow; }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape or Key.F11: Close(); break;
            case Key.Left: Move(-1); break;
            case Key.Right: Move(1); break;
            case Key.Space: OnPlay(this, e); break;
            case Key.Add or Key.OemPlus: Zoom(1.25); break;
            case Key.Subtract or Key.OemMinus: Zoom(.8); break;
            case Key.D0 or Key.NumPad0: _fit = true; ApplyZoom(); break;
            case Key.D1 or Key.NumPad1: OnActualSize(this, e); break;
            case Key.F1: _timer.Stop(); PlayButton.Content = "▶  Слайд-шоу"; new HelpWindow { Owner = this }.ShowDialog(); break;
            default: return;
        }
        e.Handled = true;
    }
}
