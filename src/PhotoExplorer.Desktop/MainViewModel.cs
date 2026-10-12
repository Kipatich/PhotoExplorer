using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PhotoExplorer.Core;

namespace PhotoExplorer.Desktop;

public sealed class MainViewModel : ObservableObject
{
    private readonly IPhotoCatalog _catalog;
    private readonly IThumbnailCache _cache;
    private readonly SettingsStore _store;
    private readonly NavigationHistory _history = new();
    private CancellationTokenSource? _browseCancellation;
    private CancellationTokenSource? _searchCancellation;
    private int _previewVersion;
    private readonly SemaphoreSlim _previewGate = new(1);
    private readonly LinkedList<PhotoItem> _recentPreviews = new();
    private int _treeVersion;
    public FolderNode? SelectedFolder { get; private set; }
    private int _columns = 3;
    private bool _closed;
    public UserSettings Settings { get; }
    public ThumbnailService Thumbnails { get; }
    public ObservableCollection<PhotoRow> Rows { get; } = [];
    public ObservableCollection<Location> Locations { get; } = [];
    public ObservableCollection<Location> Favorites { get; } = [];
    public ObservableCollection<FolderNode> Roots { get; } = [];
    public IReadOnlyList<PhotoItem> Photos { get; private set; } = [];
    public string[] SortChoices { get; } = ["По имени", "Сначала новые", "Сначала старые", "Сначала крупные"];
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand ThemeCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public event EventHandler? SelectionChanged;

