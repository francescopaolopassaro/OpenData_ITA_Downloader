namespace OpenDataDownloader;

/// <summary>
/// Represents a generic response from a CKAN API action.
/// Most CKAN endpoints wrap their results in this standard envelope:
///   { "success": bool, "result": T, "error": Error? }
/// </summary>
/// <typeparam name="T">The type of the result payload.</typeparam>
public class CkanResponse<T>
{
    /// <summary>
    /// Gets or sets whether the API call succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the result payload when Success is true.
    /// </summary>
    public T? Result { get; set; }

    /// <summary>
    /// Gets or sets error details when the API call failed.
    /// </summary>
    public Error? Error { get; set; }
}
