using Polly;
using Microsoft.Extensions.Logging;

namespace OpenDataDownloader.Core;

/// <summary>
/// Stores downloaded files on disk using a safe folder hierarchy:
///   DownloadRoot / PortalName / SafePackageName / SafeResourceName[_resourceId].ext
/// Downloads are written to a temporary ".part" file and moved into place only when
/// complete, so interrupted downloads are never mistaken for valid files.
/// Supports parallel batch downloads with configurable concurrency.
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _downloadRoot;
    private readonly AsyncPolicy _retryPolicy;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly bool _includeResourceIdInFileName;
    private bool _disposed;

    /// <summary>
    /// Creates a new FileStorageService.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for downloading file content.</param>
    /// <param name="downloadRoot">The root directory where all files will be stored.</param>
    public FileStorageService(HttpClient httpClient, string downloadRoot)
        : this(httpClient, downloadRoot, maxConcurrency: 4)
    {
    }

    /// <summary>
    /// Creates a new FileStorageService with a configurable maximum concurrency limit for batch operations.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for downloading file content.</param>
    /// <param name="downloadRoot">The root directory where all files will be stored.</param>
    /// <param name="maxConcurrency">Maximum number of parallel downloads in batch operations (default 4).</param>
    /// <param name="includeResourceIdInFileName">
    /// When true (default), a short resource-id suffix is appended to file names to avoid collisions
    /// between resources with the same name inside a dataset.
    /// </param>
    public FileStorageService(
        HttpClient httpClient,
        string downloadRoot,
        int maxConcurrency = 4,
        bool includeResourceIdInFileName = true)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(downloadRoot);
        if (maxConcurrency < 1 || maxConcurrency > 32)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be between 1 and 32.");

        _httpClient = httpClient;
        _downloadRoot = Path.GetFullPath(downloadRoot);
        Directory.CreateDirectory(_downloadRoot);
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _includeResourceIdInFileName = includeResourceIdInFileName;

        // Retry once on transient network errors during file download
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .RetryAsync(1);
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        string portalName,
        string packageName,
        CkanResource resource,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(resource.Url))
            throw new InvalidOperationException($"Resource '{resource.Name}' has no URL.");

        var filePath = GetLocalPath(portalName, packageName, resource);

        // Skip already-downloaded non-empty files
        if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
        {
            Console.WriteLine($"    Already present: {Path.GetFileName(filePath)}");
            return filePath;
        }

        Console.WriteLine($"    Downloading: {Path.GetFileName(filePath)} ...");

        try
        {
            await _retryPolicy.ExecuteAsync(() => FetchAndSaveAsync(resource.Url!, filePath, cancellationToken));

            Console.WriteLine($"    Saved: {Path.GetFileName(filePath)} at {filePath}");
            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException($"HTTP error downloading '{Path.GetFileName(filePath)}'", httpEx);
        }
    }

    /// <inheritdoc />
    public async Task<string> DownloadBatchAsync(
        string sourceUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        Console.WriteLine($"  [FileStorage] Downloading batch archive from {sourceUrl} to {fullPath} ...");

        try
        {
            await _retryPolicy.ExecuteAsync(() => FetchAndSaveAsync(sourceUrl, fullPath, cancellationToken));

            Console.WriteLine($"  [FileStorage] Batch saved: {fullPath}");
            return fullPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException($"HTTP error downloading batch archive '{fullPath}'", httpEx);
        }
    }

    /// <inheritdoc />
    public async Task<List<ResourceDownloadResult>> DownloadResourcesAsync(
        IEnumerable<CkanResource> resources,
        string portalName,
        string packageName,
        bool includeDocuments = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ThrowIfDisposed();

        var resourceList = resources.ToList();

        // Filter out invalid resources upfront
        var validResources = resourceList
            .Where(r => FormatFilter.IsSupported(r, includeDocuments) && !string.IsNullOrWhiteSpace(r.Url))
            .ToList();

        if (validResources.Count == 0)
        {
            Console.WriteLine($"  [FileStorage] No valid resources to download from {resourceList.Count} provided.");
            return new List<ResourceDownloadResult>();
        }

        var tasks = validResources.Select(async resource =>
        {
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                return await DownloadSingleResourceFromBatch(portalName, packageName, resource, cancellationToken);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }).ToList();

        var results = (await Task.WhenAll(tasks)).ToList();

        return results;
    }

    /// <summary>
    /// Computes the local path for a resource without downloading it.
    /// </summary>
    public string GetLocalPath(string portalName, string packageName, CkanResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var safePackage = MakeSafeFileName(packageName ?? "dataset");
        var safeResource = MakeSafeFileName(resource.Name ?? "resource");
        var extension = FormatFilter.ToExtension(resource);

        // Strip an extension already present in the resource name so the id suffix can be
        // inserted before the extension instead of after it.
        var baseName = safeResource.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? safeResource[..^extension.Length].TrimEnd('.')
            : safeResource;

        if (_includeResourceIdInFileName && !string.IsNullOrWhiteSpace(resource.Id))
        {
            var safeId = MakeSafeFileName(resource.Id);
            var shortId = safeId.Length > 8 ? safeId[..8] : safeId;
            baseName = $"{baseName}__{shortId}";
        }

        baseName += extension;

        var dir = Path.Combine(_downloadRoot, MakeSafeFileName(portalName), safePackage);
        return Path.Combine(dir, baseName);
    }

    private async Task<ResourceDownloadResult> DownloadSingleResourceFromBatch(
        string portalName,
        string packageName,
        CkanResource resource,
        CancellationToken cancellationToken)
    {
        var result = new ResourceDownloadResult
        {
            ResourceId = resource.Id,
            ResourceName = resource.Name,
            Format = FormatFilter.ResolveFormat(resource) ?? resource.Format
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var filePath = await DownloadAsync(portalName, packageName, resource, cancellationToken);
            result.FilePath = filePath;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        result.Duration = sw.Elapsed;
        return result;
    }

    private async Task FetchAndSaveAsync(string url, string filePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var extension = Path.GetExtension(filePath);
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            throw new DataFetcherException(
                $"The server returned an HTML page instead of '{Path.GetFileName(filePath)}'.");
        }

        var expectedLength = response.Content.Headers.ContentLength;
        if (expectedLength == 0)
        {
            throw new DataFetcherException($"The server returned an empty response for '{Path.GetFileName(filePath)}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tempPath = filePath + ".part";

        try
        {
            await using (var fs = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            var actualLength = new FileInfo(tempPath).Length;
            if (expectedLength.HasValue && expectedLength.Value != actualLength)
            {
                throw new DataFetcherException(
                    $"Incomplete download for '{Path.GetFileName(filePath)}': " +
                    $"expected {expectedLength.Value} bytes, received {actualLength}.");
            }

            if (actualLength == 0)
            {
                throw new DataFetcherException($"The server returned an empty response for '{Path.GetFileName(filePath)}'.");
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Removes invalid filename characters from a name and replaces them with underscores.
    /// Trims trailing dots to avoid Windows issues.
    /// </summary>
    internal static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        safe = safe.Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileStorageService));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _concurrencyLimiter.Dispose();
        _disposed = true;
    }
}
