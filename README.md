# OpenData Downloader

Libreria **C# / .NET 10** e **CLI** per scaricare dati open data italiani da portali **CKAN** e atti legali dall'API **Normattiva**.

## 🚀 CLI — uso rapido

```bash
# Tutti i portali CKAN configurati (DatiGovIt, BDAP_MEF, OpenGA, ANAC)
dotnet run --project src/OpenDataDownloader.Cli -- ckan --output C:\OpenData

# Un solo portale, con limite e senza scaricare (dry-run)
dotnet run --project src/OpenDataDownloader.Cli -- ckan --portal DatiGovIt --limit 100 --dry-run

# Includi anche PDF/HTML/DOC e aumenta il parallelismo
dotnet run --project src/OpenDataDownloader.Cli -- ckan --include-documents --concurrency 8

# Collezioni predefinite Normattiva (Codici, Regi decreti, DPR, ...)
dotnet run --project src/OpenDataDownloader.Cli -- normattiva-collections --formato AKN --export V

# Intero corpus Normattiva (emanazione 1861 → oggi) tramite ricerca asincrona
dotnet run --project src/OpenDataDownloader.Cli -- normattiva-all --formato AKN --export O

# Ricerca avanzata Normattiva
dotnet run --project src/OpenDataDownloader.Cli -- normattiva-search --query "privacy" --from 2020-01-01
```

### Comandi

| Comando | Descrizione |
|---------|-------------|
| `ckan` | Scarica i dataset da tutti i portali CKAN (default se si passano solo opzioni) |
| `normattiva-collections` | Scarica le collezioni preconfezionate pubblicate da Normattiva |
| `normattiva-all` | Scarica l'intero corpus tramite ricerca asincrona (1861 → oggi) |
| `normattiva-search` | Ricerca avanzata con filtri (`--query`, `--from`, `--to`) |

### Opzioni principali

| Opzione | Descrizione |
|---------|-------------|
| `-o, --output <dir>` | Cartella di output (default `./OpenData`) |
| `-p, --portal <nome>` | Limita a `DatiGovIt`, `BDAP_MEF`, `OpenGA`, `ANAC` (o `all`) |
| `--api-key <key>` | API key CKAN, inviata nell'header `X-CKAN-API-Key` |
| `--include-documents` | Include anche PDF, HTML, DOC, DOCX, ODT, … |
| `--limit <n>` | Massimo N dataset per portale (o risultati di ricerca / collezioni) |
| `--delay <ms>` | Pausa tra dataset (default 200 ms) |
| `--concurrency <n>` | Download paralleli per dataset (default 4, max 32) |
| `--query <testo>` | Ricerca full-text CKAN, oppure titolo per Normattiva |
| `--dry-run` | Mostra cosa verrebbe scaricato senza scaricare |
| `--formato <F>` | Formato export Normattiva: `AKN`, `XML`, `PDF` (default `AKN`) |
| `--export <O\|V\|M>` | `O` originario, `V` vigente, `M` multivigente |
| `--from`, `--to` | Intervallo date di emanazione (formato `yyyy-MM-dd`) |
| `--variants` | Scarica tutte le varianti O/V/M delle collezioni |
| `--email <indirizzo>` | Email opzionale per il servizio batch Normattiva |

Al termine vengono creati:

- `manifest.json` / `manifest.csv` per i download CKAN (una riga per risorsa);
- `normattiva-manifest.json` / `normattiva-manifest.csv` per gli archivi Normattiva.

### Ripresa e integrità

- I file già presenti e non vuoti vengono saltati: il download è **riprendibile**.
- Ogni file viene scritto come `.part` e rinominato solo a download completo.
- Vengono rifiutate risposte HTML al posto del file atteso, risposte vuote e download troncati.
- I nomi file includono un suffisso con l'id della risorsa per evitare collisioni.

## 📦 Uso come libreria

Clona il repository e fai riferimento al progetto `src/OpenDataDownloader.csproj` dalla tua solution, oppure incorpora i sorgenti direttamente nel tuo progetto.

### 1. Configurazione Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenDataDownloader.Core;
using OpenDataDownloader.Contracts;

var services = new ServiceCollection();

// Infrastruttura core + root directory dei download
services.AddOpenDataDownloader(downloadRoot: "./downloads", maxConcurrency: 4);

