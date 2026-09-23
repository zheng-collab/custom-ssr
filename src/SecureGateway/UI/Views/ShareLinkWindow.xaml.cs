using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SecureGateway.Models;

namespace SecureGateway.UI.Views
{
    public partial class ShareLinkWindow : Window
    {
        private readonly string _link;

        public ShareLinkWindow(ServerProfile server, string link, byte[] qrPng)
        {
            InitializeComponent();
            _link = link;
            TxtTitle.Text = server.Name;
            TxtLink.Text = link;

            if (qrPng != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(qrPng);
                bmp.EndInit();
                bmp.Freeze();
                ImgQr.Source = bmp;
            }
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_link);
                BtnCopy.Content = "Copied";
            }
            catch
            {
                BtnCopy.Content = "Copy failed";
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
