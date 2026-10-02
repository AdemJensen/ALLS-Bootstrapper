using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal enum GameUpdateOutcome
{
    UpdateDisabled,
    NoSources,
    UpdateDirectoryNotFound,
    UpToDate,
    Completed,
    Failed,
    ServerUnavailable
}

internal enum UpdateSourceOutcome
{
    NotAttempted,
    Missing,
    Disabled,
    DirectoryNotFound,
    UpToDate,
    Completed,
    Failed,
    ServerUnavailable,
    ConnectionFailed
}

internal sealed record UpdateSourceAttempt(
    string SourceId,
    UpdateSourceOutcome Outcome,
    string? Detail = null);

internal sealed class UpdateServerUnavailableException(string message, Exception innerException)
    : IOException(message, innerException);

internal sealed record GameUpdateResult(
    GameUpdateOutcome Outcome,
    IReadOnlyList<UpdateSourceAttempt> Attempts,
    int TotalFiles = 0,
    int CopiedFiles = 0,
    bool HasCopyStatistics = false);

internal sealed class UpdateCopyStatistics
{
    public int TotalFiles { get; private set; }

    public int CopiedFiles { get; private set; }

    public bool IsAvailable { get; private set; }

    public void Reset()
    {
        TotalFiles = 0;
        CopiedFiles = 0;
        IsAvailable = false;
    }

    public void SetTotal(int totalFiles)
    {
        TotalFiles = totalFiles;
        CopiedFiles = 0;
        IsAvailable = true;
    }

    public void RecordCopied() => CopiedFiles++;
}

internal enum GameUpdateProgressStage
{
    Checking,
    Downloading,
    Preparing,
    Copying
}

internal sealed record GameUpdateProgress(
    GameUpdateProgressStage Stage,
    int CompletedFiles = 0,
    int TotalFiles = 0)
{
    public bool HasFileCount => Stage is GameUpdateProgressStage.Downloading or GameUpdateProgressStage.Copying;

    public double Percentage => !HasFileCount ? 0 : TotalFiles == 0
        ? 100
        : Math.Clamp(CompletedFiles * 100.0 / TotalFiles, 0, 100);
}

internal sealed class UpdatePauseController(IProgress<bool>? pauseState = null)
{
    private readonly object sync = new();
    private TaskCompletionSource? resumeSignal;
    private bool pauseAcknowledged;

    public void RequestPause()
    {
        lock (sync)
        {
            resumeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? signal;
        var notifyResumed = false;
        lock (sync)
        {
            signal = resumeSignal;
            resumeSignal = null;
            notifyResumed = pauseAcknowledged;
            pauseAcknowledged = false;
        }

        signal?.TrySetResult();
        if (notifyResumed)
        {
            pauseState?.Report(false);
        }
    }

    public async ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task? waitTask;
        var notifyPaused = false;
        lock (sync)
        {
            waitTask = resumeSignal?.Task;
            if (waitTask is not null && !pauseAcknowledged)
            {
                pauseAcknowledged = true;
                notifyPaused = true;
            }
        }

        if (waitTask is null)
        {
            return;
        }

        if (notifyPaused)
        {
            pauseState?.Report(true);
        }

        await waitTask.WaitAsync(cancellationToken);
    }
}

