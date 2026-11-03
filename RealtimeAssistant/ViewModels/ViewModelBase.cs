using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RealtimeAssistant.Services;

namespace RealtimeAssistant.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase(IUiDispatcher? uiDispatcher = null)
    {
        UiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
    }

    protected IUiDispatcher UiDispatcher { get; }

    protected void RunOnUiThread(Action action) => UiDispatcher.Post(action);
}
