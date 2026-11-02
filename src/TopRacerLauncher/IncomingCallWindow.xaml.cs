using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>Окно входящего звонка в стиле лаунчера: имя зовущего, «Присоединиться» / «Отклонить».</summary>
public partial class IncomingCallWindow : Window
{
    private readonly DispatcherTimer _ring = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>Пользователь принял звонок.</summary>
    public bool Accepted { get; private set; }

    public IncomingCallWindow(string callerName)
    {
        InitializeComponent();

        CallerText.Text = string.IsNullOrWhiteSpace(callerName) ? "Друг" : callerName;

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        Loaded += (_, _) =>
        {
            try { SoundAlert.Warn(); } catch { /* звук не критичен */ }
            _ring.Start();
            _timeout.Start();
            AcceptButton.Focus();
        };

        _ring.Tick += (_, _) =>
        {
            try { SoundAlert.Warn(); } catch { /* звук не критичен */ }
        };

        _timeout.Tick += (_, _) => Decline();

        Closed += (_, _) =>
        {
            _ring.Stop();
            _timeout.Stop();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape) Decline();
        else if (e.Key == Key.Enter) Accept();
    }

    private bool _answered;

    private void Accept()
    {
        if (_answered) return;
        _answered = true;
        Accepted = true;
        DialogResult = true;
    }

    private void Decline()
    {
        if (_answered) return;
        _answered = true;
        Accepted = false;
        DialogResult = false;
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => Accept();

    private void Decline_Click(object sender, RoutedEventArgs e) => Decline();
}
