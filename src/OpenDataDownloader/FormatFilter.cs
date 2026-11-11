namespace OpenDataDownloader.Core;

/// <summary>
/// Filters resource formats and determines which ones are acceptable for download.
/// Supports machine-readable data formats by default; office documents can be enabled
/// explicitly via <c>includeDocuments</c>.
/// </summary>
public static class FormatFilter
{
    /// <summary>
    /// Formats considered machine-readable and therefore downloadable by default.
    /// </summary>
    private static readonly HashSet<string> _machineReadableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "JSON", "CSV", "ZIP", "GEOJSON", "XML", "RDF", "TTL", "XLSX", "XLS",
        "ODS", "SHP", "SHAPEFILE", "GML", "GPKG", "GEOPACKAGE", "KML", "TIFF", "GEOTIFF", "TIF",
        "TXT", "PARQUET",
        "JSONLD", "NDJSON", "CSV.GZ", "GZ", "GZIP", "TAR", "7Z", "RAR",
        "DBF", "PRJ", "SHX", "CPG", "WKT", "WKB", "PBF", "MVT"
    };

    /// <summary>
    /// Document formats that are skipped by default but can be included on request.
    /// </summary>
    private static readonly HashSet<string> _documentFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "PDF", "HTML", "HTM", "DOC", "DOCX", "ODT", "RTF", "EPUB", "PPT", "PPTX"
    };

    private static readonly Dictionary<string, string> _extensionByFormat =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["JSON"] = ".json",
            ["CSV"] = ".csv",
            ["CSV.GZ"] = ".csv.gz",
            ["ZIP"] = ".zip",
            ["GEOJSON"] = ".geojson",
            ["XML"] = ".xml",
            ["RDF"] = ".rdf",
            ["TTL"] = ".ttl",
            ["XLSX"] = ".xlsx",
            ["XLS"] = ".xls",
            ["ODS"] = ".ods",
            ["SHP"] = ".shp",
            ["SHAPEFILE"] = ".shp",
            ["GML"] = ".gml",
            ["GPKG"] = ".gpkg",
            ["GEOPACKAGE"] = ".gpkg",
            ["KML"] = ".kml",
            ["TIFF"] = ".tiff",
            ["GEOTIFF"] = ".tif",
            ["TIF"] = ".tif",
            ["TXT"] = ".txt",
            ["PARQUET"] = ".parquet",
            ["JSONLD"] = ".jsonld",
            ["NDJSON"] = ".ndjson",
            ["GZ"] = ".gz",
            ["GZIP"] = ".gz",
            ["TAR"] = ".tar",
            ["7Z"] = ".7z",
            ["RAR"] = ".rar",
            ["DBF"] = ".dbf",
            ["PRJ"] = ".prj",
            ["SHX"] = ".shx",
            ["CPG"] = ".cpg",
            ["WKT"] = ".wkt",
            ["WKB"] = ".wkb",
            ["PBF"] = ".pbf",
            ["MVT"] = ".mvt",
            ["PDF"] = ".pdf",
            ["HTML"] = ".html",
            ["HTM"] = ".html",
            ["DOC"] = ".doc",
            ["DOCX"] = ".docx",
            ["ODT"] = ".odt",
            ["RTF"] = ".rtf",
            ["EPUB"] = ".epub",
            ["PPT"] = ".ppt",
            ["PPTX"] = ".pptx"
        };

    private static readonly Dictionary<string, string> _formatByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".json"] = "JSON",
            [".csv"] = "CSV",
            [".zip"] = "ZIP",
            [".geojson"] = "GEOJSON",
            [".xml"] = "XML",
            [".rdf"] = "RDF",
            [".ttl"] = "TTL",
            [".xlsx"] = "XLSX",
            [".xls"] = "XLS",
            [".ods"] = "ODS",
            [".shp"] = "SHP",
            [".gml"] = "GML",
            [".gpkg"] = "GPKG",
            [".kml"] = "KML",
            [".tiff"] = "TIFF",
            [".tif"] = "TIFF",
            [".txt"] = "TXT",
            [".parquet"] = "PARQUET",
            [".jsonld"] = "JSONLD",
            [".ndjson"] = "NDJSON",
            [".gz"] = "GZ",
            [".gzip"] = "GZIP",
            [".tar"] = "TAR",
            [".7z"] = "7Z",
            [".rar"] = "RAR",
            [".dbf"] = "DBF",
            [".prj"] = "PRJ",
            [".shx"] = "SHX",
            [".cpg"] = "CPG",
            [".wkt"] = "WKT",
            [".wkb"] = "WKB",
            [".pbf"] = "PBF",
            [".mvt"] = "MVT",
            [".pdf"] = "PDF",
            [".html"] = "HTML",
            [".htm"] = "HTML",
            [".doc"] = "DOC",
            [".docx"] = "DOCX",
            [".odt"] = "ODT",
            [".rtf"] = "RTF",
            [".epub"] = "EPUB",
            [".ppt"] = "PPT",
            [".pptx"] = "PPTX"
        };

    private static readonly Dictionary<string, string> _formatByMimeType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/json"] = "JSON",
            ["application/ld+json"] = "JSONLD",
            ["application/x-ndjson"] = "NDJSON",
            ["text/csv"] = "CSV",
            ["application/csv"] = "CSV",
            ["text/plain"] = "TXT",
            ["application/zip"] = "ZIP",
            ["application/x-zip-compressed"] = "ZIP",
            ["application/xml"] = "XML",
            ["text/xml"] = "XML",
            ["application/rdf+xml"] = "RDF",
            ["text/turtle"] = "TTL",
            ["application/geo+json"] = "GEOJSON",
            ["application/vnd.geo+json"] = "GEOJSON",
            ["application/gml+xml"] = "GML",
            ["application/vnd.google-earth.kml+xml"] = "KML",
            ["application/geopackage+sqlite3"] = "GPKG",
            ["application/vnd.ms-excel"] = "XLS",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "XLSX",
            ["application/pdf"] = "PDF",
            ["text/html"] = "HTML",
            ["image/tiff"] = "TIFF"
        };

    /// <summary>
    /// Returns true if the given format is supported for download.
    /// </summary>
    /// <param name="format">The CKAN format string.</param>
    /// <param name="includeDocuments">When true, also accepts PDF/HTML/office documents.</param>
    public static bool IsSupported(string? format, bool includeDocuments = false)
    {
        var normalized = Normalize(format);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (_machineReadableFormats.Contains(normalized))
        {
            return true;
        }

        return includeDocuments && _documentFormats.Contains(normalized);
    }

    /// <summary>
    /// Returns true when the resource can be downloaded, falling back to the URL
    /// extension or MIME type when the CKAN <c>format</c> field is missing.
    /// </summary>
    public static bool IsSupported(CkanResource resource, bool includeDocuments = false)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return IsSupported(ResolveFormat(resource), includeDocuments);
    }

    /// <summary>
    /// Resolves the effective format of a resource using, in order:
    /// the CKAN format field, the URL extension and the MIME type.
    /// </summary>
    public static string? ResolveFormat(CkanResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var fromFormat = Normalize(resource.Format);
        if (!string.IsNullOrWhiteSpace(fromFormat))
        {
            return fromFormat;
        }

        var fromUrl = GetFormatFromUrl(resource.Url);
        if (!string.IsNullOrWhiteSpace(fromUrl))
        {
            return fromUrl;
        }

        if (!string.IsNullOrWhiteSpace(resource.MimeType) &&
            _formatByMimeType.TryGetValue(NormalizeMime(resource.MimeType), out var fromMime))
        {
            return fromMime;
        }

        return null;
    }

    /// <summary>
    /// Maps a CKAN format string to a file extension.
    /// Returns ".bin" for unknown formats.
    /// </summary>
    public static string ToExtension(string? format)
    {
        var normalized = Normalize(format);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ".bin";
        }

        return _extensionByFormat.TryGetValue(normalized, out var extension) ? extension : ".bin";
    }

    /// <summary>
    /// Maps a resource to a file extension, using the resolved format.
    /// </summary>
    public static string ToExtension(CkanResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return ToExtension(ResolveFormat(resource));
    }

    /// <summary>
    /// Returns the lower-case extension (with dot) of a URL path, or null.
    /// </summary>
    public static string? GetExtensionFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = url;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var fragmentIndex = path.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            path = path[..fragmentIndex];
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? null : extension.ToLowerInvariant();
    }

    /// <summary>
    /// Infers a format from the URL extension, or null when unknown.
    /// </summary>
    public static string? GetFormatFromUrl(string? url)
    {
        var extension = GetExtensionFromUrl(url);
        if (extension == null)
        {
            return null;
        }

        return _formatByExtension.TryGetValue(extension, out var format) ? format : null;
    }

    private static string Normalize(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return string.Empty;
        }

        var value = format.Trim();

        // Some portals put MIME types in the format field.
        if (value.Contains('/'))
        {
            return _formatByMimeType.TryGetValue(NormalizeMime(value), out var fromMime)
                ? fromMime
                : value.ToUpperInvariant();
        }

        // Strip parameters like "CSV; charset=utf-8".
        var semicolon = value.IndexOf(';');
        if (semicolon >= 0)
        {
            value = value[..semicolon].Trim();
        }

        // Normalise values like ".csv" or "csv (zip)".
        value = value.TrimStart('.');
        var space = value.IndexOf(' ');
        if (space > 0)
        {
            value = value[..space];
        }

        return value.ToUpperInvariant();
    }

    private static string NormalizeMime(string mime)
    {
        var semicolon = mime.IndexOf(';');
        return (semicolon >= 0 ? mime[..semicolon] : mime).Trim().ToLowerInvariant();
    }
}
