using System.Windows;
using System.Windows.Controls;
namespace PhotoExplorer.Desktop;
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _ready;
    public SettingsWindow(MainViewModel vm)
    {
        _vm = vm; InitializeComponent();
        ThemeBox.SelectedIndex = vm.Settings.Theme == "Light" ? 1 : 0;
        IntervalSlider.Value = vm.Settings.SlideshowSeconds; IntervalLabel.Text = $"{vm.Settings.SlideshowSeconds} с";
        DepthBox.Text = vm.SearchDepth.ToString();
        _ready = true; Loaded += async (_, _) => await RefreshCacheAsync();
    }
    private async Task RefreshCacheAsync()
    {
        try { CacheLabel.Text = await _vm.CacheSummaryAsync(); }
        catch (Exception ex) { CacheLabel.Text = $"Кеш недоступен: {ex.Message}"; }
    }
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _vm.Settings.Theme = ThemeBox.SelectedIndex == 1 ? "Light" : "Dark";
        ThemeManager.Apply(_vm.Settings.Theme); _vm.SaveSettings();
    }
    private void OnIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _vm.Settings.SlideshowSeconds = (int)e.NewValue; IntervalLabel.Text = $"{e.NewValue:0} с"; _vm.SaveSettings();
    }
    private async void OnClearCache(object sender, RoutedEventArgs e)
    {
        ClearButton.IsEnabled = false;
        try { await _vm.ClearCacheAsync(); await RefreshCacheAsync(); }
        catch (Exception ex) { CacheLabel.Text = $"Не удалось очистить кеш: {ex.Message}"; }
        finally { ClearButton.IsEnabled = true; }
    }
    private void OnDepthChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (int.TryParse(DepthBox.Text, out var depth) && depth >= 0)
        {
            _vm.SearchDepth = depth;
            DepthHint.Text = "0 — без ограничений; 2 — два уровня подпапок.\nПрименяется, когда включён поиск в подпапках.";
        }
        else DepthHint.Text = "Введите целое число от 0. Последнее корректное значение сохранено.";
    }
    private void OnDepthLostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => DepthBox.Text = _vm.SearchDepth.ToString();
}
