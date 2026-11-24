using RioCharts.Blazor;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Optional registration of RioCharts. Charts work without it.</summary>
public static class RioChartsServiceCollectionExtensions
{
    /// <summary>
    /// Registers defaults for every chart in the app, e.g.
    /// <c>builder.Services.AddRioCharts(new RioChartDefaults { Culture = new("sv-SE"), Height = 260 })</c>.
    /// Unlike a cascaded <see cref="RioChartDefaults"/> it reaches every chart whatever its render mode. A setting on the
    /// chart wins, then a cascaded <see cref="RioChartDefaults"/>, then these. Nothing else needs registering: without
    /// this call charts use their built-in defaults.
    /// </summary>
    public static IServiceCollection AddRioCharts(this IServiceCollection services, RioChartDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(defaults);
        services.AddSingleton(defaults);
        return services;
    }
}
