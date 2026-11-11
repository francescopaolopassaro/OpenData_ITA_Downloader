using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDataDownloader.Contracts;
using OpenDataDownloader.Core;

namespace OpenDataDownloader.Cli;

internal static class Program
{
    private static readonly IReadOnlyList<CkanPortal> DefaultPortals = new List<CkanPortal>
    {
        new()
        {
            Name = "DatiGovIt",
            BaseApiUrl = "https://www.dati.gov.it/opendata/api/3/action/"
        },
        new()
        {
            Name = "BDAP_MEF",
            BaseApiUrl = "https://bdap-opendata.rgs.mef.gov.it/SpodCkanApi/api/3/action/"
        },
        new()
        {
            Name = "OpenGA",
            BaseApiUrl = "https://openga.giustizia-amministrativa.it/api/3/action/"
        },
        new()
        {
            Name = "ANAC",
            BaseApiUrl = "https://dati.anticorruzione.it/opendata/api/3/action/"
        }
    };

    public static async Task<int> Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore: {ex.Message}");
            Console.Error.WriteLine();
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Interruzione richiesta... (attendo la chiusura pulita)");
            cts.Cancel();
        };

        try
        {
            return options.Command switch
            {
                CliOptions.CommandCkan => await RunCkanAsync(options, cts.Token),
                CliOptions.CommandNormattivaCollections => await RunNormattivaCollectionsAsync(options, cts.Token),
                CliOptions.CommandNormattivaAll => await RunNormattivaAllAsync(options, cts.Token),
                CliOptions.CommandNormattivaSearch => await RunNormattivaSearchAsync(options, cts.Token),
                _ => UnknownCommand(options.Command)
            };
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
    }

    #region CKAN

    private static async Task<int> RunCkanAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var portals = ResolvePortals(options);
        if (portals.Count == 0)
        {
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            foreach (var portal in portals)
            {
                portal.ApiKey = options.ApiKey;
            }
        }

        Console.WriteLine("=== OpenData Downloader — CKAN ===");
        Console.WriteLine($"Output:  {Path.GetFullPath(options.OutputDirectory)}");
        Console.WriteLine($"Portali: {string.Join(", ", portals.Select(p => p.Name))}");
        Console.WriteLine($"Formati: {(options.IncludeDocuments ? "dati + documenti" : "solo dati machine-readable")}");
        if (options.Limit is > 0)
        {
            Console.WriteLine($"Limite:  {options.Limit} dataset per portale");
        }
        if (options.DryRun)
        {
            Console.WriteLine("Modalità: DRY-RUN (nessun file verrà scaricato)");
        }
        Console.WriteLine();

        await using var services = BuildServices(portals, options, normattiva: null);

        if (options.DryRun)
        {
            await DryRunCkanAsync(services, portals, options, cancellationToken);
            return 0;
        }

        var orchestrator = services.GetRequiredService<OrchestrationServiceFactory>().Create();
        orchestrator.DelayBetweenPackages = TimeSpan.FromMilliseconds(options.DelayMs);
        orchestrator.IncludeDocuments = options.IncludeDocuments;
        orchestrator.MaxPackagesPerPortal = options.Limit ?? 0;

        DownloadSessionSummary summary;
        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            var searchOptions = new CkanSearchOptions
            {
                Q = options.Query,
                Limit = options.Limit ?? 1000
            };

            Console.WriteLine($"Ricerca full-text: {options.Query}");
            Console.WriteLine();
            summary = await orchestrator.DownloadMatchingAsync(portals, searchOptions, cancellationToken);
        }
        else
        {
            summary = await orchestrator.DownloadAllAsync(portals, cancellationToken);
        }

        PrintSummary(summary);
        ManifestWriter.WriteCkanManifest(summary, options.OutputDirectory, options.Command);

        Console.WriteLine();
        Console.WriteLine($"Manifest JSON: {Path.Combine(Path.GetFullPath(options.OutputDirectory), "manifest.json")}");
        Console.WriteLine($"Manifest CSV:  {Path.Combine(Path.GetFullPath(options.OutputDirectory), "manifest.csv")}");

        return summary.Success && summary.TotalFailedDownloads == 0 ? 0 : 1;
    }

    private static async Task DryRunCkanAsync(
        IServiceProvider services,
        IReadOnlyList<CkanPortal> portals,
        CliOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var portal in portals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var provider = services.GetRequiredKeyedService<IDataProvider>(portal.Name);
                var packageIds = await provider.GetPackageListAsync(cancellationToken);

                Console.WriteLine($"[{portal.Name}] {packageIds.Count} dataset trovati.");

                if (options.Limit is not > 0)
                {
                    Console.WriteLine("  (usa --limit N per elencare le risorse dei primi N dataset)");
                    continue;
                }

                var selected = packageIds.Take(options.Limit!.Value).ToList();
                var downloadable = 0;
                var skipped = 0;
                var errors = 0;

                foreach (var packageId in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var package = await provider.GetPackageDetailsAsync(packageId, cancellationToken);
                        if (package == null)
                        {
                            errors++;
                            Console.WriteLine($"  ERRORE {packageId}: package_show senza dati");
                            continue;
                        }

                        foreach (var resource in package.Resources)
                        {
                            if (string.IsNullOrWhiteSpace(resource.Url))
                            {
                                skipped++;
                                continue;
                            }

                            if (FormatFilter.IsSupported(resource, options.IncludeDocuments))
                            {
                                downloadable++;
                                Console.WriteLine(
                                    $"  OK   [{FormatFilter.ResolveFormat(resource) ?? "?"}] {resource.Name}");
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine($"  ERRORE {packageId}: {ex.Message}");
                    }
                }

                Console.WriteLine(
                    $"[{portal.Name}] Risorse scaricabili: {downloadable}, saltate: {skipped}, errori: {errors}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{portal.Name}] ERRORE: {ex.Message}");
            }
        }
    }

    private static void PrintSummary(DownloadSessionSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine("=== Riepilogo ===");
        Console.WriteLine($"Durata totale:       {summary.TotalDuration:hh\\:mm\\:ss}");
        Console.WriteLine($"Dataset trovati:     {summary.TotalPackagesFound}");
        Console.WriteLine($"Download riusciti:   {summary.TotalSuccessfulDownloads}");
        Console.WriteLine($"Download falliti:    {summary.TotalFailedDownloads}");
        Console.WriteLine($"Risorse saltate:     {summary.TotalSkippedResources}");
        Console.WriteLine($"Dataset falliti:     {summary.TotalFailedPackages}");

        foreach (var portal in summary.Portals)
        {
            var status = portal.Error != null
                ? "ERRORE"
                : portal.FailedDownloads > 0 ? "PARZIALE" : "OK";
            Console.WriteLine(
                $"  - {portal.PortalName}: {status} | dataset {portal.TotalPackages} | " +
                $"ok {portal.SuccessfulDownloads} | ko {portal.FailedDownloads} | " +
                $"saltati {portal.SkippedResources} | {portal.Duration:hh\\:mm\\:ss}");

            if (portal.Error != null)
            {
                Console.WriteLine($"      {portal.Error}");
            }
        }
    }

    #endregion

    #region Normattiva

    private static async Task<int> RunNormattivaCollectionsAsync(
        CliOptions options,
        CancellationToken cancellationToken)
    {
        var config = CreateNormattivaConfig(options);

        Console.WriteLine("=== Normattiva — Collezioni predefinite ===");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");
        Console.WriteLine($"Formato: {config.DefaultFormat} / export {config.DefaultExport}");
        Console.WriteLine();

        await using var services = BuildServices(Array.Empty<CkanPortal>(), options, config);

        if (options.DryRun)
        {
            var provider = services.GetRequiredService<INormattivaProvider>();
            var collections = await provider.GetPredefinedCollectionsAsync(config, cancellationToken);

            Console.WriteLine($"{collections.Count} collezioni disponibili:");
            foreach (var collection in collections
                         .OrderByDescending(c => c.NumeroAtti)
                         .ThenBy(c => c.NomeCollezione))
            {
                Console.WriteLine(
                    $"  - {collection.NomeCollezione} [{collection.FormatoCollezione}] " +
                    $"{collection.NumeroAtti:N0} atti");
            }

            return 0;
        }

        var orchestrator = services.GetRequiredService<OrchestrationServiceFactory>().Create();
        var destinationRoot = Path.Combine(options.OutputDirectory, "Normattiva", "Collezioni");

        var paths = await orchestrator.DownloadNormattivaPredefinedCollectionsAsync(
            config,
            destinationRoot,
            config.DefaultFormat,
            config.DefaultExport,
            options.AllVariants,
            options.Limit ?? 0,
            cancellationToken);

        ManifestWriter.WriteNormattivaManifest(paths, options.OutputDirectory, options.Command);

        Console.WriteLine();
        Console.WriteLine($"Archivi scaricati: {paths.Count}");
        Console.WriteLine($"Cartella: {Path.GetFullPath(destinationRoot)}");
        Console.WriteLine(
            $"Manifest: {Path.Combine(Path.GetFullPath(options.OutputDirectory), "normattiva-manifest.json")}");

        return paths.Count > 0 ? 0 : 1;
    }

    private static async Task<int> RunNormattivaAllAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var config = CreateNormattivaConfig(options);
        var from = options.FromDate ?? new DateTime(1861, 1, 1);
        var to = options.ToDate ?? DateTime.Today;

        if (from > to)
        {
            Console.Error.WriteLine("Errore: --from deve essere precedente o uguale a --to.");
            return 2;
        }

        Console.WriteLine("=== Normattiva — Tutti gli atti ===");
        Console.WriteLine($"Intervallo emanazione: {from:yyyy-MM-dd} → {to:yyyy-MM-dd}");
        Console.WriteLine($"Formato: {config.DefaultFormat} / export {config.DefaultExport}");
        Console.WriteLine();

        if (options.DryRun)
        {
            Console.WriteLine("DRY-RUN: verrebbe avviata una ricerca asincrona sull'intero corpus.");
            return 0;
        }

        var request = new NormattivaBatchRequest
        {
            Formato = config.DefaultFormat,
            RichiestaExport = config.DefaultExport,
            TipoRicerca = "A",
            Email = config.Email,
            ParametriRicerca = new NormattivaSearchFilter
            {
                DataInizioEmanazione = from,
                DataFineEmanazione = to
            }
        };

        var fileName =
            $"normattiva-all-{from:yyyyMMdd}-{to:yyyyMMdd}-{config.DefaultFormat}-{config.DefaultExport}.zip";
        var destinationPath = Path.Combine(options.OutputDirectory, "Normattiva", fileName);

        await using var services = BuildServices(Array.Empty<CkanPortal>(), options, config);
        var orchestrator = services.GetRequiredService<OrchestrationServiceFactory>().Create();

        var path = await orchestrator.DownloadNormattivaBatchAsync(
            config, request, destinationPath, cancellationToken);

        return WriteSingleNormattivaResult(path, options);
    }

    private static async Task<int> RunNormattivaSearchAsync(
        CliOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Query)
            && options.FromDate == null
            && options.ToDate == null)
        {
            Console.Error.WriteLine(
                "Errore: normattiva-search richiede almeno un filtro (--query, --from o --to). " +
                "Per l'intero corpus usa 'normattiva-all'.");
            return 2;
        }

        var config = CreateNormattivaConfig(options);
        var request = new NormattivaBatchRequest
        {
            Formato = config.DefaultFormat,
            RichiestaExport = config.DefaultExport,
            TipoRicerca = "A",
            Email = config.Email,
            ParametriRicerca = new NormattivaSearchFilter
            {
                TitoloRicerca = options.Query,
                DataInizioEmanazione = options.FromDate,
                DataFineEmanazione = options.ToDate
            }
        };

        Console.WriteLine("=== Normattiva — Ricerca avanzata ===");
        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            Console.WriteLine($"Titolo contiene: {options.Query}");
        }
        Console.WriteLine($"Emanazione: {options.FromDate?.ToString("yyyy-MM-dd") ?? "*"} → " +
                          $"{options.ToDate?.ToString("yyyy-MM-dd") ?? "*"}");
        Console.WriteLine($"Formato: {config.DefaultFormat} / export {config.DefaultExport}");
        Console.WriteLine();

        if (options.DryRun)
        {
            Console.WriteLine("DRY-RUN: verrebbe avviata una ricerca asincrona con questi filtri.");
            return 0;
        }

        var fileName =
            $"normattiva-search-{DateTime.Now:yyyyMMdd-HHmmss}-{config.DefaultFormat}-{config.DefaultExport}.zip";
        var destinationPath = Path.Combine(options.OutputDirectory, "Normattiva", fileName);

        await using var services = BuildServices(Array.Empty<CkanPortal>(), options, config);
        var orchestrator = services.GetRequiredService<OrchestrationServiceFactory>().Create();

        var path = await orchestrator.DownloadNormattivaBatchAsync(
            config, request, destinationPath, cancellationToken);

        return WriteSingleNormattivaResult(path, options);
    }

    private static int WriteSingleNormattivaResult(string? path, CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Normattiva non ha prodotto alcun archivio.");
            return 1;
        }

        ManifestWriter.WriteNormattivaManifest(new[] { path }, options.OutputDirectory, options.Command);

        Console.WriteLine();
        Console.WriteLine($"Archivio: {Path.GetFullPath(path)}");
        Console.WriteLine(
            $"Manifest: {Path.Combine(Path.GetFullPath(options.OutputDirectory), "normattiva-manifest.json")}");

        return 0;
    }

    #endregion

    #region Helpers

    private static ServiceProvider BuildServices(
        IEnumerable<CkanPortal> portals,
        CliOptions options,
        NormattivaConfig? normattiva)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            // The CLI prints its own progress and summary; library stack traces would be noise.
            builder.SetMinimumLevel(LogLevel.None);
        });

        services.AddCompleteOpenDataWorkflow(
            portals,
            options.OutputDirectory,
            client =>
            {
                if (!client.DefaultRequestHeaders.UserAgent.Any())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenDataDownloader/1.0");
                }
            },
            normattiva == null ? null : config => ApplyNormattivaOptions(config, options),
            options.MaxConcurrency);

        return services.BuildServiceProvider();
    }

    private static NormattivaConfig CreateNormattivaConfig(CliOptions options)
    {
        var config = new NormattivaConfig();
        ApplyNormattivaOptions(config, options);
        return config;
    }

    private static void ApplyNormattivaOptions(NormattivaConfig config, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Formato))
        {
            config.DefaultFormat = options.Formato;
        }

        if (!string.IsNullOrWhiteSpace(options.Export))
        {
            config.DefaultExport = options.Export;
        }

        if (!string.IsNullOrWhiteSpace(options.Email))
        {
            config.Email = options.Email;
        }
    }

    private static List<CkanPortal> ResolvePortals(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Portal)
            || options.Portal.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPortals
                .Select(p => new CkanPortal
                {
                    Name = p.Name,
                    BaseApiUrl = p.BaseApiUrl,
                    ApiKey = p.ApiKey
                })
                .ToList();
        }

        var portal = DefaultPortals.FirstOrDefault(p =>
            p.Name.Equals(options.Portal, StringComparison.OrdinalIgnoreCase));

        if (portal == null)
        {
            Console.Error.WriteLine($"Portale sconosciuto: '{options.Portal}'.");
            Console.Error.WriteLine(
                $"Portali disponibili: {string.Join(", ", DefaultPortals.Select(p => p.Name))}");
            return new List<CkanPortal>();
        }

        return new List<CkanPortal>
        {
            new()
            {
                Name = portal.Name,
                BaseApiUrl = portal.BaseApiUrl,
                ApiKey = portal.ApiKey
            }
        };
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando sconosciuto: '{command}'.");
        Console.Error.WriteLine();
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            OpenDataDownloader — downloader per open data italiani (CKAN) e Normattiva

            USO
              OpenDataDownloader.Cli <comando> [opzioni]

            COMANDI
              ckan                     Scarica tutti i dataset dai portali CKAN configurati
              normattiva-collections   Scarica le collezioni predefinite di Normattiva
              normattiva-all           Scarica l'intero corpus Normattiva (1861 → oggi)
              normattiva-search        Ricerca avanzata Normattiva (filtri obbligatori)

            OPZIONI GENERALI
              -o, --output <dir>       Cartella di output (default: ./OpenData)
              -p, --portal <nome>      Limita a un portale: DatiGovIt, BDAP_MEF, OpenGA, ANAC
                  --api-key <key>      API key CKAN (header X-CKAN-API-Key)
                  --include-documents  Include anche PDF, HTML, DOC, DOCX, ODT...
                  --limit <n>          Massimo N dataset per portale (o risultati di ricerca / collezioni)
                  --delay <ms>         Pausa tra dataset (default: 200)
                  --concurrency <n>    Download paralleli per dataset (default: 4)
                  --query <testo>      Ricerca full-text CKAN (oppure titolo per Normattiva)
                  --dry-run            Mostra cosa verrebbe scaricato, senza scaricare
              -h, --help               Mostra questo aiuto

            OPZIONI NORMATTIVA
                  --formato <F>        Formato export: AKN, XML, PDF (default: AKN)
                  --export <O|V|M>     O = originario, V = vigente, M = multivigente
                  --from <data>        Data inizio emanazione (es. 1861-01-01)
                  --to <data>          Data fine emanazione (default: oggi)
                  --variants           Scarica tutte le varianti O/V/M delle collezioni
                  --email <indirizzo>  Email opzionale per il servizio batch

            ESEMPI
              OpenDataDownloader.Cli ckan --output C:\OpenData
              OpenDataDownloader.Cli ckan --portal DatiGovIt --limit 100 --dry-run
              OpenDataDownloader.Cli ckan --include-documents --concurrency 8
              OpenDataDownloader.Cli normattiva-collections --formato AKN --export V
              OpenDataDownloader.Cli normattiva-all --formato AKN --export O
              OpenDataDownloader.Cli normattiva-search --query "privacy" --from 2020-01-01

            NOTE
              - I file già presenti e non vuoti vengono saltati: il download è riprendibile.
              - Ogni file viene scritto come .part e rinominato solo a download completo.
              - Al termine vengono creati manifest.json / manifest.csv (CKAN) e
                normattiva-manifest.json / .csv (Normattiva).
              - ANAC può bloccare le richieste con un WAF ("Request Rejected"); in quel caso
                il portale viene segnalato come errore e gli altri proseguono.
            """);
    }

    #endregion
}
