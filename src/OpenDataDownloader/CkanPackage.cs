namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a dataset (package) from a CKAN portal.
/// </summary>
public class CkanPackage
{
    /// <summary>
    /// Gets or sets the unique identifier of the package.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the machine-readable name of the package.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable title of the package.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of downloadable resources associated with this package.
    /// </summary>
    public List<CkanResource> Resources { get; set; } = new();
}
