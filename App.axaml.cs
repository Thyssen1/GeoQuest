using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GeoQuest.Services;
using GeoQuest.ViewModels;
using GeoQuest.Views;

namespace GeoQuest;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var game = new GameViewModel(
                AssetCountryData.Instance,
                new FlagImageLoader(),
                new FileScoreStore());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(game),
            };
            
            desktop.ShutdownRequested += (_, _) => game.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}