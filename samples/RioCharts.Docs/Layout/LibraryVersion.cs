using System.Reflection;
using RioCharts.Blazor;

namespace RioCharts.Docs.Layout;

/// <summary>
/// The version of the RioCharts library this site runs, from the assembly's informational version
/// (Nerdbank.GitVersioning: "0.1.42-preview+0123456789"), and the latest released package (the newest release tag,
/// recorded at build time by the project file): the site is built from master and usually runs a newer build.
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

    /// <summary>The latest released package version (the newest release tag when the site was built), or null.</summary>
    public static string? LatestRelease { get; } = Metadata("LatestRelease");

    /// <summary>Commits in this build since <see cref="LatestRelease"/> (0 when the site is the release itself).</summary>
    public static int CommitsSinceRelease { get; } = int.TryParse(Metadata("CommitsSinceRelease"), out var n) ? n : 0;

    /// <summary>True when the site runs a build from master that is newer than the latest package on NuGet.</summary>
    public static bool Unreleased => LatestRelease is not null && CommitsSinceRelease > 0;

    /// <summary>The version to install: the latest release when known, else the site's own.</summary>
    public static string PackageVersion => LatestRelease ?? Version;

    private static string? Metadata(string key) =>
        typeof(LibraryVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : null;
}
