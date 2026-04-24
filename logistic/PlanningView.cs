using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace logistic;

public class PlanningView : UserControl
{
    // ── Palette ─────────────────────────────────────────────────────────────
    private static readonly SolidColorBrush Surface     = new(Color.Parse("#FFFFFF"));
    private static readonly SolidColorBrush SurfaceSub  = new(Color.Parse("#F8FAFC"));
    private static readonly SolidColorBrush BorderLight = new(Color.Parse("#E2E8F0"));
    private static readonly SolidColorBrush Ink         = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush InkMuted    = new(Color.Parse("#64748B"));
    private static readonly SolidColorBrush AccentBg    = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush AccentBorder= new(Color.Parse("#93C5FD"));
    private static readonly SolidColorBrush AccentText  = new(Color.Parse("#1D4ED8"));

    // ── State ────────────────────────────────────────────────────────────────
    private readonly List<Border> _containerItems = [];
    private int _selectedContainerIndex = 0;

    private readonly List<(CheckBox Cb, ProductSpec Spec, Border Wrapper)> _products = [];
    private readonly Dictionary<ProductSpec, (Border Row, TextBox QtyBox)> _qtyMap = [];

    private StackPanel _quantityRows = null!;
    private IsometricCanvas _canvas = null!;
    private TextBlock _statsText = null!;
    private Slider _cutSlider = null!;
    private TextBlock _cutLabel = null!;

