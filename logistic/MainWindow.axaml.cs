using Avalonia.Controls;
using Avalonia.Interactivity;

namespace logistic;

public partial class MainWindow : Window
{
    private readonly SettingsWindow _settingsView = new();

    public MainWindow() => InitializeComponent();

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        MainContent.Content = _settingsView;
        BackButton.IsVisible = true;
        SettingsButton.IsVisible = false;
        TitleText.Text = "Settings";
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        MainContent.Content = null;
        BackButton.IsVisible = false;
        SettingsButton.IsVisible = true;
        TitleText.Text = "Logistic";
    }
}
