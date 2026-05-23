using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CargoFit;

// Visual editor for a layer pattern composed of sections placed left-to-right.
// Each section contains one or more sub-rows, each with its own box orientation.
public class LayerPatternEditor : UserControl
{

    private readonly List<SectionRow> _rows = [];
    private double _boxW;
    private double _boxL;

    private StackPanel _sectionsPanel = null!;
    private TextBlock  _summaryLabel  = null!;

    public event Action<LayerSection[]>? PatternChanged;

    public double BoxW { get => _boxW; set { _boxW = value; RefreshAllPreviews(); } }
    public double BoxL { get => _boxL; set { _boxL = value; RefreshAllPreviews(); } }

    public LayerSection[] Pattern
    {
        get => _rows.Select(r => r.ToSection()).ToArray();
        set
        {
            _rows.Clear();
            _sectionsPanel?.Children.Clear();
            foreach (var s in value)
                AddSection(s);
            UpdateSummary();
        }
    }

    public LayerPatternEditor(double boxW = 25.0, double boxL = 38.0)
    {
        _boxW = boxW;
        _boxL = boxL;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel { Spacing = 8 };

        _sectionsPanel = new StackPanel { Spacing = 6 };
        root.Children.Add(_sectionsPanel);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var addBtn = new Button
        {
            Content = "+ เพิ่ม Section",
            FontSize = 12,
            Padding = new Thickness(10, 5),
            Classes = { "outline" }
        };
        addBtn.Click += (_, _) =>
        {
            AddSection(new LayerSection(2, 4, false));
            UpdateSummary();
            Emit();
        };
        footer.Children.Add(addBtn);

        _summaryLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemeColors.InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_summaryLabel);

