using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RealtimeAssistant.Models;
using RealtimeAssistant.Localization;

namespace RealtimeAssistant.Services;

public sealed class OptionsService : IOptionsService
{
    public const string FileName = "options.json";

    public static string DefaultDirectory => Path.Combine(
        Environment.CurrentDirectory,
        "RealtimeAssistant");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppOptions _current = AppOptions.CreateDefault();

    public OptionsService(string? path = null)
    {
        OptionsPath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(DefaultDirectory, FileName)
            : path!;
    }

    public string OptionsPath { get; }

    public AppOptions Current => Volatile.Read(ref _current);

    public bool IsLoaded { get; private set; }

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        bool changed;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(OptionsPath))
            {
                _current = AppOptions.CreateDefault();
                await WriteAsync(_current, cancellationToken).ConfigureAwait(false);
                IsLoaded = true;
                changed = true;
            }
            else
            {
                string json = await File
                    .ReadAllTextAsync(OptionsPath, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    AppOptions? loaded = JsonSerializer.Deserialize<AppOptions>(json, SerializerOptions)
                        ?? throw new JsonException(LocalizationService.Instance["EmptyOptions"]);

                    loaded.Normalize();
                    _current = loaded;
                    IsLoaded = true;
                    changed = true;
                }
                catch (JsonException)
                {
                    QuarantineBrokenFile();

                    _current = AppOptions.CreateDefault();
                    await WriteAsync(_current, cancellationToken).ConfigureAwait(false);
                    IsLoaded = true;
                    changed = true;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteAsync(Current, cancellationToken).ConfigureAwait(false);
            IsLoaded = true;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ChatProviderConfig GetActiveChatProvider()
    {
        ChatConfig chat = Current.Chat;

        if (chat.Providers.Count == 0)
        {
            throw new InvalidOperationException(
                LocalizationService.Instance.Format("NoProviders", OptionsPath));
        }

        ChatProviderConfig? active = chat.Providers.FirstOrDefault(
            provider => string.Equals(provider.Name, chat.ActiveProvider, StringComparison.OrdinalIgnoreCase));

        return active ?? chat.Providers[0];
    }

    private async Task WriteAsync(AppOptions options, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(OptionsPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(options, SerializerOptions);

        string temporaryPath = OptionsPath + ".tmp";

        await File
            .WriteAllTextAsync(temporaryPath, json, cancellationToken)
            .ConfigureAwait(false);

        File.Move(temporaryPath, OptionsPath, overwrite: true);
    }

    private void QuarantineBrokenFile()
    {
        try
        {
            string backupPath = $"{OptionsPath}.invalid-{DateTime.Now:yyyyMMddHHmmss}.json";
            File.Move(OptionsPath, backupPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
