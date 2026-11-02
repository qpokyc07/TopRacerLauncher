using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>Режимы окна входа.</summary>
public enum GateMode
{
    /// <summary>Обычный вход при старте.</summary>
    Login,

    /// <summary>Экран ограничения: время истекло, нужен пароль.</summary>
    Restricted,

    /// <summary>Запрос пароля перед админ-меню (Ctrl+1).</summary>
    AdminPrompt,
}

public partial class LoginWindow : Window
{
    private readonly GateMode _mode;
    private int _failedAttempts;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _lockSecondsLeft;

    public LoginWindow(GateMode mode)
    {
        InitializeComponent();
        _mode = mode;
        ApplyMode();
        Loaded += (_, _) => PasswordInput.Focus();

        // Закрыть лаунчер без пароля нельзя: перехватываем Alt+F4 и крестик.
        Closing += (_, e) =>
        {
            if (_mode != GateMode.AdminPrompt && DialogResult != true)
                e.Cancel = true;
        };
    }

    private void ApplyMode()
    {
        switch (_mode)
        {
            case GateMode.Restricted:
                TitleText.Text = "Доступ ограничен";
                SubtitleText.Text = "Время игры истекло. Игра закрыта.\nВведите пароль администратора, чтобы начать новый сеанс.";
                SubmitButton.Content = "Разблокировать";
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Collapsed;
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x2A, 0x2A));
                IconText.Text = "\u26D4"; // ⛔
                break;

            case GateMode.AdminPrompt:
                TitleText.Text = "Админ-меню";
                SubtitleText.Text = "Требуется пароль администратора.\nCtrl+1 — установка времени и список игр.";
                SubmitButton.Content = "Продолжить";
                CancelButton.Content = "Отмена";
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3F, 0x5F));
                IconText.Text = "\u2699"; // ⚙
                break;

            default:
                // Обычный вход: крестик доступен, чтобы можно было закрыть приложение без пароля.
                TitleText.Text = "Вход в TopRacerLauncher";
                SubtitleText.Text = "Введите пароль администратора";
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                SubmitButton.Content = "Войти";
                break;
        }
    }

    private void Password_KeyDown(object sender, KeyEventArgs e)
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

        if (_lockSecondsLeft > 0) return;

        var password = PasswordInput.Secret;
        if (ConfigStore.VerifyPassword(App.Settings, password))
        {
            _failedAttempts = 0;
            HistoryLog.Add(_mode == GateMode.AdminPrompt ? "admin_ok" : "unlock_ok");
            DialogResult = true;
            return;
        }

        _failedAttempts++;
        PasswordInput.Clear();

        // Слышно даже из другой комнаты: кто-то пытается подобрать пароль.
        SoundAlert.Denied();
        HistoryLog.Add(
            _mode == GateMode.AdminPrompt ? "admin_fail" : "unlock_fail",
            detail: "попытка " + _failedAttempts);

        if (_failedAttempts >= 3)
        {
            StartLockout(10);
            ShowError("Слишком много попыток. Подождите 10 секунд.");
        }
        else
        {
            ShowError("Неверный пароль.");
            PasswordInput.Focus();
        }
    }

    private void StartLockout(int seconds)
    {
        _lockSecondsLeft = seconds;
        PasswordInput.IsEnabled = false;
        SubmitButton.IsEnabled = false;
        _lockTimer.Stop();
        _lockTimer.Tick -= OnLockTick;
        _lockTimer.Tick += OnLockTick;
        _lockTimer.Start();
    }

    private void OnLockTick(object? sender, EventArgs e)
    {
        _lockSecondsLeft--;
        if (_lockSecondsLeft <= 0)
        {
            _lockTimer.Stop();
            _lockTimer.Tick -= OnLockTick;
            PasswordInput.IsEnabled = true;
            SubmitButton.IsEnabled = true;
            HideError();
            PasswordInput.Focus();
        }
        else
        {
            ErrorText.Text = $"Слишком много попыток. Подождите {_lockSecondsLeft} сек.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
