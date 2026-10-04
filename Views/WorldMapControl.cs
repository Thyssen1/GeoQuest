using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GeoQuest.Models;

namespace GeoQuest.Views;

/// <summary>
/// The world, and somewhere on it that the player clicked.
///
/// The map arrives as geometry in the units BorderBaker wrote — x across 0..1, y down
/// 0..0.5 — so this control's whole job is the two-way conversion between that space and
/// screen pixels. Drawing is one direction of it and hit-testing is the other, which is
/// why they live together: if they ever disagreed, the pin would land somewhere other than
/// where it was clicked and nothing would look wrong.
/// </summary>
public sealed class WorldMapControl : Control
{
    public static readonly StyledProperty<Geometry?> MapProperty =
        AvaloniaProperty.Register<WorldMapControl, Geometry?>(nameof(Map));

    public static readonly StyledProperty<GeoPoint?> PinProperty =
        AvaloniaProperty.Register<WorldMapControl, GeoPoint?>(nameof(Pin));

    /// <summary>Where the city actually was. Shown only once the round is over.</summary>
    public static readonly StyledProperty<GeoPoint?> AnswerProperty =
        AvaloniaProperty.Register<WorldMapControl, GeoPoint?>(nameof(Answer));

    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<WorldMapControl, ICommand?>(nameof(DropCommand));

    /// <summary>Whether a click does anything. False during the reveal and after the run.</summary>
    public static readonly StyledProperty<bool> IsDroppableProperty =
        AvaloniaProperty.Register<WorldMapControl, bool>(nameof(IsDroppable), defaultValue: true);

    private static readonly IBrush Sea = SolidColorBrush.Parse("#BBE2F5");
    private static readonly IBrush Land = SolidColorBrush.Parse("#CFE8C9");
    private static readonly IBrush Ink = SolidColorBrush.Parse("#7FB37A");
    private static readonly IBrush PinFill = SolidColorBrush.Parse("#D9453B");
    private static readonly IBrush AnswerFill = SolidColorBrush.Parse("#2E9E57");
    private static readonly IBrush Chrome = Brushes.White;

    static WorldMapControl()
    {
        AffectsRender<WorldMapControl>(MapProperty, PinProperty, AnswerProperty);
    }

    public Geometry? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    public GeoPoint? Pin
    {
        get => GetValue(PinProperty);
        set => SetValue(PinProperty, value);
    }

    public GeoPoint? Answer
    {
        get => GetValue(AnswerProperty);
        set => SetValue(AnswerProperty, value);
    }

    public ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public bool IsDroppable
    {
        get => GetValue(IsDroppableProperty);
        set => SetValue(IsDroppableProperty, value);
    }

    /// <summary>
    /// The largest 2:1 box that fits what it is offered. Without this the control takes
    /// all the height going and letterboxes the map inside itself, leaving a band of the
    /// panel behind it showing above and below.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0d : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0d : availableSize.Height;

        if (width <= 0d && height <= 0d)
        {
            return new Size(0d, 0d);
        }

        var scale = width <= 0d
            ? height / WorldMap.Height
            : height <= 0d
                ? width / WorldMap.Width
                : Math.Min(width / WorldMap.Width, height / WorldMap.Height);

        return new Size(WorldMap.Width * scale, WorldMap.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        var (scale, originX, originY) = Layout();

        if (scale <= 0d)
        {
            return;
        }

        var sea = new Rect(originX, originY, WorldMap.Width * scale, WorldMap.Height * scale);

        context.DrawRectangle(Sea, null, new RoundedRect(sea, 14d));

        if (Map is { } map)
        {
            using (context.PushClip(new RoundedRect(sea, 14d)))
            using (context.PushTransform(
                       Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(originX, originY)))
            {
                // The pen is specified in map units because the transform is still applied,
                // so its width is divided back out to keep coastlines hairline at any size.
                context.DrawGeometry(Land, new Pen(Ink, 1d / scale), map);
            }
        }

        if (Answer is { } answer)
        {
            var at = Project(answer, scale, originX, originY);

            if (Pin is { } dropped)
            {
                // The line is the feedback: how far off, and in which direction.
                context.DrawLine(new Pen(AnswerFill, 2d, DashStyle.Dash), Project(dropped, scale, originX, originY), at);
            }

            DrawMarker(context, at, AnswerFill, 7d);
        }

        if (Pin is { } pin)
        {
            DrawMarker(context, Project(pin, scale, originX, originY), PinFill, 6d);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsDroppable)
        {
            return;
        }

        var (scale, originX, originY) = Layout();

        if (scale <= 0d)
        {
            return;
        }

        var point = e.GetPosition(this);

        var place = WorldMap.ToGlobe(
            (point.X - originX) / scale,
            (point.Y - originY) / scale);

        if (DropCommand is { } command && command.CanExecute(place))
        {
            command.Execute(place);
        }

        e.Handled = true;
    }

    /// <summary>
    /// How the map sits in the control: the largest it can be drawn without distorting,
    /// centred in whatever space is left over.
    /// </summary>
    private (double Scale, double OriginX, double OriginY) Layout()
    {
        var scale = Math.Min(Bounds.Width / WorldMap.Width, Bounds.Height / WorldMap.Height);

        if (scale <= 0d || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return (0d, 0d, 0d);
        }

        return (
            scale,
            (Bounds.Width - (WorldMap.Width * scale)) / 2d,
            (Bounds.Height - (WorldMap.Height * scale)) / 2d);
    }

    private static Point Project(GeoPoint place, double scale, double originX, double originY)
    {
        var (x, y) = WorldMap.ToMap(place);

        return new Point(originX + (x * scale), originY + (y * scale));
    }

    /// <summary>A ringed dot, so a marker stays visible over land or sea.</summary>
    private static void DrawMarker(DrawingContext context, Point at, IBrush fill, double radius)
    {
        context.DrawEllipse(fill, new Pen(Chrome, 2.5d), at, radius, radius);
    }
}
