namespace OpenDataDownloader.Contracts;

/// <summary>
/// Provides methods for interacting with the Normattiva open-data API (legal documents).
/// </summary>
public interface INormattivaProvider : IDisposable
{
    /// <summary>
    /// Requests a batch download archive from Normattiva based on the given filters.
    /// Polls until the archive is ready or the timeout expires.
    /// Returns the full local path to the downloaded ZIP file, or null if it failed.
    /// </summary>
    Task<string?> RequestBatchDownloadAsync(
        NormattivaConfig config,
        NormattivaBatchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