    public PlanningView()
    {
        Margin = new Thickness(20, 14, 20, 20);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(296)));

        grid.Children.Add(BuildLeftPanel());
        var right = BuildRightPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Content = grid;

        if (ContainerSpec.All.Count > 0)
            _canvas.SetData(ContainerSpec.All[0], []);
    }

    // ── Left panel ───────────────────────────────────────────────────────────

    private Control BuildLeftPanel()
    {
        var dock = new DockPanel { LastChildFill = true };

        // Stats card — docked to bottom
        var statsCard = Card(padding: new Thickness(20, 14));
        statsCard.Height = 128;
        statsCard.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(statsCard, Dock.Bottom);

        _statsText = new TextBlock
        {
            Text = "กด Start เพื่อคำนวณการบรรจุ",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = InkMuted,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 22
        };
        statsCard.Child = _statsText;
        dock.Children.Add(statsCard);

        // Layer cut slider — docked above stats
        var sliderRow = new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(sliderRow, Dock.Bottom);

        var sliderGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,38") };

        sliderGrid.Children.Add(new TextBlock
        {
            Text = "แสดงชั้น",
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        _cutSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 1,
            SmallChange = 0.05,
            LargeChange = 0.1,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_cutSlider, 1);
        sliderGrid.Children.Add(_cutSlider);

        _cutLabel = new TextBlock
        {
            Text = "100%",
            FontSize = 11,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(_cutLabel, 2);
        sliderGrid.Children.Add(_cutLabel);

        _cutSlider.ValueChanged += (_, _) =>
        {
            _canvas.SetCutRatio(_cutSlider.Value);
            _cutLabel.Text = $"{(int)Math.Round(_cutSlider.Value * 100)}%";
        };

        sliderRow.Child = sliderGrid;
        dock.Children.Add(sliderRow);

        // Canvas card — fills remaining space
        var canvasCard = new Border
        {
            Background = SurfaceSub,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true
        };
        _canvas = new IsometricCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        canvasCard.Child = _canvas;
        dock.Children.Add(canvasCard);

        return dock;
    }

    // ── Right panel ──────────────────────────────────────────────────────────

    private Control BuildRightPanel()
    {
        var panel = new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 16, 18, 16)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var inner = new StackPanel { Spacing = 4 };

        // Container section
        inner.Children.Add(SectionLabel("ตู้คอนเทนเนอร์"));
        var containerStack = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 8) };
        for (int i = 0; i < ContainerSpec.All.Count; i++)
        {
            int idx = i;
            var c = ContainerSpec.All[i];
            var item = MakeContainerItem(c.Name, c.SizeLabel, i == 0);
            item.PointerPressed += (_, _) => SelectContainer(idx);
            containerStack.Children.Add(item);
            _containerItems.Add(item);
        }
        inner.Children.Add(containerStack);

        // Product section
        inner.Children.Add(SectionLabel("สินค้า"));

        var searchBox = new TextBox
        {
            Watermark = "ค้นหา...",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 5)
        };
        inner.Children.Add(searchBox);

        var productStack = new StackPanel { Spacing = 3 };
        foreach (var spec in ProductSpec.All)
        {
            var label = $"{spec.Description} {spec.Content}";
            var cb = new CheckBox
            {
                Content = label,
                FontSize = 12,
                Foreground = Ink,
                Padding = new Thickness(6, 4)
            };
            cb.IsCheckedChanged += (_, _) => UpdateQuantitySection();

            var details = new TextBlock
            {
                Text = $"{spec.WeightPerBoxKg:G} kg  ·  {spec.Cbm:F4} m³",
                FontSize = 10,
                Foreground = InkMuted,
                Margin = new Thickness(30, 0, 0, 4)
            };

            var itemStack = new StackPanel();
            itemStack.Children.Add(cb);
            itemStack.Children.Add(details);

            var wrapper = new Border
            {
                Background = SurfaceSub,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(2, 0)
            };
            wrapper.Child = itemStack;
            productStack.Children.Add(wrapper);
            _products.Add((cb, spec, wrapper));
        }

        searchBox.TextChanged += (_, _) => FilterProducts(searchBox.Text ?? "");

        inner.Children.Add(new ScrollViewer
        {
            Content = productStack,
            Height = 178,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Quantity section
        inner.Children.Add(SectionLabel("จำนวน"));
        _quantityRows = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0) };
        inner.Children.Add(_quantityRows);

        var scroll = new ScrollViewer
        {
            Content = inner,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        rootGrid.Children.Add(scroll);

        // Start button
        var startBtn = new Button
        {
            Content = "Start",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(0, 11)
        };
        startBtn.Click += Calculate_Click;
        Grid.SetRow(startBtn, 1);
        rootGrid.Children.Add(startBtn);

        panel.Child = rootGrid;
        return panel;
    }

    // ── Component helpers ────────────────────────────────────────────────────

    private static Border Card(Thickness padding)
    {
        return new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding
        };
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = InkMuted,
        Margin = new Thickness(2, 4, 0, 0)
    };

    private static Border MakeContainerItem(string name, string size, bool selected)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };

        grid.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            Foreground = selected ? AccentText : Ink,
            VerticalAlignment = VerticalAlignment.Center
        });

        var tag = new Border
        {
            Background = selected ? AccentBg : SurfaceSub,
            BorderBrush = selected ? AccentBorder : BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2)
        };
        tag.Child = new TextBlock
        {
            Text = size,
            FontSize = 10,
            Foreground = selected ? AccentText : InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tag, 1);
        grid.Children.Add(tag);

        return new Border
        {
            Background = selected ? AccentBg : Surface,
            BorderBrush = selected ? AccentBorder : BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid
        };
    }

    private void SelectContainer(int idx)
    {
        _selectedContainerIndex = idx;
        for (int i = 0; i < _containerItems.Count; i++)
            ApplyContainerSelection(_containerItems[i], i == idx);
        if (idx >= 0 && idx < ContainerSpec.All.Count)
            _canvas.SetData(ContainerSpec.All[idx], []);
    }

    private static void ApplyContainerSelection(Border item, bool selected)
    {
        item.Background  = selected ? AccentBg     : Surface;
        item.BorderBrush = selected ? AccentBorder : BorderLight;

        if (item.Child is not Grid g) return;
        if (g.Children[0] is TextBlock name)
            name.Foreground = selected ? AccentText : Ink;
        if (g.Children.Count > 1 && g.Children[1] is Border tag)
            ApplyTagSelection(tag, selected);
    }

    private static void ApplyTagSelection(Border tag, bool selected)
    {
        tag.Background  = selected ? AccentBg     : SurfaceSub;
        tag.BorderBrush = selected ? AccentBorder : BorderLight;
        if (tag.Child is TextBlock label)
            label.Foreground = selected ? AccentText : InkMuted;
    }

    private void FilterProducts(string query)
    {
        var q = query.Trim();
        foreach (var (_, spec, wrapper) in _products)
        {
            wrapper.IsVisible = q.Length == 0 ||
                $"{spec.Description} {spec.Content}".Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateQuantitySection()
    {
        var toRemove = new List<ProductSpec>();
        foreach (var (spec, (row, _)) in _qtyMap)
        {
            var entry = _products.Find(p => p.Spec == spec);
            if (entry.Cb is null || entry.Cb.IsChecked != true)
            {
                _quantityRows.Children.Remove(row);
                toRemove.Add(spec);
            }
        }
        foreach (var spec in toRemove) _qtyMap.Remove(spec);

        foreach (var (cb, spec, _) in _products)
        {
            if (cb.IsChecked != true || _qtyMap.ContainsKey(spec)) continue;
            var row = BuildQtyRow(spec, out TextBox qtyBox);
            _quantityRows.Children.Add(row);
            _qtyMap[spec] = (row, qtyBox);
        }
    }

    private static Border BuildQtyRow(ProductSpec spec, out TextBox qtyBox)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,62,8,Auto") };

        grid.Children.Add(new TextBlock
        {
            Text = $"{spec.Description} {spec.Content}",
            FontSize = 12,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var qty = new TextBox
        {
            Text = "",
            Width = 62,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(qty, 2);
        grid.Children.Add(qty);

        var unit = new TextBlock
        {
            Text = "ลัง",
            FontSize = 12,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(unit, 4);
        grid.Children.Add(unit);

        var cbmLine = new TextBlock
        {
            FontSize = 10,
            Foreground = InkMuted,
            Margin = new Thickness(2, 3, 0, 0)
        };

        void UpdateCbm()
        {
            var clean = new string(qty.Text?.Where(char.IsDigit).ToArray() ?? []);
            if (qty.Text != clean) { qty.Text = clean; qty.CaretIndex = clean.Length; }
            cbmLine.Text = int.TryParse(clean, out int n) && n > 0
                ? $"CBM: {spec.Cbm:F4} × {n} = {spec.Cbm * n:F4} m³"
                : $"CBM: {spec.Cbm:F4} m³ / ลัง";
        }
        qty.TextChanged += (_, _) => UpdateCbm();
        UpdateCbm();

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(grid);
        stack.Children.Add(cbmLine);

        var border = new Border
        {
            Background = SurfaceSub,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7)
        };
        border.Child = stack;
        qtyBox = qty;
        return border;
    }

    // ── Calculation ──────────────────────────────────────────────────────────

    private void Calculate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedContainerIndex < 0 || _selectedContainerIndex >= ContainerSpec.All.Count) return;
        var container = ContainerSpec.All[_selectedContainerIndex];

        var placements = new List<BoxPlacement>();
        var stats = new StringBuilder();
        double currentY = Clearance;
        int productIndex = 0;

        foreach (var (cb, spec, _) in _products)
        {
            if (cb.IsChecked != true) continue;
            if (!_qtyMap.TryGetValue(spec, out var entry)) continue;

            int requested = int.TryParse(entry.QtyBox.Text, out int parsed) && parsed > 0 ? parsed : 1;

            if (spec.PatternA is not { Length: > 0 })
            {
                stats.AppendLine($"{spec.Description} {spec.Content}: ยังไม่ได้กำหนด pattern");
                productIndex++;
                continue;
            }

            var r = PlaceProduct(container, spec, requested, currentY, productIndex, placements);
            int packed = r.Packed;
            currentY = r.EndY;

            string stackInfo = (r.FullStacks, r.PartialBoxes) switch
            {
                (0, 0) => "",
                (0, _) => $"  [{r.PartialBoxes} ลัง บางส่วน]",
                (_, 0) => $"  [{r.FullStacks} ตั้งเต็ม]",
                _      => $"  [{r.FullStacks} ตั้งเต็ม + {r.PartialBoxes} ลัง]"
            };
            stats.AppendLine($"{spec.Description} {spec.Content}: {packed}/{requested} ลัง{stackInfo}" +
                             (packed < requested ? "  (ตู้เต็ม)" : ""));
            productIndex++;
        }

        double containerCbm = (double)container.InteriorW * container.InteriorL * container.InteriorH / 1_000_000;
        double usedCbm = 0;
        foreach (var p in placements) usedCbm += p.BW * p.BL * p.BH / 1_000_000;

        if (containerCbm > 0)
            stats.AppendLine($"\nรวม {usedCbm:F3} / {containerCbm:F3} m³  ({usedCbm / containerCbm * 100:F1}%)");

        _statsText.Text = stats.Length > 0 ? stats.ToString().TrimEnd() : "ยังไม่ได้เลือกสินค้า";
        _cutSlider.Value = 1.0;
        _cutLabel.Text = "100%";
        _canvas.SetData(container, placements);
    }

    private record struct ContainerDims(double W, double L, double H);
    private record struct PlaceResult(int Packed, double EndY, int FullStacks, int PartialBoxes);

    private const double Clearance = 5.0; // cm gap from each container wall

    // Depth (Y) occupied by one layer of the given pattern
    private static double LayerDepth(LayerSection[] sections, double W, double L)
    {
        double max = 0;
        foreach (var s in sections)
            max = Math.Max(max, s.Rows * (s.Rotated ? W : L));
        return max;
    }

    private static PlaceResult PlaceProduct(
        ContainerSpec container, ProductSpec spec, int requested,
        double startY, int productIndex, List<BoxPlacement> placements)
    {
        if (spec.PatternA is not { Length: > 0 }) return new(0, startY, 0, 0);

        var dims      = new ContainerDims(container.InteriorW, container.InteriorL, container.InteriorH);
        double stackDepth = LayerDepth(spec.PatternA, spec.W, spec.L);
        if (stackDepth <= 0) return new(0, startY, 0, 0);

        int maxLayers  = spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue;
        int maxHeight  = (int)Math.Floor(dims.H / spec.H);
        int layerLimit = Math.Min(maxLayers, maxHeight);

        int packed      = 0;
        int stackIndex  = 0;
        int fullStacks  = 0;
        int partialBoxes = 0;

        while (packed < requested)
        {
            double stackY = startY + stackIndex * stackDepth;
            if (stackY >= dims.L - Clearance) break;

            bool flipStart   = (stackIndex % 2 == 1) && spec.PatternB is { Length: > 0 };
            int  beforeStack = packed;
            int  layersPlaced = 0;

            for (int layer = 0; layer < layerLimit && packed < requested; layer++)
            {
                double z     = layer * spec.H;
                bool useA    = flipStart ? (layer % 2 == 1) : (layer % 2 == 0);
                var sections = useA ? spec.PatternA : (spec.PatternB ?? spec.PatternA);

                int n = PlaceLayerAt(sections, spec, dims, stackY, z, requested - packed, productIndex, placements);
                if (n < 0) break;
                packed += n;
                layersPlaced++;
            }

            if (layerLimit > 0 && layersPlaced == layerLimit)
                fullStacks++;
            else if (layersPlaced > 0)
                partialBoxes = packed - beforeStack;

            stackIndex++;
        }

        return new(packed, startY + stackIndex * stackDepth, fullStacks, partialBoxes);
    }

    private static int PlaceLayerAt(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims,
        double stackY, double z, int limit, int productIndex, List<BoxPlacement> placements)
    {
        if (z + spec.H > dims.H + 0.01) return -1;

        double tierW = 0;
        foreach (var s in sections)
            tierW += s.Cols * (s.Rotated ? spec.L : spec.W);
        if (tierW <= 0) return -1;

        int numTiers = Math.Max(1, (int)Math.Floor(dims.W / tierW));
        int packed   = 0;

        for (int tier = 0; tier < numTiers && packed < limit; tier++)
        {
            double sectionX = tier * tierW;
            foreach (var section in sections)
            {
                double bw = section.Rotated ? spec.L : spec.W;
                double bl = section.Rotated ? spec.W : spec.L;

                for (int c = 0; c < section.Cols && packed < limit; c++)
                {
                    for (int r = 0; r < section.Rows && packed < limit; r++)
                    {
                        double px = sectionX + c * bw;
                        double py = stackY + r * bl;
                        if (px + bw > dims.W + 0.01 || py + bl > dims.L - Clearance + 0.01) continue;
                        placements.Add(new BoxPlacement(px, py, z, bw, bl, spec.H, productIndex, section.Rotated));
                        packed++;
                    }
                }
                sectionX += section.Cols * bw;
            }
        }

        return packed > 0 ? packed : -1;
    }
}
