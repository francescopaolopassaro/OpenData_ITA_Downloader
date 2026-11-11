namespace OpenDataDownloader.Core;

/// <summary>
/// Detailed statistics for a single package processing session.
/// </summary>
public class PackageDownloadResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the package.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Gets or sets the display name of the package.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the package was processed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of resources downloaded from this package.
    /// </summary>
    public int SuccessfulDownloads { get; set; }

    /// <summary>
    /// Gets or sets the number of resources skipped because their format is not supported.
    /// </summary>
    public int SkippedResources { get; set; }

    /// <summary>
    /// Gets the number of resources that failed to download.
    /// </summary>
    public int FailedDownloads => Resources.Count(r => !r.Success);

    /// <summary>
    /// Gets or sets the list of individual resource download results.
    /// </summary>
    public List<ResourceDownloadResult> Resources { get; set; } = new();

    /// <summary>
    /// Gets or sets the duration taken to process this package.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets error details if processing failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Result of downloading a single resource from a package.
/// </summary>
public class ResourceDownloadResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the resource.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Gets or sets the name of the resource.
    /// </summary>
    public string? ResourceName { get; init; }

    /// <summary>
    /// Gets or sets the format of the resource (e.g., CSV, JSON).
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Gets or sets the full path where the file was saved, or null on failure.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets whether this resource download succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the duration of this resource download.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets error details if the download failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Summary statistics for one portal during a download session.
/// Extended with detailed package-level and resource-level information.
/// </summary>
public record PortalDownloadResult
{
    /// <summary>
    /// Gets or sets the display name of the portal.
    /// </summary>
    public required string PortalName { get; init; }

    /// <summary>
    /// Gets or sets the total number of packages found on the portal.
    /// </summary>
    public int TotalPackages { get; set; }

    /// <summary>
    /// Gets or sets the total time spent processing this portal.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets individual results for each package processed.
    /// </summary>
    public List<PackageDownloadResult> PackageResults { get; init; } = new();

    /// <summary>
    /// Gets or sets the total number of successful resource downloads.
    /// </summary>
    public int SuccessfulDownloads => PackageResults.Sum(p => p.SuccessfulDownloads);

    /// <summary>
    /// Gets the total number of resources that failed to download.
    /// </summary>
    public int FailedDownloads => PackageResults.Sum(p => p.FailedDownloads);

    /// <summary>
    /// Gets the total number of resources skipped because their format is not supported.
    /// </summary>
    public int SkippedResources => PackageResults.Sum(p => p.SkippedResources);

    /// <summary>
    /// Gets or sets the total number of failed packages.
    /// </summary>
    public int FailedPackages => PackageResults.Count(p => !p.Success);

    /// <summary>
    /// Gets or sets the overall success status of the portal processing.
    /// </summary>
    public bool Success => PackageResults.Count > 0 && FailedPackages == 0;

    /// <summary>
    /// Gets or sets error details if the entire portal processing failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Complete summary for an entire download session across all portals.
/// </summary>
public record DownloadSessionSummary
{
    /// <summary>
    /// Gets or sets the total duration of the entire download session.
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// Gets or sets the overall success/failure status.
    /// </summary>
    public bool Success => Portals.All(p => p.Error is null);

    /// <summary>
    /// Gets or sets detailed results per portal.
    /// </summary>
    public List<PortalDownloadResult> Portals { get; init; } = new();

    /// <summary>
    /// Gets or sets the total number of successful downloads across all portals.
    /// </summary>
    public int TotalSuccessfulDownloads => Portals.Sum(p => p.SuccessfulDownloads);

    /// <summary>
    /// Gets the total number of resources that failed to download across all portals.
    /// </summary>
    public int TotalFailedDownloads => Portals.Sum(p => p.FailedDownloads);

    /// <summary>
    /// Gets the total number of resources skipped because their format is not supported.
    /// </summary>
    public int TotalSkippedResources => Portals.Sum(p => p.SkippedResources);

    /// <summary>
    /// Gets or sets the total number of packages found across all portals.
    /// </summary>
    public int TotalPackagesFound => Portals.Sum(p => p.TotalPackages);

    /// <summary>
    /// Gets or sets the total number of failed packages across all portals.
    /// </summary>
    public int TotalFailedPackages => Portals.Sum(p => p.FailedPackages);

    /// <summary>
    /// Gets or sets error details if the session-level processing failed.
    /// </summary>
    public string? SessionError { get; set; }
}

/// <summary>
/// Types of progress events raised during download operations.
/// </summary>
public enum DownloadProgressType
{
    /// <summary>
    /// Started processing a portal.
    /// </summary>
    PortalStarted,

    /// <summary>
    /// Completed processing a portal.
    /// </summary>
    PortalCompleted,

    /// <summary>
    /// About to fetch details for a specific package.
    /// </summary>
    PackageFetching,

    /// <summary>
    /// Finished fetching details for a specific package.
    /// </summary>
    PackageFetched,

    /// <summary>
    /// Starting download of a specific resource.
    /// </summary>
    ResourceDownloading,

    /// <summary>
    /// Finished download of a specific resource.
    /// </summary>
    ResourceDownloaded,

    /// <summary>
    /// The entire download session is complete.
    /// </summary>
    SessionCompleted
}

/// <summary>
/// Progress report for observable download status updates.
/// </summary>
public class DownloadProgressReport
{
    /// <summary>
    /// Gets or sets the type of this progress event.
    /// </summary>
    public DownloadProgressType Type { get; init; }

    /// <summary>
    /// Gets or sets the display name of the portal involved (if applicable).
    /// </summary>
    public string? PortalName { get; init; }

    /// <summary>
    /// Gets or sets the package identifier (if applicable).
    /// </summary>
    public string? PackageId { get; init; }

    /// <summary>
    /// Gets or sets the package title (if available).
    /// </summary>
    public string? PackageTitle { get; init; }

    /// <summary>
    /// Gets or sets the resource identifier (if applicable).
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Gets or sets the resource name (if applicable).
    /// </summary>
    public string? ResourceName { get; init; }

    /// <summary>
    /// Gets or sets the file path where the resource was saved (if applicable).
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets or sets the current progress index (1-based) out of total items.
    /// </summary>
    public int CurrentIndex { get; init; }

    /// <summary>
    /// Gets or sets the total number of items (if known).
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets or sets any error message associated with this event.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets or sets additional context data.
    /// </summary>
    public Dictionary<string, object?>? Context { get; init; }
}
