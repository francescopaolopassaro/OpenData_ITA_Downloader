namespace OpenDataDownloader.Core;

/// <summary>
/// Filters resource formats and determines which ones are acceptable for download.
/// </summary>
public static class FormatFilter
{
    /// <summary>
    /// List of format keywords that are considered downloadable.
    /// Excludes heavy or non-machine-readable types like PDF, HTML, etc.
    /// </summary>
    private static readonly HashSet<string> _supportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "JSON", "CSV", "ZIP", "GEOJSON", "XML", "RDF", "TTL", "XLSX", "XLS"
    };

    /// <summary>
    /// Returns true if the given format is supported for download.
    /// </summary>
    public static bool IsSupported(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return false;

        return _supportedFormats.Contains(format.ToUpperInvariant());
    }

    /// <summary>
    /// Maps a CKAN format string to a file extension.
    /// Returns ".bin" for unknown formats.
    /// </summary>
    public static string ToExtension(string? format)
    {
        return (format ?? "").ToUpperInvariant() switch
        {
            "JSON" => ".json",
            "CSV" => ".csv",
            "ZIP" => ".zip",
            "GEOJSON" => ".geojson",
            "XML" => ".xml",
            "RDF" => ".rdf",
            "TTL" => ".ttl",
            "XLSX" => ".xlsx",
            "XLS" => ".xls",
            _ => ".bin"
        };
    }
}
