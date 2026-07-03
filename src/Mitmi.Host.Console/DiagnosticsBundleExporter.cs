using System.IO.Compression;
using System.Text.Json;
using Mitmi.Application.Configuration;

namespace Mitmi.Host.Console;

internal static class DiagnosticsBundleExporter
{
    private const int BundleManifestVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task ExportAsync(
        RuntimeConfiguration configuration,
        string bundlePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            throw new ArgumentException("Diagnostics bundle path is required.", nameof(bundlePath));
        }

        var resolvedBundlePath = Path.GetFullPath(bundlePath);
        if (File.Exists(resolvedBundlePath))
        {
            throw new IOException($"Diagnostics bundle '{resolvedBundlePath}' already exists.");
        }

        var bundleDirectory = Path.GetDirectoryName(resolvedBundlePath);
        if (!string.IsNullOrWhiteSpace(bundleDirectory))
        {
            Directory.CreateDirectory(bundleDirectory);
        }

        var artifacts = CollectArtifacts(configuration, resolvedBundlePath);
        await using var bundleStream = new FileStream(
            resolvedBundlePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        using var archive = new ZipArchive(bundleStream, ZipArchiveMode.Create);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddFileAsync(archive, artifact.Path, artifact.EntryName, cancellationToken);
        }

        await AddManifestAsync(
            archive,
            configuration,
            resolvedBundlePath,
            artifacts,
            cancellationToken);
    }

    private static IReadOnlyList<BundleArtifact> CollectArtifacts(
        RuntimeConfiguration configuration,
        string resolvedBundlePath)
    {
        var artifacts = new List<BundleArtifact>();

        if (File.Exists(configuration.ConfigurationFilePath))
        {
            artifacts.Add(new BundleArtifact(
                configuration.ConfigurationFilePath,
                $"configuration/{Path.GetFileName(configuration.ConfigurationFilePath)}",
                "configuration"));
        }

        if (configuration.Logging.File.Enabled &&
            !string.IsNullOrWhiteSpace(configuration.Logging.File.Path) &&
            File.Exists(configuration.Logging.File.Path))
        {
            artifacts.Add(new BundleArtifact(
                configuration.Logging.File.Path,
                $"logs/{Path.GetFileName(configuration.Logging.File.Path)}",
                "fileLog"));
        }

        if (configuration.Capture.Enabled && Directory.Exists(configuration.Capture.OutputPath))
        {
            foreach (var captureFile in Directory.GetFiles(configuration.Capture.OutputPath, "*", SearchOption.AllDirectories))
            {
                var resolvedCaptureFile = Path.GetFullPath(captureFile);
                if (string.Equals(resolvedCaptureFile, resolvedBundlePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                artifacts.Add(new BundleArtifact(
                    resolvedCaptureFile,
                    $"captures/{Path.GetRelativePath(configuration.Capture.OutputPath, resolvedCaptureFile).Replace('\\', '/')}",
                    "capture"));
            }
        }

        return artifacts;
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string filePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            useAsync: true);
        await fileStream.CopyToAsync(entryStream, cancellationToken);
    }

    private static async Task AddManifestAsync(
        ZipArchive archive,
        RuntimeConfiguration configuration,
        string bundlePath,
        IReadOnlyList<BundleArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var manifest = new BundleManifest(
            BundleManifestVersion,
            DateTimeOffset.UtcNow,
            configuration.Session.Id.Value,
            configuration.Session.Protocol.Value,
            configuration.ConfigurationFilePath,
            configuration.Logging.File.Path,
            configuration.Capture.OutputPath,
            bundlePath,
            artifacts.Select(artifact => new BundleManifestArtifact(
                artifact.Kind,
                artifact.EntryName,
                artifact.Path)).ToArray());

        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions, cancellationToken);
    }

    private sealed record BundleArtifact(
        string Path,
        string EntryName,
        string Kind);

    private sealed record BundleManifest(
        int BundleManifestVersion,
        DateTimeOffset CreatedUtc,
        string SessionId,
        string ProtocolId,
        string ConfigurationFilePath,
        string? FileLogPath,
        string CaptureOutputPath,
        string BundlePath,
        IReadOnlyList<BundleManifestArtifact> Artifacts);

    private sealed record BundleManifestArtifact(
        string Kind,
        string EntryName,
        string SourcePath);
}
