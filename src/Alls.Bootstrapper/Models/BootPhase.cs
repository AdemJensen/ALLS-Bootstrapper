using System.Text.Json.Serialization;

namespace Alls.Bootstrapper.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BootPhaseAction
{
    Continue,
    Launch,
    Exit
}

public sealed class BootPhase
{
    public int Step { get; set; }

    public string MessageKey { get; set; } = string.Empty;

    public int DurationMs { get; set; } = 1500;

    public BootPhaseAction Action { get; set; } = BootPhaseAction.Continue;
}
