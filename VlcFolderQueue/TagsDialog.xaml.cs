using System.Windows;
using System.Windows.Input;

namespace VlcFolderQueue;

public partial class TagsDialog : Window
{
    public List<string> Tags { get; private set; } = new();

    public TagsDialog(IEnumerable<string> existingTags)
    {
        InitializeComponent();
        DarkMode.Apply(this);
        TagsBox.Text = string.Join(", ", existingTags);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Tags = TagsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        DialogResult = true;
    }
}
