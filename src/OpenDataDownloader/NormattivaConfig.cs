namespace OpenDataDownloader.Contracts;

/// <summary>
/// Represents configuration for connecting to the Normattiva open-data API.
/// Endpoints and defaults reflect the real API published at
/// https://dati.normattiva.it/assets/come_fare_per/Normattiva%20OpenData.html
/// </summary>
public class NormattivaConfig
{
    /// <summary>
    /// Gets or sets the base URL of the Normattiva API. Defaults to the official endpoint.
    /// </summary>
    public string BaseApiUrl { get; set; } = "https://api.normattiva.it/t/normattiva.api/bff-opendata/v1/api/v1/";

    /// <summary>
    /// Gets or sets the endpoint that lists the pre-packaged collections.
    /// </summary>
    public string PredefinedCollectionsEndpoint { get; set; } = "collections/collection-predefinite";

    /// <summary>
    /// Gets or sets the endpoint used to download a pre-packaged collection.
    /// </summary>
    public string PredefinedCollectionDownloadEndpoint { get; set; } = "collections/download/collection-preconfezionata";

    /// <summary>
    /// Gets or sets the endpoint that starts an asynchronous search.
    /// </summary>
    public string NewAsyncSearchEndpoint { get; set; } = "ricerca-asincrona/nuova-ricerca";

    /// <summary>
    /// Gets or sets the endpoint that confirms an asynchronous search.
    /// </summary>
    public string ConfirmAsyncSearchEndpoint { get; set; } = "ricerca-asincrona/conferma-ricerca";

    /// <summary>
    /// Gets or sets the endpoint used to poll the status of an asynchronous search.
    /// </summary>
    public string CheckStatusEndpoint { get; set; } = "ricerca-asincrona/check-status";

    /// <summary>
    /// Gets or sets the endpoint used to download the archive produced by an asynchronous search.
    /// </summary>
    public string AsyncCollectionDownloadEndpoint { get; set; } = "collections/download/collection-asincrona";

    /// <summary>
    /// Gets or sets how often the asynchronous search status is polled (default 10 seconds).
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum time to wait for an asynchronous search (default 60 minutes).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Gets or sets the default export format (e.g. AKN, XML, PDF). Defaults to AKN.
    /// </summary>
    public string DefaultFormat { get; set; } = "AKN";

    /// <summary>
    /// Gets or sets the default export kind: O = originario, V = vigente, M = multivigente.
    /// </summary>
    public string DefaultExport { get; set; } = "O";

    /// <summary>
    /// Gets or sets the default search mode: C = classica, R = responsive.
    /// </summary>
    public string DefaultMode { get; set; } = "C";

    /// <summary>
    /// Gets or sets an optional e-mail address forwarded to the Normattiva batch service.
    /// </summary>
    public string? Email { get; set; }
}

/// <summary>
/// Represents the request payload for a Normattiva asynchronous batch download.
/// Mirrors the official <c>NuovaRicercaAsincronaRequest</c> schema.
/// </summary>
public class NormattivaBatchRequest
{
    /// <summary>
    /// Gets or sets the export format (e.g. AKN, XML, PDF).
    /// </summary>
    public string Formato { get; set; } = "AKN";

    /// <summary>
    /// Gets or sets the search type: S = semplice, A = avanzata.
    /// </summary>
    public string TipoRicerca { get; set; } = "A";

    /// <summary>
    /// Gets or sets the export kind: O = originario, V = vigente, M = multivigente.
    /// </summary>
    public string RichiestaExport { get; set; } = "O";

    /// <summary>
    /// Gets or sets the search mode: C = classica, R = responsive.
    /// </summary>
    public string Modalita { get; set; } = "C";

    /// <summary>
    /// Gets or sets an optional e-mail address forwarded to the service.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the search filters.
    /// </summary>
    public NormattivaSearchFilter ParametriRicerca { get; set; } = new();

    /// <summary>
    /// Creates a request that targets the whole Italian legal corpus (1861 → today).
    /// </summary>
    /// <param name="formato">Export format (default AKN).</param>
    /// <param name="richiestaExport">O = originario, V = vigente, M = multivigente.</param>
    /// <param name="to">Optional end date (default today).</param>
    public static NormattivaBatchRequest AllLaws(
        string formato = "AKN",
        string richiestaExport = "O",
        DateTime? to = null)
    {
        return new NormattivaBatchRequest
        {
            Formato = formato,
            TipoRicerca = "A",
            RichiestaExport = richiestaExport,
            ParametriRicerca = new NormattivaSearchFilter
            {
                DataInizioEmanazione = new DateTime(1861, 1, 1),
                DataFineEmanazione = to ?? DateTime.Today
            }
        };
    }
}