// Porta CKAN — nomi e URL API
services.AddCkanPortal(new CkanPortal
{
    Name = "DatiGovIt",
    BaseApiUrl = "https://www.dati.gov.it/opendata/api/3/action/"
});

services.AddCkanPortal(new CkanPortal
{
    Name = "ANAC",
    BaseApiUrl = "https://dati.anticorruzione.it/opendata/api/3/action/",
    ApiKey = "la-tua-api-key"       // opzionale — header X-CKAN-API-Key
});

// Opzionale: API Normattiva (collezioni + ricerca asincrona)
services.AddNormattiva(new NormattivaConfig());

var sp = services.BuildServiceProvider();
```

### 2. Scaricare tutti i dataset disponibili

```csharp
var factory = sp.GetRequiredService<OrchestrationServiceFactory>();
var orchestrator = factory.Create(progressCallback: report =>
{
    Console.WriteLine(
        $"[{report.Type}] {report.PortalName}  #{report.CurrentIndex}/{report.TotalCount}  {report.PackageTitle ?? report.ResourceName}");
});

orchestrator.DelayBetweenPackages = TimeSpan.FromMilliseconds(200);
orchestrator.IncludeDocuments = false;

var portals = new[]
{
    new CkanPortal { Name = "DatiGovIt", BaseApiUrl = "https://www.dati.gov.it/opendata/api/3/action/" }
};

var summary = await orchestrator.DownloadAllAsync(portals, cancellationToken);

Console.WriteLine($"{summary.TotalSuccessfulDownloads} file in {summary.TotalDuration:mm\\:ss}");
Console.WriteLine($"Falliti: {summary.TotalFailedDownloads}, saltati: {summary.TotalSkippedResources}");
```

Un singolo `OrchestrationService` risolve automaticamente il provider corretto per ogni portale, quindi si possono passare più portali alla stessa sessione.

### 3. Cercare e scaricare solo i dataset matching

```csharp
var opts = new CkanSearchOptions
{
    Q     = "statistiche economia",
    Tags  = new List<string> { "economia", "ISTAT" },
    Limit = 50,
    Sort  = "score desc"
};

var summary = await orchestrator.DownloadMatchingAsync(portals, opts, cancellationToken);
```

La libreria scarica per default i formati macchina-readabile: CSV, JSON, XML, ZIP, GEOJSON, RDF, TTL, XLSX, XLS, ODS, SHP, GML, GPKG, KML, TIFF, TXT, PARQUET e altri formati geospaziali. PDF, HTML, DOC ecc. vengono inclusi solo con `IncludeDocuments = true` (o `--include-documents`).

### 4. Normattiva — collezioni e ricerca asincrona

```csharp
var config = new NormattivaConfig
{
    DefaultFormat = "AKN",   // AKN, XML, PDF
    DefaultExport = "O"      // O = originario, V = vigente, M = multivigente
};

// Collezioni preconfezionate (Codici, DPR, Regi decreti, ...)
var paths = await orchestrator.DownloadNormattivaPredefinedCollectionsAsync(
    config,
    destinationRoot: "./downloads/Normattiva/Collezioni",
    formato: "AKN",
    formatoRichiesta: "V",
    allVariants: false,
    cancellationToken);

// Intero corpus tramite ricerca asincrona (1861 → oggi)
var zip = await orchestrator.DownloadNormattivaBatchAsync(
    config,
    NormattivaBatchRequest.AllLaws(formato: "AKN", richiestaExport: "O"),
    destinationPath: "./downloads/Normattiva/normattiva-all.zip",
    cancellationToken);
