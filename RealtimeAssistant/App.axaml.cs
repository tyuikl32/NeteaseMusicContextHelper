using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NeteaseMusicCollecter.Windows;
using RealtimeAssistant.Services;
using RealtimeAssistant.ViewModels;
using RealtimeAssistant.Views;

namespace RealtimeAssistant;

public partial class App : Application
{
    private OptionsService? _optionsService;
    private MainViewModel? _viewModel;
    private bool _shutdownHandled;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _optionsService = new OptionsService();

            _viewModel = new MainViewModel(
                new WindowsNMCollector(),
                new DeepSeekChatService(_optionsService),
                new DispatcherUiDispatcher(),
                _optionsService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };

            desktop.ShutdownRequested += OnShutdownRequested;

            _ = _viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnShutdownRequested(
        object? sender,
        ShutdownRequestedEventArgs e)
    {
        if (sender is not IClassicDesktopStyleApplicationLifetime desktop || _shutdownHandled)
            return;

        _shutdownHandled = true;

        e.Cancel = true;

        try
        {
            if (_viewModel is not null)
                await _viewModel.DisposeAsync();
        }
        catch (Exception)
        {
        }
        finally
        {
            desktop.Shutdown();
        }
    }
}
