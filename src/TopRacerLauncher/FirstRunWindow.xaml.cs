using System.Windows;
using System.Windows.Input;

namespace TopRacerLauncher;

public partial class FirstRunWindow : Window
{
    private readonly bool _changeMode;

    /// <summary>changeMode=true — окно используется для смены пароля из админ-меню.</summary>
    public FirstRunWindow(bool changeMode = false)
    {
        InitializeComponent();
        _changeMode = changeMode;
        if (changeMode)
        {
            TitleText.Text = "Смена пароля администратора";
            SubtitleText.Text = "Введите новый пароль для входа и админ-меню (Ctrl+1).";
            SubmitButton.Content = "Применить";
        }
        Loaded += (_, _) => PasswordInput1.Focus();
    }

    private void Password_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => HideError();

    private void Password2_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submit_Click(sender, e);
        }
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        var p1 = PasswordInput1.Secret;
        var p2 = PasswordInput2.Secret;

        if (p1.Length < 4)
        {
            ShowError("Пароль должен содержать минимум 4 символа.");
            PasswordInput1.Focus();
            return;
        }
        if (!string.Equals(p1, p2, StringComparison.Ordinal))
        {
            ShowError("Пароли не совпадают.");
            PasswordInput2.Clear();
            PasswordInput2.Focus();
            return;
        }

        try
        {
            ConfigStore.SetPassword(App.Settings, p1);
            ConfigStore.Save(App.Settings);
            HistoryLog.Add("password_changed", detail: _changeMode ? "смена из админ-меню" : "первый запуск");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось сохранить пароль: " + ex.Message);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_changeMode)
        {
            DialogResult = false;
        }
        else
        {
            // Первый запуск: без пароля лаунчер не работает (закрытая система).
            var answer = MessageBox.Show(
                "Без пароля лаунчер не сможет запуститься.\n\nВыйти без создания пароля?",
                "TopRacerLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                DialogResult = false;
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
