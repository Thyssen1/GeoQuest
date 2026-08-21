using Avalonia.Controls;
using GeoQuest.ViewModels;

namespace GeoQuest.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribed;

    public MainWindow()
    {
        InitializeComponent();

        // Exit is a window concern, so the shell announces the intent and the window
        // carries it out. Re-wired on DataContext change so the previewer behaves too.
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.ExitRequested -= OnExitRequested;
            }

            _subscribed = DataContext as MainWindowViewModel;

            if (_subscribed is not null)
            {
                _subscribed.ExitRequested += OnExitRequested;
            }
        };
    }

    private void OnExitRequested(object? sender, System.EventArgs e) => Close();
}
