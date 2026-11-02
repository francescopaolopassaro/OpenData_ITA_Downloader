namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents a downloadable resource within a CKAN dataset.
/// </summary>
public class CkanResource
{
    /// <summary>
    /// Gets or sets the unique identifier of the resource.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the resource.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data format (e.g. CSV, JSON, ZIP, XML).
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the direct download URL of the resource.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the resource content.
    /// </summary>
    public string? MimeType { get; set; }
}
