using Polly;

namespace OpenDataDownloader.Core;

/// <summary>
/// Stores downloaded files on disk using a safe folder hierarchy:
///   DownloadRoot / PortalName / SafePackageName / SafeResourceName.ext
/// Implements retry logic for download failures using Polly.
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _downloadRoot;
    private readonly AsyncRetryPolicy _retryPolicy;
    private bool _disposed;

    /// <summary>
    /// Creates a new FileStorageService.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for downloading file content.</param>
    /// <param name="downloadRoot">The root directory where all files will be stored.</param>
    public FileStorageService(HttpClient httpClient, string downloadRoot)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(downloadRoot);

        _httpClient = httpClient;
        _downloadRoot = Path.GetFullPath(downloadRoot);
        Directory.CreateDirectory(_downloadRoot);
        
        // Retry once on transient network errors during file download
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .RetryAsync(1, (exception, attempt) =>
            {
                Console.WriteLine($"[RETRY] Re-fetching after {exception.Message}");
            });
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        string portalName,
        string packageName,
        CkanResource resource,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileStorageService));

        if (string.IsNullOrWhiteSpace(resource.Url))
            throw new InvalidOperationException($"Resource '{resource.Name}' has no URL.");

        var safePackage = MakeSafeFileName(packageName ?? "dataset");
        var safeResource = MakeSafeFileName(resource.Name ?? "resource");
        var extension = FormatFilter.ToExtension(resource.Format);
        var fileName = $"{safeResource}{extension}";

        var dir = Path.Combine(_downloadRoot, portalName, safePackage);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, fileName);

        // Skip already-downloaded non-empty files
        if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
        {
            Console.WriteLine($"    Already present: {fileName}");
            return filePath;
        }

        Console.WriteLine($"    Downloading: {fileName} ...");

        try
        {
            await _retryPolicy.ExecuteAsync(() => 
                FetchAndSaveAsync(resource.Url!, filePath, cancellationToken), 
                cancellationToken);

            Console.WriteLine($"    Saved: {fileName} at {filePath}");
            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException($"HTTP error downloading '{fileName}'", httpEx);
        }
    }

    /// <inheritdoc />
    public Task DownloadBatchAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        // Placeholder — batch download depends on specific API requirements.
        // Callers should use a typed provider (e.g., NormattivaProvider) instead.
        return Task.FromException(new NotImplementedException(
            "Batch download not implemented here. Use a specific provider."));
    }

    private static async Task FetchAndSaveAsync(string url, string filePath, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var fs = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.None, 
            bufferSize: 81920, useAsync: true);
        await response.Content.CopyToAsync(fs, cancellationToken);
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
        return safe.Trim().TrimEnd('.');
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _disposed = true;
    }
}
