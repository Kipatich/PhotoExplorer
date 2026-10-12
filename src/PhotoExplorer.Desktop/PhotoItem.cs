using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using PhotoExplorer.Core;

namespace PhotoExplorer.Desktop;

public sealed class PhotoItem(PhotoFile file) : ObservableObject
{
    public PhotoFile File { get; } = file;
    public string Name => File.Name;
    public string Caption => $"{File.Extension}  ·  {FormatSize(File.Length)}";
    public string Date => File.LastWriteUtc.ToLocalTime().ToString("dd MMMM yyyy, HH:mm");
    private BitmapSource? _image;
    public BitmapSource? Image { get => _image; set => Set(ref _image, value); }
    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    private string _placeholder = "Загрузка…";
    public string Placeholder { get => _placeholder; set => Set(ref _placeholder, value); }
    public CancellationTokenSource? Loading { get; set; }
    public BitmapSource? DetailPreview { get; set; }
    public string? Dimensions { get; set; }
    public static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576d:0.#} МБ" : $"{Math.Max(1, bytes / 1024d):0.#} КБ";
}

public sealed record PhotoRow(IReadOnlyList<PhotoItem> Items, int Columns);
public sealed record Location(string Name, string Path, string Icon);

public sealed class FolderNode : ObservableObject
{
    private readonly IPhotoCatalog _catalog;
    private Task? _loadTask;
    private bool _expanded;
    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = [];
    public bool IsExpanded
    {
        get => _expanded;
        set { if (Set(ref _expanded, value) && value) _ = LoadAsync(); }
    }
    public FolderNode(string path, IPhotoCatalog catalog, string? name = null, bool? hasChildren = null, bool placeholder = false)
    {
        Path = path; Name = name ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/')); _catalog = catalog;
        if (placeholder) return;
        if (hasChildren == true) AddPlaceholder();
        else if (hasChildren is null) _ = ProbeAsync();
    }
    private void AddPlaceholder() => Children.Add(new("", _catalog, "Загрузка…", placeholder: true));
    private async Task ProbeAsync()
    {
        var hasChildren = await Task.Run(() => _catalog.HasSubdirectories(Path));
        if (_loadTask is null && hasChildren) AddPlaceholder();
    }
    public Task LoadAsync() => _loadTask ??= LoadChildrenAsync();
    private async Task LoadChildrenAsync()
    {
        var children = await Task.Run(() => _catalog.GetDirectories(Path)
            .Select(path => (Path: path, HasChildren: _catalog.HasSubdirectories(path))).ToArray());
        Children.Clear();
        foreach (var child in children) Children.Add(new(child.Path, _catalog, hasChildren: child.HasChildren));
    }
}
