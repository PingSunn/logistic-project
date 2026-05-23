using Avalonia.Controls;

namespace CargoFit;

public partial class SettingsWindow : UserControl
{
    private readonly ContainerSettingsPanel _containerView = new();
    private readonly ProductSettingsPanel   _productView   = new();

    public SettingsWindow()
    {
        InitializeComponent();
        SidebarList.SelectedIndex = 0;
    }

    private void SidebarList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ContentArea.Content = ((SidebarList.SelectedItem as ListBoxItem)?.Tag as string) switch
        {
            "Container" => (Control)_containerView,
            "Product"   => _productView,
            _           => null
        };
    }
}
