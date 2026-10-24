using System;
using System.Windows;
using ClubControl.Server.Data.Models;

namespace ClubControl.Server;

/// <summary>
/// Swaps the single merged theme dictionary at the application level. Implicitly-styled controls
/// (every Button/TextBox/DataGrid/etc. with no explicit Style attribute) pick this up live; windows
/// that set brush-valued properties directly in markup (e.g. MainWindow's card borders) do not, and
/// need to be recreated - see MainWindow.CloseForReal and App.ApplyThemeAndRefreshWindows.
/// </summary>
public static class ThemeManager
{
    public static void Apply(UiTheme theme)
    {
        var uri = new Uri(theme == UiTheme.Dark ? "Themes/DarkTheme.xaml" : "Themes/Theme.xaml", UriKind.Relative);
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
    }
}
