using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using RioCharts.Core;

namespace RioCharts.Blazor.Tests;

/// <summary>
/// Static server rendering (a page without an interactive render mode) renders with HtmlRenderer:
/// no circuit, no browser, OnAfterRender never runs and JS calls throw.
/// </summary>
public class StaticRenderingTests
{
    private sealed record Reading(DateTime Time, double Value);

    /// <summary>Like the JS runtime of static rendering: every call throws.</summary>
    private sealed class NoJSRuntime : IJSRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("JavaScript interop calls cannot be issued during static rendering.");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<(string Html, NoJSRuntime JS)> RenderAsync(Dictionary<string, object?> parameters)
    {
        var js = new NoJSRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<RioChart<Reading>>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return (html, js);
    }

    private static readonly Reading[] London = Enumerable.Range(0, 24).Select(i => new Reading(new DateTime(2026, 11, 16).AddHours(i), 19 + Math.Sin(i / 3.0))).ToArray();

    private static RenderFragment Series => builder =>
    {
        builder.OpenComponent<LineSeries<Reading>>(0);
        builder.AddAttribute(1, nameof(LineSeries<Reading>.Name), "London");
        builder.AddAttribute(2, nameof(LineSeries<Reading>.XValue), (Func<Reading, object?>)(r => r.Time));
        builder.AddAttribute(3, nameof(LineSeries<Reading>.YValue), (Func<Reading, double?>)(r => r.Value));
        builder.CloseComponent();
    };

    [Fact]
    public async Task A_chart_renders_as_static_html_without_calling_JS()
    {
        var (html, js) = await RenderAsync(new()
        {
            [nameof(RioChart<Reading>.Items)] = London,
            [nameof(RioChart<Reading>.Title)] = "Temperature",
            [nameof(RioChart<Reading>.ChildContent)] = Series,
        });

        Assert.Equal(0, js.Calls);
        Assert.Contains("<svg class=\"rio-chart-svg\"", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 640 300\"", html, StringComparison.Ordinal); // the default width, scaled to fit by the browser
        Assert.Contains("class=\"rio-line", html, StringComparison.Ordinal);
        Assert.Contains("Temperature", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_rendering_draws_plain_svg_without_a_tab_stop_download_button_or_canvas()
    {
        var (html, _) = await RenderAsync(new()
        {
            [nameof(RioChart<Reading>.Items)] = London,
            [nameof(RioChart<Reading>.Exportable)] = true,
            [nameof(RioChart<Reading>.SeriesLayer)] = SeriesLayer.Canvas,
            [nameof(RioChart<Reading>.ChildContent)] = Series,
        });

        Assert.DoesNotContain("tabindex", html, StringComparison.Ordinal);
        Assert.Contains("role=\"img\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rio-export-button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<canvas", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rio-hover", html, StringComparison.Ordinal);
        Assert.Contains("class=\"rio-line", html, StringComparison.Ordinal); // the series as SVG, not a blank canvas
    }

    [Fact]
    public async Task Static_legend_items_have_a_native_title()
    {
        RenderFragment two = builder =>
        {
            Series(builder);
            builder.OpenComponent<LineSeries<Reading>>(10);
            builder.AddAttribute(11, nameof(LineSeries<Reading>.Name), "Toronto");
            builder.AddAttribute(12, nameof(LineSeries<Reading>.XValue), (Func<Reading, object?>)(r => r.Time));
            builder.AddAttribute(13, nameof(LineSeries<Reading>.YValue), (Func<Reading, double?>)(r => r.Value + 3));
            builder.CloseComponent();
        };

        var (html, _) = await RenderAsync(new()
        {
            [nameof(RioChart<Reading>.Items)] = London,
            [nameof(RioChart<Reading>.ChildContent)] = two,
        });

        Assert.Contains("<title>London: ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-tip", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_compact_non_interactive_chart_renders_statically_at_its_width()
    {
        var (html, js) = await RenderAsync(new()
        {
            [nameof(RioChart<Reading>.Items)] = London,
            [nameof(RioChart<Reading>.Compact)] = true,
            [nameof(RioChart<Reading>.Interactive)] = false,
            [nameof(RioChart<Reading>.Width)] = 140.0,
            [nameof(RioChart<Reading>.Height)] = 28.0,
            [nameof(RioChart<Reading>.Texts)] = ChartTexts.Swedish,
            [nameof(RioChart<Reading>.ChildContent)] = Series,
        });

        Assert.Equal(0, js.Calls);
        Assert.Contains("viewBox=\"0 0 140 28\"", html, StringComparison.Ordinal);
        Assert.Contains("senast", html, StringComparison.Ordinal); // the Swedish summary
    }
}
