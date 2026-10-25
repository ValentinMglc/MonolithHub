using System.Windows;
using System.Windows.Media;

namespace Monolith.Controls;

/// <summary>Mini-graphique d'historique : courbe + remplissage dégradé, aligné à droite.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    /// <summary>Valeur du haut du graphique ; 0 = échelle automatique.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Nombre de points visibles sur toute la largeur.</summary>
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    IReadOnlyList<double> _values = [];

    public void SetValues(IReadOnlyList<double> values)
    {
        _values = values;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Zone transparente pour que l'élément garde sa taille même vide.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var max = Maximum > 0 ? Maximum : Math.Max(_values.Max() * 1.15, 1e-6);
        var step = ActualWidth / Math.Max(Capacity - 1, 1);
        var startX = ActualWidth - (_values.Count - 1) * step;
        const double pad = 1.5;
        var height = ActualHeight - pad * 2;

        Point At(int i) => new(startX + i * step, pad + height - Math.Clamp(_values[i] / max, 0, 1) * height);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false, false);
            for (var i = 1; i < _values.Count; i++)
                ctx.LineTo(At(i), true, true);
        }
        line.Freeze();

        if (Fill is not null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(At(0).X, ActualHeight), true, true);
                for (var i = 0; i < _values.Count; i++)
                    ctx.LineTo(At(i), false, true);
                ctx.LineTo(new Point(At(_values.Count - 1).X, ActualHeight), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(Fill, null, area);
        }

        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
    }
}

/// <summary>Historique glissant de mesures.</summary>
public sealed class History(int capacity = 60)
{
    readonly List<double> _values = new(capacity);

    public IReadOnlyList<double> Values => _values;

    public void Add(double? value)
    {
        if (_values.Count == capacity)
            _values.RemoveAt(0);
        _values.Add(value ?? 0);
    }
}
