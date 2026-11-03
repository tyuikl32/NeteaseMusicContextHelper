using System;

namespace RealtimeAssistant.Services;

public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();

    public void Post(Action action) => action();
}
