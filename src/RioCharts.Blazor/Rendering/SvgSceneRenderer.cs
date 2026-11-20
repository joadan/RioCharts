using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using RioCharts.Core.Scene;

namespace RioCharts.Blazor.Rendering;

/// <summary>
/// Renders a <see cref="Core.Scene.Scene"/> as SVG elements. Re-renders only when it gets a new
/// scene instance, so chart re-renders without layout changes cost nothing here.
/// </summary>
public sealed class SvgSceneRenderer : ComponentBase
{
    private Scene? _rendered;
    private int _clipCounter;

    [Parameter, EditorRequired] public Scene? Scene { get; set; }

    /// <summary>Prefix for generated element ids (clip paths); must be unique per chart.</summary>
    [Parameter] public string IdPrefix { get; set; } = "rio";

    /// <summary>Raised when an interactive group (e.g. a legend item) is activated.</summary>
    [Parameter] public EventCallback<NodeInteraction> OnInteraction { get; set; }

    /// <summary>Leave out the series groups (they are drawn on a canvas instead).</summary>
    [Parameter] public bool OmitSeries { get; set; }

    private bool _renderedOmit;

    protected override bool ShouldRender() => !ReferenceEquals(Scene, _rendered) || OmitSeries != _renderedOmit;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _rendered = Scene;
        _renderedOmit = OmitSeries;
        _clipCounter = 0;
        if (Scene is null)
        {
            return;
        }

