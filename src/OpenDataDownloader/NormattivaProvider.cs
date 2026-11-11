using System.Text;

namespace OpenDataDownloader.Core;

/// <summary>
/// Interacts with the Normattiva open-data API for legal document downloads.
/// Implements the documented asynchronous workflow and the pre-packaged collections API.
/// </summary>
public class NormattivaProvider : INormattivaProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxAttempts = 3;

    /// <summary>An empty ZIP archive is exactly 22 bytes (End Of Central Directory record).</summary>
    private const int EmptyZipLength = 22;

    private readonly HttpClient _httpClient;
    private bool _disposed;

    /// <summary>
    /// Creates a new NormattivaProvider.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API calls.</param>
    public NormattivaProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<string?> RequestBatchDownloadAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(request);

        var token = await StartAsyncSearchAsync(config, request, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  [Normattiva] Token ottenuto: {token}");

        var status = await ConfirmAsyncSearchAsync(config, token, cancellationToken).ConfigureAwait(false);
        if (status != null)
        {
            Console.WriteLine(
                $"  [Normattiva] Ricerca confermata — stato {status.Stato}: {status.DescrizioneStato}");
        }

        status = await WaitForCompletionAsync(config, token, cancellationToken).ConfigureAwait(false);
        var countText = status.TotAtti is { } total ? $"{total:N0} atti" : "conteggio non disponibile";
        Console.WriteLine(
            $"  [Normattiva] Elaborazione completata — {countText} (stato {status.Stato}).");

        return await DownloadAsyncCollectionAsync(config, token, destinationPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<NormattivaCollection>> GetPredefinedCollectionsAsync(
        NormattivaConfig config,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);

        var url = BuildUrl(config, config.PredefinedCollectionsEndpoint);
        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<NormattivaCollection>>(json, JsonOptions)
            ?? new List<NormattivaCollection>();
    }

    /// <inheritdoc />
    public async Task<string> DownloadPredefinedCollectionAsync(
        NormattivaConfig config,
        string collectionName,
        string formato,
        string formatoRichiesta,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var url = BuildUrl(config, config.PredefinedCollectionDownloadEndpoint)
            + $"?nome={WebUtility.UrlEncode(collectionName)}"
            + $"&formato={WebUtility.UrlEncode(formato)}"
            + $"&formatoRichiesta={WebUtility.UrlEncode(formatoRichiesta)}";

        Console.WriteLine($"  [Normattiva] Scarico collezione '{collectionName}' ({formato}/{formatoRichiesta})...");

        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken)
            .ConfigureAwait(false);
        return await SaveResponseAsync(response, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NormattivaSearchStatus> GetSearchStatusAsync(
        NormattivaConfig config,
        string token,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var url = BuildUrl(config, config.CheckStatusEndpoint) + "/" + WebUtility.UrlEncode(token);
        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken)
            .ConfigureAwait(false);

        // 303 means "search completed, download URL available".
        if (response.StatusCode == HttpStatusCode.SeeOther)
        {
            return new NormattivaSearchStatus
            {
                Stato = 3,
                DescrizioneStato = "Ricerca completata"
            };
        }

        response.EnsureSuccessStatusCode();

        // If the handler followed a redirect to the download endpoint, the response is the
        // archive itself rather than a JSON status: treat it as completed.
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return new NormattivaSearchStatus
            {
                Stato = 3,
                DescrizioneStato = "Ricerca completata"
            };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{'))
        {
            return new NormattivaSearchStatus { Stato = 3 };
        }

        return JsonSerializer.Deserialize<NormattivaSearchStatus>(json, JsonOptions)
            ?? new NormattivaSearchStatus { Stato = 3 };
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsyncCollectionAsync(
        NormattivaConfig config,
        string token,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var url = BuildUrl(config, config.AsyncCollectionDownloadEndpoint)
            + "/" + WebUtility.UrlEncode(token);

        Console.WriteLine("  [Normattiva] Scarico l'archivio della ricerca asincrona...");

        using var response = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken)
            .ConfigureAwait(false);
        return await SaveResponseAsync(response, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartAsyncSearchAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(config, config.NewAsyncSearchEndpoint);

        Console.WriteLine("  [Normattiva] Avvio ricerca asincrona...");
        using var response = await SendAsync(HttpMethod.Post, url, request, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DataFetcherException(
                $"Normattiva nuova-ricerca ha risposto {(int)response.StatusCode}: {Truncate(body)}");
        }

        var token = ExtractToken(body);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new DataFetcherException(
                $"Normattiva nuova-ricerca non ha restituito un token: {Truncate(body)}");
        }

        return token;
    }

    private async Task<NormattivaSearchStatus?> ConfirmAsyncSearchAsync(
        NormattivaConfig config,
        string token,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(config, config.ConfirmAsyncSearchEndpoint);
        var body = new { token };

        using var response = await SendAsync(HttpMethod.Put, url, body, cancellationToken)
            .ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DataFetcherException(
                $"Normattiva conferma-ricerca ha risposto {(int)response.StatusCode}: {Truncate(json)}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<NormattivaSearchStatus>(json, JsonOptions);
    }

    private async Task<NormattivaSearchStatus> WaitForCompletionAsync(
        NormattivaConfig config,
        string token,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + config.Timeout;
        var lastStatus = new NormattivaSearchStatus { Stato = 0 };
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            lastStatus = await GetSearchStatusAsync(config, token, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"  [Normattiva] Stato {lastStatus.Stato} — {lastStatus.AttiElaborati}/{lastStatus.TotAtti} " +
                $"({lastStatus.Percentuale:0.#}%) {lastStatus.DescrizioneStato}");

            if (lastStatus.IsReady)
            {
                return lastStatus;
            }

            if (lastStatus.IsFailed)
            {
                throw new DataFetcherException(
                    $"Normattiva: elaborazione fallita — {lastStatus.DescrizioneErrore ?? lastStatus.DescrizioneStato}");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Normattiva: timeout dopo {config.Timeout} (ultimo stato {lastStatus.Stato}).");
            }

            await Task.Delay(config.PollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpRequestMessage? request = null;
            try
            {
                request = new HttpRequestMessage(method, url);
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body, JsonOptions);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                request?.Dispose();
            }
        }
    }

    private static async Task<string> SaveResponseAsync(
        HttpResponseMessage response,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new DataFetcherException(
                $"Normattiva download ha risposto {(int)response.StatusCode}: {Truncate(body)}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new DataFetcherException(
                "Normattiva ha restituito una pagina HTML invece dell'archivio ZIP.");
        }

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = fullPath + ".part";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var expected = response.Content.Headers.ContentLength;
            var actual = new FileInfo(tempPath).Length;
            if (expected.HasValue && expected.Value != actual)
            {
                throw new DataFetcherException(
                    $"Download incompleto: attesi {expected.Value} byte, ricevuti {actual}.");
            }

            File.Move(tempPath, fullPath, overwrite: true);

            if (actual <= EmptyZipLength)
            {
                Console.WriteLine(
                    $"  [Normattiva] ATTENZIONE: archivio vuoto ({actual} byte). " +
                    "La ricerca non ha trovato atti: prova ad ampliare l'intervallo di date o i filtri.");
            }
            else
            {
                Console.WriteLine($"  [Normattiva] Archivio salvato: {fullPath} ({actual:N0} byte)");
            }

            return fullPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static string ExtractToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var trimmed = body.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("token", out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String)
            {
                return tokenElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // The API may return the token as plain text.
        }

        return trimmed.Trim('"');
    }

    private static string BuildUrl(NormattivaConfig config, string endpoint)
    {
        var baseUrl = config.BaseApiUrl.EndsWith('/') ? config.BaseApiUrl : config.BaseApiUrl + "/";
        return baseUrl + endpoint.TrimStart('/');
    }

    private static string Truncate(string? value, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "…";
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
            throw new ObjectDisposedException(nameof(NormattivaProvider));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }
}
