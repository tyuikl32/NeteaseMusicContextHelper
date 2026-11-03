using System;

namespace RealtimeAssistant.Models;

public static class AssistantOptions
{
    public static TimeSpan ConfirmationAbortWindow { get; } = TimeSpan.FromSeconds(60);
    public static TimeSpan NormalAbortWindow { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan CountdownTick { get; } = TimeSpan.FromSeconds(1);
}
