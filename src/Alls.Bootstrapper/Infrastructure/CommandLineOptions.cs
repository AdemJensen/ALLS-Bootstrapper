using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Infrastructure;

internal sealed record CommandLineOptions(
    string? ConfigPath,
    string? Language,
    bool Preview,
    bool Windowed)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        string? configPath = null;
        string? language = null;
        var preview = false;
        var windowed = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index].ToLowerInvariant())
            {
                case "--config" when index + 1 < arguments.Count:
                    configPath = arguments[++index];
                    break;
                case "--language" when index + 1 < arguments.Count:
                    language = arguments[++index];
                    break;
                case "--preview":
                    preview = true;
                    break;
                case "--windowed":
                    windowed = true;
                    break;
            }
        }

        return new CommandLineOptions(configPath, language, preview, windowed);
    }

    public void ApplyTo(LauncherSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(Language))
        {
            settings.Language = Language;
        }

        if (Windowed)
        {
            settings.Display.Mode = WindowMode.Windowed;
        }

        if (Preview)
        {
            foreach (var game in settings.Games)
            {
                game.Launch.Enabled = false;
                game.Update.Enabled = false;
            }

            foreach (var operation in settings.Operations)
            {
                operation.Command.Enabled = false;
            }

            settings.Display.AllowEscapeToExit = true;
        }
    }
}