/// <summary>
/// Search filters for the Normattiva advanced search.
/// Mirrors the official <c>RicercaAvanzataFilterDto</c> schema.
/// </summary>
public class NormattivaSearchFilter
{
    /// <summary>Denomination of the act (e.g. LEGGE, DECRETO LEGISLATIVO).</summary>
    public string? DenominazioneAtto { get; set; }

    /// <summary>Act number.</summary>
    public int? NumeroProvvedimento { get; set; }

    /// <summary>Act year.</summary>
    public int? AnnoProvvedimento { get; set; }

    /// <summary>Act month.</summary>
    public int? MeseProvvedimento { get; set; }

    /// <summary>Act day.</summary>
    public int? GiornoProvvedimento { get; set; }

    /// <summary>Article number.</summary>
    public int? NumeroArticolo { get; set; }

    /// <summary>Additional free-form filters.</summary>
    public Dictionary<string, string>? FiltriMap { get; set; }

    /// <summary>Title match mode: ALL_WORDS, SOME_WORDS, ENTIRE_STRING, NEITHER_WORDS.</summary>
    public string? TitoloContainsType { get; set; }

    /// <summary>Words to search in the title.</summary>
    public string? TitoloRicerca { get; set; }

    /// <summary>Words to exclude from the title.</summary>
    public string? TitoloNot { get; set; }

    /// <summary>Text match mode: ALL_WORDS, SOME_WORDS, ENTIRE_STRING, NEITHER_WORDS.</summary>
    public string? TestoContainsType { get; set; }

    /// <summary>Words to search in the text.</summary>
    public string? TestoRicerca { get; set; }

    /// <summary>Words to exclude from the text.</summary>
    public string? TestoNot { get; set; }

    /// <summary>Publication date lower bound.</summary>
    public DateTime? DataInizioPubblicazione { get; set; }

    /// <summary>Publication date upper bound.</summary>
    public DateTime? DataFinePubblicazione { get; set; }

    /// <summary>Reference date for the "vigente" version.</summary>
    public DateTime? DataVigenza { get; set; }

    /// <summary>Enactment date lower bound.</summary>
    public DateTime? DataInizioEmanazione { get; set; }

    /// <summary>Enactment date upper bound.</summary>
    public DateTime? DataFineEmanazione { get; set; }

    /// <summary>Provision class.</summary>
    public string? ClasseProvvedimento { get; set; }

    /// <summary>Sort order.</summary>
    public string? OrderType { get; set; }

    /// <summary>Limit the search to acts still in force.</summary>
    public bool? LimitaAnniVigenza { get; set; }
}

/// <summary>
/// Represents a pre-packaged Normattiva collection.
/// </summary>
public class NormattivaCollection
{
    /// <summary>Collection name, to be used as the <c>nome</c> query parameter.</summary>
    public string? NomeCollezione { get; set; }

    /// <summary>Collection format code (O, V, M).</summary>
    public string? FormatoCollezione { get; set; }

    /// <summary>Human-readable format description.</summary>
    public string? DescrizioneFormatoCollezione { get; set; }

    /// <summary>Creation date.</summary>
    public string? DataCreazione { get; set; }

    /// <summary>Number of acts contained in the collection.</summary>
    public long NumeroAtti { get; set; }
}

/// <summary>
/// Represents the status of a Normattiva asynchronous search.
/// </summary>
public class NormattivaSearchStatus
{
    /// <summary>Status code: 0 da confermare, 1 confermata, 2 in elaborazione, 3 completata, 4 errore, 5 carico eccessivo, 6 confermata con tempi prolungati.</summary>
    public int Stato { get; set; }

    /// <summary>Human-readable status description.</summary>
    public string? DescrizioneStato { get; set; }

    /// <summary>Error description (status 4).</summary>
    public string? DescrizioneErrore { get; set; }

    /// <summary>Total number of acts found (null until the search starts processing).</summary>
    public long? TotAtti { get; set; }

    /// <summary>Number of acts processed so far (null until the search starts processing).</summary>
    public long? AttiElaborati { get; set; }

    /// <summary>Completion percentage.</summary>
    public double Percentuale { get; set; }

    /// <summary>True when the archive is ready for download.</summary>
    public bool IsReady => Stato == 3;

    /// <summary>True when the search failed.</summary>
    public bool IsFailed => Stato == 4;
}

/// <summary>
/// Represents a legal document / provision retrieved through Normattiva search.
/// </summary>
public class NormattivaDocument
{
    /// <summary>Gets or sets the unique identifier of the document.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title of the document.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of provision (e.g. "LEGGE", "DECRETO").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the publication date as ISO 8601 string.</summary>
    public string PublicationDate { get; set; } = string.Empty;

    /// <summary>Gets or sets the source URL where the full text is available.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Gets or sets additional metadata keyed by field name.</summary>
    public Dictionary<string, string?> Metadata { get; set; } = new();
}
