using System;
using System.Collections.Generic;

namespace RealtimeAssistant.Models;

public sealed class AppOptions
{
    public int Version { get; set; } = 1;

    public ChatConfig Chat { get; set; } = new();

    public AssistantConfig Assistant { get; set; } = new();

    public WebSearchConfig WebSearch { get; set; } = new();

    public static AppOptions CreateDefault() => new();

    public void Normalize()
    {
        if (Chat is null)
            Chat = new ChatConfig();

        Chat.Normalize();

        if (Assistant is null)
            Assistant = new AssistantConfig();

        if (Assistant.Language is not ("zh-CN" or "en-US"))
            Assistant.Language = "zh-CN";

        WebSearch ??= new WebSearchConfig();
    }
}

public sealed class AssistantConfig
{
    public bool ColdMode { get; set; }

    public string Language { get; set; } = "zh-CN";
}

public sealed class WebSearchConfig
{
    public bool Enabled { get; set; }

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ChatConfig
{
    public string ActiveProvider { get; set; } = ChatProviderConfig.DefaultName;

    public List<ChatProviderConfig> Providers { get; set; } = [ChatProviderConfig.CreateDefault()];

    public string UserPrompt { get; set; } = SongPrompt.DefaultUserPrompt;

    public void Normalize()
    {
        Providers ??= [];
        Providers.RemoveAll(provider => provider is null);

        if (Providers.Count == 0)
            Providers.Add(ChatProviderConfig.CreateDefault());

        if (string.IsNullOrWhiteSpace(UserPrompt))
            UserPrompt = SongPrompt.DefaultUserPrompt;
    }
}

public sealed class ChatProviderConfig
{
    public const string DefaultName = "DeepSeek";

    public const string ApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";

    private const string DemoApiKey = "";

    public string Name { get; set; } = DefaultName;

    public string Endpoint { get; set; } = "https://api.deepseek.com/v1";

    public string Model { get; set; } = "deepseek-flash";

    public string ApiKey { get; set; } = string.Empty;

    public bool BuiltInSearchEnabled { get; set; }

    public static ChatProviderConfig CreateDefault() => new()
    {
        ApiKey = ResolveInitialApiKey(),
    };

    private static string ResolveInitialApiKey()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? DemoApiKey : fromEnvironment;
    }

    public override string ToString() => $"{Name} ({Model}@{Endpoint})";
}
