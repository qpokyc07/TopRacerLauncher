using System.Windows;

namespace TopRacerLauncher;

/// <summary>
/// Первый запуск: короткий мастер — киоск-режим, оверлей, достижения, автодополнение игр.
/// </summary>
public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();

        var s = App.Settings;
        KioskCheck.IsChecked = s.KioskMode;
        OverlayCheck.IsChecked = s.ControlsOverlay;
        AchievementsCheck.IsChecked = s.AchievementsEnabled;
        AutoAddCheck.IsChecked = s.AutoAddSteamGames;
        AutoScanCheck.IsChecked = s.AutoScanGames;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;

        s.KioskMode = KioskCheck.IsChecked == true;
        s.ControlsOverlay = OverlayCheck.IsChecked == true;
        s.AchievementsEnabled = AchievementsCheck.IsChecked == true;
        s.AutoAddSteamGames = AutoAddCheck.IsChecked == true;
        s.AutoScanGames = AutoScanCheck.IsChecked == true;
        s.SetupDone = true;

        App.SaveState();

        try
        {
            if (Owner is MainWindow main) main.ApplySetupChanges();
        }
        catch { /* настройки применятся при следующем запуске */ }

        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.SetupDone = true;
        App.SaveState();
        DialogResult = false;
    }
}
