using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed class GameUpdateService(ILogService log) : IDisposable
{
    private static readonly Regex HrefPattern = new(
        "href\\s*=\\s*[\\\"'](?<href>[^\\\"']+)[\\\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<bool> TryUpdateAsync(
        GameSettings game,
        IReadOnlyList<UpdateSourceSettings> configuredSources,
        CancellationToken cancellationToken)
    {
        if (!game.Update.Enabled || game.Update.SourceIds.Count == 0)
        {
            return true;
        }

        var sourcesById = configuredSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceId in game.Update.SourceIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourcesById.TryGetValue(sourceId, out var source) || !source.Enabled)
            {
                log.Error($"Update source '{sourceId}' is missing or disabled for game '{game.Id}'.");
                continue;
            }

            string? stagingDirectory = null;
            try
            {
                var sourceDirectory = source.Kind switch
                {
                    UpdateSourceKind.Usb => ResolveUsbSourceDirectory(source, game.Id),
                    UpdateSourceKind.Http => stagingDirectory = await DownloadHttpSourceAsync(
                        source,
                        game.Id,
                        cancellationToken),
                    _ => throw new InvalidDataException($"Unsupported update source kind: {source.Kind}.")
                };

                if (!Directory.Exists(sourceDirectory))
                {
                    throw new DirectoryNotFoundException($"Update directory does not exist: {sourceDirectory}");
                }

                if (!Directory.EnumerateFileSystemEntries(sourceDirectory).Any())
                {
                    throw new InvalidDataException($"Update directory is empty: {sourceDirectory}");
                }

                var targetDirectory = ResolveTargetDirectory(game);
                if (VersionsMatch(sourceDirectory, targetDirectory, game.Update))
                {
                    log.Info($"Game update skipped because {game.Update.VersionFile} matches: {game.Id} ({source.Id}).");
                    return true;
                }

                ApplyUpdate(sourceDirectory, targetDirectory, game.Update.ApplyMode);
                log.Info($"Game update completed: {game.Id} from {source.Id} using {game.Update.ApplyMode}.");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
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

        log.Error($"All configured update sources failed for game '{game.Id}'. Game startup will continue.");
        return false;
    }

    private async Task<string> DownloadHttpSourceAsync(
        UpdateSourceSettings source,
        string gameId,
        CancellationToken cancellationToken)
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
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downloadedFiles = await DownloadDirectoryAsync(
                source,
                rootUri,
                rootUri,
                stagingDirectory,
                visited,
                cancellationToken);
            if (downloadedFiles == 0)
            {
                throw new InvalidDataException(
                    $"HTTP source returned no files. Enable directory listing at {rootUri.AbsoluteUri}.");
            }

            return stagingDirectory;
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    private async Task<int> DownloadDirectoryAsync(
        UpdateSourceSettings source,
        Uri rootUri,
        Uri directoryUri,
        string stagingDirectory,
        ISet<string> visited,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(directoryUri.AbsoluteUri))
        {
            return 0;
        }

        var html = await GetStringAsync(source, directoryUri, cancellationToken);
        var links = HrefPattern.Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value))
            .Where(href => !string.IsNullOrWhiteSpace(href)
                && !href.StartsWith('#')
                && !href.StartsWith('?')
                && href is not "../" and not "./")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var downloaded = 0;
        foreach (var href in links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(directoryUri, href, out var itemUri)
                || itemUri.Scheme != rootUri.Scheme
                || !itemUri.Host.Equals(rootUri.Host, StringComparison.OrdinalIgnoreCase)
                || !itemUri.AbsolutePath.StartsWith(rootUri.AbsolutePath, StringComparison.Ordinal))
            {
                continue;
            }

            if (href.EndsWith('/'))
            {
                downloaded += await DownloadDirectoryAsync(
                    source,
                    rootUri,
                    itemUri,
                    stagingDirectory,
                    visited,
                    cancellationToken);
                continue;
            }

            var relativePath = Uri.UnescapeDataString(itemUri.AbsolutePath[rootUri.AbsolutePath.Length..])
                .Replace('/', Path.DirectorySeparatorChar);
            var targetPath = GetSafeChildPath(stagingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await DownloadFileAsync(source, itemUri, targetPath, cancellationToken);
            downloaded++;
        }

        return downloaded;
    }

    private async Task<string> GetStringAsync(
        UpdateSourceSettings source,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(source, uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task DownloadFileAsync(
        UpdateSourceSettings source,
        Uri uri,
        string targetPath,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(source, uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1_048_576, true);
        await input.CopyToAsync(output, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        UpdateSourceSettings source,
        Uri uri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(source.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{source.Username}:{source.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(source.RequestTimeoutMs);
        var response = await httpClient.SendAsync(request, completionOption, timeout.Token);
        response.EnsureSuccessStatusCode();
        return response;
    }

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

    private static void ApplyUpdate(string sourceDirectory, string targetDirectory, UpdateApplyMode mode)
    {
        ValidateDirectoriesDoNotOverlap(sourceDirectory, targetDirectory);
        if (mode == UpdateApplyMode.FullReplace)
        {
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
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(GetSafeChildPath(targetDirectory, relative));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", enumeration))
        {
            var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = GetSafeChildPath(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (overwrite || !File.Exists(targetFile))
            {
                File.Copy(sourceFile, targetFile, overwrite);
            }
        }
    }

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
