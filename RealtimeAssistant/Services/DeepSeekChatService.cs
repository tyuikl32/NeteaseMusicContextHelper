using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using RealtimeAssistant.Models;
using RealtimeAssistant.Localization;

namespace RealtimeAssistant.Services;

public sealed class DeepSeekChatService : IChatService
{
    private readonly Func<IChatClient> _clientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOptionsService? _optionsService;
    private readonly WebSearchService _webSearchService;

    private IChatClient? _client;
    private volatile bool _clientStale;
    private bool _disposed;

    public DeepSeekChatService()
        : this(() => ChatClientFactory.CreateDeepSeek(ChatProviderDefaults()), null)
    {
    }


    public DeepSeekChatService(IOptionsService optionsService)
        : this(
            () => ChatClientFactory.CreateDeepSeek(optionsService.GetActiveChatProvider()),
            optionsService)
    {
    }

    public DeepSeekChatService(
        Func<IChatClient> clientFactory,
        IOptionsService? optionsService = null,
        WebSearchService? webSearchService = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _optionsService = optionsService;
        _webSearchService = webSearchService ?? new WebSearchService();

        if (optionsService is not null)
            optionsService.Changed += OnOptionsChanged;
    }

    public bool HasClient => _client is not null;

    public async Task<ChatResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_clientStale)
            {
                _client?.Dispose();
                _client = null;
                _clientStale = false;
            }

            IChatClient client = _client ??= _clientFactory()
                .AsBuilder()
                .UseFunctionInvocation(configure: invocation => invocation.MaximumIterationsPerRequest = 4)
                .Build();

            ChatOptions options = CreateChatOptions();

            ChatResponse response = await client
                .GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_optionsService is not null)
            _optionsService.Changed -= OnOptionsChanged;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;

            _client?.Dispose();
            _client = null;
            _webSearchService.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnOptionsChanged(object? sender, EventArgs e) => _clientStale = true;

    private ChatOptions CreateChatOptions()
    {
        ChatOptions options = new();

        if (_optionsService?.GetActiveChatProvider().BuiltInSearchEnabled == true)
            (options.Tools ??= []).Add(new HostedWebSearchTool());

        if (_optionsService?.Current.WebSearch.Enabled == true)
        {
            string apiKey = _optionsService.Current.WebSearch.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(LocalizationService.Instance["SearchKeyRequired"]);

            int searches = 0;
            async Task<WebSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Increment(ref searches) > 3)
                    return new(LocalizationService.Instance["SearchLimit"], []);

                return await _webSearchService.SearchAsync(query, apiKey, cancellationToken).ConfigureAwait(false);
            }

            (options.Tools ??= []).Add(AIFunctionFactory.Create(
                SearchAsync,
                "web_search",
                "Search the internet for relevant facts and sources. Returns up to five titles, URLs and excerpts. Search selectively; at most three searches per answer."));
        }

        return options;
    }

    private static Models.ChatProviderConfig ChatProviderDefaults() => Models.ChatProviderConfig.CreateDefault();
}
