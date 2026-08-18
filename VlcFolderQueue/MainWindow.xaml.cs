using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VlcFolderQueue.Data;
using VlcFolderQueue.Services;

namespace VlcFolderQueue;

public class FolderRow
{
    public required FolderEntry Entry { get; init; }
    public bool IsNew { get; init; }
    public string Path => Entry.Path;
    public bool IsExcluded => Entry.IsExcluded;
    public bool IsEpisodic => Entry.IsEpisodic;
    public string TagsDisplay => string.Join(", ", Entry.Tags);
}

public class HistoryRow
{
    public required PlayHistoryEntry Entry { get; init; }
    public string FilePath => Entry.FilePath;
    public string PlayedUtc => Entry.PlayedUtc.ToLocalTime().ToString("g");
}

public class QueueRow
{
    public required QueueItem Item { get; init; }
    public string FilePath => Item.FilePath;
    public string DurationMinutes => (Item.DurationSeconds / 60).ToString("0.0");
}

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    private readonly LibraryStore _store = new();
    private readonly ObservableCollection<FolderRow> _folderRows = new();
    private readonly ObservableCollection<HistoryRow> _historyRows = new();
    private readonly ObservableCollection<QueueRow> _queueRows = new();
    private HashSet<string> _newlyDiscoveredFolders = new(StringComparer.OrdinalIgnoreCase);
    private QueueResult? _currentQueue;

    public MainWindow()
    {
        InitializeComponent();
        DarkMode.Apply(this);
        StateChanged += (_, _) => UpdateMaximizeRestoreIcon();
        FoldersGrid.ItemsSource = _folderRows;
        HistoryGrid.ItemsSource = _historyRows;
        QueueGrid.ItemsSource = _queueRows;
        RefreshFolders();
        RefreshHistory();

        if (_store.GetRoot() != null)
            _ = ScanLibraryAsync();
    }

    // ---- Custom title bar ----

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestoreButton_Click(sender, e);
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeRestoreIcon()
    {
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
    }

    private void RefreshFolders()
    {
        _folderRows.Clear();
        var shows = _store.Data.Folders
            .Where(f => !f.IsRoot)
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in shows)
            _folderRows.Add(new FolderRow { Entry = folder, IsNew = _newlyDiscoveredFolders.Contains(folder.Path) });

        var root = _store.GetRoot();
        RootPathText.Text = root != null ? $"Source: {root.Path}" : "No source added yet — click \"Add Folder...\" to pick your media root.";
    }

    private void RefreshHistory(string? filter = null)
    {
        _historyRows.Clear();
        var entries = _store.Data.PlayHistory.OrderByDescending(h => h.PlayedUtc).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
            entries = entries.Where(h => h.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var entry in entries)
            _historyRows.Add(new HistoryRow { Entry = entry });
    }

    // ---- Library tab ----

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your media root folder" };
        if (dialog.ShowDialog() != true) return;

        var existingRoot = _store.GetRoot();
        if (existingRoot != null && !string.Equals(existingRoot.Path, dialog.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            var confirm = MessageBox.Show(this,
                $"This replaces the current source:\n{existingRoot.Path}\n\nwith:\n{dialog.FolderName}\n\n" +
                "Every show discovered under the old source (and its files) will be removed from the library. " +
                "Play history is kept. Continue?",
                "Replace Library Source", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        _store.ReplaceRoot(dialog.FolderName);
        _store.Save();
        RefreshFolders();
    }

    private void ToggleExcludeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in FoldersGrid.SelectedItems.Cast<FolderRow>().ToList())
            row.Entry.IsExcluded = !row.Entry.IsExcluded;
        _store.Save();
        RefreshFolders();
    }

    private void ToggleEpisodicButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in FoldersGrid.SelectedItems.Cast<FolderRow>().ToList())
            row.Entry.IsEpisodic = !row.Entry.IsEpisodic;
        _store.Save();
        RefreshFolders();
    }

    private void EditTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var row = FoldersGrid.SelectedItem as FolderRow;
        if (row == null) return;

        var dialog = new TagsDialog(row.Entry.Tags) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        row.Entry.Tags = dialog.Tags;
        _store.Save();
        RefreshFolders();
    }

    private void ManageFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var row = FoldersGrid.SelectedItem as FolderRow;
        if (row == null) return;

        var files = _store.Data.Files.Where(f => string.Equals(f.FolderPath, row.Path, StringComparison.OrdinalIgnoreCase));
        var dialog = new FileExclusionDialog(row.Path, files) { Owner = this };
        dialog.ShowDialog();

        _store.Save();
    }

    private void ManageSubfoldersButton_Click(object sender, RoutedEventArgs e)
    {
        var row = FoldersGrid.SelectedItem as FolderRow;
        if (row == null) return;

        var files = _store.Data.Files.Where(f => string.Equals(f.FolderPath, row.Path, StringComparison.OrdinalIgnoreCase));
        var dialog = new SubfolderExclusionDialog(row.Path, files) { Owner = this };
        dialog.ShowDialog();

        _store.Save();
    }

    private void FoldersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FoldersGrid.SelectedItem is FolderRow)
            ManageFilesButton_Click(sender, e);
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e) => await ScanLibraryAsync();

    /// <summary>
    /// Scans the root for new/changed files and probes durations, off the UI thread so the
    /// window stays responsive. Runs automatically on launch (if a root is already set) and
    /// on demand via "Rescan Library". Shows are highlighted in the grid if they weren't in
    /// the library before this scan — newly found, unexcluded files are automatically part of
    /// the queue-eligible pool once they have a duration, no extra step needed.
    /// </summary>
    private async Task ScanLibraryAsync()
    {
        var root = _store.GetRoot();
        if (root == null) return;

        var foldersBefore = _store.Data.Folders.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        MainTabControl.IsEnabled = false;
        ScanStatusText.Text = "Scanning library for changes...";
        try
        {
            await Task.Run(() =>
            {
                FolderScanner.ScanIncludedFolders(_store);
                DurationProbe.ProbeMissingDurations(_store);
            });
            _store.Save();
        }
        finally
        {
            MainTabControl.IsEnabled = true;
        }

        _newlyDiscoveredFolders = _store.Data.Folders
            .Where(f => !f.IsRoot && !foldersBefore.Contains(f.Path))
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RefreshFolders();

        ScanStatusText.Text = _newlyDiscoveredFolders.Count > 0
            ? $"Found {_newlyDiscoveredFolders.Count} new show(s)/movie(s), highlighted below."
            : "Library up to date.";
    }

    // ---- History tab ----

    private void HistorySearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshHistory(HistorySearchBox.Text);
    }

    private void MarkUnplayedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in HistoryGrid.SelectedItems.Cast<HistoryRow>().ToList())
            _store.MarkUnplayed(row.FilePath);
        _store.Save();
        RefreshHistory(HistorySearchBox.Text);
    }

    // ---- Queue Builder tab ----

    private void GenerateQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TargetMinutesBox.Text, out var targetMinutes) || targetMinutes <= 0)
        {
            MessageBox.Show(this, "Enter a valid target runtime in minutes.", "Generate Queue");
            return;
        }

        _currentQueue = QueueBuilder.Build(_store, targetMinutes);
        _queueRows.Clear();
        foreach (var item in _currentQueue.Items)
            _queueRows.Add(new QueueRow { Item = item });

        var minutes = _currentQueue.TotalSeconds / 60;
        var shortfallNote = _currentQueue.MetLowerBound ? "" : " (not enough unplayed content to reach the target)";
        EstimatedRuntimeText.Text = $"Estimated runtime: {minutes:0.0} min{shortfallNote}";
    }

    private void ShowQueueToggle_Changed(object sender, RoutedEventArgs e)
    {
        QueueGrid.Visibility = ShowQueueToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveQueueItemButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in QueueGrid.SelectedItems.Cast<QueueRow>().ToList())
        {
            _currentQueue?.Items.Remove(row.Item);
            _queueRows.Remove(row);
        }

        if (_currentQueue != null)
        {
            var minutes = _currentQueue.TotalSeconds / 60;
            EstimatedRuntimeText.Text = $"Estimated runtime: {minutes:0.0} min";
        }
    }

    private void SendToVlcButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQueue == null || _currentQueue.Items.Count == 0)
        {
            MessageBox.Show(this, "Generate a queue first.", "Send to VLC");
            return;
        }

        var filePaths = _currentQueue.Items.Select(i => i.FilePath).ToList();
        if (!VlcLauncher.WritePlaylistAndLaunch(filePaths, out var error))
        {
            MessageBox.Show(this, error, "Send to VLC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store.MarkPlayed(filePaths);
        _store.Save();
        RefreshHistory(HistorySearchBox.Text);
    }
}
