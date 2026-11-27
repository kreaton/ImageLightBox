using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageLightBox.Core;

namespace ImageLightBox.Wpf;

/// <summary>Asks for a new file name, checked as you type, with the name but not the extension selected like in Explorer.</summary>
public partial class RenameWindow : Window
{
    private readonly string _path;

    public RenameWindow(Window owner, string path)
    {
        InitializeComponent();
        Owner = owner;
        _path = path;

        var name = Path.GetFileName(path);
        NameBox.Text = name;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.Select(0, Path.GetFileNameWithoutExtension(name).Length);
        };
    }

    public string NewName => NameBox.Text.Trim();

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        var problem = FileActions.NameProblem(_path, NewName);
        ProblemText.Text = problem ?? "";
        ProblemText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        RenameButton.IsEnabled = problem is null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (FileActions.NameProblem(_path, NewName) is null)
            DialogResult = true;
    }
}
