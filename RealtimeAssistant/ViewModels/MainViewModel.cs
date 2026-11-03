using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using NeteaseMusicCollecter;
using NeteaseMusicCollecter.Interfaces;
using NeteaseMusicCollecter.Windows;
using RealtimeAssistant.Models;
using RealtimeAssistant.Services;
using RealtimeAssistant.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RealtimeAssistant.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private static LocalizationService Text => LocalizationService.Instance;

    private readonly INMCollector _collector;
    private readonly IChatService _chatService;
    private readonly IOptionsService? _optionsService;

    private readonly List<ChatMessage> _messages = new();

    private CancellationTokenSource? _operationCts;
    private TaskCompletionSource<bool>? _confirmation;

    private int _sessionVersion;

    private bool _started;
    private bool _disposed;
    private SongInfo? _currentSong;
    private string? _statusKey;
    private object[] _statusArguments = [];

    public event EventHandler? SettingsRequested;
    public MainViewModel()
        : this(new WindowsNMCollector(), new DeepSeekChatService(), new DispatcherUiDispatcher())
    {
    }
    public MainViewModel(
        INMCollector collector,
        IChatService chatService,
        IUiDispatcher uiDispatcher,
        IOptionsService? optionsService = null)
        : base(uiDispatcher)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _optionsService = optionsService;
        Text.LanguageChanged += OnLanguageChanged;
    }


    [ObservableProperty]
    private string _songTitle = Text["WaitingForSong"];


    [ObservableProperty]
    private string _artistName = string.Empty;

    public ObservableCollection<ConversationMessageViewModel> Conversation { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendQuestionCommand))]
    private string _questionInput = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputMode))]
    [NotifyPropertyChangedFor(nameof(IsRunningMode))]
    [NotifyPropertyChangedFor(nameof(IsConfirmMode))]
    private BottomPanelMode _currentBottomPanelMode = BottomPanelMode.Input;

    public bool IsInputMode => CurrentBottomPanelMode == BottomPanelMode.Input;

    public bool IsRunningMode => CurrentBottomPanelMode == BottomPanelMode.Running;

    public bool IsConfirmMode => CurrentBottomPanelMode == BottomPanelMode.Confirm;

    public IOptionsService? OptionsService => _optionsService;

    [ObservableProperty]
    private bool _isTopmost;


    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        _started = true;

        if (_optionsService is not null)
        {
            try
            {
                await _optionsService.LoadAsync(cancellationToken);
                Text.SetLanguage(_optionsService.Current.Assistant.Language);
            }
            catch (Exception ex)
            {
                ReportError("LoadOptionsFailed", ex);
            }
        }

        _collector.SongChanged += OnSongChanged;
        _collector.ErrorOccurred += OnCollectorError;

        try
        {
            await _collector.InitializeAsync(cancellationToken);
            await _collector.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReportError("CollectorStartFailed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        Text.LanguageChanged -= OnLanguageChanged;

        _collector.SongChanged -= OnSongChanged;
        _collector.ErrorOccurred -= OnCollectorError;

        TryCancel(Interlocked.Exchange(ref _operationCts, null));
        Interlocked.Exchange(ref _confirmation, null)?.TrySetCanceled();

        try
        {
            await _chatService.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await _collector.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    [RelayCommand]
    private void Abort()
    {
        TryCancel(Volatile.Read(ref _operationCts));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ConfirmAnalysis()
    {
        _confirmation?.TrySetResult(true);
    }

    [RelayCommand]
    private void DeclineAnalysis()
    {
        _confirmation?.TrySetResult(false);
    }


  
    [RelayCommand(CanExecute = nameof(CanSendQuestion))]
    private async Task SendQuestionAsync()
    {
        string question = QuestionInput.Trim();

        if (question.Length == 0 || _messages.Count == 0)
            return;

        int version = Volatile.Read(ref _sessionVersion);

        QuestionInput = string.Empty;

        _messages.Add(new ChatMessage(ChatRole.User, question));
        AppendMessage("You", question);

        CurrentBottomPanelMode = BottomPanelMode.Running;
        SetStatus("WaitingForReply");

        await RunOperationAsync(
            async cancellationToken =>
            {
                ChatMessage[] request = _messages.ToArray();

                ChatResponse response = await _chatService.CompleteAsync(request, cancellationToken);

                if (!IsCurrent(version))
                    return;

                _messages.AddRange(response.Messages);
                AppendMessage("Assistant", FormatAnswer(response), true);

                EnterInputMode();
            },
            version);
    }

    private bool CanSendQuestion()
        => !_disposed
           && _messages.Count > 0
           && !string.IsNullOrWhiteSpace(QuestionInput)
           && CurrentBottomPanelMode != BottomPanelMode.Running;

    private void OnSongChanged(object? sender, SongChangedEventArgs e)
    {
        SongInfo song = e.Song;
        RunOnUiThread(() => StartSongSession(song));
    }

    private void StartSongSession(SongInfo song)
    {
        if (_disposed)
            return;

        int version = Interlocked.Increment(ref _sessionVersion);

        _currentSong = song;
        SongTitle = string.IsNullOrWhiteSpace(song.Name) ? Text["UnnamedSong"] : song.Name;
        ArtistName = song.Artists.Count == 0
            ? string.Empty
            : string.Join(" / ", song.Artists.Select(artist => artist.Name));

        _messages.Clear();
        _messages.Add(new ChatMessage(ChatRole.System, SongPrompt.SystemPrompt));
        _messages.Add(new ChatMessage(
            ChatRole.User,
            SongPrompt.BuildInitialPrompt(song, _optionsService?.Current.Chat.UserPrompt)));

        //QuestionInput = string.Empty;
        bool coldMode = _optionsService?.Current.Assistant.ColdMode ?? false;
        CurrentBottomPanelMode = coldMode ? BottomPanelMode.Confirm : BottomPanelMode.Running;
        SetCountdown(coldMode ? "ConfirmationCountdown" : "AnalysisCountdown",
            coldMode ? AssistantOptions.ConfirmationAbortWindow : AssistantOptions.NormalAbortWindow);

        SendQuestionCommand.NotifyCanExecuteChanged();

        _ = AnalyzeSongAsync(version,song);
    }

    private async Task AnalyzeSongAsync(int version,SongInfo song)
    {
        await RunOperationAsync(
            async cancellationToken =>
            {
                bool coldMode = _optionsService?.Current.Assistant.ColdMode ?? false;
                if (coldMode)
                {
                    bool confirmed = await WaitForConfirmationAsync(version, cancellationToken);
                    if (!confirmed)
                    {
                        if (IsCurrent(version))
                            EnterInputMode();

                        return;
                    }
                }
                else
                {
                    await RunAbortWindowAsync(version, cancellationToken);
                }

                if (!IsCurrent(version))
                    return;

                CurrentBottomPanelMode = BottomPanelMode.Running;
                SetStatus("WaitingForReply");

                ChatMessage[] request = _messages.ToArray();

                ChatResponse response = await _chatService.CompleteAsync(request, cancellationToken);
                if (!IsCurrent(version))
                    return;

                Conversation.Clear();
                _messages.AddRange(response.Messages);
                AppendMessage("Assistant", FormatAnswer(response), true,
                    $"{song.Name} - {string.Join('/', song.Artists.Select(artist => artist.Name))}");

                EnterInputMode();
            },
            version);
    }


    private async Task RunAbortWindowAsync(int version, CancellationToken cancellationToken)
    {
        TimeSpan remaining = AssistantOptions.NormalAbortWindow;

        while (remaining > TimeSpan.Zero)
        {
            if (!IsCurrent(version))
                return;

            SetCountdown("AnalysisCountdown", remaining);

            TimeSpan step = remaining < AssistantOptions.CountdownTick
                ? remaining
                : AssistantOptions.CountdownTick;

            await Task.Delay(step, cancellationToken);

            remaining -= step;
        }
    }

    private void SetCountdown(string key, TimeSpan remaining)
        => SetStatus(key, (int)Math.Ceiling(remaining.TotalSeconds));

    private void SetStatus(string key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        StatusText = Text.Format(key, arguments);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_disposed)
                return;

            if (_currentSong is null)
                SongTitle = Text["WaitingForSong"];
            else if (string.IsNullOrWhiteSpace(_currentSong.Name))
                SongTitle = Text["UnnamedSong"];

            if (_statusKey is not null)
                StatusText = Text.Format(_statusKey, _statusArguments);

            foreach (ConversationMessageViewModel message in Conversation)
                message.RefreshLanguage();
        });
    }

    private async Task<bool> WaitForConfirmationAsync(int version, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool>? previous = Interlocked.Exchange(ref _confirmation, confirmation);
        previous?.TrySetCanceled();

        try
        {
            TimeSpan remaining = AssistantOptions.ConfirmationAbortWindow;

            while (remaining > TimeSpan.Zero)
            {
                if (!IsCurrent(version))
                    return false;

                SetCountdown("ConfirmationCountdown", remaining);

                TimeSpan step = remaining < AssistantOptions.CountdownTick
                    ? remaining
                    : AssistantOptions.CountdownTick;
                Task delay = Task.Delay(step, cancellationToken);
                Task completed = await Task.WhenAny(confirmation.Task, delay);

                if (completed == confirmation.Task)
                    return await confirmation.Task;

                await delay;
                remaining -= step;
            }

            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _confirmation, null, confirmation);
        }
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, int version)
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _operationCts, cts);

        TryCancel(previous);

        try
        {
            await operation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            OnOperationCancelled(version);
        }
        catch (Exception ex)
        {
            OnOperationFailed(version, ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _operationCts, null, cts);
            cts.Dispose();
        }
    }
   
    private void OnOperationCancelled(int version)
    {
        if (!IsCurrent(version))
            return;

        AppendMessage("Notice", Text["Aborted"]);
        EnterInputMode();
    }

    private void OnOperationFailed(int version, Exception exception)
    {
        if (!IsCurrent(version))
            return;

        AppendMessage("Notice", $"⚠ {exception.Message}");
        EnterInputMode();
    }

    private void EnterInputMode()
    {
        _statusKey = null;
        _statusArguments = [];
        StatusText = string.Empty;
        CurrentBottomPanelMode = BottomPanelMode.Input;
        SendQuestionCommand.NotifyCanExecuteChanged();
    }

    private void OnCollectorError(object? sender, Exception exception)
    {
        RunOnUiThread(() => ReportError("CollectorError", exception));
    }

    private void ReportError(string scope, Exception exception)
    {
        if (_disposed)
            return;

        AppendMessage("Notice", $"⚠ {Text.Format("ScopedError", Text[scope], exception.Message)}");

        if (CurrentBottomPanelMode == BottomPanelMode.Running)
            EnterInputMode();
    }

    private bool IsCurrent(int version)
        => !_disposed && Volatile.Read(ref _sessionVersion) == version;

    private static void TryCancel(CancellationTokenSource? source)
    {
        if (source is null)
            return;

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string FormatAnswer(ChatResponse response)
    {
        ChatMessage? message = response.Messages.LastOrDefault(item => item.Role == ChatRole.Assistant);
        if (message is null || string.IsNullOrWhiteSpace(message.Text))
            return Text["EmptyAnswer"];

        StringBuilder answer = new(message.Text);
        HashSet<string> sources = new(StringComparer.Ordinal);
        foreach (AIContent content in message.Contents)
        {
            if (content.Annotations is null)
                continue;

            foreach (CitationAnnotation citation in content.Annotations.OfType<CitationAnnotation>())
            {
                string? url = citation.Url?.ToString();
                if (string.IsNullOrEmpty(url) || message.Text.Contains(url, StringComparison.Ordinal) || !sources.Add(url))
                    continue;

                answer.AppendLine().AppendLine().Append(citation.Title).Append(" <").Append(url).Append('>');
            }
        }

        return answer.ToString();
    }

    private void AppendMessage(string author, string content, bool isMarkdown = false, string? heading = null)
    {
        Conversation.Add(new ConversationMessageViewModel(author, content, isMarkdown, heading));
    }
}
