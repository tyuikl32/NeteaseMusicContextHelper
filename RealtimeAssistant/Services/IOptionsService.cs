using System;
using System.Threading;
using System.Threading.Tasks;
using RealtimeAssistant.Models;

namespace RealtimeAssistant.Services;

public interface IOptionsService
{
    string OptionsPath { get; }

    AppOptions Current { get; }

    bool IsLoaded { get; }

    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
    ChatProviderConfig GetActiveChatProvider();
}
