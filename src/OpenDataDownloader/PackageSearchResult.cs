namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a package entry returned from a CKAN search_list call.
/// Contains lightweight metadata for filtering before loading full details.
/// </summary>
public class PackageSearchResult
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
    /// Gets or sets the state of the package (e.g., "active", "deleted").
    /// </summary>
    public string State { get; set; } = "active";

    /// <summary>
    /// Gets or sets the list of tags associated with this package.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of resources linked to this package.
    /// </summary>
    public int NumResources { get; set; }

    /// <summary>
    /// Gets or sets the license identifier (if any).
    /// </summary>
    public string? LicenseId { get; set; }

    /// <summary>
    /// Gets or sets the group/category identifiers.
    /// </summary>
    public List<string> Groups { get; set; } = new();
}
