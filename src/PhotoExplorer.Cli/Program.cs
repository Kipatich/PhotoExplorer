using System.Text;
using System.Text.Json;
using PhotoExplorer.Core;
using PhotoExplorer.Storage;

Console.OutputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    {
        Console.WriteLine("""
            Фото проводник · консоль

            list <папка> [--search <строка>] [--recursive] [--depth <уровни>] [--sort name|newest|oldest|largest] [--json]
            cache stats [--db <файл>]
            cache clear [--db <файл>]

            Примеры:
              PhotoExplorer.Cli list "D:\Фото" --recursive --search море
              PhotoExplorer.Cli list "D:\Фото" --sort newest --json
              PhotoExplorer.Cli cache stats

            Поддерживаются JPEG, PNG, BMP, GIF, TIFF, ICO, WDP, JXR.
            Поиск выполняется по имени. Ctrl+C отменяет операцию.
            --depth: глубина подпапок; 0 — без ограничений. Действует вместе с --recursive.
            Коды завершения: 0 — успех, 1 — ошибка, 130 — отмена.
            """);
        return 0;
    }
    if (args[0] == "list")
    {
        if (args.Length < 2) throw new ArgumentException("Укажите папку после list.");
        var folder = args[1]; var query = ""; var recursive = false; var json = false; var sort = PhotoSort.Name; var depth = 0;
        for (var i = 2; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Пропущено значение параметра.");
            switch (args[i])
            {
                case "--search": query = Value(); break;
                case "--recursive": recursive = true; break;
                case "--depth":
                    if (!int.TryParse(Value(), out depth) || depth < 0) throw new ArgumentException("Глубина должна быть целым числом от 0.");
                    break;
                case "--json": json = true; break;
                case "--sort": sort = Value() switch { "name" => PhotoSort.Name, "newest" => PhotoSort.Newest, "oldest" => PhotoSort.Oldest, "largest" => PhotoSort.Largest, var value => throw new ArgumentException($"Неизвестная сортировка: {value}") }; break;
                default: throw new ArgumentException($"Неизвестный параметр: {args[i]}");
            }
        }
        var result = await new PhotoCatalog().BrowseAsync(new(folder, query, recursive, sort, depth), cancellation.Token);
        if (json) Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            foreach (var file in result.Files) Console.WriteLine($"{file.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {file.Length,12:N0}  {file.FullPath}");
            Console.WriteLine($"Найдено: {result.Files.Count}. Пропущено папок: {result.SkippedDirectories}.");
        }
        return 0;
    }
    if (args[0] == "cache")
    {
        if (args.Length < 2 || args[1] is not ("stats" or "clear")) throw new ArgumentException("Используйте cache stats или cache clear.");
        var path = AppPaths.CacheFile;
        if (args.Length > 2)
        {
            if (args.Length != 4 || args[2] != "--db") throw new ArgumentException("Допустимый параметр: --db <файл>.");
            path = args[3];
        }
        var cache = new SqliteThumbnailCache(path);
        if (args[1] == "clear") { await cache.ClearAsync(cancellation.Token); Console.WriteLine("Кеш очищен."); }
        else { var stats = await cache.GetStatisticsAsync(cancellation.Token); Console.WriteLine($"Миниатюр: {stats.Entries:N0}\nРазмер данных: {stats.Bytes:N0} байт\nБаза: {Path.GetFullPath(path)}"); }
        return 0;
    }
    throw new ArgumentException($"Неизвестная команда: {args[0]}. Используйте --help.");
}
catch (OperationCanceledException) { Console.Error.WriteLine("Операция отменена."); return 130; }
catch (Exception ex) { Console.Error.WriteLine($"Ошибка: {ex.Message}"); return 1; }
