namespace PhotoExplorer.Core;

public sealed class PhotoCatalog : IPhotoCatalog
{
    public static IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr" };

    public Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken cancellationToken = default, IProgress<BrowseProgress>? progress = null) => Task.Run(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaxDepth);
        var root = Path.GetFullPath(request.Folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Папка не найдена: {root}");
        var files = new List<PhotoFile>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var skipped = 0;
        long scannedFiles = 0;
        var scannedDirectories = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Report(bool force = false)
        {
            if (!force && watch.ElapsedMilliseconds < 100) return;
            progress?.Report(new(scannedFiles, scannedDirectories, files.Count, pending.Count));
            watch.Restart();
        }
        Report(true);
        while (pending.TryPop(out var entry))
        {
            var (folder, depth) = entry;
            cancellationToken.ThrowIfCancellationRequested();
            scannedDirectories++;
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scannedFiles++;
                    Report();
                    if (!SupportedExtensions.Contains(Path.GetExtension(path))) continue;
                    if (!Path.GetFileName(path).Contains(request.Query.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    try { files.Add(PhotoFile.FromPath(path)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (request.Recursive && (request.MaxDepth == 0 || depth < request.MaxDepth))
                    foreach (var directory in Directory.EnumerateDirectories(folder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Push((directory, depth + 1));
                        Report();
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (folder == root) throw;
                skipped++;
            }
            Report();
        }
        Report(true);
        IEnumerable<PhotoFile> sorted = request.Sort switch
        {
            PhotoSort.Newest => files.OrderByDescending(f => f.LastWriteUtc),
            PhotoSort.Oldest => files.OrderBy(f => f.LastWriteUtc),
            PhotoSort.Largest => files.OrderByDescending(f => f.Length),
            _ => files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return new BrowseResult(sorted.ThenByPath().ToArray(), skipped);
    }, cancellationToken);

    public IReadOnlyList<string> GetDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(p => (File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public bool HasSubdirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Any(p =>
                (File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

internal static class PhotoOrdering
{
    public static IEnumerable<PhotoFile> ThenByPath(this IEnumerable<PhotoFile> items) => items is IOrderedEnumerable<PhotoFile> ordered
        ? ordered.ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase) : items;
}