    public MainViewModel(IPhotoCatalog catalog, IThumbnailCache cache, ThumbnailService thumbnails, SettingsStore store, UserSettings settings)
    {
        _catalog = catalog; _cache = cache; Thumbnails = thumbnails; _store = store; Settings = settings;
        _recursive = settings.Recursive; _sortIndex = (int)settings.Sort; _thumbnailSize = settings.ThumbnailSize;
        BackCommand = new RelayCommand(() => { var path = _history.Back(); if (path is not null) _ = NavigateAsync(path, false); }, () => _history.CanGoBack && !IsBusy);
        ForwardCommand = new RelayCommand(() => { var path = _history.Forward(); if (path is not null) _ = NavigateAsync(path, false); }, () => _history.CanGoForward && !IsBusy);
        UpCommand = new RelayCommand(() => { var parent = Directory.GetParent(Folder); if (parent is not null) _ = NavigateAsync(parent.FullName); }, () => !string.IsNullOrEmpty(Folder) && Directory.GetParent(Folder) is not null);
        RefreshCommand = new RelayCommand(() => _ = NavigateAsync(Folder, false));
        FavoriteCommand = new RelayCommand(ToggleFavorite, () => Directory.Exists(Folder));
        ThemeCommand = new RelayCommand(() => { Settings.Theme = Settings.Theme == "Dark" ? "Light" : "Dark"; ThemeManager.Apply(Settings.Theme); SaveSettings(); });
        ClearSearchCommand = new RelayCommand(() => Query = "");
        AddLocation("Изображения", Environment.SpecialFolder.MyPictures, "\uEB9F");
        AddLocation("Рабочий стол", Environment.SpecialFolder.DesktopDirectory, "\uE7F4");
        AddLocation("Домашняя папка", Environment.SpecialFolder.UserProfile, "\uE80F");
        foreach (var path in settings.Favorites.Where(Directory.Exists)) Favorites.Add(new(System.IO.Path.GetFileName(path), path, "\uE734"));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) Roots.Add(new(drive.RootDirectory.FullName, catalog, drive.Name));
    }

    private void AddLocation(string name, Environment.SpecialFolder folder, string icon)
    {
        var path = Environment.GetFolderPath(folder);
        if (Directory.Exists(path)) Locations.Add(new(name, path, icon));
    }

    private string _folder = "";
    public string Folder { get => _folder; private set { Set(ref _folder, value); Address = value; Raise(nameof(FolderTitle)); Raise(nameof(FavoriteLabel)); Raise(nameof(FavoriteIcon)); } }
    public string FolderTitle => string.IsNullOrEmpty(Folder) ? "Ваша фототека" : System.IO.Path.GetFileName(Folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Folder;
    private string _address = "";
    public string Address { get => _address; set => Set(ref _address, value); }
    private string _query = "";
    public string Query { get => _query; set { if (Set(ref _query, value)) { Raise(nameof(HasQuery)); DebounceSearch(); } } }
    public bool HasQuery => !string.IsNullOrEmpty(Query);
    private bool _recursive;
    public bool Recursive { get => _recursive; set { if (Set(ref _recursive, value)) { Settings.Recursive = value; DebounceSearch(); } } }
    public int SearchDepth
    {
        get => Settings.SearchDepth;
        set
        {
            value = Math.Max(0, value);
            if (Settings.SearchDepth == value) return;
            Settings.SearchDepth = value; Raise(); SaveSettings();
            if (Recursive) DebounceSearch();
        }
    }
    private int _sortIndex;
    public int SortIndex { get => _sortIndex; set { if (Set(ref _sortIndex, value)) { Settings.Sort = (PhotoSort)value; DebounceSearch(); } } }
    private int _thumbnailSize;
    public int ThumbnailSize { get => _thumbnailSize; set { if (Set(ref _thumbnailSize, value)) { Settings.ThumbnailSize = value; Raise(nameof(TileHeight)); } } }
    public double TileHeight => ThumbnailSize * .69;
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Raise(nameof(IsEmpty)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsEmpty => !IsBusy && Photos.Count == 0;
    private string _status = "Выберите папку с изображениями";
    public string Status { get => _status; set => Set(ref _status, value); }
    private string _scanProgressText = "Проверено файлов: 0 · папок: 0 · изображений: 0";
    public string ScanProgressText { get => _scanProgressText; private set => Set(ref _scanProgressText, value); }
    public string CountLabel => $"{Photos.Count:N0} изображений";
    public string TotalSize => PhotoItem.FormatSize(Photos.Sum(p => p.File.Length));
    public string FavoriteLabel => Favorites.Any(f => f.Path == Folder) ? "Убрать из избранного" : "В избранное";
    public string FavoriteIcon => Favorites.Any(f => f.Path == Folder) ? "★" : "☆";
    private PhotoItem? _selected;
    public PhotoItem? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) { _selected.IsSelected = false; _selected.PropertyChanged -= SelectedPhotoChanged; }
            Set(ref _selected, value);
            if (value is not null) { value.IsSelected = true; value.PropertyChanged += SelectedPhotoChanged; }
            Raise(nameof(HasSelection));
            _ = LoadPreviewAsync(value);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool HasSelection => Selected is not null;
    private BitmapSource? _preview;
    public BitmapSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    private string _dimensions = "—";
    public string Dimensions { get => _dimensions; private set => Set(ref _dimensions, value); }
    private string _previewError = "";
    public string PreviewError { get => _previewError; private set => Set(ref _previewError, value); }

    public async Task NavigateAsync(string folder, bool addHistory = true)
    {
        if (_closed || string.IsNullOrWhiteSpace(folder)) return;
        _browseCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _browseCancellation = cancellation;
        IsBusy = true;
        Status = "Сканируем директории…";
        ScanProgressText = "Проверено файлов: 0 · папок: 0 · изображений: 0";
        try
        {
            var fullPath = System.IO.Path.GetFullPath(folder);
            var progress = new Progress<BrowseProgress>(p =>
            {
                if (_browseCancellation != cancellation || cancellation.IsCancellationRequested || _closed) return;
                ScanProgressText = $"Проверено файлов: {p.FilesScanned:N0} · папок: {p.DirectoriesScanned:N0} · изображений: {p.ImagesFound:N0}";
                Status = $"Сканируем директории… {ScanProgressText}";
            });
            var result = await _catalog.BrowseAsync(new(fullPath, Query, Recursive, (PhotoSort)SortIndex, SearchDepth), cancellation.Token, progress);
            if (cancellation.IsCancellationRequested || _closed) return;
            var oldPath = Selected?.File.FullPath;
            foreach (var photo in Photos) { photo.Loading?.Cancel(); photo.Image = null; }
            Folder = fullPath;
            _ = SynchronizeTreeAsync(fullPath);
            if (addHistory) _history.Visit(fullPath);
            Photos = result.Files.Select(f => new PhotoItem(f)).ToArray();
            foreach (var previous in _recentPreviews) previous.DetailPreview = null;
            _recentPreviews.Clear();
            RebuildRows();
            Selected = Photos.FirstOrDefault(p => p.File.FullPath == oldPath) ?? Photos.FirstOrDefault();
            Settings.LastFolder = fullPath;
            Status = result.SkippedDirectories > 0 ? $"Пропущено недоступных папок: {result.SkippedDirectories}" : "Готово";
            Raise(nameof(CountLabel)); Raise(nameof(TotalSize)); Raise(nameof(IsEmpty));
            SaveSettings();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) Status = $"Не удалось открыть папку: {ex.Message}"; }
        finally { if (_browseCancellation == cancellation) { _browseCancellation = null; IsBusy = false; } cancellation.Dispose(); }
    }

    private async void DebounceSearch()
    {
        _searchCancellation?.Cancel();
        var cancellation = _searchCancellation = new();
        try { await Task.Delay(300, cancellation.Token); await NavigateAsync(Folder, false); }
        catch (OperationCanceledException) { }
        finally { if (_searchCancellation == cancellation) _searchCancellation = null; cancellation.Dispose(); }
    }

    public void SetColumns(double width)
    {
        var columns = Math.Max(1, (int)(width / (ThumbnailSize + 18)));
        if (columns == _columns) return;
        _columns = columns; RebuildRows();
    }
    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var chunk in Photos.Chunk(_columns)) Rows.Add(new(chunk, _columns));
    }
    public void SelectPath(string path) => Selected = Photos.FirstOrDefault(p => string.Equals(p.File.FullPath, System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) ?? Selected;
    public void MoveSelection(int delta)
    {
        if (Photos.Count == 0) return;
        var index = Selected is null ? 0 : Photos.ToList().IndexOf(Selected);
        Selected = Photos[Math.Clamp(index + delta, 0, Photos.Count - 1)];
    }
    public int Columns => _columns;

    public async Task SynchronizeTreeAsync(string path)
    {
        var version = ++_treeVersion;
        var rootPath = System.IO.Path.GetPathRoot(path)!;
        var node = Roots.FirstOrDefault(n => string.Equals(n.Path, rootPath, StringComparison.OrdinalIgnoreCase));
        if (node is null) { node = new(rootPath, _catalog, rootPath); Roots.Add(node); }
        var relative = System.IO.Path.GetRelativePath(rootPath, path);
        foreach (var part in relative.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            await node.LoadAsync();
            if (version != _treeVersion || _closed) return;
            node.IsExpanded = true;
            var childPath = System.IO.Path.Combine(node.Path, part);
            var child = node.Children.FirstOrDefault(n => string.Equals(n.Path, childPath, StringComparison.OrdinalIgnoreCase));
            // An explicitly opened hidden folder must still be reachable in the tree.
            if (child is null) { child = new(childPath, _catalog); node.Children.Add(child); }
            node = child;
        }
        if (version != _treeVersion || _closed) return;
        if (SelectedFolder is not null) SelectedFolder.IsSelected = false;
        SelectedFolder = node; node.IsSelected = true;
    }

    private void SelectedPhotoChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoItem.Image) && Preview is null && Selected?.Image is { } image) Preview = image;
    }

    private async Task LoadPreviewAsync(PhotoItem? photo)
    {
        var version = ++_previewVersion;
        Preview = photo?.DetailPreview ?? photo?.Image;
        PreviewError = ""; Dimensions = photo?.Dimensions ?? "—";
        if (photo is null) return;
        if (photo.DetailPreview is not null) { TouchPreview(photo); return; }
        try
        {
            await _previewGate.WaitAsync();
            (BitmapSource Image, int Width, int Height) result;
            try
            {
                if (version != _previewVersion || _closed) return;
                result = await Task.Run(() => WpfThumbnailGenerator.DecodeWithInfo(photo.File.FullPath, 900));
            }
            finally { _previewGate.Release(); }
            if (version != _previewVersion || _closed) return;
            photo.DetailPreview = result.Image; photo.Dimensions = $"{result.Width:N0} × {result.Height:N0}";
            TouchPreview(photo);
            Preview = result.Image; Dimensions = photo.Dimensions;
        }
        catch (Exception) { if (version == _previewVersion && Preview is null) PreviewError = "Не удалось прочитать изображение"; }
    }

    private void TouchPreview(PhotoItem photo)
    {
        _recentPreviews.Remove(photo); _recentPreviews.AddFirst(photo);
        while (_recentPreviews.Count > 8) { _recentPreviews.Last!.Value.DetailPreview = null; _recentPreviews.RemoveLast(); }
    }

    private void ToggleFavorite()
    {
        var existing = Favorites.FirstOrDefault(f => f.Path == Folder);
        if (existing is null) Favorites.Add(new(FolderTitle, Folder, "\uE734")); else Favorites.Remove(existing);
        Settings.Favorites = Favorites.Select(f => f.Path).ToList();
        Raise(nameof(FavoriteLabel)); Raise(nameof(FavoriteIcon)); SaveSettings();
    }
    public async Task<string> CacheSummaryAsync()
    {
        var stats = await _cache.GetStatisticsAsync();
        return $"{stats.Entries:N0} миниатюр · {PhotoItem.FormatSize(stats.Bytes)} / 512 МБ";
    }
    public async Task ClearCacheAsync() { await _cache.ClearAsync(); Status = "Кеш миниатюр очищен"; }
    public void SaveSettings()
    {
        try { _store.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = $"Не удалось сохранить настройки: {ex.Message}"; }
    }
    public void Close()
    {
        _closed = true;
        _browseCancellation?.Cancel(); _searchCancellation?.Cancel();
        foreach (var photo in Photos) photo.Loading?.Cancel();
        SaveSettings();
    }
}
