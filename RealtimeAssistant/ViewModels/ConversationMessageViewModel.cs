using CommunityToolkit.Mvvm.ComponentModel;
using RealtimeAssistant.Localization;

namespace RealtimeAssistant.ViewModels;

public sealed class ConversationMessageViewModel(
    string author,
    string content,
    bool isMarkdown = false,
    string? heading = null) : ObservableObject
{
    public string Author => LocalizationService.Instance[author];
    public string Content { get; } = content;
    public bool IsMarkdown { get; } = isMarkdown;
    public string? Heading { get; } = heading;
    public bool IsPlainText => !IsMarkdown;
    public bool HasHeading => !string.IsNullOrWhiteSpace(Heading);

    public void RefreshLanguage() => OnPropertyChanged(nameof(Author));
}
