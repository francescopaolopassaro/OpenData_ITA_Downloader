# Design

Ho letto tutto il codice sorgente e i test del progetto. Ecco la mia analisi completa di **cosa c'è** e **cosa manca**.

---

## Panoramica attuale

Il progetto è una **class library C# (.NET 10.0)** che scarica dati open data italiani da portali CKAN (dati.gov.it, ANAC, MEF, OpenGA) e documenti legali dall'API Normattiva.

### Cosa esiste già:

| Area | File | Descrizione |
|------|------|-------------|
| **Contracts** (`OpenDataDownloader.Contracts`) | `IDataProvider`, `INormattivaProvider`, `CkanPackage`, `CkanResource`, `CkanPortal`, `CkanResponse<T>`, `Error`, `NormattivaConfig` (con request/response/document) | Interfacce e modelli dati |
| **Core** (`OpenDataDownloader.Core`) | `CkanDataProvider`, `FileStorageService`, `FormatFilter`, `NormattivaProvider`, `OrchestrationService` | Implementazioni |
| **Infrastruttura** | `DataFetcherException`, `GlobalUsings` | Eccezione custom + global usings |
| **Tests** | `FormatFilterTests.cs` | Test su FormatFilter (xUnit + Theory/InlineData) |
| **Dependencies** | Microsoft.Extensions.Logging.Abstractions, Polly | Logging e resilienza |

---

## Cosa manca

### 1. **Filtri avanzati per package discovery**
`IDataProvider` ha solo `GetPackageListAsync()` e `GetPackageDetailsAsync()`. Non esiste un metodo tipo `SearchPackagesAsync()` o `GetPackageListAsync(filter)` con parametri come q/tags/tags_contains/categorie. Molte API CKAN supportano query search — l'endpoint CKAN `search_list` permette di filtrare per termini, tag, categorie, ecc. Manca totalmente.

### 2. **Mancano i parametri di filtro nell'orchestratore**
`OrchestrationService.DownloadAllAsync(IEnumerable<CkanPortal>, ...)` scarica SEMPRE TUTTO. Non c'è alcun meccanismo per filtrare quali package/processare (es. per nome, per tag, per formato). Il filtro `FormatFilter` è applicato alle risorse ma non ai package stessi.

### 3. **Nessun modello DownloadResult/risultato dettagliato**
`PortalResult` nel summary ha solo conteggi grezzi (TotalPackages, ProcessedPackages, SuccessfulDownloads, FailedPackages, Error). Non distingue tra:
- Package con successo vs fallimento singolarmente
- Resource-level statistics
- Tempi di elaborazione (durata totale, per-portal, per-package)

### 4. **Progresso non osservabile**
Il servizio usa `Console.WriteLine` come unico feedback. Non c'è:
- Interfaccia `IProgress<T>` o evento di progresso
- Report di stato durante operazioni lunghe (es. polling Normattiva: "Still processing X/30")
- ProgressReport/ProgressEventArgs pattern

### 5. **Batch download FileStorageService non implementato**
L'interfaccia `IFileStorageService` dichiara `DownloadBatchAsync(...)`, ma l'implementazione restituisce semplicemente `NotImplementedException`. Dovrebbe scaricare più risorse in parallelo con limiti di concorrenza.

### 6. **Setup DI insufficiente / No factory classes**
Non ci sono classi factory o extension method per istanziare rapidamente `HttpClient` configurato con headers, retry policies, base URL. L'utente deve creare tutto manualmente. Non esiste un `OpenDataDownloaderServiceCollectionExtensions` per il DI.

### 7. **Copertura test molto limitata**
Esiste SOLO `FormatFilterTests.cs` (8 test). Manca:
- Test per `CkanDataProvider` (mock HTTP)
- Test per `FileStorageService` (mock filesystem)
- Test per `NormattivaProvider` (mock responses)
- Test per `OrchestrationService` (integration test)
- Test per `MakeSafeFileName` e altri metodi interni
- Directory `tests/OpenDataDownloader.Tests/Models/` è vuota (ci sono sottocartelle?)

### 8. **Autenticazione assente**
Alcuni portali CKAN richiedono API key negli header (es. `X-CKAN-API-Key`). Nessun supporto all'autenticazione: né constructor parameter né configurazione tramite `CkanPortal`. `NormattivaConfig` pure manca di credenziali/tokens.

### 9. **Validazione input mancante**
Nessuna validazione centralizzata dei config/input:
- `CkanPortal.BaseApiUrl` potrebbe essere male formattato
- `NormattivaConfig` endpoint personalizzati senza validazione
- Nessun DTO validation con `System.ComponentModel.DataAnnotations`

### 10. **Documentazione d'uso / Samples assenti**
Il progetto è una class library con documentazione XML interna (ben fatta), ma:
- Nessun file README.md o esempio di utilizzo
- Nessun progetto sample/console app nella solution
- La struttura è pensata come libreria riutilizzabile ma non mostra come si integra

---

## Livello di criticità

| # | Cosa manca | Criticità |
|---|-----------|-----------|
| 1 | Filtri ricerca packages | Media-Alta (funzionalità principale incompleta) |
| 4 | Progresso osservabile | Alta (esperienza utente scarsa per operativi lunghi) |
| 5 | Batch download implementato | Media (membro interfaciale non implementato) |
| 6 | DI Registration | Media (usabilità della libreria ridotta) |
| 7 | Test coverage estesa | Alta (qualità non verificabile) |
| 8 | Autenticazione API | Dipende dai target portal (se serve è alta) |

Vuoi che proceda a implementare alcune di queste carenze? Se sì, quale priorità ti interessa?
