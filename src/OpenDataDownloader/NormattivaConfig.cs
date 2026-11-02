namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents configuration for connecting to the Normattiva open-data API.
/// </summary>
public class NormattivaConfig
{
    /// <summary>
    /// Gets or sets the base URL of the Normattiva API. Defaults to the official endpoint.
    /// </summary>
    public string BaseApiUrl { get; set; } = "https://api.normattiva.it/t/normattiva.api/bff-opendata/v1/api/v1/";

    /// <summary>
    /// Gets or sets the simple search (ricerca semplice) endpoint path.
    /// </summary>
    public string SearchEndpoint { get; set; } = "ricerca/semplice";

    /// <summary>
    /// Gets or sets the batch download request endpoint path.
    /// </summary>
    public string BatchDownloadEndpoint { get; set; } = "download/richiesta-zip";

    /// <summary>
    /// Gets or sets the ZIP download endpoint path (reached with a token).
    /// </summary>
    public string DownloadEndpoint { get; set; } = "download/zip";
}

/// <summary>
/// Represents the batch download request payload for Normattiva.
/// </summary>
public class NormattivaBatchRequest
{
    /// <summary>
    /// Gets or sets the requested output format (e.g. "ZIP").
    /// </summary>
    public string FormatoRichiesta { get; set; } = "ZIP";

    /// <summary>
    /// Gets or sets optional search filters for selecting which documents to include.
    /// </summary>
    public Dictionary<string, object>? Filtro { get; set; }
}

/// <summary>
/// Represents the response from a Normattiva batch download request, containing an operation token.
/// </summary>
public class NormattivaBatchResponse
{
    /// <summary>
    /// Gets or sets the unique token used to poll/download the generated archive.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the status of the asynchronous operation (e.g. "PROCESSING", "COMPLETED").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets an error message if the request failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents a Normattiva legal document / provision retrieved through search or batch download.
/// </summary>
public class NormattivaDocument
{
    /// <summary>
    /// Gets or sets the unique identifier of the document.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the document.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of provision (e.g. "LEGGE", "DECRETO").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publication date as ISO 8601 string.
    /// </summary>
    public string PublicationDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source URL where the full text is available.
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Gets or sets additional metadata keyed by field name.
    /// </summary>
    public Dictionary<string, string?> Metadata { get; set; } = new();
}
