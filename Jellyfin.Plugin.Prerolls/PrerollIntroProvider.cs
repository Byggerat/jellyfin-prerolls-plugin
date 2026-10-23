using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prerolls;

/// <summary>
/// Supplies one random preroll from the configured folder as intro for movies.
/// </summary>
public class PrerollIntroProvider : IIntroProvider
{
    private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".m4v", ".mov", ".avi", ".webm", ".ts"
    };

    private static readonly Lock _lastPathLock = new();
    private static string? _lastPath;

    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<PrerollIntroProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrerollIntroProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PrerollIntroProvider}"/> interface.</param>
    public PrerollIntroProvider(ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<PrerollIntroProvider> logger)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Prerolls";

    /// <inheritdoc />
    public async Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        if (item is not Movie)
        {
            return [];
        }

        var folder = Plugin.Instance?.Configuration.PrerollFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return [];
        }

        try
        {
            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Preroll folder {Folder} does not exist", folder);
                return [];
            }

            var files = Directory.EnumerateFiles(folder)
                .Where(f => _videoExtensions.Contains(Path.GetExtension(f)))
                .ToArray();
            if (files.Length == 0)
            {
                _logger.LogWarning("Preroll folder {Folder} contains no videos", folder);
                return [];
            }

            var path = PickRandom(files);
            var video = await GetOrCreateVideo(path).ConfigureAwait(false);
            if (video is null)
            {
                return [];
            }

            return [new IntroInfo { ItemId = video.Id }];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select a preroll from {Folder}", folder);
            return [];
        }
    }

    private static string PickRandom(string[] files)
    {
        lock (_lastPathLock)
        {
            string path;
            do
            {
                path = files[Random.Shared.Next(files.Length)];
            }
            while (files.Length > 1 && string.Equals(path, _lastPath, StringComparison.Ordinal));

            _lastPath = path;
            return path;
        }
    }

    /// <summary>
    /// Jellyfin only plays intros that exist in its database, so register the file on first use.
    /// </summary>
    private async Task<Video?> GetOrCreateVideo(string path)
    {
        if (_libraryManager.ResolvePath(_fileSystem.GetFileInfo(path)) is not Video video)
        {
            _logger.LogWarning("Could not resolve preroll {Path} as a video", path);
            return null;
        }

        if (_libraryManager.GetItemById(video.Id) is Video existing)
        {
            return existing;
        }

        _libraryManager.CreateItem(video, null);
        // Probe the file so duration and streams are known for playback.
        await video.RefreshMetadata(new MetadataRefreshOptions(new DirectoryService(_fileSystem)), CancellationToken.None).ConfigureAwait(false);
        return video;
    }
}
