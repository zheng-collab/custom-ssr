using System;
using System.Threading.Tasks;
using System.Windows;
using SecureGateway.Models;
using SecureGateway.Services;
using SecureGateway.Utils;

namespace SecureGateway.UI.ViewModels
{
    /// <summary>WPF flavour of the shared main view-model: dialogs, dispatcher, clipboard, editor.</summary>
    public class MainViewModel : MainViewModelBase
    {
        public MainViewModel() : base(new SystemProxyService())
        {
        }

        protected override void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        protected override Task ShowMessageAsync(string title, string message, MessageKind kind)
        {
            var icon = kind switch
            {
                MessageKind.Warning => MessageBoxImage.Warning,
                MessageKind.Error => MessageBoxImage.Error,
                _ => MessageBoxImage.Information
            };
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            return Task.CompletedTask;
        }

        protected override Task<bool> ConfirmAsync(string title, string message)
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        protected override Task<bool> EditServerAsync(ServerProfile server)
        {
            var dialog = new Views.ServerEditWindow(server) { Owner = Application.Current.MainWindow };
            return Task.FromResult(dialog.ShowDialog() == true);
        }

        protected override Task<string> GetClipboardTextAsync()
        {
            return Task.FromResult(Clipboard.ContainsText() ? Clipboard.GetText() : "");
        }

        protected override void ApplyAutoStart(bool enable) => AutoStartHelper.SetAutoStart(enable);
    }
}
