using System.Windows;
using System.Windows.Threading;
using CmdWarden.Contracts;

namespace CmdWarden.ApprovalGate;

/// <summary>
/// Canary alarm (#29): a card in the lower right corner, on top of all windows, like a toast.
/// It does not take the focus and closes itself after one minute.
/// </summary>
public partial class AlarmWindow : Window
{
    public AlarmWindow(string text)
    {
        InitializeComponent();
        Heading.Text = $"{ProductInfo.Name}: canary token used";
        Body.Text = text;
        Loaded += (_, _) => ScreenPlacement.BottomRight(this);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => Close();
        timer.Start();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
