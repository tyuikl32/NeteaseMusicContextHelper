using System;
using System.Threading;
using Avalonia.Threading;

namespace RealtimeAssistant.Services;

public sealed class DispatcherUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher dispatcher = Dispatcher.UIThread;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }
}
