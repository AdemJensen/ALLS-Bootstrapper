using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal enum BootSequenceOutcome
{
    Completed,
    Launched,
    PreviewCompleted,
    TargetMissing,
    LaunchFailed,
    ExitRequested,
    MenuRequested
}

internal enum BootInterrupt
{
    None,
    SkipToLaunch,
    OpenMenu
}

internal sealed record BootSequenceResult(BootSequenceOutcome Outcome, string? Error = null);

internal sealed class BootSequenceControl
{
    private readonly object gate = new();
    private TaskCompletionSource<BootInterrupt> signal = CreateSignal();

    public void RequestSkip()
    {
        lock (gate)
        {
            signal.TrySetResult(BootInterrupt.SkipToLaunch);
        }
    }

    public void RequestMenu()
    {
        lock (gate)
        {
            signal.TrySetResult(BootInterrupt.OpenMenu);
        }
    }

    public async Task<BootInterrupt> WaitAsync(int durationMs, CancellationToken cancellationToken)
    {
        TaskCompletionSource<BootInterrupt> current;
        lock (gate)
        {
            current = signal;
        }

        if (current.Task.IsCompleted)
        {
            return ConsumeSignal(current);
        }

        if (durationMs <= 0)
        {
            return BootInterrupt.None;
        }

        var delay = Task.Delay(durationMs, cancellationToken);
        var completed = await Task.WhenAny(delay, current.Task);
        if (completed == delay)
        {
            await delay;
            return BootInterrupt.None;
        }

        return ConsumeSignal(current);
    }

    private BootInterrupt ConsumeSignal(TaskCompletionSource<BootInterrupt> current)
    {
        lock (gate)
        {
            if (!current.Task.IsCompletedSuccessfully)
            {
                return BootInterrupt.None;
            }

            if (ReferenceEquals(signal, current))
            {
                signal = CreateSignal();
            }

            return current.Task.Result;
        }
    }

    private static TaskCompletionSource<BootInterrupt> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class BootSequenceService(ProcessLauncher launcher, ILogService log)
{
    public async Task<BootSequenceResult> RunAsync(
        IReadOnlyList<BootPhase> timeline,
        LaunchSettings launchSettings,
        Action<BootPhase> showPhase,
        BootSequenceControl control,
        Action beforeLaunch,
        CancellationToken cancellationToken)
    {
        var launchIndex = FindLaunchIndex(timeline);
        var skippedToLaunch = false;
        for (var index = 0; index < timeline.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = timeline[index];
            log.Info($"Entering STEP {phase.Step:00} ({phase.MessageKey}).");
            showPhase(phase);

            if (skippedToLaunch && phase.Action == BootPhaseAction.Launch)
            {
                await Task.Yield();
            }

            var duration = skippedToLaunch && phase.Action == BootPhaseAction.Launch ? 0 : phase.DurationMs;
            var interrupt = await control.WaitAsync(duration, cancellationToken);
            if (interrupt == BootInterrupt.OpenMenu)
            {
                return new BootSequenceResult(BootSequenceOutcome.MenuRequested);
            }

            if (interrupt == BootInterrupt.SkipToLaunch && launchIndex >= 0 && index < launchIndex)
            {
                skippedToLaunch = true;
                index = launchIndex - 1;
                continue;
            }

            switch (phase.Action)
            {
                case BootPhaseAction.Exit:
                    return new BootSequenceResult(BootSequenceOutcome.ExitRequested);

                case BootPhaseAction.Launch:
                {
                    if (launchSettings.Enabled)
                    {
                        beforeLaunch();
                    }

                    var launch = await launcher.LaunchAsync(launchSettings, cancellationToken);
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

                    return new BootSequenceResult(BootSequenceOutcome.Launched);
                }
            }
        }

        return new BootSequenceResult(BootSequenceOutcome.Completed);
    }

    private static int FindLaunchIndex(IReadOnlyList<BootPhase> timeline)
    {
        for (var index = timeline.Count - 1; index >= 0; index--)
        {
            if (timeline[index].Action == BootPhaseAction.Launch)
            {
                return index;
            }
        }

        return -1;
    }
}
