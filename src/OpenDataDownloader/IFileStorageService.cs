namespace OpenDataDownloader.Contracts;

/// <summary>
/// Abstraction for storing downloaded files on disk with a safe folder hierarchy.
/// </summary>
public interface IFileStorageService : IDisposable
{
    /// <summary>
    /// Downloads a file from the given URL and stores it under the specified portal, package, and resource names.
    /// Returns the full path where the file was saved.
    /// </summary>
    Task<string> DownloadAsync(
        string portalName,
        string packageName,
        CkanResource resource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a batch archive (e.g. ZIP from Normattiva) and saves it at the given destination path.
    /// </summary>
    Task DownloadBatchAsync(string destinationPath, CancellationToken cancellationToken = default);
}
