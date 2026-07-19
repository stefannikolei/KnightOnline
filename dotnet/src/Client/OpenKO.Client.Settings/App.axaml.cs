using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenKO.Client.Configuration;

namespace OpenKO.Client.Settings;

/// <summary>The Avalonia application: loads options.json and shows the settings window.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string dir = Program.SettingsDirectory;
            GameSettings settings = GameSettingsStore.Load(dir);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new SettingsViewModel(settings, dir),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