        Render(builder, Scene.Static, Scene.Styles);
        Render(builder, Scene.Series, Scene.Styles);
        Render(builder, Scene.Overlay, Scene.Styles);
    }

    private void Render(RenderTreeBuilder b, Node node, StyleTable styles)
    {
        // Each node gets its own region so sequence numbers stay stable inside loops and recursion.
        b.OpenRegion(0);
        switch (node)
        {
            case GroupNode g:
                RenderGroup(b, g, styles);
                break;
            case PathNode p:
                b.OpenElement(1, "path");
                AddClass(b, 2, p.Class);
                b.AddAttribute(3, "d", SvgWriter.PathD(p.Path));
                AddPaint(b, 4, styles.Get(p.Style));
                b.CloseElement();
                break;
            case RectNode r:
                b.OpenElement(18, "rect");
                AddClass(b, 19, r.Class);
                b.AddAttribute(21, "x", SvgWriter.N(r.Bounds.X));
                b.AddAttribute(22, "y", SvgWriter.N(r.Bounds.Y));
                b.AddAttribute(23, "width", SvgWriter.N(r.Bounds.Width));
                b.AddAttribute(24, "height", SvgWriter.N(r.Bounds.Height));
                if (r.CornerRadius > 0)
                {
                    b.AddAttribute(25, "rx", SvgWriter.N(r.CornerRadius));
                }

                AddPaint(b, 26, styles.Get(r.Style));
                b.CloseElement();
                break;
            case TextNode t:
                RenderText(b, t, styles);
                break;
        }

        b.CloseRegion();
    }

    private void RenderGroup(RenderTreeBuilder b, GroupNode g, StyleTable styles)
    {
        string? clipId = null;
        if (g.Clip is { } clip)
        {
            clipId = $"{IdPrefix}-clip-{_clipCounter++}";
            b.OpenElement(40, "clipPath");
            b.AddAttribute(41, "id", clipId);
            b.OpenElement(42, "rect");
            b.AddAttribute(43, "x", SvgWriter.N(clip.X));
            b.AddAttribute(44, "y", SvgWriter.N(clip.Y));
            b.AddAttribute(45, "width", SvgWriter.N(clip.Width));
            b.AddAttribute(46, "height", SvgWriter.N(clip.Height));
            b.CloseElement();
            b.CloseElement();
        }

        b.OpenElement(50, "g");
        if (g.Key is not null)
        {
            b.SetKey(g.Key);
        }

        AddClass(b, 51, g.Class);
        if (clipId is not null)
        {
            b.AddAttribute(52, "clip-path", $"url(#{clipId})");
        }

        if (SvgWriter.Transform(g.Transform) is { } transform)
        {
            b.AddAttribute(53, "transform", transform);
        }

        if (g.Interaction is { } interaction)
        {
            b.AddAttribute(54, "role", "button");
            b.AddAttribute(55, "tabindex", "0");
            if (interaction.Pressed is { } pressed)
            {
                b.AddAttribute(56, "aria-pressed", pressed ? "true" : "false");
            }

            b.AddAttribute(57, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => OnInteraction.InvokeAsync(interaction)));
            b.AddAttribute(58, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
                e.Key is "Enter" or " " or "Spacebar" ? OnInteraction.InvokeAsync(interaction) : Task.CompletedTask));
        }

        if (g.AriaLabel is not null)
        {
            b.AddAttribute(59, "aria-label", g.AriaLabel);
            if (g.Interaction is null)
            {
                b.AddAttribute(60, "role", "group");
            }
        }

        if (g.Key is not null && g.Key.StartsWith("annotation:", StringComparison.Ordinal))
        {
            b.AddAttribute(61, "data-key", g.Key);
        }

        if (g.Title is not null)
        {
            b.OpenElement(66, "title");
            b.AddContent(67, g.Title);
            b.CloseElement();
        }

        foreach (var child in g.Children)
        {
            if (OmitSeries && child is GroupNode cg && CanvasSeriesWriter.IsSeries(cg))
            {
                continue;
            }

            Render(b, child, styles);
        }

        b.CloseElement();
    }

    private static void RenderText(RenderTreeBuilder b, TextNode t, StyleTable styles)
    {
        var style = styles.Get(t.Style);
        b.OpenElement(68, "text");
        AddClass(b, 69, t.Class);
        b.AddAttribute(71, "x", SvgWriter.N(t.At.X));
        b.AddAttribute(72, "y", SvgWriter.N(t.At.Y));
        b.AddAttribute(73, "text-anchor", t.Anchor switch { Anchor.Start => "start", Anchor.End => "end", _ => "middle" });
        b.AddAttribute(74, "dominant-baseline", t.Baseline switch { Baseline.Top => "hanging", Baseline.Bottom => "alphabetic", _ => "central" });
        b.AddAttribute(75, "font-size", SvgWriter.N(style.FontSize));
        if (style.FontWeight != 400)
        {
            b.AddAttribute(76, "font-weight", style.FontWeight);
        }

        b.AddAttribute(77, "fill", SvgWriter.Color(style.Fill ?? ThemeTokens.Text));
        if (t.Rotation != 0)
        {
            b.AddAttribute(78, "transform", $"rotate({SvgWriter.N(t.Rotation)} {SvgWriter.N(t.At.X)} {SvgWriter.N(t.At.Y)})");
        }

        b.AddContent(79, t.Text);
        b.CloseElement();
    }

    private static void AddClass(RenderTreeBuilder b, int sequence, string? cssClass)
    {
        if (cssClass is not null)
        {
            b.AddAttribute(sequence, "class", cssClass);
        }
    }

    private static void AddPaint(RenderTreeBuilder b, int seq, Style s)
    {
        b.AddAttribute(seq, "fill", s.Fill is null ? "none" : SvgWriter.Color(s.Fill));
        if (s.FillOpacity is { } fo)
        {
            b.AddAttribute(seq + 1, "fill-opacity", SvgWriter.N(fo));
        }

        if (s.Stroke is not null)
        {
            b.AddAttribute(seq + 2, "stroke", SvgWriter.Color(s.Stroke));
            b.AddAttribute(seq + 3, "stroke-width", SvgWriter.N(s.StrokeWidth ?? 1));
            b.AddAttribute(seq + 4, "stroke-linejoin", "round");
            b.AddAttribute(seq + 5, "stroke-linecap", "round");
        }

        if (s.DashArray is not null)
        {
            b.AddAttribute(seq + 6, "stroke-dasharray", s.DashArray);
        }

        if (s.Opacity is { } o)
        {
            b.AddAttribute(seq + 7, "opacity", SvgWriter.N(o));
        }
    }
}
