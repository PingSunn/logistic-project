using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace logistic;

internal sealed class ProductSettingsPanel : UserControl
{
    private StackPanel _productRowsPanel = null!;
    private readonly List<ProductEditRow> _productEditRows = [];

    private record ProductEditRow(
        TextBox Description, TextBox Content, TextBox PackSize,
        TextBox Weight,
        TextBox W, TextBox L, TextBox H,
        TextBox MaxLayers, TextBox CondoCountBox,
        LayerPatternEditor PatternA, LayerPatternEditor PatternB,
        Border Card, TextBlock CbmHint);

    private const string CsvHeader = "Description,Content,PackSize,WeightPerBoxKg,W,L,H";

    public ProductSettingsPanel()
    {
        Content = new ScrollViewer { Content = BuildRoot() };
    }

    private StackPanel BuildRoot()
    {
        var root = new StackPanel { Spacing = 20, Margin = new Avalonia.Thickness(32, 28) };

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
            Foreground = ThemeColors.Ink,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleText);

        var templateBtn = new Button { Content = "⬇  Template", Classes = { "ghost" }, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        templateBtn.Click += ProductTemplateBtn_Click;
        Grid.SetColumn(templateBtn, 1);
        header.Children.Add(templateBtn);

        var importBtn = new Button { Content = "↑  Import CSV", Classes = { "outline" }, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
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
            Foreground = ThemeColors.InkMuted,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(subtitle, 1);
        Grid.SetColumnSpan(subtitle, 4);
        header.Children.Add(subtitle);

        root.Children.Add(header);

        // ── Search / filter ───────────────────────────────────────────────────
        var searchBox = new TextBox { Watermark = "ค้นหาสินค้า (ชื่อ, ปริมาณ, pack size)…", FontSize = 13, Margin = new Avalonia.Thickness(0, 0, 0, 4) };
        var matchLabel = new TextBlock { FontSize = 12, Foreground = ThemeColors.InkMuted, Margin = new Avalonia.Thickness(0, 4, 0, 0), IsVisible = false };

        searchBox.TextChanged += (_, _) =>
        {
            var q = searchBox.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(q))
            {
                foreach (var row in _productEditRows) row.Card.IsVisible = true;
                matchLabel.IsVisible = false;
            }
            else
            {
                int count = 0;
                foreach (var row in _productEditRows)
                {
                    bool match = (row.Description.Text?.ToLowerInvariant().Contains(q) ?? false)
                              || (row.Content.Text?.ToLowerInvariant().Contains(q) ?? false)
                              || (row.PackSize.Text?.ToLowerInvariant().Contains(q) ?? false);
                    row.Card.IsVisible = match;
                    if (match) count++;
                }
                matchLabel.Text = $"พบ {count} รายการ";
                matchLabel.IsVisible = true;
            }
        };
        root.Children.Add(searchBox);
        root.Children.Add(matchLabel);

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
            Foreground = ThemeColors.Success,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 12, 0),
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
        return root;
    }

    private Border BuildProductCard(ProductSpec? spec)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = ThemeColors.BorderLight,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(10),
            Padding = new Avalonia.Thickness(16, 14),
        };

        var inner = new StackPanel { Spacing = 10 };

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

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        var weightLabel = new StackPanel { Spacing = 4 };
        weightLabel.Children.Add(new TextBlock { Text = "น้ำหนัก (KG)", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeColors.InkFaint });
        var weightBox = new TextBox { Text = spec?.WeightPerBoxKg.ToString("G") ?? "0", Width = 90 };
        weightLabel.Children.Add(weightBox);
        row2.Children.Add(weightLabel);

        inner.Children.Add(row2);

        var row3 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,*,8,*") };

        TextBox MakeDimBox(string val, string label, int col)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeColors.InkFaint });
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

        var cbmHint = new TextBlock { FontSize = 12, Foreground = ThemeColors.InkFaint };
        cbmHint.Text = spec is not null ? $"CBM: {spec.Cbm:F6} m³" : "CBM: 0.000000 m³";

        void UpdateCbm()
        {
            double.TryParse(wBox.Text, out var w);
            double.TryParse(lBox.Text, out var l);
            double.TryParse(hBox.Text, out var h);
            cbmHint.Text = $"CBM: {w * l * h / 1_000_000:F6} m³";
        }

        wBox.TextChanged += (_, _) => UpdateCbm();
        lBox.TextChanged += (_, _) => UpdateCbm();
        hBox.TextChanged += (_, _) => UpdateCbm();

        inner.Children.Add(cbmHint);

        inner.Children.Add(new TextBlock { Text = "Pattern A — ชั้นคี่ (1, 3, 5...)", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeColors.InkFaint, Margin = new Avalonia.Thickness(0, 6, 0, 2) });

        double initW = spec?.W ?? 25.0;
        double initL = spec?.L ?? 38.0;
        var patternAEditor = new LayerPatternEditor(initW, initL);
        if (spec?.PatternA is { Length: > 0 } pa) patternAEditor.Pattern = pa;
        inner.Children.Add(patternAEditor);

        inner.Children.Add(new TextBlock { Text = "Pattern B — ชั้นคู่ Interlock (2, 4, 6...)", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeColors.InkFaint, Margin = new Avalonia.Thickness(0, 6, 0, 2) });

        var patternBEditor = new LayerPatternEditor(initW, initL);
        if (spec?.PatternB is { Length: > 0 } pb) patternBEditor.Pattern = pb;
        inner.Children.Add(patternBEditor);

        var stackRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        stackRow.Children.Add(new TextBlock { Text = "ชั้นต่อ ต๊ง", FontSize = 12, Foreground = ThemeColors.InkMuted, VerticalAlignment = VerticalAlignment.Center });
        var maxLayersBox = new TextBox { Text = (spec?.MaxLayers ?? 0).ToString(), Width = 60, FontSize = 12, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Watermark = "0" };
        stackRow.Children.Add(maxLayersBox);
        stackRow.Children.Add(new TextBlock { Text = "(0 = เต็มความสูงตู้)", FontSize = 11, Foreground = ThemeColors.InkFaint, VerticalAlignment = VerticalAlignment.Center });
        stackRow.Children.Add(new TextBlock { Text = "คอนโด", FontSize = 12, Foreground = ThemeColors.InkMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0, 0, 0) });
        var condoCountBox = new TextBox { Text = (spec?.CondoCount ?? 0).ToString(), Width = 60, FontSize = 12, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Watermark = "0" };
        stackRow.Children.Add(condoCountBox);
        stackRow.Children.Add(new TextBlock { Text = "(0 = อัตโนมัติ)", FontSize = 11, Foreground = ThemeColors.InkFaint, VerticalAlignment = VerticalAlignment.Center });
        inner.Children.Add(stackRow);

        card.Child = inner;

        wBox.TextChanged += (_, _) => { if (double.TryParse(wBox.Text, out var w)) { patternAEditor.BoxW = w; patternBEditor.BoxW = w; } };
        lBox.TextChanged += (_, _) => { if (double.TryParse(lBox.Text, out var l)) { patternAEditor.BoxL = l; patternBEditor.BoxL = l; } };

        var productRow = new ProductEditRow(descBox, contentBox, packBox, weightBox, wBox, lBox, hBox,
            maxLayersBox, condoCountBox, patternAEditor, patternBEditor, card, cbmHint);
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
            int.TryParse(row.MaxLayers.Text, out var maxLayers);
            int.TryParse(row.CondoCountBox.Text, out var condoCount);
            specs.Add(new ProductSpec(
                row.Description.Text.Trim(),
                row.Content.Text?.Trim() ?? "",
                row.PackSize.Text?.Trim() ?? "",
                weight,
                w, l, h,
                row.PatternA.Pattern,
                row.PatternB.Pattern,
                maxLayers, condoCount));
        }
        return specs;
    }

    private async void ProductTemplateBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Download Product Template",
                SuggestedFileName = "products_template.csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });

            if (file is null) return;

            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            sb.AppendLine("Aloe,365ML,Pack 24,9.9,21.9,33.4,20.5");

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sb.ToString());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProductSettingsPanel.Template] {ex}"); }
    }

    private async void ProductImportBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
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
                if (isFirst) { isFirst = false; continue; }
                var parts = line.Split(',');
                if (parts.Length < 7) continue;
                double.TryParse(parts[3], out var weight);
                double.TryParse(parts[4], out var w);
                double.TryParse(parts[5], out var l);
                double.TryParse(parts[6], out var h);
                specs.Add(new ProductSpec(parts[0], parts[1], parts[2], weight, w, l, h));
            }

            _productEditRows.Clear();
            _productRowsPanel.Children.Clear();
            foreach (var p in specs)
                _productRowsPanel.Children.Add(BuildProductCard(p));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProductSettingsPanel.Import] {ex}"); }
    }

    private async void ProductExportBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
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
                sb.AppendLine($"{p.Description},{p.Content},{p.PackSize},{p.WeightPerBoxKg},{p.W},{p.L},{p.H}");

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sb.ToString());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProductSettingsPanel.Export] {ex}"); }
    }
}
