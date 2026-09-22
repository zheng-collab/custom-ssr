using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecureGateway.Models;

namespace SecureGateway.Mac.Views
{
    public partial class ServerEditWindow : Window
    {
        private readonly ServerProfile _server;

        public ServerEditWindow(ServerProfile server)
        {
            InitializeComponent();
            _server = server;

            CmbProtocol.ItemsSource = Enum.GetValues<ProxyProtocol>();
            CmbTransport.ItemsSource = new[] { V2RayTransport.TCP, V2RayTransport.WebSocket, V2RayTransport.HTTP2, V2RayTransport.GRPC };
            CmbSecurity.ItemsSource = new[] { "auto", "aes-128-gcm", "chacha20-poly1305", "none", "zero" };
            CmbEncryption.ItemsSource = Enum.GetValues<ShadowsocksEncryption>();

            DataContext = server;
            UpdateProtocolPanels();
        }

        private void OnProtocolChanged(object sender, SelectionChangedEventArgs e) => UpdateProtocolPanels();

        private void UpdateProtocolPanels()
        {
            var isV2Ray = CmbProtocol.SelectedItem is ProxyProtocol p ? p == ProxyProtocol.V2Ray : _server.Protocol == ProxyProtocol.V2Ray;
            V2RayPanel.IsVisible = isV2Ray;
            ShadowsocksPanel.IsVisible = !isV2Ray;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            // Bindings commit on focus loss; make sure the focused field is written back first.
            FocusManager?.ClearFocus();

            if (string.IsNullOrWhiteSpace(_server.Address))
            {
                await MessageWindow.ShowAsync(this, "Validation", "Server address is required.", MessageWindow.Kind.Warning);
                return;
            }
            if (_server.Port <= 0 || _server.Port > 65535)
            {
                await MessageWindow.ShowAsync(this, "Validation", "Port must be between 1 and 65535.", MessageWindow.Kind.Warning);
                return;
            }
            if (_server.Protocol == ProxyProtocol.V2Ray && string.IsNullOrWhiteSpace(_server.V2RayUserId))
            {
                await MessageWindow.ShowAsync(this, "Validation", "V2Ray User ID is required.", MessageWindow.Kind.Warning);
                return;
            }
            if (_server.Protocol == ProxyProtocol.Shadowsocks && string.IsNullOrWhiteSpace(_server.SsPassword))
            {
                await MessageWindow.ShowAsync(this, "Validation", "Shadowsocks password is required.", MessageWindow.Kind.Warning);
                return;
            }

            Close(true);
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Close(false);
    }
}
