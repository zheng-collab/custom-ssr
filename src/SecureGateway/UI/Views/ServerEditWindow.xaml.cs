using System.Windows;
using SecureGateway.Models;

namespace SecureGateway.UI.Views
{
    public partial class ServerEditWindow : Window
    {
        private readonly ServerProfile _server;

        public ServerEditWindow(ServerProfile server)
        {
            InitializeComponent();
            _server = server;
            DataContext = server;
            UpdateProtocolPanels();
        }

        private void OnProtocolChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateProtocolPanels();
        }

        private void UpdateProtocolPanels()
        {
            if (V2RayPanel == null || ShadowsocksPanel == null) return;

            if (_server.Protocol == ProxyProtocol.V2Ray)
            {
                V2RayPanel.Visibility = Visibility.Visible;
                ShadowsocksPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                V2RayPanel.Visibility = Visibility.Collapsed;
                ShadowsocksPanel.Visibility = Visibility.Visible;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_server.Address))
            {
                MessageBox.Show("Server address is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_server.Port <= 0 || _server.Port > 65535)
            {
                MessageBox.Show("Port must be between 1 and 65535.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_server.Protocol == ProxyProtocol.V2Ray && string.IsNullOrWhiteSpace(_server.V2RayUserId))
            {
                MessageBox.Show("V2Ray User ID is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_server.Protocol == ProxyProtocol.Shadowsocks && string.IsNullOrWhiteSpace(_server.SsPassword))
            {
                MessageBox.Show("Shadowsocks password is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
