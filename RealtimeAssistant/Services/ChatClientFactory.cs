using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using RealtimeAssistant.Models;
using RealtimeAssistant.Localization;

namespace RealtimeAssistant.Services;

public static class ChatClientFactory
{
    public static IChatClient CreateDeepSeek(ChatProviderConfig provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException(
                LocalizationService.Instance.Format("MissingProviderKey", provider.Name));
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            throw new InvalidOperationException(
                LocalizationService.Instance.Format("MissingProviderModel", provider.Name));
        }

        if (!Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException(
                LocalizationService.Instance.Format("InvalidProviderEndpoint", provider.Name, provider.Endpoint));
        }

        OpenAIClientOptions options = new()
        {
            Endpoint = endpoint,
        };

        ChatClient chatClient = new(
            model: provider.Model,
            credential: new ApiKeyCredential(provider.ApiKey),
            options: options);

        return chatClient.AsIChatClient();
    }
}
