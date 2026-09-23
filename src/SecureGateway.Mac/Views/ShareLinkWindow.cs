using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SecureGateway.Models;

namespace SecureGateway.Mac.Views
{
    /// <summary>Share link + QR code for phone clients (v2rayNG, Shadowsocks, Shadowrocket).</summary>
    public class ShareLinkWindow : Window
    {
        public ShareLinkWindow(ServerProfile server, string link, byte[] qrPng)
        {
            Title = "Share Link / QR Code";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(24) };

            root.Children.Add(new TextBlock { Text = server.Name, FontSize = 16, FontWeight = FontWeight.Bold });
            root.Children.Add(new TextBlock
            {
                Text = "Scan with your phone's VPN app (Android: v2rayNG or Shadowsocks; iPhone: Shadowrocket) or copy the link. " +
                       "Anyone with this link can use the server — share it only with staff.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A6ADC8")),
                Margin = new Thickness(0, 4, 0, 14)
            });

            var qrBox = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            if (qrPng != null)
                qrBox.Child = new Image { Source = new Bitmap(new MemoryStream(qrPng)), Width = 260, Height = 260 };
            root.Children.Add(qrBox);

            var linkBox = new TextBox
            {
                Text = link, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                FontFamily = new FontFamily("Menlo, Consolas, monospace"), MaxHeight = 90,
                Margin = new Thickness(0, 14, 0, 0)
            };
            root.Children.Add(linkBox);

            var copy = new Button { Content = "Copy link", MinWidth = 110 };
            copy.Classes.Add("accent");
            copy.Click += async (_, _) =>
            {
                try
                {
                    if (Clipboard != null) { await Clipboard.SetTextAsync(link); copy.Content = "Copied"; }
                }
                catch { copy.Content = "Copy failed"; }
            };
            var close = new Button { Content = "Close", MinWidth = 90 };
            close.Click += (_, _) => Close();

            root.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10, Margin = new Thickness(0, 14, 0, 0), Children = { copy, close }
            });

            Content = root;
        }
    }
}
