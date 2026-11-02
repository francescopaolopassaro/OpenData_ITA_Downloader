using System.Net.Http.Json;
using System.Web;
using Polly;

namespace OpenDataDownloader.Core;

/// <summary>
/// Provides dataset discovery and retrieval from a CKAN portal.
/// Implements automatic retry using exponential back-off via Polly.
/// </summary>
public class CkanDataProvider : IDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseApiUrl;
    private readonly ResiliencePolicies? _policies;
    private bool _disposed;

    /// <summary>
    /// Creates a new CkanDataProvider using the specified HTTP client base address as the API root.
    /// Configures resilience policies (retry, timeout) automatically.
    /// </summary>
    public CkanDataProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        
        _httpClient = httpClient;
        _baseApiUrl = EnsureTrailingSlash(httpClient.BaseAddress?.ToString() ?? string.Empty);
        
        // Build resilience policies: retries with exponential back-off + timeout guard
        _policies = CreateResiliencePolicies();
    }

    /// <inheritdoc />
    public string Name => "CKAN";

    /// <inheritdoc />
    public async Task<List<string>> GetPackageListAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CkanDataProvider));

        var url = $"{_baseApiUrl}package_list";

        try
        {
            var response = await _policies!.RetryPolicy.ExecuteAsync(() => 
                _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken), 
                cancellationToken);

            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ckanResponse = JsonSerializer.Deserialize<CkanResponse<List<string>>>(
                jsonContent, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (ckanResponse == null || !ckanResponse.Success)
            {
                var errorMsg = ckanResponse?.Error?.Message ?? "Unknown error";
                throw new InvalidOperationException($"package_list request failed: {errorMsg}");
            }

            return ckanResponse.Result ?? new List<string>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException("HTTP error while fetching package list", httpEx);
        }
        catch (JsonException jsonEx)
        {
            throw new DataFetcherException("Failed to parse package list response", jsonEx);
        }
    }

    /// <inheritdoc />
    public async Task<CkanPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CkanDataProvider));

        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty.", nameof(packageId));

        var encodedId = HttpUtility.UrlEncode(packageId);
        var url = $"{_baseApiUrl}package_show?id={encodedId}";

        try
        {
            var response = await _policies!.RetryPolicy.ExecuteAsync(() =>
                _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ckanResponse = JsonSerializer.Deserialize<CkanResponse<CkanPackage>>(
                jsonContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (ckanResponse == null)
                return null;

            if (!ckanResponse.Success)
            {
                Console.WriteLine($"  [WARN] package_show failed for {packageId}: {ckanResponse.Error?.Message}");
                return null;
            }

            return ckanResponse.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException($"HTTP error fetching details for package {packageId}", httpEx);
        }
        catch (JsonException jsonEx)
        {
            throw new DataFetcherException($"Failed to parse package details for {packageId}", jsonEx);
        }
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url : url + '/';
    }

    private static ResiliencePolicies CreateResiliencePolicies()
    {
        // Retry policy: up to 3 attempts with exponential back-off (1s, 2s, 4s)
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (exception, timeSpan, attempt, context) =>
                {
                    Console.WriteLine($"[RETRY] Attempt {attempt}/{3} after {timeSpan.TotalSeconds}s — {exception.Message}");
                });

        // Timeout policy: fail fast if any single request takes longer than 60 seconds
        var timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(60));

        // Compose: apply both (timeout inside retry so that each retry can use full 60s)
        var compositePolicy = Policy.Wrap(retryPolicy, timeoutPolicy);

        return new ResiliencePolicies { RetryPolicy = compositePolicy };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _httpClient.Dispose();
        _disposed = true;
    }

    // Helper class to hold composed resilience policies
    private class ResiliencePolicies
    {
        public AsyncPolicy RetryPolicy { get; set!; }
    }
}
