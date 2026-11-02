namespace OpenDataDownloader.Core;

/// <summary>
/// Orchestrates the complete download workflow: iterates through a list of CKAN portals,
/// retrieves each dataset's details, filters resources by format, and saves them to disk.
/// Also supports a separate Normattiva batch-download workflow.
/// </summary>
public class OrchestrationService
{
    private readonly IDataProvider _dataProvider;
    private readonly IFileStorageService _fileStorage;
    private readonly INormattivaProvider? _normattivaProvider;
    private readonly ILogger<OrchestrationService>? _logger;

    /// <summary>
    /// Creates a new orchestration service.
    /// </summary>
    /// <param name="dataProvider">The data provider (e.g., CkanDataProvider).</param>
    /// <param name="fileStorage">The file storage service responsible for saving downloaded files.</param>
    /// <param name="normattivaProvider">Optional provider for Normattiva legal-document batch downloads.</param>
    /// <param name="logger">Optional logger for structured logging.</param>
    public OrchestrationService(
        IDataProvider dataProvider,
        IFileStorageService fileStorage,
        INormattivaProvider? normattivaProvider = null,
        ILogger<OrchestrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataProvider);
        ArgumentNullException.ThrowIfNull(fileStorage);

        _dataProvider = dataProvider;
        _fileStorage = fileStorage;
        _normattivaProvider = normattivaProvider;
        _logger = logger;
    }

    /// <summary>
    /// Downloads every downloadable resource from all configured portals.
    /// Each portal is processed sequentially with a small delay between datasets
    /// to avoid overloading the remote API.
    /// </summary>
    /// <param name="portals">The list of CKAN portals to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A summary containing success/failure counts per portal.</returns>
    public async Task<DownloadSummary> DownloadAllAsync(
        IEnumerable<CkanPortal> portals,
        CancellationToken cancellationToken = default)
    {
        var summary = new DownloadSummary();
        var portalsList = portals.ToList();

        foreach (var portal in portalsList)
        {
            var portalResult = new PortalResult { PortalName = portal.Name };

            try
            {
                _logger?.LogInformation("Starting download from portal '{Portal}'", portal.Name);
                Console.WriteLine($"\n--- Downloading from {portal.Name} ---");

                // Step 1: Get list of all package IDs
                var packageIds = await _dataProvider.GetPackageListAsync(cancellationToken);
                portalResult.TotalPackages = packageIds.Count;

                if (_logger != null)
                    _logger.LogInformation("Found {Count} packages on '{Portal}'", packageIds.Count, portal.Name);
                else
                    Console.WriteLine($"  Found {packageIds.Count} packages.");

                int index = 0;
                foreach (var packageId in packageIds)
                {
                    index++;
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    portalResult.ProcessedPackages++;

                    try
                    {
                        var package = await _dataProvider.GetPackageDetailsAsync(packageId, cancellationToken);
                        if (package == null || !package.Resources.Any())
                        {
                            _logger?.LogDebug("No resources found for package {PackageId}", packageId);
                            continue;
                        }

                        // Step 2: Process each resource
                        foreach (var resource in package.Resources)
                        {
                            // Check format support
                            if (!FormatFilter.IsSupported(resource.Format))
                            {
                                _logger?.LogDebug(
                                    "Skipping unsupported format '{Format}' for resource '{Resource}'",
                                    resource.Format, resource.Name);
                                continue;
                            }

                            // Check URL availability
                            if (string.IsNullOrWhiteSpace(resource.Url))
                            {
                                _logger?.LogWarning("Missing URL for resource '{Resource}'", resource.Name);
                                continue;
                            }

                            // Download the file
                            await _fileStorage.DownloadAsync(portal.Name, package.Name, resource, cancellationToken);
                            portalResult.SuccessfulDownloads++;
                        }
                    }
                    catch (Exception ex)
                    {
                        portalResult.FailedPackages++;
                        _logger?.LogError(ex, "Error processing package {PackageId}", packageId);
                        Console.WriteLine($"    [ERROR package {packageId}]: {ex.Message}");
                    }

                    // Small delay to avoid overwhelming the API
                    await Task.Delay(200, cancellationToken);
                }

                Console.WriteLine("--- Finished downloading from " + portal.Name + " ---");
            }
            catch (Exception ex)
            {
                portalResult.Error = ex.Message;
                _logger?.LogError(ex, "Fatal error for portal '{Portal}'", portal.Name);
                Console.WriteLine($"[ERROR] {portal.Name}: {ex.Message}");
            }

            summary.Portals.Add(portalResult);
        }

        return summary;
    }

    /// <summary>
    /// Executes a batch download from Normattiva.
    /// Only runs when an INormattivaProvider is configured.
    /// </summary>
    /// <param name="config">Normattiva configuration settings.</param>
    /// <param name="request">The batch request defining which documents to include.</param>
    /// <param name="destinationPath">Where to save the downloaded ZIP archive.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The full path of the downloaded ZIP file, or null on failure.</returns>
    public async Task<string?> DownloadNormattivaBatchAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (_normattivaProvider == null)
        {
            _logger?.LogWarning("Normattiva provider not configured. Skipping batch download.");
            return null;
        }

        try
        {
            return await _normattivaProvider.RequestBatchDownloadAsync(
                config, request, destinationPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during Normattiva batch download");
            throw;
        }
    }

    /// <summary>
    /// Contains summary statistics for one download session.
    /// </summary>
    public record DownloadSummary(List<PortalResult> Portals);

    /// <summary>
    /// Summary statistics for one portal.
    /// </summary>
    public record PortalResult
    {
        public required string PortalName { get; init; }
        public int TotalPackages { get; set; }
        public int ProcessedPackages { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedPackages { get; set; }
        public string? Error { get; set; }
    }
}
