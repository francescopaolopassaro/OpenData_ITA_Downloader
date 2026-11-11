using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OpenDataDownloader.Core;

/// <summary>
/// Orchestrates the complete download workflow: iterates through a list of CKAN portals,
/// retrieves each dataset's details, filters resources by format, and saves them to disk.
/// Supports progressive search-based filtering for targeted downloads and detailed result tracking.
/// Also supports Normattiva batch-download workflows (pre-packaged collections and full-corpus search).
/// </summary>
public class OrchestrationService
{
    private readonly IDataProvider? _dataProvider;
    private readonly IFileStorageService _fileStorage;
    private readonly INormattivaProvider? _normattivaProvider;
    private readonly ILogger<OrchestrationService>? _logger;
    private readonly Action<DownloadProgressReport>? _progressCallback;
    private readonly Func<CkanPortal, IDataProvider> _providerResolver;

    /// <summary>
    /// Event raised for each significant download progress update.
    /// Subscribable alternative to the callback constructor parameter.
    /// </summary>
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Gets or sets the delay applied between two datasets (default 200 ms) to avoid
    /// overloading remote APIs.
    /// </summary>
    public TimeSpan DelayBetweenPackages { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets a value indicating whether office documents (PDF, HTML, DOC...) are
    /// downloaded in addition to machine-readable formats.
    /// </summary>
    public bool IncludeDocuments { get; set; }

    /// <summary>
    /// Gets or sets an optional maximum number of datasets to process per portal.
    /// Zero or negative means no limit.
    /// </summary>
    public int MaxPackagesPerPortal { get; set; }

    /// <summary>
    /// Creates a new orchestration service.
    /// </summary>
    /// <param name="dataProvider">
    /// The fallback data provider. May be null when <paramref name="providerResolver"/> is supplied.
    /// </param>
    /// <param name="fileStorage">The file storage service responsible for saving downloaded files.</param>
    /// <param name="normattivaProvider">Optional provider for Normattiva legal-document batch downloads.</param>
    /// <param name="logger">Optional logger for structured logging.</param>
    /// <param name="progressCallback">Optional callback for real-time progress reports.</param>
    /// <param name="providerResolver">
    /// Optional resolver that returns the correct provider for each portal. When omitted,
    /// <paramref name="dataProvider"/> is used for every portal.
    /// </param>
    public OrchestrationService(
        IDataProvider? dataProvider,
        IFileStorageService fileStorage,
        INormattivaProvider? normattivaProvider = null,
        ILogger<OrchestrationService>? logger = null,
        Action<DownloadProgressReport>? progressCallback = null,
        Func<CkanPortal, IDataProvider>? providerResolver = null)
    {
        if (dataProvider == null && providerResolver == null)
        {
            throw new ArgumentNullException(
                nameof(dataProvider),
                "Either a data provider or a provider resolver must be supplied.");
        }

        ArgumentNullException.ThrowIfNull(fileStorage);

        _dataProvider = dataProvider;
        _fileStorage = fileStorage;
        _normattivaProvider = normattivaProvider;
        _logger = logger;
        _progressCallback = progressCallback;
        _providerResolver = providerResolver ?? (_ => dataProvider!);
    }

    #region CKAN Download Methods

    /// <summary>
    /// Downloads every downloadable resource from all configured portals.
    /// Each portal is processed sequentially with a small delay between datasets
    /// to avoid overloading the remote API. Uses the package ID listing approach.
    /// </summary>
    /// <param name="portals">The list of CKAN portals to process.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A detailed session summary with per-package results.</returns>
    public async Task<DownloadSessionSummary> DownloadAllAsync(
        IEnumerable<CkanPortal> portals,
        CancellationToken cancellationToken = default)
    {
        return await DownloadWithFilterAsync(portals, searchOptions: null, cancellationToken);
    }

    /// <summary>
    /// Downloads resources matching the specified search criteria from all configured portals.
    /// Uses the CKAN package_search endpoint for efficient pre-filtering before loading full metadata.
    /// </summary>
    /// <param name="portals">The list of CKAN portals to process.</param>
    /// <param name="searchOptions">Search options for filtering packages (q, tags, categories, etc.).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A detailed session summary with per-package results.</returns>
    public async Task<DownloadSessionSummary> DownloadMatchingAsync(
        IEnumerable<CkanPortal> portals,
        CkanSearchOptions searchOptions,
        CancellationToken cancellationToken = default)
    {
        return await DownloadWithFilterAsync(portals, searchOptions, cancellationToken);
    }

    /// <summary>
    /// Internal method that performs download with optional search filtering.
    /// When filter is null, fetches all packages via package_list.
    /// When filter is provided, uses package_search for pre-filtering.
    /// </summary>
    private async Task<DownloadSessionSummary> DownloadWithFilterAsync(
        IEnumerable<CkanPortal> portals,
        CkanSearchOptions? searchOptions,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var summary = new DownloadSessionSummary();
        var portalsList = portals.ToList();

        foreach (var portal in portalsList)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var portalResult = new PortalDownloadResult { PortalName = portal.Name };
            var portalSw = Stopwatch.StartNew();

            try
            {
                var provider = _providerResolver(portal);

                _logger?.LogInformation("Starting download from portal '{Portal}'", portal.Name);
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.PortalStarted,
                    PortalName = portal.Name,
                    TotalCount = portalsList.Count,
                    CurrentIndex = 1
                });

                List<string> packageIdsToProcess;

                if (searchOptions != null)
                {
                    _logger?.LogInformation(
                        "Using search filter for portal '{Portal}' — Q: {Q}, Tags: {TagCount}, Limit: {Limit}",
                        portal.Name, searchOptions.Q, searchOptions.Tags?.Count ?? 0, searchOptions.Limit);

                    // Search first to identify relevant packages
                    var searchResults = await provider.SearchPackagesAsync(searchOptions, cancellationToken);
                    packageIdsToProcess = searchResults.Select(p => p.Id).Distinct().ToList();
                    portalResult.TotalPackages = packageIdsToProcess.Count;

                    if (MaxPackagesPerPortal > 0 && packageIdsToProcess.Count > MaxPackagesPerPortal)
                    {
                        packageIdsToProcess = packageIdsToProcess.Take(MaxPackagesPerPortal).ToList();
                    }

                    if (searchResults.Count > 0)
                    {
                        _logger?.LogInformation("Search found {Count} matching packages", searchResults.Count);
                        Console.WriteLine($"  [{portal.Name}] Search found {searchResults.Count} matching packages.");
                    }
                    else
                    {
                        _logger?.LogWarning("Search returned no results for portal '{Portal}'", portal.Name);
                        Console.WriteLine($"  [{portal.Name}] No matching packages found.");
                        summary.Portals.Add(portalResult);
                        continue;
                    }

                    // Build a map from search results for quick title lookup
                    var searchMap = searchResults.ToDictionary(s => s.Id, s => s.Title);

                    // Process each matched package
                    for (int i = 0; i < packageIdsToProcess.Count; i++)
                    {
                        var pkgId = packageIdsToProcess[i];
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var pkgTitle = searchMap.TryGetValue(pkgId, out var title) ? title : null;

                        _logger?.LogDebug(
                            "Processing package {Index}/{Total} ({PackageId})",
                            i + 1, packageIdsToProcess.Count, pkgId);

                        var pkgResult = await ProcessSinglePackageAsync(
                            portal, provider, pkgId, pkgTitle, cancellationToken);
                        portalResult.PackageResults.Add(pkgResult);

                        await DelayBetweenPackagesAsync(i, packageIdsToProcess.Count, cancellationToken);
                    }
                }
                else
                {
                    // Legacy behavior: fetch all package IDs
                    var packageIds = await provider.GetPackageListAsync(cancellationToken);
                    portalResult.TotalPackages = packageIds.Count;
                    packageIdsToProcess = packageIds;

                    if (MaxPackagesPerPortal > 0 && packageIdsToProcess.Count > MaxPackagesPerPortal)
                    {
                        packageIdsToProcess = packageIdsToProcess.Take(MaxPackagesPerPortal).ToList();
                    }

                    if (_logger != null)
                        _logger.LogInformation("Found {Count} packages on '{Portal}'", packageIds.Count, portal.Name);
                    else
                        Console.WriteLine($"  [{portal.Name}] Found {packageIds.Count} packages.");

                    EmitProgress(new DownloadProgressReport
                    {
                        Type = DownloadProgressType.PortalStarted,
                        PortalName = portal.Name,
                        TotalCount = packageIds.Count
                    });

                    for (int i = 0; i < packageIdsToProcess.Count; i++)
                    {
                        var pkgId = packageIdsToProcess[i];
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        _logger?.LogDebug(
                            "Processing package {Index}/{Total} ({PackageId})",
                            i + 1, packageIdsToProcess.Count, pkgId);

                        var pkgResult = await ProcessSinglePackageAsync(
                            portal, provider, pkgId, null, cancellationToken);
                        portalResult.PackageResults.Add(pkgResult);

                        await DelayBetweenPackagesAsync(i, packageIdsToProcess.Count, cancellationToken);
                    }
                }

                portalResult.Duration = portalSw.Elapsed;
                Console.WriteLine($"[{portal.Name}] Completed in {portalSw.Elapsed:mm\\:ss}.");
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.PortalCompleted,
                    PortalName = portal.Name
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                portalResult.Duration = portalSw.Elapsed;
                portalResult.Error = "Cancelled";
                _logger?.LogWarning("Download from portal '{Portal}' was cancelled.", portal.Name);
                summary.Portals.Add(portalResult);
                break;
            }
            catch (Exception ex)
            {
                portalResult.Duration = portalSw.Elapsed;
                portalResult.Error = ex.Message;
                _logger?.LogError(ex, "Fatal error for portal '{Portal}'", portal.Name);
                Console.WriteLine($"[{portal.Name}] ERRORE: {ex.Message}");
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.PortalCompleted,
                    PortalName = portal.Name,
                    Error = ex.Message
                });
            }

            summary.Portals.Add(portalResult);
        }

        summary.TotalDuration = sw.Elapsed;
        EmitProgress(new DownloadProgressReport
        {
            Type = DownloadProgressType.SessionCompleted,
            TotalCount = summary.Portals.Count
        });

        return summary;
    }

    /// <summary>
    /// Processes a single package: fetches its details, filters resources, and downloads them.
    /// </summary>
    private async Task<PackageDownloadResult> ProcessSinglePackageAsync(
        CkanPortal portal,
        IDataProvider provider,
        string packageId,
        string? packageTitle,
        CancellationToken cancellationToken)
    {
        var pkgTimer = Stopwatch.StartNew();
        var result = new PackageDownloadResult
        {
            PackageId = packageId,
            PackageName = packageTitle ?? packageId
        };

        try
        {
            EmitProgress(new DownloadProgressReport
            {
                Type = DownloadProgressType.PackageFetching,
                PortalName = portal.Name,
                PackageId = packageId,
                PackageTitle = packageTitle
            });

            var package = await provider.GetPackageDetailsAsync(packageId, cancellationToken);
            if (package == null)
            {
                result.Success = false;
                result.Error = "package_show returned no data (dataset deleted, private or inaccessible).";
                return result;
            }

            if (package.Resources.Count == 0)
            {
                result.Success = true; // Not having resources isn't a failure
                result.PackageName = package.Title;
                result.Duration = pkgTimer.Elapsed;
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.PackageFetched,
                    PortalName = portal.Name,
                    PackageId = packageId,
                    PackageTitle = package.Title,
                });
                return result;
            }

            result.PackageName = package.Title;

            // Apply additional filters on the package itself
            var validResources = package.Resources
                .Where(r => FormatFilter.IsSupported(r, IncludeDocuments) && !string.IsNullOrWhiteSpace(r.Url))
                .ToList();

            result.SkippedResources = package.Resources.Count - validResources.Count;

            // Emit a start event for each resource, then download the whole batch in
            // parallel (the FileStorageService enforces the concurrency limit).
            foreach (var resource in validResources)
            {
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.ResourceDownloading,
                    PortalName = portal.Name,
                    PackageId = package.Id,
                    PackageTitle = package.Title,
                    ResourceId = resource.Id,
                    ResourceName = resource.Name
                });
            }

            var downloadResults = await _fileStorage.DownloadResourcesAsync(
                validResources, portal.Name, package.Name, IncludeDocuments, cancellationToken);

            result.Resources.AddRange(downloadResults);
            result.SuccessfulDownloads = downloadResults.Count(r => r.Success);

            foreach (var downloaded in downloadResults.Where(r => r.Success))
            {
                EmitProgress(new DownloadProgressReport
                {
                    Type = DownloadProgressType.ResourceDownloaded,
                    PortalName = portal.Name,
                    PackageId = package.Id,
                    ResourceId = downloaded.ResourceId,
                    ResourceName = downloaded.ResourceName,
                    FilePath = downloaded.FilePath
                });
            }

            // A package is successful only when every resource that should have been
            // downloaded actually succeeded.
            result.Success = result.Resources.All(r => r.Success);
            EmitProgress(new DownloadProgressReport
            {
                Type = DownloadProgressType.PackageFetched,
                PortalName = portal.Name,
                PackageId = packageId,
                PackageTitle = package.Title
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.Success = false;
            result.Error = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger?.LogError(ex, "Error processing package {PackageId}", packageId);
            EmitProgress(new DownloadProgressReport
            {
                Type = DownloadProgressType.PackageFetched,
                PortalName = portal.Name,
                PackageId = packageId,
                Error = ex.Message
            });
        }

        result.Duration = pkgTimer.Elapsed;
        return result;
    }

    private async Task DelayBetweenPackagesAsync(int index, int total, CancellationToken cancellationToken)
    {
        if (index >= total - 1 || DelayBetweenPackages <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(DelayBetweenPackages, cancellationToken);
    }

    #endregion

    #region Normattiva

    /// <summary>
    /// Executes a batch download from Normattiva using the asynchronous search workflow.
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
    /// Downloads the whole Italian legal corpus (enactment dates 1861 → today)
    /// using the Normattiva asynchronous search.
    /// </summary>
    public Task<string?> DownloadNormattivaAllLawsAsync(
        NormattivaConfig config,
        string destinationPath,
        string? formato = null,
        string? richiestaExport = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var request = NormattivaBatchRequest.AllLaws(
            formato ?? config.DefaultFormat,
            richiestaExport ?? config.DefaultExport,
            to);

        return DownloadNormattivaBatchAsync(config, request, destinationPath, cancellationToken);
    }

    /// <summary>
    /// Downloads every pre-packaged Normattiva collection (or one variant per collection).
    /// </summary>
    /// <param name="config">Normattiva configuration settings.</param>
    /// <param name="destinationRoot">Directory where the ZIP archives will be saved.</param>
    /// <param name="formato">Export format (default from config).</param>
    /// <param name="formatoRichiesta">Collection variant O/V/M (default from config).</param>
    /// <param name="allVariants">When true, downloads every O/V/M variant instead of one per collection.</param>
    /// <param name="maxCollections">Optional maximum number of collections to download (0 = all).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<List<string>> DownloadNormattivaPredefinedCollectionsAsync(
        NormattivaConfig config,
        string destinationRoot,
        string? formato = null,
        string? formatoRichiesta = null,
        bool allVariants = false,
        int maxCollections = 0,
        CancellationToken cancellationToken = default)
    {
        if (_normattivaProvider == null)
        {
            _logger?.LogWarning("Normattiva provider not configured. Skipping collections download.");
            return new List<string>();
        }

        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(destinationRoot);

        var format = formato ?? config.DefaultFormat;
        var variant = formatoRichiesta ?? config.DefaultExport;

        var collections = await _normattivaProvider
            .GetPredefinedCollectionsAsync(config, cancellationToken);

        var selected = allVariants
            ? collections
            : collections
                .GroupBy(c => c.NomeCollezione ?? string.Empty)
                .Select(group =>
                    group.FirstOrDefault(c =>
                        string.Equals(c.FormatoCollezione, variant, StringComparison.OrdinalIgnoreCase))
                    ?? group.First())
                .ToList();

        if (maxCollections > 0)
        {
            selected = selected.Take(maxCollections).ToList();
        }

        Directory.CreateDirectory(destinationRoot);
        var paths = new List<string>();

        foreach (var collection in selected)
        {
            if (string.IsNullOrWhiteSpace(collection.NomeCollezione))
            {
                continue;
            }

            var safeName = FileStorageService.MakeSafeFileName(collection.NomeCollezione);
            var fileName = $"{safeName}_{collection.FormatoCollezione}_{format}.zip";
            var destination = Path.Combine(destinationRoot, fileName);

            if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            {
                Console.WriteLine($"  [Normattiva] Già presente: {fileName}");
                paths.Add(destination);
                continue;
            }

            var path = await _normattivaProvider.DownloadPredefinedCollectionAsync(
                config,
                collection.NomeCollezione,
                format,
                collection.FormatoCollezione ?? variant,
                destination,
                cancellationToken);

            paths.Add(path);
        }

        return paths;
    }

    #endregion

    #region Progress Management

    /// <summary>
    /// Raises the ProgressChanged event and invokes the progress callback.
    /// Thread-safe via lock.
    /// </summary>
    private void EmitProgress(DownloadProgressReport report)
    {
        _progressCallback?.Invoke(report);
        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(report));
    }

    #endregion
}

/// <summary>
/// EventArgs carrying a DownloadProgressReport for the ProgressChanged event.
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the progress report carried by this event.
    /// </summary>
    public DownloadProgressReport Report { get; }

    public DownloadProgressEventArgs(DownloadProgressReport report)
    {
        Report = report;
    }
}
