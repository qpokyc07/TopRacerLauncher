using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>Красное всплывающее уведомление о новом сообщении.</summary>
public partial class NoticeWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly Action? _onClick;

    public NoticeWindow(string title, string body, Action? onClick = null)
    {
        InitializeComponent();

        TitleText.Text = title;
        BodyText.Text = body;
        _onClick = onClick;

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 24;
            Top = area.Bottom - ActualHeight - 24;
            _timer.Start();
        };

        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
        MouseLeftButtonUp += (_, _) =>
        {
            _onClick?.Invoke();
            Close();
        };
    }
}
