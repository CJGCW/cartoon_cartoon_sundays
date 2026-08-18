using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using VlcFolderQueue.Data;
using VlcFolderQueue.Services;

namespace VlcFolderQueue;

public class SubfolderExclusionRow
{
    public required string RelativePath { get; init; }
    public required List<FileEntry> Files { get; init; }
    public int FileCount => Files.Count;

    /// <summary>True only if every file currently under this subfolder is excluded.
    /// Setting it applies that state to every file under this subfolder at once.</summary>
    public bool IsExcluded
    {
        get => Files.All(f => f.IsExcluded);
        set { foreach (var f in Files) f.IsExcluded = value; }
    }
}

public partial class SubfolderExclusionDialog : Window
{
    private readonly List<SubfolderExclusionRow> _allRows;
    private readonly ObservableCollection<SubfolderExclusionRow> _visibleRows = new();

    public SubfolderExclusionDialog(string folderPath, IEnumerable<FileEntry> files)
    {
        InitializeComponent();
        DarkMode.Apply(this);

        FolderNameText.Text = folderPath;

        var prefix = folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        _allRows = files
            .GroupBy(f => System.IO.Path.GetDirectoryName(f.Path) ?? prefix, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > prefix.Length) // skip files sitting directly in the show folder itself
            .Select(g => new SubfolderExclusionRow
            {
                RelativePath = g.Key.Substring(prefix.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                Files = g.ToList()
            })
            .OrderBy(r => r.RelativePath, NaturalPathComparer.Instance)
            .ToList();

        SubfoldersGrid.ItemsSource = _visibleRows;
        ApplyFilter(null);
    }

    private void ApplyFilter(string? filter)
    {
        _visibleRows.Clear();
        var rows = string.IsNullOrWhiteSpace(filter)
            ? _allRows
            : _allRows.Where(r => r.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var row in rows)
            _visibleRows.Add(row);
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter(FilterBox.Text);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ExcludeSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in SubfoldersGrid.SelectedItems.Cast<SubfolderExclusionRow>().ToList())
            row.IsExcluded = true;
        SubfoldersGrid.Items.Refresh();
    }

    private void IncludeSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in SubfoldersGrid.SelectedItems.Cast<SubfolderExclusionRow>().ToList())
            row.IsExcluded = false;
        SubfoldersGrid.Items.Refresh();
    }
}
