using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RealtimeAssistant.Localization;

namespace RealtimeAssistant.Services;

public sealed class WebSearchService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public WebSearchService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<WebSearchResult> SearchAsync(
        string query,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            return new(LocalizationService.Instance["InvalidSearchQuery"], []);

        if (string.IsNullOrWhiteSpace(apiKey))
            return new(LocalizationService.Instance["MissingSearchKey"], []);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "https://api.tavily.com/search");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Content = JsonContent.Create(new
            {
                query = query.Trim(),
                search_depth = "basic",
                max_results = 5,
                include_answer = false,
                include_raw_content = false,
                include_images = false,
            });

            using HttpResponseMessage response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(LocalizationService.Instance.Format("SearchHttpError", (int)response.StatusCode), []);

            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false),
                cancellationToken: timeout.Token).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("results", out JsonElement results) ||
                results.ValueKind != JsonValueKind.Array)
                return new(LocalizationService.Instance["InvalidSearchResult"], []);

            List<WebSearchHit> hits = [];
            foreach (JsonElement item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string url = GetString(item, "url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    continue;

                hits.Add(new(
                    Limit(GetString(item, "title"), 200),
                    url,
                    Limit(GetString(item, "content"), 1500)));

                if (hits.Count == 5)
                    break;
            }

            return new(null, hits);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LocalizationService.Instance["SearchTimeout"], []);
        }
        catch (HttpRequestException)
        {
            return new(LocalizationService.Instance["SearchConnectionFailed"], []);
        }
        catch (JsonException)
        {
            return new(LocalizationService.Instance["InvalidSearchResult"], []);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private static string GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Limit(string value, int length)
        => value.Length <= length ? value : value[..length];
}

public sealed record WebSearchResult(string? Error, IReadOnlyList<WebSearchHit> Results);

public sealed record WebSearchHit(string Title, string Url, string Content);
