using System.Net.Http.Json;
using Polly;

namespace OpenDataDownloader.Core;

/// <summary>
/// Interacts with the Normattiva open-data API for legal document batch downloads.
/// Handles token-based polling with exponential back-off via Polly.
/// </summary>
public class NormattivaProvider : INormattivaProvider
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;
    private bool _disposed;

    /// <summary>
    /// Creates a new NormattivaProvider.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API calls.</param>
    public NormattivaProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        
        // Retry on transient failures during API communication
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (exception, timeSpan, attempt, context) =>
                {
                    Console.WriteLine($"[RETRY] Normattiva API attempt {attempt}/3 after {timeSpan.TotalSeconds}s — {exception.Message}");
                });
    }

    /// <inheritdoc />
    public async Task<string?> RequestBatchDownloadAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NormattivaProvider));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var batchUrl = EnsureTrailingSlash(config.BaseApiUrl) + config.BatchDownloadEndpoint;

        try
        {
            // Step 1: Send batch download request
            var requestBody = JsonSerializer.Serialize(request, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            Console.WriteLine($"  [Normattiva] Sending batch request to {batchUrl}...");

            var batchResponse = await _retryPolicy.ExecuteAsync(() => 
                _httpClient.PostAsync(batchUrl, content, cancellationToken), 
                cancellationToken);

            batchResponse.EnsureSuccessStatusCode();

            var batchResultJson = await batchResponse.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"  [Normattiva] Batch response: {batchResultJson}");

            using var doc = JsonDocument.Parse(batchResultJson);
            if (!doc.RootElement.TryGetProperty("token", out var tokenElement) || !tokenElement.TryGetString(out var token))
            {
                Console.WriteLine("  [Normattiva] No token in response. Check documentation.");
                return null;
            }

            Console.WriteLine($"  [Normattiva] Token obtained: {token}. Waiting for archive generation...");

            // Step 2: Poll until archive is ready or timeout expires
            var maxAttempts = 30; // up to 30 minutes total with 60s intervals
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

                var downloadUrl = $"{EnsureTrailingSlash(config.BaseApiUrl)}{config.DownloadEndpoint}?token={HttpUtility.UrlEncode(token!)}";
                
                var downloadResponse = await _retryPolicy.ExecuteAsync(() => 
                    _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
                    cancellationToken);

                if (downloadResponse.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    Console.WriteLine($"  [Normattiva] Still processing ({attempt}/{maxAttempts}). Will retry...");
                    continue;
                }

                if (downloadResponse.IsSuccessStatusCode)
                {
                    // Step 3: Download the ZIP file
                    var fullPath = Path.GetFullPath(destinationPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                    await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 
                        bufferSize: 81920, useAsync: true);
                    await downloadResponse.Content.CopyToAsync(fs, cancellationToken);

                    Console.WriteLine($"  [Normattiva] ZIP saved at: {fullPath}");
                    return fullPath;
                }

                downloadResponse.EnsureSuccessStatusCode();
            }

            Console.WriteLine($"  [Normattiva] Timeout after {maxAttempts} attempts.");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException("HTTP error communicating with Normattiva API", httpEx);
        }
        catch (JsonException jsonEx)
        {
            throw new DataFetcherException("Failed to parse Normattiva API response", jsonEx);
        }
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url : url + '/';
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
