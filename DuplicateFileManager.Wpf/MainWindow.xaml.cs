using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace DuplicateFileManager.Wpf;

public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuplicateFileManager", "settings.json");
    private static readonly HttpClient HttpClient = new();
    private string? savedRootFolder;
    private AppConfigLocations configLocations = AppConfigLocations.Default;
    private CancellationTokenSource? scanCancellation;
    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        var settings = LoadSettings();
        savedRootFolder = settings.RootFolder is { Length: > 0 } rootFolder && Directory.Exists(rootFolder) ? rootFolder : null;
        configLocations = new AppConfigLocations(
            string.IsNullOrWhiteSpace(settings.WhisparrConfigPath) ? AppConfigLocations.Default.WhisparrConfigPath : settings.WhisparrConfigPath,
            string.IsNullOrWhiteSpace(settings.RadarrConfigPath) ? AppConfigLocations.Default.RadarrConfigPath : settings.RadarrConfigPath,
            string.IsNullOrWhiteSpace(settings.SonarrConfigPath) ? AppConfigLocations.Default.SonarrConfigPath : settings.SonarrConfigPath);
        NamingProviderSelector.SelectedValue = settings.NamingProvider ?? NamingProvider.Whisparr.ToString();
        if (savedRootFolder is not null)
        {
            SidebarRootText.Text = savedRootFolder;
            StatusText.Text = $"Saved root folder: {savedRootFolder}";
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        savedRootFolder = dialog.FolderName;
        SaveSettings();
        await ScanFolderAsync(dialog.FolderName);
    }

    private void NamingProviderSelector_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var window = new SettingsWindow(configLocations) { Owner = this };
        if (window.ShowDialog() == true)
        {
            configLocations = window.ConfigLocations;
            SaveSettings();
            StatusText.Text = "Naming-rule config locations saved.";
        }
    }

    private async Task ScanFolderAsync(string rootFolder)
    {
        scanCancellation?.Dispose();
        scanCancellation = new CancellationTokenSource();
        var cancellationToken = scanCancellation.Token;
        ScanButton.IsEnabled = false;
        ChangeRootButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        StopScanButton.Visibility = Visibility.Visible;
        StopScanButton.IsEnabled = true;
        StatusText.Text = "Finding files...";
        SidebarRootText.Text = rootFolder;

        try
        {
            var namingRules = await LoadNamingRulesAsync(SelectedNamingProvider);
            var files = await Task.Run(() => EnumerateFiles(rootFolder, cancellationToken), cancellationToken);
            StatusText.Text = $"Hashing {files.Count:N0} files locally...";
            var groups = await Task.Run(() => FindDuplicateGroups(files, rootFolder, namingRules, cancellationToken), cancellationToken);

            DuplicateGroups.Clear();
            foreach (var group in groups)
            {
                DuplicateGroups.Add(group);
            }

            GroupCountText.Text = $"{groups.Count} group{(groups.Count == 1 ? string.Empty : "s")}";
            PotentialText.Text = FormatBytes(groups.Sum(group => group.ReclaimBytes));
            StatusText.Text = groups.Count == 0
                ? $"No exact duplicates found in {rootFolder}."
                : namingRules.Enabled
                    ? $"Scan complete for {rootFolder}. {namingRules.ProviderName} naming rules protect managed files from removal."
                    : $"Scan complete for {rootFolder}. {namingRules.ProviderName} naming rules were unavailable, so no files were protected.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan stopped. Previous results were kept.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "The folder could not be scanned.";
            MessageBox.Show(this, exception.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            StopScanButton.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
            ChangeRootButton.IsEnabled = true;
            scanCancellation?.Dispose();
            scanCancellation = null;
        }
    }

    private void StopScanButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        scanCancellation?.Cancel();
        StopScanButton.IsEnabled = false;
        StatusText.Text = "Stopping scan...";
    }

    private static List<string> EnumerateFiles(string rootFolder, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = new List<string>();
        foreach (var path in Directory.EnumerateFiles(rootFolder, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > 0)
            {
                files.Add(path);
            }
        }
        return files;
    }

    private static ScanSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ScanSettings.Default;
            }

            return JsonSerializer.Deserialize<ScanSettings>(File.ReadAllText(SettingsPath)) ?? ScanSettings.Default;
        }
        catch (IOException)
        {
            return ScanSettings.Default;
        }
        catch (JsonException)
        {
            return ScanSettings.Default;
        }
    }

    private NamingProvider SelectedNamingProvider => Enum.TryParse<NamingProvider>(NamingProviderSelector.SelectedValue?.ToString(), out var provider) ? provider : NamingProvider.Whisparr;

    private void SaveSettings()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ScanSettings(savedRootFolder ?? string.Empty, SelectedNamingProvider.ToString(), configLocations.WhisparrConfigPath, configLocations.RadarrConfigPath, configLocations.SonarrConfigPath)));
    }

    private static List<DuplicateGroup> FindDuplicateGroups(IEnumerable<string> paths, string rootFolder, NamingRules namingRules, CancellationToken cancellationToken)
    {
        var bySize = paths.Select(path => new FileInfo(path)).GroupBy(file => file.Length).Where(group => group.Count() > 1);
        var byHash = new Dictionary<string, List<FileInfo>>();

        foreach (var sizeGroup in bySize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byFingerprint = new ConcurrentDictionary<string, ConcurrentBag<FileInfo>>();
            Parallel.ForEach(sizeGroup, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4), CancellationToken = cancellationToken }, file =>
            {
                var fingerprint = CreateFastFingerprint(file);
                byFingerprint.GetOrAdd(fingerprint, _ => []).Add(file);
            });

            var fullHashCandidates = byFingerprint.Values.Where(files => files.Count > 1).SelectMany(files => files).ToArray();
            var confirmedHashes = new ConcurrentDictionary<string, ConcurrentBag<FileInfo>>();
            Parallel.ForEach(fullHashCandidates, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4), CancellationToken = cancellationToken }, file =>
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                confirmedHashes.GetOrAdd(hash, _ => []).Add(file);
            });

            foreach (var (hash, files) in confirmedHashes)
            {
                if (!byHash.TryGetValue(hash, out var duplicates))
                {
                    duplicates = [];
                    byHash.Add(hash, duplicates);
                }
                duplicates.AddRange(files);
            }
        }

        return byHash.Values.Where(files => files.Count > 1).Select((files, index) =>
        {
            var sortedFiles = files.OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            var fileItems = sortedFiles.Select((file, fileIndex) =>
            {
                var isManaged = IsManagedFile(file, rootFolder, namingRules);
                return new DuplicateFile
                {
                    FullPath = file.FullName,
                    Name = file.Name,
                    Directory = Path.GetRelativePath(rootFolder, file.DirectoryName ?? rootFolder),
                    SizeText = FormatBytes(file.Length),
                    ModifiedText = file.LastWriteTime.ToString("d"),
                    ProtectionProvider = isManaged ? namingRules.ProviderName : null,
                    IsSelected = fileIndex > 0 && !isManaged,
                };
            }).ToList();
            var reclaimBytes = sortedFiles[0].Length * (sortedFiles.Count - 1);
            var extension = Path.GetExtension(sortedFiles[0].Name).TrimStart('.').ToUpperInvariant();
            return new DuplicateGroup
            {
                Name = sortedFiles[0].Name,
                Kind = $"{(string.IsNullOrEmpty(extension) ? "File" : extension)} file",
                Copies = sortedFiles.Count,
                Reclaim = FormatBytes(reclaimBytes),
                ReclaimBytes = reclaimBytes,
                Files = fileItems,
            };
        }).OrderByDescending(group => group.ReclaimBytes).ToList();
    }

    private static string CreateFastFingerprint(FileInfo file)
    {
        const int chunkSize = 65536;
        var buffer = new byte[chunkSize];
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.RandomAccess);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var firstRead = stream.Read(buffer, 0, buffer.Length);
        hash.AppendData(buffer, 0, firstRead);
        if (file.Length > chunkSize)
        {
            stream.Seek(-chunkSize, SeekOrigin.End);
            var lastRead = stream.Read(buffer, 0, buffer.Length);
            hash.AppendData(buffer, 0, lastRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task<NamingRules> LoadNamingRulesAsync(NamingProvider provider)
    {
        var configPath = configLocations.GetPath(provider);
        if (provider == NamingProvider.None || !File.Exists(configPath))
        {
            return NamingRules.Disabled(provider);
        }

        var config = XDocument.Load(configPath).Root;
        var port = config?.Element("Port")?.Value;
        var apiKey = config?.Element("ApiKey")?.Value;
        if (string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(apiKey))
        {
            return NamingRules.Disabled(provider);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/v3/config/naming");
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return NamingRules.Disabled(provider);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rules = document.RootElement;
        return new NamingRules(provider, GetBoolean(rules, "renameMovies") || GetBoolean(rules, "renameEpisodes") || GetBoolean(rules, "renameScenes"), GetString(rules, "movieFolderFormat"), GetString(rules, "seriesFolderFormat"), GetString(rules, "sceneFolderFormat"), GetString(rules, "standardMovieFormat"), GetString(rules, "standardEpisodeFormat"), GetString(rules, "dailyEpisodeFormat"), GetString(rules, "standardSceneFormat"));
    }

    private static bool IsManagedFile(FileInfo file, string rootFolder, NamingRules rules)
    {
        var relativePath = Path.GetRelativePath(rootFolder, file.FullName).Replace('/', '\\');
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);

        if (!rules.Enabled)
        {
            return false;
        }

        if ((rules.Provider is NamingProvider.Whisparr or NamingProvider.Radarr) && MatchesManagedRoot(relativePath, rules.MovieFolderFormat) && MatchesConfiguredName(nameWithoutExtension, rules.StandardMovieFormat, @"^.+ \(\d{4}\)(?: \[.+\])?$"))
        {
            return true;
        }

        if (rules.Provider == NamingProvider.Whisparr && MatchesManagedRoot(relativePath, rules.SceneFolderFormat) && MatchesConfiguredName(nameWithoutExtension, rules.StandardSceneFormat, @"^.+ on \d{4}-\d{2}-\d{2} @ .+ - .+"))
        {
            return true;
        }

        return rules.Provider == NamingProvider.Sonarr && MatchesManagedRoot(relativePath, rules.SeriesFolderFormat) && (MatchesConfiguredName(nameWithoutExtension, rules.StandardEpisodeFormat, @"^.+ - S\d{1,3}E\d{1,3}.*") || MatchesConfiguredName(nameWithoutExtension, rules.DailyEpisodeFormat, @"^.+ - \d{4}[ ._-]\d{2}[ ._-]\d{2}.*"));
    }

    private static bool MatchesConfiguredName(string fileName, string format, string defaultPattern)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        var pattern = format.Contains("{Release Year}", StringComparison.OrdinalIgnoreCase) ? @"^.+ \(\d{4}\)(?: \[.+\])?$"
            : format.Contains("{season", StringComparison.OrdinalIgnoreCase) ? @"^.+ - S\d{1,3}E\d{1,3}.*"
            : format.Contains("{Air-Date}", StringComparison.OrdinalIgnoreCase) ? @"^.+ - \d{4}[ ._-]\d{2}[ ._-]\d{2}.*"
            : format.Contains("{Scene CleanTitle}", StringComparison.OrdinalIgnoreCase) ? @"^.+ on \d{4}-\d{2}-\d{2} @ .+ - .+"
            : defaultPattern;
        return Regex.IsMatch(fileName, pattern);
    }

    private static bool GetBoolean(JsonElement rules, string propertyName) => rules.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    private static string GetString(JsonElement rules, string propertyName) => rules.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool MatchesManagedRoot(string relativePath, string folderFormat)
    {
        var managedRoot = folderFormat.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrWhiteSpace(managedRoot)
            && (managedRoot.StartsWith('{') || relativePath.StartsWith($"{managedRoot}\\", StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var selectedFiles = DuplicateGroups.SelectMany(group => group.Files).Where(file => file.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            MessageBox.Show(this, "Select one or more duplicate copies to remove.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(this, $"Permanently delete {selectedFiles.Count} selected file{(selectedFiles.Count == 1 ? string.Empty : "s")}?", "Remove duplicates", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var file in selectedFiles)
        {
            File.Delete(file.FullPath);
        }

        StatusText.Text = $"Removed {selectedFiles.Count} selected duplicate file{(selectedFiles.Count == 1 ? string.Empty : "s")}. Scan again to refresh the results.";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var units = new[] { "KB", "MB", "GB", "TB" };
        var exponent = Math.Min((int)Math.Floor(Math.Log(bytes, 1024)) - 1, units.Length - 1);
        return $"{bytes / Math.Pow(1024, exponent + 1):0.#} {units[exponent]}";
    }
}

public sealed class DuplicateGroup
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int Copies { get; init; }
    public required string Reclaim { get; init; }
    public required long ReclaimBytes { get; init; }
    public required List<DuplicateFile> Files { get; init; }
}

public sealed class DuplicateFile
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required string SizeText { get; init; }
    public required string ModifiedText { get; init; }
    public string? ProtectionProvider { get; init; }
    public string ProtectionStatus => ProtectionProvider is null ? "Review copy" : $"{ProtectionProvider} managed";
    public bool IsSelected { get; set; }
}

public enum NamingProvider
{
    None,
    Whisparr,
    Radarr,
    Sonarr,
}

public sealed record NamingRules(NamingProvider Provider, bool Enabled, string MovieFolderFormat, string SeriesFolderFormat, string SceneFolderFormat, string StandardMovieFormat, string StandardEpisodeFormat, string DailyEpisodeFormat, string StandardSceneFormat)
{
    public static NamingRules None { get; } = new(NamingProvider.None, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    public static NamingRules Disabled(NamingProvider provider) => new(provider, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    public string ProviderName => Provider == NamingProvider.None ? "No" : Provider.ToString();
}

public sealed record ScanSettings(string RootFolder, string? NamingProvider, string? WhisparrConfigPath, string? RadarrConfigPath, string? SonarrConfigPath)
{
    public static ScanSettings Default { get; } = new(string.Empty, nameof(DuplicateFileManager.Wpf.NamingProvider.Whisparr), null, null, null);
}