internal sealed class GameUpdateService(ILogService log) : IDisposable
{
    private static readonly Regex HrefPattern = new(
        "href\\s*=\\s*[\\\"'](?<href>[^\\\"']+)[\\\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<GameUpdateResult> TryUpdateAsync(
        GameSettings game,
        IReadOnlyList<UpdateSourceSettings> configuredSources,
        CancellationToken cancellationToken,
        IProgress<GameUpdateProgress>? progress = null,
        UpdatePauseController? pauseController = null)
    {
        progress?.Report(new GameUpdateProgress(GameUpdateProgressStage.Checking));
        await WaitIfPausedAsync(pauseController, cancellationToken);
        var configuredSourceIds = game.Update.SourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (!game.Update.Enabled)
        {
            return new GameUpdateResult(
                GameUpdateOutcome.UpdateDisabled,
                configuredSourceIds
                    .Select(id => new UpdateSourceAttempt(id, UpdateSourceOutcome.NotAttempted))
                    .ToList());
        }

        if (configuredSourceIds.Count == 0)
        {
            return new GameUpdateResult(GameUpdateOutcome.NoSources, []);
        }

        var sourcesById = configuredSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var attempts = new List<UpdateSourceAttempt>();
        var copyStatistics = new UpdateCopyStatistics();
        var attemptedEnabledSource = false;
        var updateDirectoryNotFound = false;
        var sourceFailed = false;
        var serverUnavailable = false;
        for (var sourceIndex = 0; sourceIndex < configuredSourceIds.Count; sourceIndex++)
        {
            var sourceId = configuredSourceIds[sourceIndex];
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourcesById.TryGetValue(sourceId, out var source))
            {
                var detail = $"Update source '{sourceId}' is not configured.";
                attempts.Add(new UpdateSourceAttempt(sourceId, UpdateSourceOutcome.Missing, detail));
                log.Error($"{detail} Game: '{game.Id}'.");
                continue;
            }

            if (!source.Enabled)
            {
                var detail = $"Update source '{sourceId}' is disabled.";
                attempts.Add(new UpdateSourceAttempt(sourceId, UpdateSourceOutcome.Disabled, detail));
                log.Error($"{detail} Game: '{game.Id}'.");
                continue;
            }

            attemptedEnabledSource = true;
            string? stagingDirectory = null;
            try
            {
                var sourceDirectory = source.Kind switch
                {
                    UpdateSourceKind.Usb => ResolveUsbSourceDirectory(source, game.Id),
                    UpdateSourceKind.Http => stagingDirectory = await DownloadHttpSourceAsync(
                        source,
                        game.Id,
                        cancellationToken,
                        progress,
                        pauseController),
                    _ => throw new InvalidDataException($"Unsupported update source kind: {source.Kind}.")
                };

                if (!Directory.Exists(sourceDirectory))
                {
                    throw new DirectoryNotFoundException($"Update directory does not exist: {sourceDirectory}");
                }

                if (!Directory.EnumerateFileSystemEntries(sourceDirectory)
                    .Any(path => !IsIgnoredUpdateFile(path)))
                {
                    throw new InvalidDataException($"Update directory is empty: {sourceDirectory}");
                }

                var targetDirectory = ResolveTargetDirectory(game);
                if (VersionsMatch(sourceDirectory, targetDirectory, game.Update))
                {
                    log.Info($"Game update skipped because {game.Update.VersionFile} matches: {game.Id} ({source.Id}).");
                    attempts.Add(new UpdateSourceAttempt(
                        source.Id,
                        UpdateSourceOutcome.UpToDate,
                        $"{game.Update.VersionFile} matches the installed version."));
                    AppendNotAttemptedSources(attempts, configuredSourceIds, sourceIndex + 1);
                    return new GameUpdateResult(GameUpdateOutcome.UpToDate, attempts);
                }

                await ApplyUpdateAsync(
                    sourceDirectory,
                    targetDirectory,
                    game.Update.ApplyMode,
                    game.Update.VersionFile,
                    cancellationToken,
                    progress,
                    pauseController,
                    copyStatistics);
                log.Info($"Game update completed: {game.Id} from {source.Id} using {game.Update.ApplyMode}.");
                attempts.Add(new UpdateSourceAttempt(
                    source.Id,
                    UpdateSourceOutcome.Completed,
                    $"Applied using {game.Update.ApplyMode} to {targetDirectory}."));
                AppendNotAttemptedSources(attempts, configuredSourceIds, sourceIndex + 1);
                return new GameUpdateResult(
                    GameUpdateOutcome.Completed,
                    attempts,
                    copyStatistics.TotalFiles,
                    copyStatistics.CopiedFiles,
                    copyStatistics.IsAvailable);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UpdateServerUnavailableException exception)
            {
                serverUnavailable = true;
                sourceFailed |= source.UnavailableIsFailure;
                attempts.Add(new UpdateSourceAttempt(
                    source.Id,
                    source.UnavailableIsFailure ? UpdateSourceOutcome.ConnectionFailed : UpdateSourceOutcome.ServerUnavailable,
                    exception.Message));
                if (source.UnavailableIsFailure)
                {
                    log.Error($"HTTP update server connection failed: {game.Id} from {source.Id}.", exception);
                }
                else
                {
                    log.Info($"HTTP update server unavailable; skipped by configuration: {game.Id} from {source.Id}. {exception.Message}");
                }
            }
            catch (DirectoryNotFoundException exception)
            {
                updateDirectoryNotFound = true;
                attempts.Add(new UpdateSourceAttempt(
                    source.Id,
                    UpdateSourceOutcome.DirectoryNotFound,
                    exception.Message));
                log.Info($"No update directory for game '{game.Id}' in source '{source.Id}': {exception.Message}");
            }
            catch (Exception exception)
            {
                sourceFailed = true;
                attempts.Add(new UpdateSourceAttempt(source.Id, UpdateSourceOutcome.Failed, exception.Message));
                log.Error($"Game update source failed: {game.Id} from {source.Id}.", exception);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(stagingDirectory))
                {
                    TryDeleteDirectory(stagingDirectory);
                }
            }
        }

        var outcome = sourceFailed
            ? GameUpdateOutcome.Failed
            : updateDirectoryNotFound
                ? GameUpdateOutcome.UpdateDirectoryNotFound
                : serverUnavailable
                    ? GameUpdateOutcome.ServerUnavailable
                    : attemptedEnabledSource
                        ? GameUpdateOutcome.Failed
                        : GameUpdateOutcome.NoSources;
        if (outcome == GameUpdateOutcome.Failed)
        {
            log.Error($"No update source succeeded and at least one attempt failed for game '{game.Id}'. Game startup will continue.");
        }

        return new GameUpdateResult(
            outcome,
            attempts,
            copyStatistics.TotalFiles,
            copyStatistics.CopiedFiles,
            copyStatistics.IsAvailable);
    }

    private static void AppendNotAttemptedSources(
        ICollection<UpdateSourceAttempt> attempts,
        IReadOnlyList<string> sourceIds,
        int startIndex)
    {
        for (var index = startIndex; index < sourceIds.Count; index++)
        {
            attempts.Add(new UpdateSourceAttempt(sourceIds[index], UpdateSourceOutcome.NotAttempted));
        }
    }

    private async Task<string> DownloadHttpSourceAsync(
        UpdateSourceSettings source,
        string gameId,
        CancellationToken cancellationToken,
        IProgress<GameUpdateProgress>? progress,
        UpdatePauseController? pauseController)
    {
        if (!Uri.TryCreate(EnsureTrailingSlash(source.BaseUrl), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"Invalid HTTP update URL for source '{source.Id}'.");
        }

        var rootUri = AppendUriPath(baseUri, source.Path);
        if (source.ContainsMultipleGames)
        {
            rootUri = AppendUriPath(rootUri, Uri.EscapeDataString(gameId));
        }

        rootUri = new Uri(EnsureTrailingSlash(rootUri.AbsoluteUri));
        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"alls-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            progress?.Report(new GameUpdateProgress(GameUpdateProgressStage.Preparing));
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            await CollectHttpFilesAsync(
                source,
                rootUri,
                rootUri,
                stagingDirectory,
                visited,
                files,
                cancellationToken,
                pauseController);
            if (files.Count == 0)
            {
                throw new InvalidDataException(
                    $"HTTP source returned no files. Enable directory listing at {rootUri.AbsoluteUri}.");
            }

            progress?.Report(new GameUpdateProgress(GameUpdateProgressStage.Downloading, 0, files.Count));
            var downloadedFiles = 0;
            foreach (var file in files)
            {
                await WaitIfPausedAsync(pauseController, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);
                await DownloadFileAsync(source, file.Value, file.Key, cancellationToken, pauseController);
                downloadedFiles++;
                progress?.Report(new GameUpdateProgress(
                    GameUpdateProgressStage.Downloading,
                    downloadedFiles,
                    files.Count));
            }

            return stagingDirectory;
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    private async Task CollectHttpFilesAsync(
        UpdateSourceSettings source,
        Uri rootUri,
        Uri directoryUri,
        string stagingDirectory,
        ISet<string> visited,
        Dictionary<string, Uri> files,
        CancellationToken cancellationToken,
        UpdatePauseController? pauseController)
    {
        await WaitIfPausedAsync(pauseController, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!visited.Add(directoryUri.AbsoluteUri))
        {
            return;
        }

        var html = await GetStringAsync(source, directoryUri, cancellationToken, directoryUri == rootUri);
        var links = HrefPattern.Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value))
            .Where(href => !string.IsNullOrWhiteSpace(href)
                && !href.StartsWith('#')
                && !href.StartsWith('?')
                && href is not "../" and not "./")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var href in links)
        {
            await WaitIfPausedAsync(pauseController, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(directoryUri, href, out var itemUri)
                || itemUri.Scheme != rootUri.Scheme
                || !itemUri.Authority.Equals(rootUri.Authority, StringComparison.OrdinalIgnoreCase)
                || !itemUri.AbsolutePath.StartsWith(rootUri.AbsolutePath, StringComparison.Ordinal))
            {
                continue;
            }

            if (href.EndsWith('/'))
            {
                await CollectHttpFilesAsync(
                    source,
                    rootUri,
                    itemUri,
                    stagingDirectory,
                    visited,
                    files,
                    cancellationToken,
                    pauseController);
                continue;
            }

            var relativePath = Uri.UnescapeDataString(itemUri.AbsolutePath[rootUri.AbsolutePath.Length..])
                .Replace('/', Path.DirectorySeparatorChar);
            if (IsIgnoredUpdateFile(relativePath))
            {
                continue;
            }

            var targetPath = GetSafeChildPath(stagingDirectory, relativePath);
            files.TryAdd(targetPath, itemUri);
        }
    }

    private async Task<string> GetStringAsync(
        UpdateSourceSettings source,
        Uri uri,
        CancellationToken cancellationToken,
        bool initialRequest)
    {
        try
        {
            // Classify availability only before the first response headers arrive. Content read errors
            // after a response are update failures even when an unavailable server may be skipped.
            using var response = await SendAsync(source, uri, cancellationToken, initialRequest);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(source.RequestTimeoutMs);
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (UpdateServerUnavailableException)
        {
            throw;
        }
        catch (DirectoryNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new IOException($"Failed to read HTTP directory '{uri}': {exception.Message}", exception);
        }
    }

    private async Task DownloadFileAsync(
        UpdateSourceSettings source,
        Uri uri,
        string targetPath,
        CancellationToken cancellationToken,
        UpdatePauseController? pauseController)
    {
        try
        {
            using var response = await SendAsync(source, uri, cancellationToken);
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1_048_576, true);
            await CopyStreamAsync(input, output, cancellationToken, pauseController);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new IOException($"Failed to download HTTP file '{uri}': {exception.Message}", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        UpdateSourceSettings source,
        Uri uri,
        CancellationToken cancellationToken,
        bool initialRequest = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(source.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{source.Username}:{source.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(source.RequestTimeoutMs);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (Exception exception) when (initialRequest && IsServerUnavailable(exception, cancellationToken))
        {
            throw new UpdateServerUnavailableException(
                $"HTTP update server did not respond at '{uri}': {exception.Message}", exception);
        }

        try
        {
            if (initialRequest && response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new DirectoryNotFoundException($"HTTP update directory does not exist: {uri}");
            }

            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static bool IsServerUnavailable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && (exception is OperationCanceledException
            || exception is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError });

    private static string ResolveUsbSourceDirectory(UpdateSourceSettings source, string gameId)
    {
        if (string.IsNullOrWhiteSpace(source.DriveLetter))
        {
            throw new InvalidDataException($"USB update source '{source.Id}' has no driveLetter.");
        }

        var drive = source.DriveLetter.Trim().TrimEnd('\\', '/');
        if (drive.Length == 1 && char.IsLetter(drive[0]))
        {
            drive += ":";
        }

        var root = drive.EndsWith(':') ? drive + Path.DirectorySeparatorChar : drive;
        var path = Path.Combine(root, source.Path);
        if (source.ContainsMultipleGames)
        {
            path = Path.Combine(path, gameId);
        }

        return Path.GetFullPath(path);
    }

    private static string ResolveTargetDirectory(GameSettings game)
    {
        var workingDirectory = Path.GetFullPath(game.Launch.WorkingDirectory, AppContext.BaseDirectory);
        return Path.IsPathRooted(game.Update.TargetDirectory)
            ? Path.GetFullPath(game.Update.TargetDirectory)
            : Path.GetFullPath(game.Update.TargetDirectory, workingDirectory);
    }

    private static bool VersionsMatch(
        string sourceDirectory,
        string targetDirectory,
        GameUpdateSettings settings)
    {
        if (settings.ApplyMode == UpdateApplyMode.AddNewOnly)
        {
            return false;
        }

        var sourceVersionPath = Path.Combine(sourceDirectory, settings.VersionFile);
        var targetVersionPath = Path.Combine(targetDirectory, settings.VersionFile);
        return File.Exists(sourceVersionPath)
            && File.Exists(targetVersionPath)
            && string.Equals(
                File.ReadAllText(sourceVersionPath).Trim(),
                File.ReadAllText(targetVersionPath).Trim(),
                StringComparison.Ordinal);
    }

    private static async Task ApplyUpdateAsync(
        string sourceDirectory,
        string targetDirectory,
        UpdateApplyMode mode,
        string versionFile,
        CancellationToken cancellationToken,
        IProgress<GameUpdateProgress>? progress,
        UpdatePauseController? pauseController,
        UpdateCopyStatistics copyStatistics)
    {
        copyStatistics.Reset();
        progress?.Report(new GameUpdateProgress(GameUpdateProgressStage.Preparing));
        await WaitIfPausedAsync(pauseController, cancellationToken);
        ValidateDirectoriesDoNotOverlap(sourceDirectory, targetDirectory);
        if (mode == UpdateApplyMode.FullReplace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateFullReplaceTarget(targetDirectory);
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
        }

        Directory.CreateDirectory(targetDirectory);
        var overwrite = mode != UpdateApplyMode.AddNewOnly;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", enumeration))
        {
            await WaitIfPausedAsync(pauseController, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(GetSafeChildPath(targetDirectory, relative));
        }

        var filesToCopy = new List<(string Source, string Target, bool IsVersionMarker)>();
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", enumeration))
        {
            await WaitIfPausedAsync(pauseController, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsIgnoredUpdateFile(sourceFile))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = GetSafeChildPath(
                targetDirectory,
                relativePath);
            if (overwrite || !File.Exists(targetFile))
            {
                filesToCopy.Add((
                    sourceFile,
                    targetFile,
                    string.Equals(relativePath, versionFile, StringComparison.OrdinalIgnoreCase)));
            }
        }

        // The version file is the commit marker. Copy it only after every payload
        // file succeeds so an interrupted or failed update is attempted again.
        filesToCopy.Sort((left, right) => left.IsVersionMarker.CompareTo(right.IsVersionMarker));
        copyStatistics.SetTotal(filesToCopy.Count);
        progress?.Report(new GameUpdateProgress(GameUpdateProgressStage.Copying, 0, filesToCopy.Count));

        var copiedFiles = 0;
        foreach (var file in filesToCopy)
        {
            await WaitIfPausedAsync(pauseController, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
            if (file.IsVersionMarker)
            {
                await CopyVersionMarkerAsync(
                    file.Source,
                    file.Target,
                    overwrite,
                    cancellationToken,
                    pauseController);
            }
            else
            {
                await CopyFileAsync(
                    file.Source,
                    file.Target,
                    overwrite,
                    cancellationToken,
                    pauseController);
            }

            copiedFiles++;
            copyStatistics.RecordCopied();
            progress?.Report(new GameUpdateProgress(
                GameUpdateProgressStage.Copying,
                copiedFiles,
                filesToCopy.Count));
        }
    }

    private static async Task CopyVersionMarkerAsync(
        string sourcePath,
        string targetPath,
        bool overwrite,
        CancellationToken cancellationToken,
        UpdatePauseController? pauseController)
    {
        var temporaryPath = $"{targetPath}.alls-{Guid.NewGuid():N}.tmp";
        try
        {
            await CopyFileAsync(
                sourcePath,
                temporaryPath,
                false,
                cancellationToken,
                pauseController);
            await WaitIfPausedAsync(pauseController, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A leftover temporary marker does not affect version matching.
            }
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string targetPath,
        bool overwrite,
        CancellationToken cancellationToken,
        UpdatePauseController? pauseController)
    {
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1_048_576,
            true);
        await using var output = new FileStream(
            targetPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1_048_576,
            true);
        await CopyStreamAsync(input, output, cancellationToken, pauseController);
    }

    private static async Task CopyStreamAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken,
        UpdatePauseController? pauseController)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1_048_576);
        try
        {
            while (true)
            {
                await WaitIfPausedAsync(pauseController, cancellationToken);
                var bytesRead = await input.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    return;
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ValueTask WaitIfPausedAsync(
        UpdatePauseController? pauseController,
        CancellationToken cancellationToken) =>
        pauseController?.WaitIfPausedAsync(cancellationToken) ?? ValueTask.CompletedTask;

    private static void ValidateFullReplaceTarget(string targetDirectory)
    {
        var fullPath = Path.GetFullPath(targetDirectory);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)
            || string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("FullReplace cannot target a drive or filesystem root.");
        }
    }

    private static void ValidateDirectoriesDoNotOverlap(string sourceDirectory, string targetDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var targetRoot = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (sourceRoot.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
            || targetRoot.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Update source and target directories cannot overlap.");
        }
    }

    private static string GetSafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(relativePath, fullRoot);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Update entry escapes its configured root: {relativePath}");
        }

        return fullPath;
    }

    private static bool IsIgnoredUpdateFile(string path) =>
        Path.GetFileName(path).Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);

    private static Uri AppendUriPath(Uri baseUri, string path)
    {
        var result = baseUri;
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            result = new Uri(EnsureTrailingSlash(result.AbsoluteUri) + Uri.EscapeDataString(segment) + "/");
        }

        return result;
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Temporary update data can be cleaned up by the operating system later.
        }
    }

    public void Dispose() => httpClient.Dispose();
}
