using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TopRacerLauncher;

/// <summary>Строка командной палитры.</summary>
public sealed class PaletteItem
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public Action Run { get; init; } = () => { };
}

/// <summary>Быстрый поиск по играм и действиям (Ctrl+K).</summary>
public partial class CommandPalette : Window
{
    private readonly List<PaletteItem> _all;

    public CommandPalette(IEnumerable<PaletteItem> items)
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        _all = items.ToList();
        Results.ItemsSource = _all;
        if (_all.Count > 0) Results.SelectedIndex = 0;

        Loaded += (_, _) => Query.Focus();
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => Filter();

    private void Filter()
    {
        var query = Query.Text.Trim();

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all.Where(i => i.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Results.ItemsSource = filtered;
        Results.SelectedIndex = filtered.Count > 0 ? 0 : -1;

        Hint.Text = filtered.Count == 0
            ? "Ничего не найдено"
            : "Enter — выполнить, ↑↓ — выбор, Esc — закрыть";
    }

    private void Query_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Down:
                e.Handled = true;
                Move(1);
                break;

            case Key.Up:
                e.Handled = true;
                Move(-1);
                break;

            case Key.Enter:
                e.Handled = true;
                RunSelected();
                break;
        }
    }

    private void Move(int delta)
    {
        if (Results.Items.Count == 0) return;

        int index = Results.SelectedIndex + delta;
        if (index < 0) index = Results.Items.Count - 1;
        if (index >= Results.Items.Count) index = 0;

        Results.SelectedIndex = index;
    }

    private void Results_Click(object sender, MouseButtonEventArgs e) => RunSelected();

    private void RunSelected()
    {
        if (Results.SelectedItem is not PaletteItem item) return;

        Close();
        Application.Current.Dispatcher.BeginInvoke(item.Run);
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();
}
