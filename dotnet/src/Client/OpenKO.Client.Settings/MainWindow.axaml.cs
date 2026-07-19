using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenKO.Client.Settings;

/// <summary>
/// The settings dialog. OK writes options.json and closes; Übernehmen (Apply) writes
/// without closing; Abbrechen (Cancel) closes without writing — the COptionDlg buttons.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Apply();
        Close();
    }

    private void OnApply(object? sender, RoutedEventArgs e) => ViewModel?.Apply();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
