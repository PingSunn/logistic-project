using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace logistic;

public partial class SettingsWindow : UserControl
{
    private readonly UserControl _containerView;

    public SettingsWindow()
    {
        InitializeComponent();
        _containerView = BuildContainerView();
        SidebarList.SelectedIndex = 0;
    }

    private void SidebarList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.SelectedItem is not ListBoxItem item) return;

        ContentArea.Content = (item.Tag as string) switch
        {
            "Container" => _containerView,
            _           => null
        };
    }

    // ── Container page ────────────────────────────────────────────────────────

    private StackPanel _rowsPanel = null!;
    private readonly List<EditRow> _editRows = [];

    private record EditRow(TextBox Name, TextBox SizeLabel, TextBox W, TextBox L, TextBox H, StackPanel Container, TextBlock Hint);

    private UserControl BuildContainerView()
    {
        var root = new StackPanel { Spacing = 12 };

        // Title + Import / Export
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        titleRow.Children.Add(new TextBlock
        {
            Text = "Container Sizes",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var importBtn = new Button { Content = "Import", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        importBtn.Click += ImportBtn_Click;
        Grid.SetColumn(importBtn, 1);
        titleRow.Children.Add(importBtn);

        var exportBtn = new Button { Content = "Export" };
        exportBtn.Click += ExportBtn_Click;
        Grid.SetColumn(exportBtn, 2);
        titleRow.Children.Add(exportBtn);

        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = "Enter actual (nominal) dimensions. Calculations subtract 15 cm per side.",
            FontSize = 12,
            Foreground = Brushes.Gray
        });

        // Column headers
        root.Children.Add(BuildHeaderRow());

        // Editable rows
        _rowsPanel = new StackPanel { Spacing = 4 };
        foreach (var c in ContainerSpec.All)
            _rowsPanel.Children.Add(BuildEditRow(c));
        root.Children.Add(_rowsPanel);

        // Add + Save
        var bottomRow = new Grid();
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var addBtn = new Button { Content = "+ Add", HorizontalAlignment = HorizontalAlignment.Left };
        addBtn.Click += (_, _) => _rowsPanel.Children.Add(BuildEditRow(null));
        bottomRow.Children.Add(addBtn);

        var saveBtn = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right };
        saveBtn.Click += SaveBtn_Click;
        Grid.SetColumn(saveBtn, 1);
        bottomRow.Children.Add(saveBtn);

        root.Children.Add(bottomRow);

        return new UserControl { Content = new ScrollViewer { Content = root } };
    }

    private static Grid BuildHeaderRow()
    {
        var g = MakeRowGrid();
        string[] labels = ["Name", "Size", "W (cm)", "L (cm)", "H (cm)", ""];
        for (int i = 0; i < labels.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = labels[i],
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    private StackPanel BuildEditRow(ContainerSpec? spec)
    {
        var g = MakeRowGrid();
        g.Margin = new Avalonia.Thickness(0, 2, 0, 0);

        TextBox MakeBox(string text, int col)
        {
            var tb = new TextBox { Text = text, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
            return tb;
        }

        var nameBox = MakeBox(spec?.Name ?? "", 0);
        var sizeBox = MakeBox(spec?.SizeLabel ?? "", 1);
        var wBox    = MakeBox(spec?.NominalW.ToString() ?? "0", 2);
        var lBox    = MakeBox(spec?.NominalL.ToString() ?? "0", 3);
        var hBox    = MakeBox(spec?.NominalH.ToString() ?? "0", 4);

        var delBtn = new Button
        {
            Content = "✕",
            Padding = new Avalonia.Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(delBtn, 5);
        g.Children.Add(delBtn);

        // Interior-dimension hint
        var hint = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Avalonia.Thickness(2, 0, 0, 4)
        };

        void UpdateHint()
        {
            int.TryParse(wBox.Text, out var w);
            int.TryParse(lBox.Text, out var l);
            int.TryParse(hBox.Text, out var h);
            hint.Text = $"Used in calc: W {w - 15} × L {l - 15} × H {h - 15} cm";
        }

        wBox.TextChanged += (_, _) => UpdateHint();
        lBox.TextChanged += (_, _) => UpdateHint();
        hBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();

        var wrapper = new StackPanel { Spacing = 0 };
        wrapper.Children.Add(g);
        wrapper.Children.Add(hint);

        var row = new EditRow(nameBox, sizeBox, wBox, lBox, hBox, wrapper, hint);
        _editRows.Add(row);

        delBtn.Click += (_, _) =>
        {
            _editRows.Remove(row);
            _rowsPanel.Children.Remove(wrapper);
        };

        return wrapper;
    }

    private static Grid MakeRowGrid()
    {
        var g = new Grid();
        // Name=2*, Size=1*, W=1*, L=1*, H=1*, Del=Auto
        g.ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        return g;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        var specs = CollectSpecs();
        ContainerSpec.All.Clear();
        ContainerSpec.All.AddRange(specs);
    }

    // ── Import / Export ───────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private async void ImportBtn_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Containers",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        var specs = await JsonSerializer.DeserializeAsync<ContainerSpec[]>(stream);
        if (specs is null) return;

        _editRows.Clear();
        _rowsPanel.Children.Clear();
        foreach (var c in specs)
            _rowsPanel.Children.Add(BuildEditRow(c));
    }

    private async void ExportBtn_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Containers",
            SuggestedFileName = "containers.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, CollectSpecs(), _jsonOpts);
    }

    private List<ContainerSpec> CollectSpecs()
    {
        var specs = new List<ContainerSpec>();
        foreach (var row in _editRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name.Text)) continue;
            int.TryParse(row.W.Text, out var w);
            int.TryParse(row.L.Text, out var l);
            int.TryParse(row.H.Text, out var h);
            specs.Add(new ContainerSpec(
                row.Name.Text.Trim(),
                row.SizeLabel.Text?.Trim() ?? "",
                w, l, h));
        }
        return specs;
    }
}
