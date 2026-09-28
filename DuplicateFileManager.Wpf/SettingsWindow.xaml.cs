using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace DuplicateFileManager.Wpf;

public partial class SettingsWindow : Window
{
    public AppConfigLocations ConfigLocations { get; private set; }

    public SettingsWindow(AppConfigLocations configLocations)
    {
        InitializeComponent();
        ConfigLocations = configLocations;
        ShowConfigLocations();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog { Title = "Choose config.xml", Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() != true || sender is not Button { Tag: string provider })
        {
            return;
        }

        ConfigLocations = provider switch
        {
            "Whisparr" => ConfigLocations with { WhisparrConfigPath = dialog.FileName },
            "Radarr" => ConfigLocations with { RadarrConfigPath = dialog.FileName },
            "Sonarr" => ConfigLocations with { SonarrConfigPath = dialog.FileName },
            _ => ConfigLocations,
        };
        ShowConfigLocations();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string provider }) return;
        ConfigLocations = provider switch
        {
            "Whisparr" => ConfigLocations with { WhisparrConfigPath = AppConfigLocations.Default.WhisparrConfigPath },
            "Radarr" => ConfigLocations with { RadarrConfigPath = AppConfigLocations.Default.RadarrConfigPath },
            "Sonarr" => ConfigLocations with { SonarrConfigPath = AppConfigLocations.Default.SonarrConfigPath },
            _ => ConfigLocations,
        };
        ShowConfigLocations();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void ShowConfigLocations()
    {
        WhisparrPathTextBox.Text = ConfigLocations.WhisparrConfigPath;
        RadarrPathTextBox.Text = ConfigLocations.RadarrConfigPath;
        SonarrPathTextBox.Text = ConfigLocations.SonarrConfigPath;
    }
}

public sealed record AppConfigLocations(string WhisparrConfigPath, string RadarrConfigPath, string SonarrConfigPath)
{
    public static AppConfigLocations Default { get; } = new(@"C:\ProgramData\Whisparr\config.xml", @"C:\ProgramData\Radarr\config.xml", @"C:\ProgramData\Sonarr\config.xml");

    public string GetPath(NamingProvider provider) => provider switch
    {
        NamingProvider.Whisparr => WhisparrConfigPath,
        NamingProvider.Radarr => RadarrConfigPath,
        NamingProvider.Sonarr => SonarrConfigPath,
        _ => string.Empty,
    };
}