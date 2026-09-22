using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SecureGateway.Mac.Views
{
    /// <summary>Minimal themed message / confirm dialog (Avalonia has no built-in MessageBox).</summary>
    public class MessageWindow : Window
    {
        public enum Kind { Info, Warning, Error, Question }

        private bool _result;

        private MessageWindow(string title, string message, Kind kind, bool confirm)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.Parse("#1E1E2E"));

            var accent = kind switch
            {
                Kind.Warning => "#FAB387",
                Kind.Error => "#F38BA8",
                Kind.Question => "#89B4FA",
                _ => "#A6E3A1"
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 18)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            if (confirm)
            {
                var no = new Button { Content = "No", MinWidth = 90 };
                no.Click += (_, _) => { _result = false; Close(); };
                var yes = new Button { Content = "Yes", MinWidth = 90 };
                yes.Classes.Add("accent");
                yes.Click += (_, _) => { _result = true; Close(); };
                buttons.Children.Add(no);
                buttons.Children.Add(yes);
            }
            else
            {
                var ok = new Button { Content = "OK", MinWidth = 90 };
                ok.Classes.Add("accent");
                ok.Click += (_, _) => { _result = true; Close(); };
                buttons.Children.Add(ok);
            }

            var bar = new Border
            {
                Background = new SolidColorBrush(Color.Parse(accent)),
                Width = 4, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 14, 0)
            };

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            body.Children.Add(bar);
            var content = new StackPanel();
            content.Children.Add(text);
            content.Children.Add(buttons);
            Grid.SetColumn(content, 1);
            body.Children.Add(content);

            Content = new Border { Padding = new Thickness(22, 20), Child = body };
        }

        public static async Task ShowAsync(Window owner, string title, string message, Kind kind = Kind.Info)
        {
            var w = new MessageWindow(title, message, kind, confirm: false);
            if (owner != null) await w.ShowDialog(owner);
            else { w.Show(); await w.WaitClosedAsync(); }
        }

        public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
        {
            var w = new MessageWindow(title, message, Kind.Question, confirm: true);
            if (owner != null) await w.ShowDialog(owner);
            else { w.Show(); await w.WaitClosedAsync(); }
            return w._result;
        }

        private Task WaitClosedAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Closed += (_, _) => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