```

Il provider implementa il flusso reale documentato:

1. `POST ricerca-asincrona/nuova-ricerca` → token
2. `PUT ricerca-asincrona/conferma-ricerca` con `{ "token": ... }`
3. `GET ricerca-asincrona/check-status/{token}` (polling; `303` = pronto)
4. `GET collections/download/collection-asincrona/{token}` → ZIP

Le collezioni preconfezionate usano:

- `GET collections/collection-predefinite`
- `GET collections/download/collection-preconfezionata?nome=...&formato=AKN&formatoRichiesta=O`

> **Nota sugli archivi vuoti.** Se la ricerca non trova atti, Normattiva restituisce comunque
> uno ZIP valido ma vuoto (22 byte). La CLI lo salva e mostra un avviso: in quel caso amplia
> l'intervallo `--from`/`--to` o rimuovi i filtri. Esempio verificato: il 1–2 gennaio 2024 non
> produce atti, mentre gennaio–marzo 2024 ne produce 56 (archivio AKN da ~1,7 MB).

## 📁 Struttura del progetto

| Percorso | Contenuto |
|----------|-----------|
| `src/OpenDataDownloader.csproj` | Libreria principale (`net10.0`, nullable enabled) |
| `src/OpenDataDownloader/` | Sorgenti della libreria (`IDataProvider`, `CkanDataProvider`, `FileStorageService`, `NormattivaProvider`, `OrchestrationService`, `FormatFilter`, …) |
| `src/OpenDataDownloader.Cli/` | CLI eseguibile (`Program.cs`, `CliOptions.cs`, `ManifestWriter.cs`) |
| `tests/OpenDataDownloader.Tests/` | Test unitari (xUnit + Moq) |

## 🏗 Architettura

### Interfacce chiave

| Interfaccia | Responsabilità |
|-------------|----------------|
| **`IDataProvider`** | Scoperta e recupero dataset da un portale CKAN (`package_list`, `package_show`, `package_search`) |
| **`IFileStorageService`** | Download risorse su disco con gerarchia sicura, retry e parallelismo controllato |
| **`INormattivaProvider`** | Collezioni preconfezionate e ricerca asincrona Normattiva |

### Servizi

| Classe | Dettaglio |
|--------|-----------|
| **`CkanDataProvider`** | Wrapper HTTP per CKAN con **retry Polly** (back-off esponenziale), timeout 60 s, paginazione `package_search` e fallback automatico se `package_list` non è disponibile |
| **`FileStorageService`** | Gerarchia `DownloadRoot / PortalName / SafePackageName / SafeResourceName__id.ext`; scrittura atomica `.part`, `SemaphoreSlim` per parallelismo configurabile |
| **`NormattivaProvider`** | Flusso asincrono reale (nuova-ricerca → conferma → polling → download) e download delle collezioni preconfezionate |
| **`OrchestrationService`** | Orchestratore end-to-end: risoluzione provider per portale, filtri, delay, progresso osservabile |
| **`FormatFilter`** | Validazione formati con fallback su estensione URL e MIME type |

### Progresso osservabile

Due modalità per tracciare lo stato della sessione:

1. **Callback** — `Action<DownloadProgressReport>` al costruttore o alla factory
2. **Evento** — `event EventHandler<DownloadProgressEventArgs> ProgressChanged`

Il report contiene tipo evento (PortalStarted, PackageFetching, ResourceDownloading, …), indici, tempi, errori.

### Risultati dettagliati

Ogni sessione restituisce un **`DownloadSessionSummary`** con:

- `TotalDuration` — tempo totale della sessione
- `Portals` — `PortalDownloadResult[]` con durata per-portale e conteggi
- `TotalSuccessfulDownloads`, `TotalFailedDownloads`, `TotalSkippedResources`
- `PackageResults` — risultati individuali per ogni package (success/failure, tempi, download per risorsa)
- `ResourceDownloadResult` — esito singolo (`FilePath`, `Duration`, `Error`)

## 🔐 Autenticazione

Alcuni portali CKAN richiedono una API key. Si configura tramite la proprietà `ApiKey` di `CkanPortal`:

```csharp
services.AddCkanPortal(new CkanPortal
{
    Name       = "ANAC",
    BaseApiUrl = "https://dati.anticorruzione.it/opendata/api/3/action/",
    ApiKey     = "your-secret-key"       // inserisce X-CKAN-API-Key in automatico
});
```

## ⚙️ Concurrency & resilienza

- `FileStorageService` accetta `maxConcurrency` (default 4, range 1–32).
- Tutte le chiamate HTTP usano **Polly 8.x**: retry con back-off esponenziale + timeout globale.
- I nomi file vengono sanificati: caratteri invalidi sostituiti con `_`.
- I portali che bloccano le richieste (es. ANAC con WAF `Request Rejected`) vengono segnalati come errore senza interrompere gli altri portali.

## 🧪 Testing

```bash
dotnet test
```

Stack: **xUnit** + **Moq** + **Microsoft.NET.Test.Sdk** + coverlet.collector.

## 📝 Licenza

MIT License — see [LICENSE](LICENSE).
