using Polly;
using Microsoft.Extensions.Logging;

namespace OpenDataDownloader.Core;

/// <summary>
/// Provides dataset discovery and retrieval from a CKAN portal.
/// Implements automatic retry using exponential back-off via Polly,
/// pagination for search results and a package_search fallback when package_list
/// is unavailable.
/// </summary>
public class CkanDataProvider : IDataProvider, IDisposable
{
    private const int MaxPageSize = 1000;
    private const int MaxPackages = 1_000_000;

    private readonly HttpClient _httpClient;
    private readonly string _baseApiUrl;
    private readonly string? _apiKey;
    private readonly ILogger<CkanDataProvider>? _logger;
    private readonly AsyncPolicy _retryPolicy;
    private bool _disposed;

    /// <summary>
    /// Creates a new CkanDataProvider using the specified HTTP client base address as the API root.
    /// Configures resilience policies (retry, timeout) automatically.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured for the target CKAN portal.</param>
    /// <param name="apiKey">Optional API key for portals requiring authentication. Will be set in X-CKAN-API-Key header.</param>
    /// <param name="logger">Optional logger.</param>
    public CkanDataProvider(
        HttpClient httpClient,
        string? apiKey = null,
        ILogger<CkanDataProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _baseApiUrl = EnsureTrailingSlash(httpClient.BaseAddress?.ToString() ?? string.Empty);
        _apiKey = apiKey;
        _logger = logger;

        // Some portals (e.g. OpenGA) reject requests without a User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenDataDownloader/1.0");
        }

