using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenDataDownloader.Core;

/// <summary>
/// Extension methods for registering OpenDataDownloader services with Dependency Injection.
/// </summary>
public static class OpenDataDownloaderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core OpenDataDownloader infrastructure: HttpClient, FileStorageService, and factory.
    /// Use this as a starting point, then add individual CKAN portals via AddCkanPortal().
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="downloadRoot">The root directory where downloaded files will be stored (default: "./Downloads").</param>
    /// <param name="configureHttpClient">Optional callback to configure the shared HttpClient used by all providers.</param>
    /// <param name="maxConcurrency">Maximum number of parallel file downloads (default: 4).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenDataDownloader(
        this IServiceCollection services,
        string? downloadRoot = null,
        Action<HttpClient>? configureHttpClient = null,
        int maxConcurrency = 4)
    {
        // Register a named HttpClient for general HTTP calls
        services.AddHttpClient("OpenDataDownloader", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            configureHttpClient?.Invoke(client);
            ApplyDefaultHeaders(client, userAgent: null, accept: "*/*");
        });

        // Register FileStorageService
        services.AddSingleton<IFileStorageService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient("OpenDataDownloader");

            return new FileStorageService(httpClient, downloadRoot ?? "./Downloads", maxConcurrency);
        });

        // Register the OrchestrationServiceFactory
        services.AddSingleton<OrchestrationServiceFactory>();

        return services;
    }

    /// <summary>
    /// Registers a CKAN data provider for a specific portal with optional API key authentication.
    /// The provider is registered keyed by portal name so callers can resolve it by name.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="portal">The CKAN portal configuration.</param>
    /// <param name="configureHttpClient">Optional callback to customize the portal's HttpClient.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCkanPortal(
        this IServiceCollection services,
        CkanPortal portal,
        Action<HttpClient>? configureHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(portal);
        ArgumentException.ThrowIfNullOrEmpty(portal.BaseApiUrl);

        // Register a named HttpClient for this portal
        var clientName = $"ckan.{portal.Name}";

        services.AddHttpClient(clientName, client =>
        {
            var url = portal.BaseApiUrl;
            if (!url.EndsWith('/'))
                url += '/';
            client.BaseAddress = new Uri(url);
            client.Timeout = TimeSpan.FromSeconds(60);
            configureHttpClient?.Invoke(client);

            // Some portals (e.g. OpenGA) reject requests without a User-Agent, and some
            // WAFs (e.g. ANAC) additionally require a browser-like UA plus an Accept header.
            ApplyDefaultHeaders(
                client,
                portal.UserAgent ?? CkanPortal.DefaultUserAgent,
                accept: "application/json");
        });

        // Register the provider as a keyed service — handles API key internally
        services.AddKeyedSingleton<IDataProvider>(portal.Name, (sp, key) =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

            // Inject API key header if configured on the portal
            if (!string.IsNullOrWhiteSpace(portal.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Remove("X-CKAN-API-Key");
                httpClient.DefaultRequestHeaders.Add("X-CKAN-API-Key", portal.ApiKey);
            }

            var provider = new CkanDataProvider(
                httpClient,
                portal.ApiKey,
                sp.GetService<ILogger<CkanDataProvider>>())
            {
                Name = portal.Name
            };
            return provider;
        });

        return services;
    }

    /// <summary>
    /// Registers a Normattiva provider with the given configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="normattivaConfig">The Normattiva configuration settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNormattiva(
        this IServiceCollection services,
        NormattivaConfig normattivaConfig)
    {
        ArgumentNullException.ThrowIfNull(normattivaConfig);

        services.AddHttpClient<INormattivaProvider, NormattivaProvider>(client =>
        {
            client.BaseAddress = new Uri(normattivaConfig.BaseApiUrl.EndsWith('/')
                ? normattivaConfig.BaseApiUrl
                : normattivaConfig.BaseApiUrl + "/");
            client.Timeout = TimeSpan.FromMinutes(65); // Normattiva polling can take long

            ApplyDefaultHeaders(
                client,
                CkanPortal.DefaultUserAgent,
                accept: "application/json");
        });

        return services;
    }

    /// <summary>
    /// Adds the default User-Agent and Accept headers to a client when they are not already set.
    /// A browser-like User-Agent is required by some WAFs (e.g. ANAC), and the Accept header
    /// is also part of the request signature those WAFs check.
    /// </summary>
    private static void ApplyDefaultHeaders(HttpClient client, string? userAgent, string accept)
    {
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                string.IsNullOrWhiteSpace(userAgent) ? CkanPortal.DefaultUserAgent : userAgent);
        }

        if (!client.DefaultRequestHeaders.Contains("Accept"))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", accept);
        }
    }

    /// <summary>
    /// Convenience method that registers everything needed for a complete download workflow.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="portals">List of CKAN portals to register.</param>
    /// <param name="downloadRoot">Download root directory (default: "./Downloads").</param>
    /// <param name="configureHttpClient">Optional callback to configure shared HttpClient.</param>
    /// <param name="configureNormattiva">Optional callback to configure Normattiva settings.</param>
    /// <param name="maxConcurrency">Maximum number of parallel file downloads (default: 4).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCompleteOpenDataWorkflow(
        this IServiceCollection services,
        IEnumerable<CkanPortal> portals,
        string? downloadRoot = null,
        Action<HttpClient>? configureHttpClient = null,
        Action<NormattivaConfig>? configureNormattiva = null,
        int maxConcurrency = 4)
    {
        // Register core infrastructure
        services.AddOpenDataDownloader(downloadRoot, configureHttpClient, maxConcurrency);

        // Register each portal
        foreach (var portal in portals)
        {
            services.AddCkanPortal(portal, configureHttpClient);
        }

        // Optionally register Normattiva
        if (configureNormattiva != null)
        {
            var config = new NormattivaConfig();
            configureNormattiva(config);
            services.AddNormattiva(config);
        }

        return services;
    }
}

