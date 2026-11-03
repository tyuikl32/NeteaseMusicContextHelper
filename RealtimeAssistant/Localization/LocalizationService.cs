using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace RealtimeAssistant.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "RealtimeAssistant.Localization.Strings", typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("zh-CN");

    public static LocalizationService Instance { get; } = new();

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English"),
    ];

    public string Language => _culture.Name;

    public string this[string key] => Resources.GetString(key, _culture) ?? key;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string Format(string key, params object[] arguments)
        => string.Format(_culture, this[key], arguments);

    public void SetLanguage(string? language)
    {
        string code = language == "en-US" ? "en-US" : "zh-CN";
        if (Language == code)
            return;

        _culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record LanguageOption(string Code, string Name);
