using System.ComponentModel.DataAnnotations;

namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a CKAN portal configuration for data retrieval.
/// </summary>
public class CkanPortal
{
    /// <summary>
    /// Gets or sets the display name of the portal.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base API URL, e.g. "https://www.dati.gov.it/opendata/api/3/action/".
    /// </summary>
    [Required]
    [Url(ErrorMessage = "BaseApiUrl must be a valid URI.")]
    public string BaseApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional API key for portals requiring authentication (e.g., ANAC).
    /// The key is passed in the X-CKAN-API-Key header.
    /// </summary>
    public string? ApiKey { get; set; }
}