        // Add API key header if provided
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-CKAN-API-Key");
            _httpClient.DefaultRequestHeaders.Add("X-CKAN-API-Key", apiKey);
        }

        // Build resilience policies: retries with exponential back-off + timeout guard
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));

        var timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(60));
        _retryPolicy = Policy.WrapAsync(retryPolicy, timeoutPolicy);
    }

    /// <inheritdoc />
    public string Name { get; set; } = "CKAN";

    /// <inheritdoc />
    public async Task<List<string>> GetPackageListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            return await GetPackageListCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or DataFetcherException or InvalidOperationException)
        {
            _logger?.LogWarning(
                ex,
                "package_list failed for {BaseUrl}; falling back to package_search pagination.",
                _baseApiUrl);

            return await GetPackageListViaSearchAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<CkanPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty.", nameof(packageId));

        var encodedId = WebUtility.UrlEncode(packageId);
        var url = $"{_baseApiUrl}package_show?id={encodedId}";

        try
        {
            var response = await _retryPolicy.ExecuteAsync(ct =>
                _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureJson(jsonContent, "package_show");

            var ckanResponse = JsonSerializer.Deserialize<CkanResponse<CkanPackage>>(
                jsonContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (ckanResponse == null)
                return null;

            if (!ckanResponse.Success)
            {
                _logger?.LogWarning(
                    "package_show failed for {PackageId}: {Error}",
                    packageId,
                    ckanResponse.Error?.Message);
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
            throw new DataFetcherException(
                $"Failed to parse package details for {packageId}: {jsonEx.Message}", jsonEx);
        }
    }

    /// <inheritdoc />
    public async Task<List<PackageSearchResult>> SearchPackagesAsync(
        CkanSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);

        var requested = options.Limit <= 0 ? int.MaxValue : options.Limit;
        var pageSize = Math.Min(MaxPageSize, requested);

        var results = new List<PackageSearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;

        while (results.Count < requested && start < MaxPackages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (page, total) = await SearchPackagesPageAsync(options, start, pageSize, cancellationToken)
                .ConfigureAwait(false);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                if (!string.IsNullOrWhiteSpace(item.Id) && seen.Add(item.Id))
                {
                    results.Add(item);
                }
            }

            start += page.Count;

            if (page.Count < pageSize || start >= total)
            {
                break;
            }
        }

        return results;
    }

    private async Task<List<string>> GetPackageListCoreAsync(CancellationToken cancellationToken)
    {
        var url = $"{_baseApiUrl}package_list";

        var response = await _retryPolicy.ExecuteAsync(ct =>
            _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), cancellationToken);

        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureJson(jsonContent, "package_list");

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

    private async Task<List<string>> GetPackageListViaSearchAsync(CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;

        while (start < MaxPackages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (page, total) = await SearchPackagesPageAsync(
                    new CkanSearchOptions { Limit = MaxPageSize },
                    start,
                    MaxPageSize,
                    cancellationToken)
                .ConfigureAwait(false);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                var name = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name;
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    names.Add(name);
                }
            }

            start += page.Count;

            if (page.Count < MaxPageSize || start >= total)
            {
                break;
            }
        }

        _logger?.LogInformation(
            "package_search fallback returned {Count} package names for {BaseUrl}.",
            names.Count,
            _baseApiUrl);

        return names;
    }

    private async Task<(List<PackageSearchResult> Results, int Total)> SearchPackagesPageAsync(
        CkanSearchOptions options,
        int start,
        int rows,
        CancellationToken cancellationToken)
    {
        var queryParams = new List<string>
        {
            $"rows={rows}",
            $"start={start}"
        };

        if (!string.IsNullOrWhiteSpace(options.Q))
        {
            queryParams.Add($"q={WebUtility.UrlEncode(options.Q)}");
        }

        if (!string.IsNullOrWhiteSpace(options.Sort))
        {
            queryParams.Add($"sort={WebUtility.UrlEncode(options.Sort)}");
        }

        foreach (var filter in BuildFilterQueries(options))
        {
            queryParams.Add($"fq={WebUtility.UrlEncode(filter)}");
        }

        var url = $"{_baseApiUrl}package_search?{string.Join("&", queryParams)}";

        try
        {
            var response = await _retryPolicy.ExecuteAsync(ct =>
                _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureJson(jsonContent, "package_search");

            using var document = JsonDocument.Parse(jsonContent);
            return ParseSearchResults(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            throw new DataFetcherException("HTTP error while searching packages", httpEx);
        }
        catch (JsonException jsonEx)
        {
            throw new DataFetcherException(
                $"Failed to parse search results response: {jsonEx.Message}", jsonEx);
        }
    }

    private static IEnumerable<string> BuildFilterQueries(CkanSearchOptions options)
    {
        if (options.Tags != null)
        {
            foreach (var tag in options.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                yield return $"tags:\"{EscapeFilterValue(tag)}\"";
            }
        }

        if (options.TagsContains is { Count: > 0 })
        {
            var tags = options.TagsContains
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => $"\"{EscapeFilterValue(t)}\"");
            yield return $"tags:({string.Join(" AND ", tags)})";
        }

        if (options.Categories != null)
        {
            foreach (var category in options.Categories.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                yield return $"groups:\"{EscapeFilterValue(category)}\"";
            }
        }
    }

    private static string EscapeFilterValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static (List<PackageSearchResult> Results, int Total) ParseSearchResults(JsonDocument doc)
    {
        var results = new List<PackageSearchResult>();
        var total = 0;

        var root = doc.RootElement;
        if (!TryGetProperty(root, "success", out var successEl) || !successEl.GetBoolean())
            return (results, total);

        if (!TryGetProperty(root, "result", out var resultEl))
            return (results, total);

        if (TryGetProperty(resultEl, "count", out var countEl) && countEl.ValueKind == JsonValueKind.Number)
        {
            total = countEl.GetInt32();
        }

        // The result object from package_search has a "results" array
        if (!TryGetProperty(resultEl, "results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
            return (results, total);

        foreach (var element in resultsEl.EnumerateArray())
        {
            var pkg = new PackageSearchResult();

            TryGetString(element, "id", out var id);
            pkg.Id = id;

            TryGetString(element, "name", out var name);
            pkg.Name = name;

            TryGetString(element, "title", out var title);
            pkg.Title = title;

            TryGetString(element, "state", out var state);
            pkg.State = state;

            TryGetString(element, "license_id", out var licenseId);
            pkg.LicenseId = licenseId;

            // Tags — could be array of strings or array of objects with "name" field
            if (TryGetProperty(element, "tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tagObj in tagsEl.EnumerateArray())
                {
                    if (tagObj.ValueKind == JsonValueKind.String)
                        pkg.Tags.Add(tagObj.GetString() ?? string.Empty);
                    else if (TryGetStringDirect(tagObj, "name", out var tName) && tName != null)
                        pkg.Tags.Add(tName);
                }
            }

            // Groups
            if (TryGetProperty(element, "groups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var groupObj in groupsEl.EnumerateArray())
                {
                    if (groupObj.ValueKind == JsonValueKind.String)
                        pkg.Groups.Add(groupObj.GetString() ?? string.Empty);
                    else if (TryGetStringDirect(groupObj, "title", out var gTitle) && gTitle != null)
                        pkg.Groups.Add(gTitle);
                }
            }

            // NumResources
            if (TryGetProperty(element, "num_resources", out var numResEl) &&
                numResEl.ValueKind == JsonValueKind.Number)
            {
                pkg.NumResources = numResEl.GetInt32();
            }

            results.Add(pkg);
        }

        return (results, total);
    }

    private static void EnsureJson(string content, string action)
    {
        var trimmed = content.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            var preview = content.Length > 200 ? content[..200] : content;
            throw new JsonException(
                $"The response of '{action}' is not JSON. The portal may be blocking the request. " +
                $"Preview: {preview}");
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        return element.TryGetProperty(propertyName, out property);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetStringDirect(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return !string.IsNullOrEmpty(value);
        }
        value = null;
        return false;
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url : url + '/';
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CkanDataProvider));
        }
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
