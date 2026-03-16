using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace logistic;

public partial class SettingsWindow : UserControl
{
    private readonly UserControl _containerView;
    private readonly UserControl _productView;

    public SettingsWindow()
    {
        InitializeComponent();
        _containerView = BuildContainerView();
        _productView   = BuildProductView();
        SidebarList.SelectedIndex = 0;
    }

    private void SidebarList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.SelectedItem is not ListBoxItem item) return;
        ContentArea.Content = (item.Tag as string) switch
        {
            "Container" => _containerView,
            "Product"   => _productView,
            _           => null
        };
    }

    // ── Container page ────────────────────────────────────────────────────────

    private StackPanel _rowsPanel = null!;
    private readonly List<EditRow> _editRows = [];

    private record EditRow(TextBox Name, TextBox SizeLabel, TextBox W, TextBox L, TextBox H, Border Card, TextBlock Hint);

    private UserControl BuildContainerView()
    {
        var root = new StackPanel { Spacing = 20, Margin = new Thickness(32, 28) };

        // ── Header ───────────────────────────────────────────────────────────
        var header = new Grid();
        header.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        header.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var titleText = new TextBlock
        {
            Text = "Container Sizes",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#1E293B")),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleText);

        var importBtn = new Button { Content = "↑  Import", Classes = { "outline" }, Margin = new Thickness(0, 0, 8, 0) };
        importBtn.Click += ImportBtn_Click;
        Grid.SetColumn(importBtn, 1);
        header.Children.Add(importBtn);

        var exportBtn = new Button { Content = "↓  Export", Classes = { "outline" } };
        exportBtn.Click += ExportBtn_Click;
        Grid.SetColumn(exportBtn, 2);
        header.Children.Add(exportBtn);

        var subtitle = new TextBlock
        {
            Text = "Enter nominal (actual) dimensions — 15 cm is subtracted per side for calculations.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#64748B")),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(subtitle, 1);
        Grid.SetColumnSpan(subtitle, 3);
        header.Children.Add(subtitle);

        root.Children.Add(header);

        // ── Rows ─────────────────────────────────────────────────────────────
        _rowsPanel = new StackPanel { Spacing = 10 };
        foreach (var c in ContainerSpec.All)
            _rowsPanel.Children.Add(BuildCardRow(c));
        root.Children.Add(_rowsPanel);

        // ── Footer ───────────────────────────────────────────────────────────
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var addBtn = new Button { Content = "+  Add Container", Classes = { "outline" }, HorizontalAlignment = HorizontalAlignment.Left };
        addBtn.Click += (_, _) => _rowsPanel.Children.Add(BuildCardRow(null));
        footer.Children.Add(addBtn);

        var saveStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#16A34A")),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            IsVisible = false
        };
        Grid.SetColumn(saveStatus, 1);
        footer.Children.Add(saveStatus);

        var saveBtn = new Button { Content = "Save Changes", Classes = { "primary" } };
        Grid.SetColumn(saveBtn, 2);
        footer.Children.Add(saveBtn);

        saveBtn.Click += async (_, e) =>
        {
            SaveBtn_Click(saveBtn, e);
            saveStatus.Text = "✓  Saved";
            saveStatus.IsVisible = true;
            await System.Threading.Tasks.Task.Delay(2000);
            saveStatus.IsVisible = false;
        };

        root.Children.Add(footer);

        return new UserControl { Content = new ScrollViewer { Content = root } };
    }

    private Border BuildCardRow(ContainerSpec? spec)
    {
        // ── Card layout ──────────────────────────────────────────────────────
        //  Row 1:  [Name TextBox ──────────]  [Size TextBox]  [✕]
        //  Row 2:  W [___] cm    L [___] cm    H [___] cm
        //  Row 3:  hint text (gray, small)

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14),
        };

        var inner = new StackPanel { Spacing = 10 };

        // Row 1 — name + size label + delete
        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row1.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(110)));
        row1.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var nameBox = new TextBox
        {
            Text = spec?.Name ?? "",
            Watermark = "Container name",
            FontWeight = FontWeight.Medium,
            Margin = new Thickness(0, 0, 8, 0)
        };
        row1.Children.Add(nameBox);

        var sizeBox = new TextBox
        {
            Text = spec?.SizeLabel ?? "",
            Watermark = "e.g. 20 ft",
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(sizeBox, 1);
        row1.Children.Add(sizeBox);

        var delBtn = new Button
        {
            Content = "✕",
            Classes = { "danger" },
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(delBtn, 2);
        row1.Children.Add(delBtn);

        inner.Children.Add(row1);

        // Row 2 — W / L / H
        var row2 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,*,8,*") };

        TextBox MakeDimBox(string val, string label, int col)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
            });
            var box = new TextBox { Text = val };
            stack.Children.Add(box);
            Grid.SetColumn(stack, col);
            row2.Children.Add(stack);
            return box;
        }

        var wBox = MakeDimBox(spec?.NominalW.ToString() ?? "0", "Width (cm)", 0);
        var lBox = MakeDimBox(spec?.NominalL.ToString() ?? "0", "Length (cm)", 2);
        var hBox = MakeDimBox(spec?.NominalH.ToString() ?? "0", "Height (cm)", 4);

        inner.Children.Add(row2);

        // Row 3 — hint
        var hint = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
        };

        void UpdateHint()
        {
            int.TryParse(wBox.Text, out var w);
            int.TryParse(lBox.Text, out var l);
            int.TryParse(hBox.Text, out var h);
            hint.Text = $"Effective interior: {w - 15} × {l - 15} × {h - 15} cm";
        }

        wBox.TextChanged += (_, _) => UpdateHint();
        lBox.TextChanged += (_, _) => UpdateHint();
        hBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();

        inner.Children.Add(hint);
        card.Child = inner;

        var row = new EditRow(nameBox, sizeBox, wBox, lBox, hBox, card, hint);
        _editRows.Add(row);

        delBtn.Click += (_, _) =>
        {
            _editRows.Remove(row);
            _rowsPanel.Children.Remove(card);
        };

        return card;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        var specs = CollectSpecs();
        ContainerSpec.All.Clear();
        ContainerSpec.All.AddRange(specs);
        ContainerSpec.Save();
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
            _rowsPanel.Children.Add(BuildCardRow(c));
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

    // ── Product page ──────────────────────────────────────────────────────────

    private StackPanel _productRowsPanel = null!;
    private readonly List<ProductEditRow> _productEditRows = [];

    private record ProductEditRow(
        TextBox Description, TextBox Content, TextBox PackSize,
        TextBox Weight, CheckBox RscBox, CheckBox AutoBox,
        TextBox W, TextBox L, TextBox H,
        Border Card, TextBlock CbmHint);

    private UserControl BuildProductView()
    {
        var root = new StackPanel { Spacing = 20, Margin = new Thickness(32, 28) };

        // ── Header ───────────────────────────────────────────────────────────
        var header = new Grid();
        header.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        header.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var titleText = new TextBlock
        {
            Text = "สินค้า",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#1E293B")),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleText);

        var templateBtn = new Button { Content = "⬇  Template", Classes = { "ghost" }, Margin = new Thickness(0, 0, 8, 0) };
        templateBtn.Click += ProductTemplateBtn_Click;
        Grid.SetColumn(templateBtn, 1);
        header.Children.Add(templateBtn);

        var importBtn = new Button { Content = "↑  Import CSV", Classes = { "outline" }, Margin = new Thickness(0, 0, 8, 0) };
        importBtn.Click += ProductImportBtn_Click;
        Grid.SetColumn(importBtn, 2);
        header.Children.Add(importBtn);

        var exportBtn = new Button { Content = "↓  Export CSV", Classes = { "outline" } };
        exportBtn.Click += ProductExportBtn_Click;
        Grid.SetColumn(exportBtn, 3);
        header.Children.Add(exportBtn);

        var subtitle = new TextBlock
        {
            Text = "กรอกขนาดกล่องสินค้า (ซม.) — CBM คำนวณอัตโนมัติ",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#64748B")),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(subtitle, 1);
        Grid.SetColumnSpan(subtitle, 4);
        header.Children.Add(subtitle);

        root.Children.Add(header);

        // ── Rows ─────────────────────────────────────────────────────────────
        _productRowsPanel = new StackPanel { Spacing = 10 };
        foreach (var p in ProductSpec.All)
            _productRowsPanel.Children.Add(BuildProductCard(p));
        root.Children.Add(_productRowsPanel);

        // ── Footer ───────────────────────────────────────────────────────────
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var addBtn = new Button { Content = "+  เพิ่มสินค้า", Classes = { "outline" }, HorizontalAlignment = HorizontalAlignment.Left };
        addBtn.Click += (_, _) => _productRowsPanel.Children.Add(BuildProductCard(null));
        footer.Children.Add(addBtn);

        var saveStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#16A34A")),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            IsVisible = false
        };
        Grid.SetColumn(saveStatus, 1);
        footer.Children.Add(saveStatus);

        var saveBtn = new Button { Content = "Save Changes", Classes = { "primary" } };
        Grid.SetColumn(saveBtn, 2);
        footer.Children.Add(saveBtn);

        saveBtn.Click += async (_, _) =>
        {
            var specs = CollectProductSpecs();
            ProductSpec.All.Clear();
            ProductSpec.All.AddRange(specs);
            ProductSpec.Save();
            saveStatus.Text = "✓  Saved";
            saveStatus.IsVisible = true;
            await System.Threading.Tasks.Task.Delay(2000);
            saveStatus.IsVisible = false;
        };

        root.Children.Add(footer);

        return new UserControl { Content = new ScrollViewer { Content = root } };
    }

    private Border BuildProductCard(ProductSpec? spec)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14),
        };

        var inner = new StackPanel { Spacing = 10 };

        // Row 1 — Description | Content | PackSize | ✕
        var row1 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,120,8,100,8,Auto") };

        var descBox = new TextBox { Text = spec?.Description ?? "", Watermark = "ชื่อสินค้า", FontWeight = FontWeight.Medium };
        row1.Children.Add(descBox);

        var contentBox = new TextBox { Text = spec?.Content ?? "", Watermark = "ปริมาณ เช่น 365ML" };
        Grid.SetColumn(contentBox, 2);
        row1.Children.Add(contentBox);

        var packBox = new TextBox { Text = spec?.PackSize ?? "", Watermark = "Pack Size เช่น Pack 24" };
        Grid.SetColumn(packBox, 4);
        row1.Children.Add(packBox);

        var delBtn = new Button { Content = "✕", Classes = { "danger" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(delBtn, 6);
        row1.Children.Add(delBtn);

        inner.Children.Add(row1);

        // Row 2 — Weight | RSC checkbox | Auto checkbox
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        var weightLabel = new StackPanel { Spacing = 4 };
        weightLabel.Children.Add(new TextBlock { Text = "น้ำหนัก (KG)", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(Color.Parse("#94A3B8")) });
        var weightBox = new TextBox { Text = spec?.WeightPerBoxKg.ToString("G") ?? "0", Width = 90 };
        weightLabel.Children.Add(weightBox);
        row2.Children.Add(weightLabel);

        var rscBox = new CheckBox { Content = "RSC", IsChecked = spec?.BoxTypeRsc ?? false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        var autoBox = new CheckBox { Content = "Auto", IsChecked = spec?.BoxTypeAuto ?? false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        row2.Children.Add(rscBox);
        row2.Children.Add(autoBox);

        inner.Children.Add(row2);

        // Row 3 — W / L / H
        var row3 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,*,8,*") };

        TextBox MakeDimBox(string val, string label, int col)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
            });
            var box = new TextBox { Text = val };
            stack.Children.Add(box);
            Grid.SetColumn(stack, col);
            row3.Children.Add(stack);
            return box;
        }

        var wBox = MakeDimBox(spec?.W.ToString("G") ?? "0", "W (ซม.)", 0);
        var lBox = MakeDimBox(spec?.L.ToString("G") ?? "0", "L (ซม.)", 2);
        var hBox = MakeDimBox(spec?.H.ToString("G") ?? "0", "H (ซม.)", 4);

        inner.Children.Add(row3);

        // Row 4 — CBM hint
        var cbmHint = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
        };

        void UpdateCbm()
        {
            double.TryParse(wBox.Text, out var w);
            double.TryParse(lBox.Text, out var l);
            double.TryParse(hBox.Text, out var h);
            var cbm = w * l * h / 1_000_000;
            cbmHint.Text = $"CBM: {cbm:F6} m³";
        }

        wBox.TextChanged += (_, _) => UpdateCbm();
        lBox.TextChanged += (_, _) => UpdateCbm();
        hBox.TextChanged += (_, _) => UpdateCbm();
        UpdateCbm();

        inner.Children.Add(cbmHint);
        card.Child = inner;

        var productRow = new ProductEditRow(descBox, contentBox, packBox, weightBox, rscBox, autoBox, wBox, lBox, hBox, card, cbmHint);
        _productEditRows.Add(productRow);

        delBtn.Click += (_, _) =>
        {
            _productEditRows.Remove(productRow);
            _productRowsPanel.Children.Remove(card);
        };

        return card;
    }

    private List<ProductSpec> CollectProductSpecs()
    {
        var specs = new List<ProductSpec>();
        foreach (var row in _productEditRows)
        {
            if (string.IsNullOrWhiteSpace(row.Description.Text)) continue;
            double.TryParse(row.Weight.Text, out var weight);
            double.TryParse(row.W.Text, out var w);
            double.TryParse(row.L.Text, out var l);
            double.TryParse(row.H.Text, out var h);
            specs.Add(new ProductSpec(
                row.Description.Text.Trim(),
                row.Content.Text?.Trim() ?? "",
                row.PackSize.Text?.Trim() ?? "",
                weight,
                row.RscBox.IsChecked == true,
                row.AutoBox.IsChecked == true,
                w, l, h));
        }
        return specs;
    }

    // ── Product Import / Export (CSV) ─────────────────────────────────────────

    private const string CsvHeader = "Description,Content,PackSize,WeightPerBoxKg,BoxTypeRsc,BoxTypeAuto,W,L,H";

    private async void ProductTemplateBtn_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download Product Template",
            SuggestedFileName = "products_template.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });

        if (file is null) return;

        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        sb.AppendLine("Aloe,365ML,Pack 24,9.9,False,True,21.9,33.4,20.5");

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(sb.ToString());
    }

    private async void ProductImportBtn_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Products",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);

        var specs = new List<ProductSpec>();
        var isFirst = true;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (isFirst) { isFirst = false; continue; } // skip header
            var parts = line.Split(',');
            if (parts.Length < 9) continue;
            double.TryParse(parts[3], out var weight);
            bool.TryParse(parts[4], out var rsc);
            bool.TryParse(parts[5], out var auto);
            double.TryParse(parts[6], out var w);
            double.TryParse(parts[7], out var l);
            double.TryParse(parts[8], out var h);
            specs.Add(new ProductSpec(parts[0], parts[1], parts[2], weight, rsc, auto, w, l, h));
        }

        _productEditRows.Clear();
        _productRowsPanel.Children.Clear();
        foreach (var p in specs)
            _productRowsPanel.Children.Add(BuildProductCard(p));
    }

    private async void ProductExportBtn_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Products",
            SuggestedFileName = "products.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });

        if (file is null) return;

        var specs = CollectProductSpecs();
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        foreach (var p in specs)
            sb.AppendLine($"{p.Description},{p.Content},{p.PackSize},{p.WeightPerBoxKg},{p.BoxTypeRsc},{p.BoxTypeAuto},{p.W},{p.L},{p.H}");

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(sb.ToString());
    }
}
