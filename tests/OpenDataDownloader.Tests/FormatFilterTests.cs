using OpenDataDownloader.Contracts;
using OpenDataDownloader.Core;
using Xunit;

namespace OpenDataDownloader.Tests;

/// <summary>
/// Unit tests for FormatFilter class.
/// </summary>
public class FormatFilterTests
{
    [Theory]
    [InlineData("JSON", true)]
    [InlineData("CSV", true)]
    [InlineData("XML", true)]
    [InlineData("ZIP", true)]
    [InlineData("GEOJSON", true)]
    [InlineData("RDF", true)]
    [InlineData("TTL", true)]
    [InlineData("XLSX", true)]
    [InlineData("XLS", true)]
    [InlineData("ODS", true)]
    [InlineData("SHP", true)]
    [InlineData("GML", true)]
    [InlineData("GPKG", true)]
    [InlineData("KML", true)]
    [InlineData("TIFF", true)]
    [InlineData("TXT", true)]
    [InlineData("PARQUET", true)]
    [InlineData("json", true)] // case-insensitive
    [InlineData("Json", true)]
    [InlineData("pdf", false)]
    [InlineData("html", false)]
    [InlineData("doc", false)]
    [InlineData("PNG", false)]
    public void IsSupported_ReturnsCorrectResult(string format, bool expected)
    {
        var result = FormatFilter.IsSupported(format);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("PDF", true)]
    [InlineData("HTML", true)]
    [InlineData("DOC", true)]
    [InlineData("DOCX", true)]
    [InlineData("ODT", true)]
    [InlineData("PNG", false)]
    public void IsSupported_WithDocuments_ReturnsCorrectResult(string format, bool expected)
    {
        var result = FormatFilter.IsSupported(format, includeDocuments: true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsSupported_Null_ReturnsFalse()
    {
        var result = FormatFilter.IsSupported((string?)null);
        Assert.False(result);
    }

    [Fact]
    public void IsSupported_EmptyString_ReturnsFalse()
    {
        var result = FormatFilter.IsSupported("");
        Assert.False(result);
    }

    [Theory]
    [InlineData("JSON", ".json")]
    [InlineData("csv", ".csv")]
    [InlineData("ZIP", ".zip")]
    [InlineData("GeoJSON", ".geojson")]
    [InlineData("XML", ".xml")]
    [InlineData("RDF", ".rdf")]
    [InlineData("TTL", ".ttl")]
    [InlineData("xlsx", ".xlsx")]
    [InlineData("xls", ".xls")]
    [InlineData("ODS", ".ods")]
    [InlineData("SHP", ".shp")]
    [InlineData("GML", ".gml")]
    [InlineData("GPKG", ".gpkg")]
    [InlineData("KML", ".kml")]
    [InlineData("TIFF", ".tiff")]
    [InlineData("PARQUET", ".parquet")]
    [InlineData("PDF", ".pdf")]
    [InlineData("", ".bin")]
    [InlineData(null, ".bin")]
    public void ToExtension_ReturnsCorrectExtension(string? format, string expected)
    {
        var result = FormatFilter.ToExtension(format);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CSV", "https://example.org/file.bin", "CSV")]
    [InlineData(null, "https://example.org/file.csv", "CSV")]
    [InlineData("", "https://example.org/data/file.geojson?token=1", "GEOJSON")]
    [InlineData(null, "https://example.org/download.zip", "ZIP")]
    [InlineData(null, "https://example.org/file.unknown", null)]
    public void ResolveFormat_PrefersFormatThenUrl(string? format, string url, string? expected)
    {
        var resource = new CkanResource { Format = format, Url = url };
        var result = FormatFilter.ResolveFormat(resource);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveFormat_FallsBackToMimeType()
    {
        var resource = new CkanResource
        {
            Url = "https://example.org/download",
            MimeType = "text/csv"
        };

        Assert.Equal("CSV", FormatFilter.ResolveFormat(resource));
    }

    [Fact]
    public void IsSupported_ResourceWithoutFormat_UsesUrlExtension()
    {
        var resource = new CkanResource
        {
            Name = "shapefile",
            Url = "https://example.org/limiti.shp"
        };

        Assert.True(FormatFilter.IsSupported(resource));
    }

    [Fact]
    public void IsSupported_ResourcePdf_OnlyWithDocuments()
    {
        var resource = new CkanResource
        {
            Name = "bando",
            Url = "https://example.org/bando.pdf"
        };

        Assert.False(FormatFilter.IsSupported(resource));
        Assert.True(FormatFilter.IsSupported(resource, includeDocuments: true));
    }
}