/// <summary>
/// Factory for creating OrchestrationService instances from resolved dependencies.
/// </summary>
public class OrchestrationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;

    public OrchestrationServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Creates an OrchestrationService. When <paramref name="providerName"/> is null the
    /// service resolves the correct keyed provider for each portal automatically, so a
    /// single orchestrator can safely process multiple portals.
    /// </summary>
    /// <param name="providerName">
    /// Optional portal name. When supplied, that provider is used for every portal
    /// (legacy single-portal behaviour).
    /// </param>
    /// <param name="progressCallback">Optional progress callback for real-time reports.</param>
    /// <returns>A configured OrchestrationService instance.</returns>
    public OrchestrationService Create(
        string? providerName = null,
        Action<DownloadProgressReport>? progressCallback = null)
    {
        IDataProvider ResolveProvider(CkanPortal portal)
        {
            var name = providerName ?? portal.Name;
            var provider = _serviceProvider.GetKeyedService<IDataProvider>(name)
                ?? _serviceProvider.GetService<IDataProvider>();

            if (provider == null)
            {
                throw new KeyNotFoundException(
                    $"No IDataProvider found for '{name}'. Call AddCkanPortal first.");
            }

            return provider;
        }

        return new OrchestrationService(
            dataProvider: null,
            _serviceProvider.GetRequiredService<IFileStorageService>(),
            _serviceProvider.GetService<INormattivaProvider>(),
            _serviceProvider.GetService<ILogger<OrchestrationService>>(),
            progressCallback,
            providerResolver: ResolveProvider);
    }
}

/// <summary>
/// HttpMessageHandler that injects the X-CKAN-API-Key header on outbound requests.
/// </summary>
internal sealed class ApiKeyHttpMessageHandler : DelegatingHandler
{
    private readonly string _apiKey;

    public ApiKeyHttpMessageHandler(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-CKAN-API-Key", _apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
