namespace OpenDataDownloader.Contracts;

/// <summary>
/// Abstraction for storing downloaded files on disk with a safe folder hierarchy.
/// Supports both individual and parallel batch downloads.
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
    /// Downloads a batch archive (e.g. ZIP from Normattiva) from the given source URL and saves it at the destination path.
    /// </summary>
    /// <param name="sourceUrl">The URL of the archive to download.</param>
    /// <param name="destinationPath">The local path where the archive will be saved.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<string> DownloadBatchAsync(
        string sourceUrl,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads multiple resources in parallel using configurable concurrency.
    /// Each resource is placed under PortalName/SafePackageName/SafeResourceName.ext.
    /// Returns a list of results indicating success/failure per resource.
    /// </summary>
    /// <param name="resources">The resources to download.</param>
    /// <param name="portalName">The portal name used for the folder structure.</param>
    /// <param name="packageName">The package name used for the folder structure.</param>
    /// <param name="includeDocuments">When true, also downloads PDF/HTML/office documents.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<List<ResourceDownloadResult>> DownloadResourcesAsync(
        IEnumerable<CkanResource> resources,
        string portalName,
        string packageName,
        bool includeDocuments = false,
        CancellationToken cancellationToken = default);
}
