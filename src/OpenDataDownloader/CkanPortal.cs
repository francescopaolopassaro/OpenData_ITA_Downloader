namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a CKAN portal configuration for data retrieval.
/// </summary>
public class CkanPortal
{
    /// <summary>
    /// Gets or sets the display name of the portal.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base API URL, e.g. "https://www.dati.gov.it/opendata/api/3/action/".
    /// </summary>
    public string BaseApiUrl { get; set; } = string.Empty;
}
