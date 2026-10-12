namespace PhotoExplorer.Core;

public sealed record PhotoFile(string FullPath, string Name, long Length, DateTime LastWriteUtc)
{
    public string Extension => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public static PhotoFile FromPath(string path)
    {
        var file = new FileInfo(path);
        return new(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc);
    }
}

public enum PhotoSort { Name, Newest, Oldest, Largest }
public sealed record BrowseRequest(string Folder, string Query = "", bool Recursive = false, PhotoSort Sort = PhotoSort.Name, int MaxDepth = 0);
public sealed record BrowseProgress(long FilesScanned, int DirectoriesScanned, int ImagesFound, int PendingDirectories);
public sealed record BrowseResult(IReadOnlyList<PhotoFile> Files, int SkippedDirectories);
public sealed record Thumbnail(byte[] Bytes, int Width, int Height);
public sealed record CacheStatistics(long Entries, long Bytes);
public sealed class ThumbnailReadyEventArgs(PhotoFile file, bool fromCache) : EventArgs
{
    public PhotoFile File { get; } = file;
    public bool FromCache { get; } = fromCache;
}

public interface IPhotoCatalog
{
    Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken cancellationToken = default, IProgress<BrowseProgress>? progress = null);
    IReadOnlyList<string> GetDirectories(string folder);
    bool HasSubdirectories(string folder);
}

public interface IThumbnailGenerator
{
    Task<Thumbnail> GenerateAsync(PhotoFile file, int size, CancellationToken cancellationToken = default);
}

public interface IThumbnailCache
{
    Task<Thumbnail?> GetAsync(PhotoFile file, int size, CancellationToken cancellationToken = default);
    Task PutAsync(PhotoFile file, int size, Thumbnail thumbnail, CancellationToken cancellationToken = default);
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoExplorer");
    public static string CacheFile => Path.Combine(DataDirectory, "thumbnails.db");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
