namespace PhotoExplorer.Core;

public sealed class NavigationHistory
{
    private readonly List<string> _entries = [];
    private int _index = -1;
    public string? Current => _index >= 0 ? _entries[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index + 1 < _entries.Count;
    public event EventHandler? Changed;

    public void Visit(string folder)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (string.Equals(Current, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        if (CanGoForward) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(path);
        _index = _entries.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? Back() { if (!CanGoBack) return null; _index--; Changed?.Invoke(this, EventArgs.Empty); return Current; }
    public string? Forward() { if (!CanGoForward) return null; _index++; Changed?.Invoke(this, EventArgs.Empty); return Current; }
}
