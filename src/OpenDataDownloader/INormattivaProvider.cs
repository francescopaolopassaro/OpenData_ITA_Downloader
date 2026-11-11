namespace OpenDataDownloader.Contracts;

/// <summary>
/// Provides methods for interacting with the Normattiva open-data API (legal documents).
/// Implements the real asynchronous-search workflow:
/// nuova-ricerca → conferma-ricerca → check-status → collection-asincrona.
/// </summary>
public interface INormattivaProvider : IDisposable
{
    /// <summary>
    /// Starts an asynchronous search, confirms it, polls until the archive is ready and
    /// downloads the resulting ZIP archive.
    /// </summary>
    /// <param name="config">Normattiva configuration settings.</param>
    /// <param name="request">The search/export request.</param>
    /// <param name="destinationPath">Local path where the ZIP archive will be saved.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The full local path of the downloaded ZIP file, or null on failure.</returns>
    Task<string?> RequestBatchDownloadAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of pre-packaged collections published by Normattiva.
    /// </summary>
    Task<List<NormattivaCollection>> GetPredefinedCollectionsAsync(
        NormattivaConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a pre-packaged collection as a ZIP archive.
    /// </summary>
    /// <param name="config">Normattiva configuration settings.</param>
    /// <param name="collectionName">Collection name (e.g. "Codici").</param>
    /// <param name="formato">Export format (e.g. AKN, XML, PDF).</param>
    /// <param name="formatoRichiesta">Collection variant: O = originario, V = vigente, M = multivigente.</param>
    /// <param name="destinationPath">Local path where the ZIP archive will be saved.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<string> DownloadPredefinedCollectionAsync(
        NormattivaConfig config,
        string collectionName,
        string formato,
        string formatoRichiesta,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the status of an asynchronous search.
    /// </summary>
    Task<NormattivaSearchStatus> GetSearchStatusAsync(
        NormattivaConfig config,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the archive produced by an asynchronous search.
    /// </summary>
    Task<string> DownloadAsyncCollectionAsync(
        NormattivaConfig config,
        string token,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
