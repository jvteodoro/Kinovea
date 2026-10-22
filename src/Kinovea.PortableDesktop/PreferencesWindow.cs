using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kinovea.PortableDesktop.Presentation;

namespace Kinovea.PortableDesktop;

internal sealed class PreferencesWindow : Window
{
    private readonly ComboBox culture = new() { ItemsSource = new[] { "en", "pt-BR" }, Width = 180 };
    private readonly ComboBox theme = new() { ItemsSource = new[] { "Dark", "Light" }, Width = 180 };
    private readonly ComboBox shortcut = new() { ItemsSource = new[] { "Ctrl+O", "Ctrl+Shift+O" }, Width = 180 };
    private readonly NumericUpDown captureInterval = new() { Minimum = 10, Maximum = 1000, Increment = 10, Width = 180 };
    private readonly PortableApplicationState state;

    public PreferencesWindow(PortableApplicationState state)
    {
        this.state = state;
        PortableDesktopStrings strings = new(state.UiCulture);
        Title = strings.PreferencesTitle;
        Width = 440;
        Height = 330;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        culture.SelectedItem = state.UiCulture;
        theme.SelectedItem = state.Theme;
        shortcut.SelectedItem = state.OpenShortcut;
        captureInterval.Value = state.SyntheticCaptureIntervalMilliseconds;

        var save = new Button { Content = strings.Save, MinWidth = 96 };
        var cancel = new Button { Content = strings.Cancel, MinWidth = 96 };
        save.Click += (_, _) =>
        {
            state.UiCulture = culture.SelectedItem as string ?? "en";
            state.Theme = theme.SelectedItem as string ?? "Dark";
            state.OpenShortcut = shortcut.SelectedItem as string ?? "Ctrl+O";
            state.SyntheticCaptureIntervalMilliseconds = decimal.ToInt32(captureInterval.Value ?? 40);
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);

        var form = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        form.Children.Add(Row(strings.Culture, culture));
        form.Children.Add(Row(strings.Theme, theme));
        form.Children.Add(Row(strings.OpenShortcut, shortcut));
        form.Children.Add(Row(strings.CaptureInterval, captureInterval));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        form.Children.Add(actions);
        Content = form;
    }

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }
}
