using OpenDataDownloader.Core;

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

    [Fact]
    public void IsSupported_Null_ReturnsFalse()
    {
        var result = FormatFilter.IsSupported(null);
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
    [InlineData("PDF", ".bin")]
    [InlineData("", ".bin")]
    [InlineData(null, ".bin")]
    public void ToExtension_ReturnsCorrectExtension(string? format, string expected)
    {
        var result = FormatFilter.ToExtension(format);
        Assert.Equal(expected, result);
    }
}
