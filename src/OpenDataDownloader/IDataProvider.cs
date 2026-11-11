namespace OpenDataDownloader.Contracts;

/// <summary>
/// Provides methods for discovering and retrieving datasets from a CKAN portal or similar data source.
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// Gets the name of this data provider.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Lists all package identifiers available from the data source.
    /// </summary>
    Task<List<string>> GetPackageListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves full metadata (including resources) for the dataset with the given identifier.
    /// Returns null when the dataset is not found.
    /// </summary>
    Task<CkanPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches packages using the CKAN search_list endpoint with the specified options.
    /// Returns lightweight package summaries suitable for filtering before loading full details.
    /// </summary>
    Task<List<PackageSearchResult>> SearchPackagesAsync(
        CkanSearchOptions options,
        CancellationToken cancellationToken = default);
}
