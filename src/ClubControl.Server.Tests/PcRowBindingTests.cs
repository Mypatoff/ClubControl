using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using ClubControl.Shared;

namespace ClubControl.Server.Tests;

/// <summary>
/// Renders a real grouped ListBox like the main panel's and checks that a card on screen shows the new
/// values after <see cref="PcRow.CopyFrom"/> - the panel no longer rebuilds its cards every second.
/// </summary>
public class PcRowBindingTests
{
    private static PcRow Row(string remaining, int id = 1, int number = 1) => new()
    {
        Id = id, MachineGuid = Guid.NewGuid(), IsOnline = true, Enabled = true, IsManual = false, Kind = ResourceKind.Computer,
        Number = number, TariffId = 1, State = PcState.PrepaySession, KindIcon = "", TariffDisplay = "", StateDisplay = "",
        SessionTypeDisplay = "", AmountDisplay = "", StartDisplay = "", EndDisplay = "", RemainingDisplay = remaining,
    };

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new Exception("Failed on the UI thread: " + error.Message, error);
    }

    private static void Flush() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    [Fact]
    public void A_card_in_a_grouped_sorted_list_shows_new_values_after_CopyFrom()
    {
        RunOnStaThread(() =>
        {
            var first = Row("10:00", id: 1, number: 1);
            var second = Row("20:00", id: 2, number: 2);

            // The same view the main panel builds (MainWindow.BuildCardView).
            var view = new ListCollectionView(new List<PcRow> { first, second });
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PcRow.IsOther)));
            view.SortDescriptions.Add(new SortDescription(nameof(PcRow.IsOther), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(PcRow.Number), ListSortDirection.Ascending));

            var list = new ListBox
            {
                ItemsSource = view,
                ItemTemplate = (DataTemplate)XamlReader.Parse(
                    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding PrimaryDisplay}' /></DataTemplate>"),
            };
            list.GroupStyle.Add(new GroupStyle());
            // Shown for real (off screen), as in the app, so the template is applied and the cards are generated.
            var window = new Window { Content = list, Left = -10000, Top = -10000, Width = 400, Height = 400, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Show();
            Flush();
            Flush();

            var texts = Descendants<TextBlock>(list).Select(t => t.Text).Where(t => t.Contains(':')).ToList();
            Assert.Equal(new[] { "10:00", "20:00" }, texts);

            first.CopyFrom(Row("09:59", id: 1, number: 1));
            Flush();

            texts = Descendants<TextBlock>(list).Select(t => t.Text).Where(t => t.Contains(':')).ToList();
            Assert.Equal(new[] { "09:59", "20:00" }, texts);
            window.Close();
        });
    }
}
