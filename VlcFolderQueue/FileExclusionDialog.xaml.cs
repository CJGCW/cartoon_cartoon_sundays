using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using VlcFolderQueue.Data;
using VlcFolderQueue.Services;

namespace VlcFolderQueue;

public class FileExclusionRow
{
    public required FileEntry Entry { get; init; }
    public required string RelativePath { get; init; }
    public bool IsExcluded
    {
        get => Entry.IsExcluded;
        set => Entry.IsExcluded = value;
    }
}

public partial class FileExclusionDialog : Window
{
    private readonly List<FileExclusionRow> _allRows;
    private readonly ObservableCollection<FileExclusionRow> _visibleRows = new();

    public FileExclusionDialog(string folderPath, IEnumerable<FileEntry> files)
    {
        InitializeComponent();
        DarkMode.Apply(this);

        FolderNameText.Text = folderPath;

        var prefixLength = folderPath
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Length;

        _allRows = files
            .OrderBy(f => f.Path, NaturalPathComparer.Instance)
            .Select(f => new FileExclusionRow
            {
                Entry = f,
                RelativePath = f.Path.Length > prefixLength
                    ? f.Path.Substring(prefixLength).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    : f.Path
            })
            .ToList();

        FilesGrid.ItemsSource = _visibleRows;
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
        foreach (var row in FilesGrid.SelectedItems.Cast<FileExclusionRow>().ToList())
            row.IsExcluded = true;
        FilesGrid.Items.Refresh();
    }

    private void IncludeSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in FilesGrid.SelectedItems.Cast<FileExclusionRow>().ToList())
            row.IsExcluded = false;
        FilesGrid.Items.Refresh();
    }
}
