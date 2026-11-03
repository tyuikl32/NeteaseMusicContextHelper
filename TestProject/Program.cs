using NeteaseMusicCollecter;
using NeteaseMusicCollecter.Windows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Formats.Tar;


public static class LLMClientFactory
{
    public static IChatClient Create()
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.deepseek.com/v1")
        };

        var chatClient = new ChatClient(
            model: "deepseek-flash",
            credential: new ApiKeyCredential(""),
            options: options
        );

        return chatClient.AsIChatClient();
    }
}
class Program
{
    static IChatClient client = LLMClientFactory.Create();
    static CancellationTokenSource? pendingMusicCts;
    static readonly object ctsLock = new();
    public static void HandleMusic(object? sender, SongChangedEventArgs e)
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(
            ref pendingMusicCts,
            newCts
        );
        oldCts?.Cancel();

        Console.WriteLine(
                $"{e.Song.Name} - {string.Join('/', e.Song.Artists.Select(a => a.Name))}" +
                $"（{e.Song.Album?.Name}，{e.Song.Duration:mm\\:ss}）");
        Task.Run(async () => await RunSongTaskAsync(e.Song, newCts));
    }
    private static async Task RunSongTaskAsync(
    SongInfo song,
    CancellationTokenSource cts)
    {
        try
        {
            await PassToLLM(song, cts.Token);
        }
        finally
        {
            lock (ctsLock)
            {
                if (ReferenceEquals(pendingMusicCts, cts))
                    pendingMusicCts = null;

                cts.Dispose();
            }
        }
    }
    public static async Task PassToLLM(
    SongInfo s,
    CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(5000, cancellationToken);
            string prompt =
          $"这首乐曲的名字是 {s.Name} 由作曲家 " +
          $"{string.Join('/', s.Artists.Select(a => a.Name))} 制作" +
          $"来自专辑 {s.Album?.Name}" +
          "请合理运用搜索功能,尽快地以简短的回复给出这首歌的创作背景，曲风，以及相关上下文（例如荣誉、收录等，如果没有就不用提及）。\n" +
          "如果有（例如游戏剧情配乐或同人音乐），还应给出歌曲背景，歌曲表达等。但如果没有，则不用提及。" +
          "按照以下格式尽量简短回复：\n" +
          "[创作背景]：\n" +
          "[曲风]：\n" +
          "[其他信息]：";
            Console.WriteLine("冷静期已过，正在等待回复...");
            var response = await client.GetResponseAsync(
            [
                new Microsoft.Extensions.AI.ChatMessage(
            ChatRole.System,
            "You are a music expert." +
            "You should give a response that is brief, without losing professionality."
        ),
        new Microsoft.Extensions.AI.ChatMessage(
            ChatRole.User,
            prompt
        )
            ]
            );

            Console.WriteLine(response.Text);
        }
        catch (OperationCanceledException)
        {
            return;
        }
      
    }
    static async Task  Main(string[] args)
    {
        BaseNMCollector collector = new WindowsNMCollector();
        await collector.InitializeAsync();

        collector.SongChanged += HandleMusic;

        await collector.StartAsync();

        Console.ReadKey();

    }

}