        root.Children.Add(footer);
        Content = root;
        UpdateSummary();
    }

    private void AddSection(LayerSection s)
    {
        var row = new SectionRow(s, _boxW, _boxL, OnSectionChanged, OnSectionRemoved);
        _rows.Add(row);
        _sectionsPanel.Children.Add(row.Card);
    }

    private void OnSectionChanged()
    {
        RefreshAllPreviews();
        UpdateSummary();
        Emit();
    }

    private void OnSectionRemoved(SectionRow row)
    {
        _sectionsPanel.Children.Remove(row.Card);
        _rows.Remove(row);
        UpdateSummary();
        Emit();
    }

    private void RefreshAllPreviews()
    {
        foreach (var r in _rows)
            r.UpdatePreview(_boxW, _boxL);
    }

    private void UpdateSummary()
    {
        int total = _rows.Sum(r => r.TotalBoxes);
        _summaryLabel.Text = total > 0 ? $"รวม {total} ลัง/ชั้น" : "";
    }

    private void Emit() => PatternChanged?.Invoke(Pattern);

    // ── SectionRow ────────────────────────────────────────────────────────────

    private sealed class SectionRow
    {
        public Border Card { get; }
        public int TotalBoxes => _subRows.Sum(r => r.Rows * r.Cols);

        private readonly List<SubRowEntry> _subRows = [];
        private readonly Action _onChange;
        private readonly Action<SectionRow> _onRemove;
        private StackPanel _subRowsPanel = null!;
        private Canvas     _preview      = null!;
        private double     _boxW;
        private double     _boxL;

        public LayerSection ToSection()
        {
            var subs = _subRows.Select(r => new SectionSubRow(r.Rows, r.Cols, r.Rotated)).ToArray();
            // Single sub-row: write legacy format so existing JSON stays clean
            return subs.Length == 1
                ? new LayerSection(subs[0].Rows, subs[0].Cols, subs[0].Rotated)
                : new LayerSection(0, 0, false, subs);
        }

        public SectionRow(LayerSection s, double boxW, double boxL,
                          Action onChange, Action<SectionRow> onRemove)
        {
            _boxW     = boxW;
            _boxL     = boxL;
            _onChange = onChange;
            _onRemove = onRemove;
            Card      = BuildCard(s);
        }

        private Border BuildCard(LayerSection s)
        {
            var inner = new StackPanel { Spacing = 6 };

            // Sub-rows panel
            _subRowsPanel = new StackPanel { Spacing = 4 };
            inner.Children.Add(_subRowsPanel);

            // Footer: add sub-row | delete section
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var addSubBtn = new Button
            {
                Content = "+ เพิ่มแถวย่อย",
                FontSize = 11,
                Padding  = new Thickness(8, 4),
                Classes  = { "ghost" }
            };
            addSubBtn.Click += (_, _) =>
            {
                var cols = _subRows.Count > 0 ? _subRows[0].Cols : 2;
                AddSubRowEntry(new SectionSubRow(1, cols, false));
                DrawPreview();
                _onChange();
            };
            footerGrid.Children.Add(addSubBtn);

            var delSectionBtn = new Button
            {
                Content = "✕ ลบ",
                FontSize = 11,
                Padding  = new Thickness(8, 4),
                Classes  = { "danger" }
            };
            delSectionBtn.Click += (_, _) => _onRemove(this);
            Grid.SetColumn(delSectionBtn, 1);
            footerGrid.Children.Add(delSectionBtn);

            inner.Children.Add(footerGrid);

            // Preview canvas
            _preview = new Canvas { Height = 80 };
            inner.Children.Add(_preview);

            // Init sub-rows after _preview is assigned
            foreach (var sub in s.GetSubRows())
                AddSubRowEntry(sub);
            DrawPreview();

            return new Border
            {
                Background      = ThemeColors.SurfaceSub,
                BorderBrush     = ThemeColors.BorderLight,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(12, 10),
                Child           = inner
            };
        }

        private void AddSubRowEntry(SectionSubRow sub)
        {
            var entry = new SubRowEntry(sub, OnSubChanged, OnSubRemoved);
            _subRows.Add(entry);
            _subRowsPanel.Children.Add(entry.Row);
            UpdateDeleteButtons();
        }

        private void OnSubChanged()
        {
            DrawPreview();
            _onChange();
        }

        private void OnSubRemoved(SubRowEntry entry)
        {
            if (_subRows.Count <= 1) return;
            _subRowsPanel.Children.Remove(entry.Row);
            _subRows.Remove(entry);
            UpdateDeleteButtons();
            DrawPreview();
            _onChange();
        }

        private void UpdateDeleteButtons()
        {
            bool canDelete = _subRows.Count > 1;
            foreach (var r in _subRows)
                r.SetDeleteEnabled(canDelete);
        }

        public void UpdatePreview(double boxW, double boxL)
        {
            _boxW = boxW;
            _boxL = boxL;
            DrawPreview();
        }

        private void DrawPreview()
        {
            _preview.Children.Clear();
            if (_subRows.Count == 0) return;

            double totalH = _subRows.Sum(r => r.Rows * (r.Rotated ? _boxW : _boxL));
            double maxW   = _subRows.Max(r => r.Cols * (r.Rotated ? _boxL : _boxW));
            double scale  = Math.Min(200.0 / Math.Max(maxW, 1), 80.0 / Math.Max(totalH, 1));

            double drawY = 0;
            foreach (var sub in _subRows)
            {
                double bw    = sub.Rotated ? _boxL : _boxW;
                double bl    = sub.Rotated ? _boxW : _boxL;
                double cellW = bw * scale;
                double cellH = bl * scale;
                var    fill  = sub.Rotated ? ThemeColors.BoxRotated : ThemeColors.BoxNormal;

                for (int r = 0; r < sub.Rows; r++)
                {
                    for (int c = 0; c < sub.Cols; c++)
                    {
                        var rect = new Border
                        {
                            Width           = Math.Max(cellW - 2, 1),
                            Height          = Math.Max(cellH - 2, 1),
                            Background      = fill,
                            BorderBrush     = Brushes.White,
                            BorderThickness = new Thickness(1),
                            CornerRadius    = new CornerRadius(2)
                        };
                        Canvas.SetLeft(rect, c * cellW + 1);
                        Canvas.SetTop(rect,  drawY + r * cellH + 1);
                        _preview.Children.Add(rect);
                    }
                }
                drawY += sub.Rows * cellH;
            }
        }

        // ── SubRowEntry ───────────────────────────────────────────────────────

        private sealed class SubRowEntry
        {
            public Control Row { get; }
            public int  Rows    { get; private set; }
            public int  Cols    { get; private set; }
            public bool Rotated { get; private set; }

            private readonly Action             _onChange;
            private readonly Action<SubRowEntry> _onRemove;
            private Button _delBtn = null!;

            public SubRowEntry(SectionSubRow sub,
                               Action onChange, Action<SubRowEntry> onRemove)
            {
                Rows      = sub.Rows;
                Cols      = sub.Cols;
                Rotated   = sub.Rotated;
                _onChange = onChange;
                _onRemove = onRemove;
                Row       = BuildRow();
            }

            public void SetDeleteEnabled(bool enabled) => _delBtn.IsEnabled = enabled;

            private Control BuildRow()
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing     = 6
                };

                panel.Children.Add(Label("แถว"));
                panel.Children.Add(SpinBox(Rows, v => { Rows = v; _onChange(); }));
                panel.Children.Add(Label("คอล"));
                panel.Children.Add(SpinBox(Cols, v => { Cols = v; _onChange(); }));

                var orientBtn = new Button
                {
                    FontSize = 11,
                    Padding  = new Thickness(7, 3),
                    Classes  = { "outline" }
                };
                void RefreshOrient() => orientBtn.Content = Rotated ? "↺ R" : "→ N";
                RefreshOrient();
                orientBtn.Click += (_, _) => { Rotated = !Rotated; RefreshOrient(); _onChange(); };
                panel.Children.Add(orientBtn);

                _delBtn = new Button
                {
                    Content = "−",
                    FontSize = 11,
                    Padding  = new Thickness(6, 3),
                    Classes  = { "danger" }
                };
                _delBtn.Click += (_, _) => _onRemove(this);
                panel.Children.Add(_delBtn);

                return panel;
            }

            private static TextBlock Label(string text) => new()
            {
                Text                = text,
                FontSize            = 12,
                Foreground          = ThemeColors.Ink,
                VerticalAlignment   = VerticalAlignment.Center
            };

            private static Border SpinBox(int initial, Action<int> onChanged)
            {
                int value = initial;
                var display = new TextBlock
                {
                    Text                    = value.ToString(),
                    Width                   = 24,
                    FontSize                = 13,
                    TextAlignment           = TextAlignment.Center,
                    VerticalAlignment       = VerticalAlignment.Center,
                    HorizontalAlignment     = HorizontalAlignment.Center
                };

                void Update(int delta)
                {
                    value       = Math.Max(1, value + delta);
                    display.Text = value.ToString();
                    onChanged(value);
                }

                var minus = new Button { Content = "−", FontSize = 11, Padding = new Thickness(5, 2) };
                var plus  = new Button { Content = "+", FontSize = 11, Padding = new Thickness(5, 2) };
                minus.Click += (_, _) => Update(-1);
                plus.Click  += (_, _) => Update(+1);

                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                row.Children.Add(minus);
                row.Children.Add(display);
                row.Children.Add(plus);
                return new Border { Child = row };
            }
        }
    }
}
