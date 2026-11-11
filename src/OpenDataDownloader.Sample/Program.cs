using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDataDownloader.Contracts;
using OpenDataDownloader.Core;

// Esempio d'uso completo della libreria OpenDataDownloader.
//
// Esegui:  dotnet run --project src/OpenDataDownloader.Sample -- -c ckan-all -l 5 -o ./scaricati

var options = SampleOptions.Parse(args);
if (options.ShowHelp)
{
    SampleOptions.PrintHelp();
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nInterruzione richiesta... (attendo chiusura pulita)");
    cts.Cancel();
};

try
{
    var services = new ServiceCollection();
    services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning).AddConsole());

    var portals = new List<CkanPortal>
    {
        new() { Name = "DatiGovIt", BaseApiUrl = "https://www.dati.gov.it/opendata/api/3/action/" },
        new() { Name = "BDAP_MEF", BaseApiUrl = "https://bdap-opendata.rgs.mef.gov.it/SpodCkanApi/api/3/action/" },
        new() { Name = "OpenGA", BaseApiUrl = "https://openga.giustizia-amministrativa.it/api/3/action/" },
        new()
        {
            Name = "ANAC",
            BaseApiUrl = "https://dati.anticorruzione.it/opendata/api/3/action/"
        }
    };

    services.AddCompleteOpenDataWorkflow(
        portals,
        options.OutputDirectory,
        configureNormattiva: cfg =>
        {
            cfg.DefaultFormat = "AKN";
            cfg.DefaultExport = "O";
            // cfg.Email = "tua@email.com";
        },
        maxConcurrency: options.Concurrency);

    await using var sp = services.BuildServiceProvider();

    var factory = sp.GetRequiredService<OrchestrationServiceFactory>();
    var orchestrator = factory.Create(progressCallback: report =>
    {
        Console.WriteLine(
            $"[{report.Type}] {report.PortalName}  #{report.CurrentIndex}/{report.TotalCount}  {report.PackageTitle ?? report.ResourceName}");
    });

    orchestrator.DelayBetweenPackages = TimeSpan.FromMilliseconds(options.DelayMs);
    orchestrator.IncludeDocuments = options.IncludeDocuments;
    orchestrator.MaxPackagesPerPortal = options.Limit ?? 0;

    var summary = options.Command switch
    {
        "ckan-all" => await orchestrator.DownloadAllAsync(portals, cts.Token),
        "ckan-search" => await RunSearchAsync(orchestrator, portals, options, cts.Token),
        _ => throw new ArgumentException($"Comando sconosciuto: {options.Command}")
    };

    Console.WriteLine();
    Console.WriteLine($"=== Riepilogo {options.Command} ===");
    Console.WriteLine($"Durata:          {summary.TotalDuration:hh\\:mm\\:ss}");
    Console.WriteLine($"Dataset trovati: {summary.TotalPackagesFound}");
    Console.WriteLine($"Download OK:     {summary.TotalSuccessfulDownloads}");
    Console.WriteLine($"Download KO:     {summary.TotalFailedDownloads}");
    Console.WriteLine($"Risorse saltate: {summary.TotalSkippedResources}");
    Console.WriteLine($"Dataset KO:      {summary.TotalFailedPackages}");
    Console.WriteLine();
    Console.WriteLine($"Manifest JSON: {Path.Combine(Path.GetFullPath(options.OutputDirectory), "manifest.json")}");
    Console.WriteLine($"Manifest CSV:  {Path.Combine(Path.GetFullPath(options.OutputDirectory), "manifest.csv")}");

    return summary.TotalFailedDownloads == 0 ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operazione annullata.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Errore fatale: {ex.Message}");
    return 1;
}

static async Task<DownloadSessionSummary> RunSearchAsync(
    OrchestrationService orchestrator,
    IReadOnlyList<CkanPortal> portals,
    SampleOptions options,
    CancellationToken ct)
{
    var searchOptions = new CkanSearchOptions
    {
        Q = options.Query ?? "sanità",
        Tags = options.Tags,
        Limit = options.Limit ?? 100,
        Sort = "score desc"
    };

    Console.WriteLine($"Ricerca full-text: {searchOptions.Q}");
    return await orchestrator.DownloadMatchingAsync(portals, searchOptions, ct);
}

internal sealed class SampleOptions
{
    public string Command { get; set; } = "ckan-all";
    public bool ShowHelp { get; set; }
    public string OutputDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "OpenDataSample");
    public int? Limit { get; set; }
    public int DelayMs { get; set; } = 200;
    public int Concurrency { get; set; } = 4;
    public bool IncludeDocuments { get; set; }
    public string? Query { get; set; }
    public List<string> Tags { get; set; } = new();

    public static SampleOptions Parse(string[] args)
    {
        var o = new SampleOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--help" or "-h":
                    o.ShowHelp = true;
                    break;
                case "--output" or "-o":
                    o.OutputDirectory = args[++i];
                    break;
                case "--command" or "-c":
                    o.Command = args[++i];
                    break;
                case "--limit" or "-l":
                    o.Limit = int.Parse(args[++i]);
                    break;
                case "--delay" or "-d":
                    o.DelayMs = int.Parse(args[++i]);
                    break;
                case "--concurrency" or "-x":
                    o.Concurrency = int.Parse(args[++i]);
                    break;
                case "--include-documents":
                    o.IncludeDocuments = true;
                    break;
                case "--query" or "-q":
                    o.Query = args[++i];
                    break;
                case "--tag" or "-t":
                    o.Tags.Add(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"Opzione sconosciuta: {a}");
                    PrintHelp();
                    Environment.Exit(2);
                    break;
            }
        }
        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            OpenDataDownloader.Sample — esempio di uso della libreria

            USO
              dotnet run --project src/OpenDataDownloader.Sample [opzioni]

            OPZIONI
              -c, --command <ckan-all|ckan-search>  Operazione da eseguire (default: ckan-all)
              -o, --output <dir>                    Cartella di output (default: ./OpenDataSample)
              -l, --limit <n>                       Massimo N dataset per portale
              -d, --delay <ms>                      Pausa tra dataset (default: 200)
              -x, --concurrency <n>                 Download paralleli (default: 4)
              --include-documents                   Include PDF/HTML/DOC
              -q, --query <testo>                   Ricerca full-text (per ckan-search)
              -t, --tag <tag>                       Filtro per tag (ripetibile)
              -h, --help                            Mostra questo aiuto

            ESEMPI
              dotnet run --project src/OpenDataDownloader.Sample -c ckan-all -l 5 -o ./scaricati
              dotnet run --project src/OpenDataDownloader.Sample -c ckan-search -q "sanità" -l 20
            """);
    }
}
