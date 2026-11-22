using System.Reflection;
using RioCharts.Blazor;

namespace RioCharts.Docs.Layout;

/// <summary>
/// The version of the RioCharts library this site runs, from the assembly's informational version
/// (Nerdbank.GitVersioning: "0.1.42-preview+0123456789"), so the badge matches the packages.
/// </summary>
public static class LibraryVersion
{
    private static readonly string Informational =
        typeof(RioChart<>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>The package version, e.g. "0.1.42-preview"; empty when unknown.</summary>
    public static string Version { get; } = Informational.Split('+')[0];

    /// <summary>The short commit the site was built from, or null.</summary>
    public static string? Commit { get; } = Informational.Split('+') is [_, var sha, ..] && sha.Length > 0
        ? sha[..Math.Min(7, sha.Length)]
        : null;
}
