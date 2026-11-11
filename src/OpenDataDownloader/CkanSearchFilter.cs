namespace OpenDataDownloader.Contracts;

/// <summary>
/// Filter criteria for searching CKAN packages.
/// Supports common CKAN search_parameters fields: q term, tags list, category/group tag, offset for pagination, and max_rows limit.
/// </summary>
public sealed class CkanSearchFilter
{
    /// <summary>
    /// Gets or sets the free-text search query (CKAN "q" parameter). May include field prefixes such as "title:myquery".
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Gets or sets a list of tags to match against packages. Requires CKAN 2.9+ with "tags_contains" logic or a manual "q" construction.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Gets or sets the group (category) tag to scope the search to a specific CKAN group.
    /// </summary>
    public string? GroupTag { get; set; }

    /// <summary>
    /// Gets or sets the number of results to skip for pagination (defaults to 0).
    /// </summary>
    public int Offset { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of results to return (defaults to 100, capped by CKAN server configuration).
    /// </summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Builds the CKAN search query string from the active filter fields.
    /// Returns null when no filter is configured.
    /// </summary>
    internal string? BuildQueryString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Query))
            parts.Add(Query);

        if (Tags != null && Tags.Count > 0)
            parts.Add("tags:" + string.Join(" tags:", Tags.Select(t => t!.ToLowerInvariant())));

        if (!string.IsNullOrWhiteSpace(GroupTag))
            parts.Add("groups:" + GroupTag!.ToLowerInvariant());

        return parts.Count > 0 ? string.Join(" AND ", parts) : null;
    }
}
