using System.Windows;

namespace TopRacerLauncher;

/// <summary>Окно акции: бесплатные минуты за подписку на соцсети.</summary>
public partial class PromoWindow : Window
{
    public PromoWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
