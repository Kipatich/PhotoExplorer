using Microsoft.Data.Sqlite;
using PhotoExplorer.Core;
using PhotoExplorer.Storage;
using Xunit;

namespace PhotoExplorer.Tests;

public sealed class BehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PhotoExplorerTests", Guid.NewGuid().ToString("N"));
    public BehaviorTests() => Directory.CreateDirectory(_root);
    private string FileAt(string name, string contents = "pixels")
    {
        var path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); return path;
    }
    [Fact] public async Task SearchFiltersExtensionsAndSupportsNestedCaseInsensitiveMatches()
    {
        FileAt("Море.JPG"); FileAt("море.txt"); FileAt("nested/МОРЕ.png"); FileAt("nested/лес.jpg");
        var catalog = new PhotoCatalog();
        var shallow = await catalog.BrowseAsync(new(_root, "мОрЕ"));
        var recursive = await catalog.BrowseAsync(new(_root, "мОрЕ", true));
        Assert.Single(shallow.Files); Assert.Equal(2, recursive.Files.Count);
    }
    [Fact] public async Task SortUsesFileSizeAndMissingFolderReportsFailure()
    {
        FileAt("small.png", "a"); FileAt("large.png", "abcdef");
        var catalog = new PhotoCatalog();
        Assert.Equal("large.png", (await catalog.BrowseAsync(new(_root, Sort: PhotoSort.Largest))).Files[0].Name);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => catalog.BrowseAsync(new(Path.Combine(_root, "missing"))));
    }
    [Fact] public async Task BrowseHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PhotoCatalog().BrowseAsync(new(_root), cancellation.Token));
    }
    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task DepthIsRelativeToRootAndZeroIsUnlimited(int depth, int expected)
    {
        FileAt("root.jpg"); FileAt("one/one.jpg"); FileAt("one/two/two.png"); FileAt("one/two/three/three.jpg");
        var catalog = new PhotoCatalog();
        var result = await catalog.BrowseAsync(new(_root, Recursive: true, MaxDepth: depth));
        Assert.Equal(expected, result.Files.Count);
        Assert.Single((await catalog.BrowseAsync(new(_root, MaxDepth: depth))).Files);
    }
    [Fact] public async Task ProgressCountsAllFilesAndDirectoriesWithoutInventingATotal()
    {
        FileAt("photo.jpg"); FileAt("text.txt"); FileAt("child/photo.png");
        var events = new List<BrowseProgress>();
        await new PhotoCatalog().BrowseAsync(new(_root, Recursive: true), progress: new SynchronousProgress(events.Add));
        Assert.Equal(0, events[0].FilesScanned);
        Assert.Equal(new BrowseProgress(3, 2, 2, 0), events[^1]);
        Assert.True(events.Zip(events.Skip(1)).All(pair => pair.First.FilesScanned <= pair.Second.FilesScanned));
    }
    [Fact] public void TreeProbeIgnoresFilesAndDetectsSubdirectories()
    {
        var catalog = new PhotoCatalog(); FileAt("photo.jpg");
        Assert.False(catalog.HasSubdirectories(_root));
        Directory.CreateDirectory(Path.Combine(_root, "child"));
        Assert.True(catalog.HasSubdirectories(_root));
        Assert.False(catalog.HasSubdirectories(Path.Combine(_root, "missing")));
    }
    [Fact] public void SearchDepthAndPanelWidthsPersistWithValidation()
    {
        var path = FileAt("preferences.json"); var store = new SettingsStore(path);
        store.Save(new() { SearchDepth = 2, LeftPanelWidth = 350, RightPanelWidth = 410 });
        var settings = store.Load();
        Assert.Equal(2, settings.SearchDepth); Assert.Equal(350, settings.LeftPanelWidth); Assert.Equal(410, settings.RightPanelWidth);
        store.Save(new() { SearchDepth = -3, LeftPanelWidth = -10, RightPanelWidth = 2000 });
        settings = store.Load();
        Assert.Equal(0, settings.SearchDepth); Assert.Equal(170, settings.LeftPanelWidth); Assert.Equal(560, settings.RightPanelWidth);
    }
    private sealed class SynchronousProgress(Action<BrowseProgress> report) : IProgress<BrowseProgress>
    {
        public void Report(BrowseProgress value) => report(value);
    }
    [Fact] public void HistoryDropsForwardBranchAndAvoidsDuplicates()
    {
        var history = new NavigationHistory(); var changed = 0; history.Changed += (_, _) => changed++;
        history.Visit(_root); history.Visit(_root); history.Visit(Path.Combine(_root, "one"));
        Assert.Equal(_root, history.Back()); Assert.True(history.CanGoForward);
        history.Visit(Path.Combine(_root, "two")); Assert.False(history.CanGoForward); Assert.Equal(4, changed);
    }
    [Fact] public void SettingsRecoverFromInvalidJsonAndRoundTrip()
    {
        var path = FileAt("settings.json", "{broken"); var store = new SettingsStore(path);
        Assert.Equal("Dark", store.Load().Theme); Assert.NotNull(store.LoadWarning);
        store.Save(new() { Theme = "Light", ThumbnailSize = 280, Favorites = [_root], LastFolder = _root });
        var loaded = new SettingsStore(path).Load(); Assert.Equal("Light", loaded.Theme); Assert.Equal(280, loaded.ThumbnailSize); Assert.Single(loaded.Favorites);
    }
    [Fact] public async Task SqlitePersistsAndInvalidatesChangedFilesAndSizes()
    {
        var file = PhotoFile.FromPath(FileAt("frame.png")); var database = Path.Combine(_root, "cache.db");
        var cache = new SqliteThumbnailCache(database); var thumbnail = new Thumbnail([1, 2, 3], 100, 80);
        await cache.PutAsync(file, 160, thumbnail);
        var reopened = new SqliteThumbnailCache(database);
        Assert.Equal(thumbnail.Bytes, (await reopened.GetAsync(file, 160))!.Bytes);
        Assert.Null(await reopened.GetAsync(file, 320));
        Assert.Null(await reopened.GetAsync(file with { LastWriteUtc = file.LastWriteUtc.AddSeconds(1) }, 160));
        Assert.Null(await reopened.GetAsync(file with { Length = file.Length + 1 }, 160));
        await reopened.PutAsync(file with { Length = file.Length + 1 }, 160, thumbnail);
        Assert.Equal(1, (await reopened.GetStatisticsAsync()).Entries);
        await reopened.ClearAsync(); Assert.Equal(0, (await reopened.GetStatisticsAsync()).Entries);
    }
    [Fact] public async Task ConcurrentCacheAccessAndServiceReuseRemainConsistent()
    {
        var cache = new SqliteThumbnailCache(Path.Combine(_root, "cache.db"));
        var files = Enumerable.Range(0, 20).Select(i => PhotoFile.FromPath(FileAt($"{i}.png"))).ToArray();
        await Task.WhenAll(files.Select(f => cache.PutAsync(f, 160, new([1, 2], 16, 16))));
        Assert.Equal(20, (await cache.GetStatisticsAsync()).Entries);
        var generator = new FakeGenerator(); var service = new ThumbnailService(cache, generator); var cacheHits = 0;
        service.Ready += (_, e) => { if (e.FromCache) cacheHits++; };
        await service.GetAsync(files[0], 160); Assert.Equal(0, generator.Calls); Assert.Equal(1, cacheHits);
        await service.GetAsync(files[0], 320); await service.GetAsync(files[0], 320); Assert.Equal(1, generator.Calls);
    }
    private sealed class FakeGenerator : IThumbnailGenerator
    {
        public int Calls { get; private set; }
        public Task<Thumbnail> GenerateAsync(PhotoFile file, int size, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(new Thumbnail([3, 4], 16, 16)); }
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }
}
