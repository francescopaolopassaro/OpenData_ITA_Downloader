using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenDataDownloader.Core;

namespace OpenDataDownloader.Cli;

/// <summary>
/// Writes machine-readable manifests describing what was downloaded.
/// </summary>
internal static class ManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteCkanManifest(
        DownloadSessionSummary summary,
        string outputDirectory,
        string command)
    {
        Directory.CreateDirectory(outputDirectory);

        var manifest = new
        {
            generatedAt = DateTimeOffset.Now,
            command,
            totalDurationSeconds = Math.Round(summary.TotalDuration.TotalSeconds, 1),
            totalSuccessfulDownloads = summary.TotalSuccessfulDownloads,
            totalFailedDownloads = summary.TotalFailedDownloads,
            totalSkippedResources = summary.TotalSkippedResources,
            totalPackagesFound = summary.TotalPackagesFound,
            totalFailedPackages = summary.TotalFailedPackages,
            portals = summary.Portals.Select(portal => new
            {
                name = portal.PortalName,
                totalPackages = portal.TotalPackages,
                successfulDownloads = portal.SuccessfulDownloads,
                failedDownloads = portal.FailedDownloads,
                skippedResources = portal.SkippedResources,
                failedPackages = portal.FailedPackages,
                durationSeconds = Math.Round(portal.Duration.TotalSeconds, 1),
                error = portal.Error,
                packages = portal.PackageResults.Select(package => new
                {
                    packageId = package.PackageId,
                    packageName = package.PackageName,
                    success = package.Success,
                    successfulDownloads = package.SuccessfulDownloads,
                    failedDownloads = package.FailedDownloads,
                    skippedResources = package.SkippedResources,
                    error = package.Error,
                    resources = package.Resources.Select(resource => new
                    {
                        resourceId = resource.ResourceId,
                        resourceName = resource.ResourceName,
                        format = resource.Format,
                        status = resource.Success ? "ok" : "failed",
                        filePath = resource.FilePath,
                        durationSeconds = Math.Round(resource.Duration.TotalSeconds, 2),
                        error = resource.Error
                    })
                })
            })
        };

        WriteJson(Path.Combine(outputDirectory, "manifest.json"), manifest);

        var csvPath = Path.Combine(outputDirectory, "manifest.csv");
        using var writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(
            "portal,package_id,package_name,resource_id,resource_name,format,status,file_path,error");

        foreach (var portal in summary.Portals)
        {
            foreach (var package in portal.PackageResults)
            {
                foreach (var resource in package.Resources)
                {
                    writer.WriteLine(string.Join(
                        ",",
                        new[]
                        {
                            portal.PortalName,
                            package.PackageId,
                            package.PackageName,
                            resource.ResourceId ?? string.Empty,
                            resource.ResourceName ?? string.Empty,
                            resource.Format ?? string.Empty,
                            resource.Success ? "ok" : "failed",
                            resource.FilePath ?? string.Empty,
                            resource.Error ?? string.Empty
                        }.Select(EscapeCsv)));
                }
            }
        }
    }

    public static void WriteNormattivaManifest(
        IEnumerable<string> paths,
        string outputDirectory,
        string command)
    {
        Directory.CreateDirectory(outputDirectory);

        var entries = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new
                {
                    filePath = info.FullName,
                    fileName = info.Name,
                    exists = info.Exists,
                    sizeBytes = info.Exists ? info.Length : 0,
                    lastWriteTime = info.Exists ? info.LastWriteTime : (DateTime?)null
                };
            })
            .ToList();

        var manifest = new
        {
            generatedAt = DateTimeOffset.Now,
            command,
            archives = entries
        };

        WriteJson(Path.Combine(outputDirectory, "normattiva-manifest.json"), manifest);

        var csvPath = Path.Combine(outputDirectory, "normattiva-manifest.csv");
        using var writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("file_name,file_path,exists,size_bytes,last_write_time");

        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(
                ",",
                new[]
                {
                    entry.fileName,
                    entry.filePath,
                    entry.exists ? "true" : "false",
                    entry.sizeBytes.ToString(CultureInfo.InvariantCulture),
                    entry.lastWriteTime?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
                }.Select(EscapeCsv)));
        }
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
