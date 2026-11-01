using System.Windows;

namespace TopRacerLauncher;

/// <summary>Короткая справка «как играть» в три шага.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
