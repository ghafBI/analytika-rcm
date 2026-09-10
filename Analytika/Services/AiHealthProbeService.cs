using System.Diagnostics;
using System.Net.Http.Headers;
using Analytika.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytika.Services;

public sealed class AiHealthProbeService
{
    private readonly IAiSettingsService _settings;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _clients;

    public AiHealthProbeService(IAiSettingsService settings, AppDbContext db, IHttpClientFactory clients)
    {
        _settings = settings;
        _db = db;
        _clients = clients;
    }

    public async Task<object> GetStatusAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync();
        var primary = await ProbeOpenAiAsync("NVIDIA", settings.ApiBaseUrl, settings.Model,
            settings.Enabled, await _settings.GetApiKeyAsync(), cancellationToken);
        var fallback = await ProbeOpenAiAsync("OpenZen", settings.OpenZenApiBaseUrl, settings.OpenZenModel,
            settings.OpenZenEnabled, await _settings.GetOpenZenApiKeyAsync(), cancellationToken);
        var rows = await _db.SystemSettings.AsNoTracking().Where(x => x.Category == "Ollama")
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var ollamaEnabled = !rows.TryGetValue("Enabled", out var raw) || !bool.TryParse(raw, out var parsed) || parsed;
        var ollama = await ProbeAsync("Ollama", rows.GetValueOrDefault("Model") ?? "qwen2.5:7b-instruct",
            ollamaEnabled, true, new HttpRequestMessage(HttpMethod.Get,
                $"{(rows.GetValueOrDefault("BaseUrl") ?? "http://localhost:11434").TrimEnd('/')}/api/tags"), cancellationToken);
        return new
        {
            configuredProvider = primary.Provider,
            configuredModel = primary.Model,
            primary,
            fallback,
            fallbackReady = fallback.Enabled && fallback.CredentialConfigured && fallback.Reachable,
            local = ollama
        };
    }

    private async Task<ProviderHealth> ProbeOpenAiAsync(string provider, string baseUrl, string model,
        bool enabled, string? apiKey, CancellationToken cancellationToken)
    {
        if (!enabled) return new(provider, model, false, !string.IsNullOrWhiteSpace(apiKey), false, "disabled", null, null);
        if (string.IsNullOrWhiteSpace(apiKey)) return new(provider, model, true, false, false, "not_configured", null, null);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await ProbeAsync(provider, model, true, true, request, cancellationToken);
    }

    private async Task<ProviderHealth> ProbeAsync(string provider, string model, bool enabled,
        bool credentialConfigured, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!enabled) return new(provider, model, false, credentialConfigured, false, "disabled", null, null);
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _clients.CreateClient("ai-health").SendAsync(request, timeout.Token);
            return new(provider, model, true, credentialConfigured, response.IsSuccessStatusCode,
                response.IsSuccessStatusCode ? "reachable" : "unreachable", (int)response.StatusCode, sw.ElapsedMilliseconds);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(provider, model, true, credentialConfigured, false, "unreachable", null, sw.ElapsedMilliseconds);
        }
    }
}

public sealed record ProviderHealth(string Provider, string Model, bool Enabled,
    bool CredentialConfigured, bool Reachable, string State, int? StatusCode, long? LatencyMs);
