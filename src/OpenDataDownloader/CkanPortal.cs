using System.ComponentModel.DataAnnotations;

namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a CKAN portal configuration for data retrieval.
/// </summary>
public class CkanPortal
{
    /// <summary>
    /// Default User-Agent used for portal requests. It looks like a browser on purpose:
    /// several public administrations (e.g. ANAC) put a WAF in front of the CKAN API that
    /// rejects generic bot User-Agents, while still allowing a browser-compatible string.
    /// The string keeps the downloader identity for transparency.
    /// </summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (compatible; OpenDataDownloader/1.0; " +
        "+https://github.com/fpassaro/OpenData_ITA_Downloader) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

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

    /// <summary>
    /// Gets or sets an optional User-Agent override for this portal.
    /// When null, <see cref="DefaultUserAgent"/> is used.
    /// </summary>
    public string? UserAgent { get; set; }
}
