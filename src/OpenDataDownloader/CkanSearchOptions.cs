namespace OpenDataDownloader.Contracts;

/// <summary>
/// Options for searching packages in a CKAN portal using the search_list API endpoint.
/// </summary>
public class CkanSearchOptions
{
    /// <summary>
    /// Full-text search query term.
    /// </summary>
    public string? Q { get; set; }

    /// <summary>
    /// Exact tags to filter by (e.g., "transport", "mobility").
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Tag matching mode — packages must contain ALL these tags.
    /// </summary>
    public List<string>? TagsContains { get; set; }

    /// <summary>
    /// Group/category identifier to restrict results (e.g., "geographics", "economic").
    /// </summary>
    public List<string>? Categories { get; set; }

    /// <summary>
    /// Maximum number of results to return (default 100, max typically 1000).
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Sorting expression (e.g., "score desc, metadata_created desc").
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>
    /// When true, only return fields available via the simple_fields (reduces payload size).
    /// </summary>
    public bool Simple { get; set; }
}
