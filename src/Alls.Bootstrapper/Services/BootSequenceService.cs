using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal enum BootSequenceOutcome
{
    Completed,
    PreviewCompleted,
    TargetMissing,
    LaunchFailed,
    ExitRequested
}

internal sealed record BootSequenceResult(BootSequenceOutcome Outcome, string? Error = null);

internal sealed class BootSequenceService(ProcessLauncher launcher, ILogService log)
{
    public async Task<BootSequenceResult> RunAsync(
        LauncherSettings settings,
        Action<BootPhase> showPhase,
        CancellationToken cancellationToken)
    {
        foreach (var phase in settings.Timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log.Info($"Entering STEP {phase.Step:00} ({phase.MessageKey}).");
            showPhase(phase);

            if (phase.DurationMs > 0)
            {
                await Task.Delay(phase.DurationMs, cancellationToken);
            }

            switch (phase.Action)
            {
                case BootPhaseAction.Exit:
                    return new BootSequenceResult(BootSequenceOutcome.ExitRequested);

                case BootPhaseAction.Launch:
                {
                    var launch = await launcher.LaunchAsync(settings.Launch, cancellationToken);
                    if (launch.Skipped)
                    {
                        return new BootSequenceResult(BootSequenceOutcome.PreviewCompleted);
                    }

                    if (!launch.Success)
                    {
                        var outcome = launch.Target is null
                            ? BootSequenceOutcome.TargetMissing
                            : BootSequenceOutcome.LaunchFailed;
                        return new BootSequenceResult(outcome, launch.Error);
                    }

                    if (settings.Launch.PostLaunchDelayMs > 0)
                    {
                        await Task.Delay(settings.Launch.PostLaunchDelayMs, cancellationToken);
                    }

                    return new BootSequenceResult(BootSequenceOutcome.Completed);
                }
            }
        }

        return new BootSequenceResult(BootSequenceOutcome.Completed);
    }
}
