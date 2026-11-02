using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace OpenDataDownloader
{
    // Modello per la risposta generica di CKAN
    public class CkanResponse<T>
    {
        public bool success { get; set; }
        public T result { get; set; }
        public Error error { get; set; }
    }

    public class Error
    {
        public string message { get; set; }
    }

    // Modello per i metadati di un dataset (package_show)
    public class CkanPackage
    {
        public string id { get; set; }
        public string name { get; set; }
        public string title { get; set; }
        public List<CkanResource> resources { get; set; }
    }

    public class CkanResource
    {
        public string id { get; set; }
        public string name { get; set; }
        public string format { get; set; }
        public string url { get; set; }
        public string mimetype { get; set; }
    }

    // Configurazione per un portale CKAN
    public class CkanPortal
    {
        public string Name { get; set; }
        public string BaseApiUrl { get; set; } // es. https://.../api/3/action/
    }

    // Configurazione per Normattiva
    public class NormattivaConfig
    {
        public string BaseApiUrl { get; set; } = "https://api.normattiva.it/t/normattiva.api/bff-opendata/v1/api/v1/";
        public string SearchEndpoint { get; set; } = "ricerca/semplice";
        public string BatchDownloadEndpoint { get; set; } = "download/richiesta-zip";
        public string DownloadEndpoint { get; set; } = "download/zip";
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string DownloadRoot = Path.Combine(Environment.CurrentDirectory, "OpenData");

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Open Data Downloader (CKAN + Normattiva) ===\n");

            // Portali CKAN da scaricare
            var ckanPortals = new List<CkanPortal>
            {
                new CkanPortal
                {
                    Name = "ANAC",
                    BaseApiUrl = "https://dati.anticorruzione.it/opendata/api/3/action/"
                },
                new CkanPortal
                {
                    Name = "BDAP_MEF",
                    BaseApiUrl = "https://bdap-opendata.rgs.mef.gov.it/SpodCkanApi/api/3/action/"
                },
                new CkanPortal
                {
                    Name = "OpenGA",
                    BaseApiUrl = "https://openga.giustizia-amministrativa.it/api/3/action/"
                },
                new CkanPortal
                {
                    Name = "DatiGovIt",
                    BaseApiUrl = "https://www.dati.gov.it/opendata/api/3/action/"
                }
            };

            foreach (var portal in ckanPortals)
            {
                Console.WriteLine($"\n--- Inizio download da {portal.Name} ---");
                try
                {
                    await DownloadAllFromCkanPortal(portal);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRORE] {portal.Name}: {ex.Message}");
                }
                Console.WriteLine($"--- Fine download da {portal.Name} ---");
            }

            // Normattiva (workflow separato)
            Console.WriteLine("\n--- Inizio download da Normattiva ---");
            try
            {
                await DownloadNormattivaBatch();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRORE] Normattiva: {ex.Message}");
            }

            Console.WriteLine("\nDownload completato. Premi un tasto per uscire.");
            Console.ReadKey();
        }

        // ---------- Logica CKAN ----------
        static async Task DownloadAllFromCkanPortal(CkanPortal portal)
        {
            var packageIds = await GetPackageList(portal.BaseApiUrl);
            Console.WriteLine($"Trovati {packageIds.Count} dataset.");

            int index = 0;
            foreach (var packageId in packageIds)
            {
                index++;
                Console.WriteLine($"  [{index}/{packageIds.Count}] Dataset: {packageId}");
                try
                {
                    var package = await GetPackageShow(portal.BaseApiUrl, packageId);
                    if (package?.resources == null || package.resources.Count == 0)
                    {
                        Console.WriteLine("    Nessuna risorsa trovata.");
                        continue;
                    }

                    foreach (var res in package.resources)
                    {
                        // Scarica solo formati "leggeri" o ZIP (escludi HTML, PDF enormi, ecc.)
                        var formato = (res.format ?? "").ToUpperInvariant();
                        if (!IsDownloadableFormat(formato))
                        {
                            Console.WriteLine($"    Salto formato non supportato: {res.format}");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(res.url))
                        {
                            Console.WriteLine($"    URL mancante per risorsa: {res.name}");
                            continue;
                        }

                        await DownloadFile(res.url, portal.Name, package.name, res.name, res.format);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    [ERRORE dataset {packageId}]: {ex.Message}");
                }

                // Piccola pausa per non sovraccaricare le API
                await Task.Delay(200);
            }
        }

        static async Task<List<string>> GetPackageList(string baseApiUrl)
        {
            var url = baseApiUrl + "package_list";
            var response = await httpClient.GetStringAsync(url);
            var ckanResp = JsonSerializer.Deserialize<CkanResponse<List<string>>>(response);
            if (ckanResp == null || !ckanResp.success)
                throw new Exception($"package_list fallita: {ckanResp?.error?.message}");
            return ckanResp.result;
        }

        static async Task<CkanPackage> GetPackageShow(string baseApiUrl, string packageId)
        {
            var encodedId = HttpUtility.UrlEncode(packageId);
            var url = baseApiUrl + "package_show?id=" + encodedId;
            var response = await httpClient.GetStringAsync(url);
            var ckanResp = JsonSerializer.Deserialize<CkanResponse<CkanPackage>>(response);
            if (ckanResp == null || !ckanResp.success)
            {
                Console.WriteLine($"    package_show fallita per {packageId}: {ckanResp?.error?.message}");
                return null;
            }
            return ckanResp.result;
        }

        static bool IsDownloadableFormat(string format)
        {
            var allowed = new[] { "JSON", "CSV", "ZIP", "GEOJSON", "XML", "RDF", "TTL", "XLSX", "XLS" };
            return allowed.Contains(format);
        }

        // ---------- Download file generico ----------
        static async Task DownloadFile(string url, string portalName, string packageName, string resourceName, string format)
        {
            try
            {
                // Costruisci un nome file sicuro
                var safePackage = MakeSafeFileName(packageName ?? "dataset");
                var safeResource = MakeSafeFileName(resourceName ?? "risorsa");
                var ext = GetExtensionFromFormat(format);
                var fileName = $"{safeResource}{ext}";

                var dir = Path.Combine(DownloadRoot, portalName, safePackage);
                Directory.CreateDirectory(dir);
                var filePath = Path.Combine(dir, fileName);

                // Evita di riscaricare file già presenti (opzionale)
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                {
                    Console.WriteLine($"    Già presente: {fileName}");
                    return;
                }

                Console.WriteLine($"    Scarico: {fileName} ...");
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
                Console.WriteLine($"    Salvato: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [ERRORE download] {url}: {ex.Message}");
            }
        }

        static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            return safe.Trim().TrimEnd('.');
        }

        static string GetExtensionFromFormat(string format)
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

        // ---------- Logica Normattiva (workflow semplificato) ----------
        static async Task DownloadNormattivaBatch()
        {
            var config = new NormattivaConfig();
            // Esempio: richiedi uno ZIP per un insieme di atti.
            // L'API reale richiede parametri di ricerca; qui mostriamo la struttura della chiamata batch.
            // Nota: adatta i parametri in base alla documentazione ufficiale (par. 1.3.8 "Download collezione preconfezionata").
            // Rif: https://dati.normattiva.it/assets/come_fare_per/Normattiva%20OpenData.html

            var requestBody = new
            {
                // Parametri di esempio: potresti dover specificare un filtro (es. tipoProvvedimento, anno, ecc.)
                formatoRichiesta = "ZIP",
                // "filtro" : { ... } // Aggiungi qui i criteri di ricerca desiderati
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            Console.WriteLine("  Invio richiesta batch a Normattiva...");
            var batchUrl = config.BaseApiUrl + config.BatchDownloadEndpoint;
            var batchResponse = await httpClient.PostAsync(batchUrl, content);

            if (!batchResponse.IsSuccessStatusCode)
            {
                var errBody = await batchResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"  Errore nella richiesta batch: {batchResponse.StatusCode} - {errBody}");
                return;
            }

            var batchResultJson = await batchResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"  Risposta batch: {batchResultJson}");

            // La risposta dovrebbe contenere un token per il download
            using var doc = JsonDocument.Parse(batchResultJson);
            if (!doc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                Console.WriteLine("  Nessun token trovato nella risposta. Controlla la documentazione.");
                return;
            }

            var token = tokenElement.GetString();
            Console.WriteLine($"  Token ottenuto: {token}. Attendo la generazione dello ZIP...");

            // Polling (semplificato): attendi e poi scarica
            // In produzione, implementa un polling con intervalli e timeout.
            await Task.Delay(5000); // Attesa fissa di 5 secondi (adatta)

            var downloadUrl = $"{config.BaseApiUrl}{config.DownloadEndpoint}?token={token}";
            var zipPath = Path.Combine(DownloadRoot, "Normattiva", "normattiva_batch.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? DownloadRoot);

            Console.WriteLine($"  Scarico ZIP da: {downloadUrl}");
            using var downloadResponse = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            downloadResponse.EnsureSuccessStatusCode();

            await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await downloadResponse.Content.CopyToAsync(fs);
            Console.WriteLine($"  ZIP salvato in: {zipPath}");
        }
    }
}