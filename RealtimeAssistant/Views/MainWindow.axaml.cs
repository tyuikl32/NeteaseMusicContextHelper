using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Markdown.Avalonia;
using System;
using System.Collections.Specialized;
using RealtimeAssistant.ViewModels;
namespace RealtimeAssistant.Views;
using Avalonia.Interactivity;
using RealtimeAssistant.Services;

public partial class MainWindow : Window
{
    private MainViewModel? _subscribedViewModel;
    private bool _followConversation = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        QuestionInputBox.AddHandler(InputElement.KeyDownEvent, InputBox_KeyDown,
            RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.SettingsRequested -= OnSettingsRequested;
            _subscribedViewModel.Conversation.CollectionChanged -= OnConversationChanged;
        }

        _subscribedViewModel = DataContext as MainViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.SettingsRequested += OnSettingsRequested;
            _subscribedViewModel.Conversation.CollectionChanged += OnConversationChanged;
        }
        _followConversation = true;
        ScrollToLatest();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is null)
            return;

        _subscribedViewModel.SettingsRequested -= OnSettingsRequested;
        _subscribedViewModel.Conversation.CollectionChanged -= OnConversationChanged;
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _followConversation = true;

        ScrollToLatest();
    }

    private void OnConversationScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0)
            ScrollToLatest();
        else if (e.OffsetDelta.Y != 0)
            _followConversation = ConversationScrollViewer.Offset.Y >=
                ConversationScrollViewer.Extent.Height - ConversationScrollViewer.Viewport.Height - 16;
    }

    private void ScrollToLatest()
    {
        if (_followConversation)
            Dispatcher.UIThread.Post(() =>
            {
                if (_followConversation)
                    ConversationScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Loaded);
    }

    private void OnMarkdownLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is MarkdownScrollViewer { Engine: Markdown.Avalonia.Markdown engine })
            engine.HyperlinkCommand = new RelayCommand<string>(OpenLink);
    }

    private async void OpenLink(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            await Launcher.LaunchUriAsync(uri);
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel { OptionsService: IOptionsService optionsService })
            return;

        await new SettingsWindow(new SettingsViewModel(optionsService)).ShowDialog(this);
    }
    
    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;

        if (DataContext is RealtimeAssistant.ViewModels.MainViewModel vm &&
            vm.SendQuestionCommand.CanExecute(null))
        {
            vm.SendQuestionCommand.Execute(null);
        }
    }
}
