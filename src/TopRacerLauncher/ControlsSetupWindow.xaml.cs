using System.Windows;

namespace TopRacerLauncher;

/// <summary>
/// Настройка клавиш оверлея для одной игры: открывается правой кнопкой по карточке.
/// </summary>
public partial class ControlsSetupWindow : Window
{
    private readonly GameItem _game;

    public ControlsSetupWindow(GameItem game)
    {
        InitializeComponent();

        _game = game;

        HeaderText.Text = "Оверлей: " + game.Title;
        KeysBox.Text = game.Controls;
        KeysBox.TextChanged += (_, _) => UpdatePreview();

        UpdatePreview();
        Loaded += (_, _) => KeysBox.Focus();
    }

    private void UpdatePreview()
    {
        var keys = GameControls.ParseKeys(KeysBox.Text);

        PreviewText.Text = keys.Count == 0
            ? "Клавиши не выбраны — оверлей возьмёт набор по жанру игры."
            : "Будет подсвечено: " + string.Join("  ", keys.Select(GameControls.Label));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _game.Controls = KeysBox.Text.Trim();
        App.SaveState();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
