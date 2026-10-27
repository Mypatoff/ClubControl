using System.Collections.Generic;
using System.Windows;
using ClubControl.Server.Resources;

namespace ClubControl.Server.Windows;

/// <summary>One PC in the stop dialog: its name, when it started and how long it ran, the unused money and the time left (already formatted for display).</summary>
public record StopPrepayLine(string Title, string Money, string TimeLeft, string UnusedLabel, string Items = "", string Started = "");

/// <summary>
/// Confirms stopping prepay sessions early and shows how much prepaid money is still unused on each
/// (SPEC 5: only the time used is recorded as income; the unused money shown here is handed back).
/// DialogResult is true when the admin presses Stop.
/// </summary>
public partial class StopPrepayWindow : Window
{
    public StopPrepayWindow(List<StopPrepayLine> lines, string totalMoney, int itemsDueUzs = 0)
    {
        InitializeComponent();

        Title = Strings.Get("ToolbarStop");
        HeaderText.Text = Strings.Get("StopPrepayHeader");
        LinesList.ItemsSource = lines;

        TotalLabel.Text = Strings.Get("StopPrepayTotal");
        TotalText.Text = totalMoney;
        TotalCard.Visibility = lines.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Items added during the session are not covered by the prepayment: the admin collects them now.
        ItemsDueLabel.Text = Strings.Get("ItemsDueLabel");
        ItemsDueText.Text = $"{itemsDueUzs:N0} UZS";
        ItemsDueCard.Visibility = itemsDueUzs > 0 ? Visibility.Visible : Visibility.Collapsed;

        NoteText.Text = Strings.Get("StopPrepayRefundNote");
        CancelButton.Content = Strings.Get("Cancel");
        StopButton.Content = Strings.Get("ToolbarStop");
    }

    private void OnStop(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
