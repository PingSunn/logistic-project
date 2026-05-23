using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CargoFit;

internal sealed class ContainerSettingsPanel : UserControl
{
    private StackPanel _rowsPanel = null!;
    private readonly List<EditRow> _editRows = [];

    private record EditRow(TextBox Name, TextBox SizeLabel, TextBox W, TextBox L, TextBox H, TextBox Gap, Border Card, TextBlock Hint);

    public ContainerSettingsPanel()
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

        var titleText = new TextBlock
        {
            Text = "Container Sizes",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeColors.Ink,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleText);

        var importBtn = new Button { Content = "↑  Import", Classes = { "outline" }, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        importBtn.Click += ImportBtn_Click;
        Grid.SetColumn(importBtn, 1);
        header.Children.Add(importBtn);

        var exportBtn = new Button { Content = "↓  Export", Classes = { "outline" } };
        exportBtn.Click += ExportBtn_Click;
        Grid.SetColumn(exportBtn, 2);
        header.Children.Add(exportBtn);

        var subtitle = new TextBlock
        {
            Text = "ใส่ขนาดจริงของตู้ — ระบบจะลบค่า Gap จากทุกด้านเพื่อคำนวณพื้นที่ใช้งาน",
            FontSize = 13,
            Foreground = ThemeColors.InkMuted,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
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

        saveBtn.Click += async (_, e) =>
        {
            SaveBtn_Click(saveBtn, e);
            saveStatus.Text = "✓  Saved";
            saveStatus.IsVisible = true;
            await System.Threading.Tasks.Task.Delay(2000);
            saveStatus.IsVisible = false;
        };

        root.Children.Add(footer);
        return root;
    }

    private Border BuildCardRow(ContainerSpec? spec)
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

        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row1.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(110)));
        row1.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var nameBox = new TextBox { Text = spec?.Name ?? "", Watermark = "Container name", FontWeight = FontWeight.Medium, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        row1.Children.Add(nameBox);

        var sizeBox = new TextBox { Text = spec?.SizeLabel ?? "", Watermark = "e.g. 20 ft", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        Grid.SetColumn(sizeBox, 1);
        row1.Children.Add(sizeBox);

        var delBtn = new Button { Content = "✕", Classes = { "danger" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(delBtn, 2);
        row1.Children.Add(delBtn);

        inner.Children.Add(row1);

        var row2 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,*,8,*") };

        TextBox MakeDimBox(string val, string label, int col)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ThemeColors.InkFaint });
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

        var row3 = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,8,80") };
        row3.Children.Add(new TextBlock
        {
            Text = "GAP (CM)",
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            Foreground = ThemeColors.InkFaint,
            VerticalAlignment = VerticalAlignment.Center
        });
        var gapBox = new TextBox { Text = (spec?.Gap ?? 5).ToString() };
        Grid.SetColumn(gapBox, 2);
        row3.Children.Add(gapBox);
        inner.Children.Add(row3);

        var hint = new TextBlock { FontSize = 12, Foreground = ThemeColors.InkFaint };

        void UpdateHint()
        {
            int.TryParse(wBox.Text, out var w);
            int.TryParse(lBox.Text, out var l);
            int.TryParse(hBox.Text, out var h);
            int.TryParse(gapBox.Text, out var gap);
            hint.Text = $"ภายใน: {w - gap} × {l - gap} × {h - gap} cm";
        }

        wBox.TextChanged += (_, _) => UpdateHint();
        lBox.TextChanged += (_, _) => UpdateHint();
        hBox.TextChanged += (_, _) => UpdateHint();
        gapBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();

        inner.Children.Add(hint);
        card.Child = inner;

        var row = new EditRow(nameBox, sizeBox, wBox, lBox, hBox, gapBox, card, hint);
        _editRows.Add(row);

        delBtn.Click += (_, _) =>
        {
            _editRows.Remove(row);
            _rowsPanel.Children.Remove(card);
        };

        return card;
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        var specs = CollectSpecs();
        ContainerSpec.All.Clear();
        ContainerSpec.All.AddRange(specs);
        ContainerSpec.Save();
    }

    private async void ImportBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSettingsPanel.Import] {ex}"); }
    }

    private async void ExportBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Containers",
                SuggestedFileName = "containers.json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });

            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, CollectSpecs(), JsonOptions.WriteIndented);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSettingsPanel.Export] {ex}"); }
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
            int.TryParse(row.Gap.Text, out var gap);
            if (gap <= 0) gap = 5;
            specs.Add(new ContainerSpec(row.Name.Text.Trim(), row.SizeLabel.Text?.Trim() ?? "", w, l, h, gap));
        }
        return specs;
    }
}
