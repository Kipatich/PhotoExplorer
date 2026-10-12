using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PhotoExplorer.Desktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var light = theme == "Light";
        var colors = new Dictionary<string, string>
        {
            ["Canvas"] = light ? "#F4F5F7" : "#111419",
            ["Panel"] = light ? "#FFFFFF" : "#191D24",
            ["Raised"] = light ? "#EDEFF3" : "#222832",
            ["Hover"] = light ? "#E2E6ED" : "#2B3340",
            ["Line"] = light ? "#DCE0E7" : "#2B313B",
            ["Text"] = light ? "#192130" : "#E9ECF2",
            ["Muted"] = light ? "#626D7D" : "#919BAA",
            ["Accent"] = light ? "#7C3AED" : "#C4B5FD",
            ["Selection"] = light ? "#EDE9FE" : "#382650"
        };
        foreach (var pair in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
            brush.Freeze();
            Application.Current.Resources[pair.Key] = brush;
        }
    }
}
