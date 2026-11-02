namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents an error response returned by a CKAN API call.
/// </summary>
public class Error
{
    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
