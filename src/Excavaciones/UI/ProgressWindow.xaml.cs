using System.Windows;
using System.Windows.Threading;

namespace Excavaciones.UI;

public partial class ProgressWindow : Window
{
    public ProgressWindow() { InitializeComponent(); }

    public void Report(string message, int current, int total)
    {
        Dispatcher.Invoke(() =>
        {
            Message.Text = message;
            Bar.Maximum = Math.Max(1, total);
            Bar.Value = Math.Min(current, total);
            Detail.Text = total > 0 ? $"{current} / {total}" : string.Empty;
        }, DispatcherPriority.Background);
    }
}
