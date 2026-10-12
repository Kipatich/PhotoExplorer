using System.IO;
using System.Windows;
using PhotoExplorer.Core;
using PhotoExplorer.Storage;

namespace PhotoExplorer.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var options = LaunchOptions.Parse(e.Args);
            var store = new SettingsStore(AppPaths.SettingsFile);
            var settings = store.Load();
            if (options.Light) settings.Theme = "Light";
            ThemeManager.Apply(settings.Theme);
            var cache = new SqliteThumbnailCache(AppPaths.CacheFile);
            var vm = new MainViewModel(new PhotoCatalog(), cache, new ThumbnailService(cache, new WpfThumbnailGenerator()), store, settings);
            var window = new MainWindow(vm);
            MainWindow = window;
            window.Show();
            var folder = options.Demo ? DemoImages.Ensure(Path.Combine(AppPaths.DataDirectory, "Демонстрация"))
                : options.File is not null ? Path.GetDirectoryName(Path.GetFullPath(options.File))!
                : options.Folder ?? (Directory.Exists(settings.LastFolder) ? settings.LastFolder : null)
                  ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            await vm.NavigateAsync(folder);
            if (options.File is not null) vm.SelectPath(options.File);
            if (options.Help) window.ShowHelp();
            if (options.Fullscreen) window.ShowViewer();
            if (store.LoadWarning is not null) vm.Status = store.LoadWarning;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не удалось запустить Фото проводник", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

public sealed record LaunchOptions(string? Folder, string? File, bool Demo, bool Fullscreen, bool Light, bool Help)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? folder = null, file = null;
        bool demo = false, fullscreen = false, light = false, help = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Для {args[i - 1]} необходимо значение.");
            switch (args[i])
            {
                case "--folder": folder = Value(); break;
                case "--file": file = Value(); break;
                case "--demo": demo = true; break;
                case "--fullscreen": fullscreen = true; break;
                case "--light": light = true; break;
                case "--help" or "-h": help = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException($"Неизвестный параметр: {args[i]}");
                    if (Directory.Exists(args[i])) folder = args[i]; else file = args[i];
                    break;
            }
        }
        if (file is not null && !System.IO.File.Exists(file)) throw new FileNotFoundException("Изображение не найдено.", file);
        if (folder is not null && !Directory.Exists(folder)) throw new DirectoryNotFoundException($"Папка не найдена: {folder}");
        return new(folder, file, demo, fullscreen, light, help);
    }
}
