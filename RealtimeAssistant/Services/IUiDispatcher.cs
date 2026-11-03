using System;

namespace RealtimeAssistant.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
