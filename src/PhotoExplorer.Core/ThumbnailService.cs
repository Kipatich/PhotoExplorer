namespace PhotoExplorer.Core;

public sealed class ThumbnailService(IThumbnailCache cache, IThumbnailGenerator generator)
{
    private readonly SemaphoreSlim _workers = new(3);
    public event EventHandler<ThumbnailReadyEventArgs>? Ready;

    public async Task<Thumbnail> GetAsync(PhotoFile file, int size, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 2048);
        await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = await cache.GetAsync(file, size, cancellationToken).ConfigureAwait(false);
            if (cached is not null) { Ready?.Invoke(this, new(file, true)); return cached; }
            var thumbnail = await generator.GenerateAsync(file, size, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Do not cache pixels under an obsolete file signature if the file changed while decoding.
            var current = PhotoFile.FromPath(file.FullPath);
            if (current.Length == file.Length && current.LastWriteUtc == file.LastWriteUtc)
                await cache.PutAsync(file, size, thumbnail, cancellationToken).ConfigureAwait(false);
            Ready?.Invoke(this, new(file, false));
            return thumbnail;
        }
        finally { _workers.Release(); }
    }
}
