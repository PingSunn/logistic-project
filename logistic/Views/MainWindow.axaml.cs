using Avalonia.Controls;
using Avalonia.Interactivity;

namespace logistic;

public partial class MainWindow : Window
{
    private readonly SettingsWindow _settingsWindow = new();

    public MainWindow()
    {
        InitializeComponent();
        MainContent.Content = new PlanningView();
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Settings",
            Width = 860,
            Height = 620,
            Content = _settingsWindow
        };
        // Detach on close so the same UserControl can be re-parented next time.
        win.Closed += (_, _) => win.Content = null;
        win.ShowDialog(this);
    }
}
