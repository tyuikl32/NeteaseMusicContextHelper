using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealtimeAssistant.Models;
using RealtimeAssistant.Services;
using RealtimeAssistant.Localization;
using System.Collections.Generic;

namespace RealtimeAssistant.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IOptionsService _optionsService;

    public SettingsViewModel(IOptionsService optionsService)
    {
        _optionsService = optionsService;
        AppOptions options = optionsService.Current;
        Providers = new ObservableCollection<ChatProviderConfig>(
            options.Chat.Providers.Select(CloneProvider));
        ColdMode = options.Assistant.ColdMode;
        SelectedLanguage = Languages.FirstOrDefault(language => language.Code == options.Assistant.Language)
            ?? Languages[0];
        UserPrompt = options.Chat.UserPrompt;
        WebSearchEnabled = options.WebSearch.Enabled;
        WebSearchApiKey = options.WebSearch.ApiKey;
        SelectedProvider = Providers.FirstOrDefault(
            provider => string.Equals(
                provider.Name,
                options.Chat.ActiveProvider,
                StringComparison.OrdinalIgnoreCase)) ?? Providers.FirstOrDefault();
    }

    public ObservableCollection<ChatProviderConfig> Providers { get; }

    public IReadOnlyList<LanguageOption> Languages => LocalizationService.Languages;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = LocalizationService.Languages[0];

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private ChatProviderConfig? _selectedProvider;

    [ObservableProperty]
    private string _providerName = string.Empty;

    [ObservableProperty]
    private string _endpoint = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _builtInSearchEnabled;

    [ObservableProperty]
    private bool _webSearchEnabled;

    [ObservableProperty]
    private string _webSearchApiKey = string.Empty;

    [ObservableProperty]
    private string _userPrompt = string.Empty;

    [ObservableProperty]
    private bool _coldMode;

    [ObservableProperty]
    private string _errorText = string.Empty;

    partial void OnSelectedProviderChanged(ChatProviderConfig? value)
    {
        if (value is null)
            return;

        ProviderName = value.Name;
        Endpoint = value.Endpoint;
        Model = value.Model;
        ApiKey = value.ApiKey;
        BuiltInSearchEnabled = value.BuiltInSearchEnabled;
    }

    partial void OnBuiltInSearchEnabledChanged(bool value)
    {
        if (SelectedProvider is not null)
            SelectedProvider.BuiltInSearchEnabled = value;
    }

    [RelayCommand]
    private void AddProvider()
    {
        ChatProviderConfig provider = new()
        {
            Name = LocalizationService.Instance.Format("NewProviderName", Providers.Count + 1),
            Endpoint = "https://api.openai.com/v1",
            Model = string.Empty,
            ApiKey = string.Empty,
        };
        Providers.Add(provider);
        SelectedProvider = provider;
        ErrorText = string.Empty;
        RemoveProviderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveProvider))]
    private void RemoveProvider()
    {
        if (SelectedProvider is null || Providers.Count <= 1)
            return;

        int index = Providers.IndexOf(SelectedProvider);
        Providers.Remove(SelectedProvider);
        SelectedProvider = Providers[Math.Min(index, Providers.Count - 1)];
        RemoveProviderCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveProvider() => Providers.Count > 1;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProvider is null)
            return;

        string name = ProviderName.Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Model))
        {
            ErrorText = LocalizationService.Instance["ProviderFieldsRequired"];
            return;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            ErrorText = LocalizationService.Instance["InvalidEndpoint"];
            return;
        }

        if (Providers.Any(provider => provider != SelectedProvider &&
            string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = LocalizationService.Instance["DuplicateProviderName"];
            return;
        }

        if (WebSearchEnabled && string.IsNullOrWhiteSpace(WebSearchApiKey))
        {
            ErrorText = LocalizationService.Instance["SearchKeyRequired"];
            return;
        }

        SelectedProvider.Name = name;
        SelectedProvider.Endpoint = Endpoint.Trim();
        SelectedProvider.Model = Model.Trim();
        SelectedProvider.ApiKey = ApiKey.Trim();
        SelectedProvider.BuiltInSearchEnabled = BuiltInSearchEnabled;

        AppOptions options = _optionsService.Current;
        options.Chat.Providers = Providers.Select(CloneProvider).ToList();
        options.Chat.ActiveProvider = name;
        options.Chat.UserPrompt = UserPrompt;
        options.Assistant.ColdMode = ColdMode;
        string previousLanguage = options.Assistant.Language;
        options.Assistant.Language = SelectedLanguage.Code;
        options.WebSearch.Enabled = WebSearchEnabled;
        options.WebSearch.ApiKey = WebSearchApiKey.Trim();

        try
        {
            await _optionsService.SaveAsync();
            LocalizationService.Instance.SetLanguage(SelectedLanguage.Code);
            ErrorText = string.Empty;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            options.Assistant.Language = previousLanguage;
            ErrorText = LocalizationService.Instance.Format("SaveFailed", exception.Message);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static ChatProviderConfig CloneProvider(ChatProviderConfig provider) => new()
    {
        Name = provider.Name,
        Endpoint = provider.Endpoint,
        Model = provider.Model,
        ApiKey = provider.ApiKey,
        BuiltInSearchEnabled = provider.BuiltInSearchEnabled,
    };
}
