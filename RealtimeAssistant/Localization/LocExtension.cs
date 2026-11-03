using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace RealtimeAssistant.Localization;

public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };
}
