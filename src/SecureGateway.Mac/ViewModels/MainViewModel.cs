using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using SecureGateway.Mac.Platform;
using SecureGateway.Mac.Views;
using SecureGateway.Models;
using SecureGateway.UI.ViewModels;

namespace SecureGateway.Mac.ViewModels
{
    /// <summary>Avalonia flavour of the shared main view-model.</summary>
    public class MainViewModel : MainViewModelBase
    {
        /// <summary>Owner for dialogs; set by MainWindow.</summary>
        public Window Owner { get; set; }

        public MainViewModel() : base(new MacSystemProxyService())
        {
        }

        protected override void RunOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }

        protected override Task ShowMessageAsync(string title, string message, MessageKind kind)
        {
            var k = kind switch
            {
                MessageKind.Warning => MessageWindow.Kind.Warning,
                MessageKind.Error => MessageWindow.Kind.Error,
                _ => MessageWindow.Kind.Info
            };
            return MessageWindow.ShowAsync(Owner, title, message, k);
        }

        protected override Task<bool> ConfirmAsync(string title, string message)
            => MessageWindow.ConfirmAsync(Owner, title, message);

        protected override async Task<bool> EditServerAsync(ServerProfile server)
        {
            var dialog = new ServerEditWindow(server);
            return Owner != null ? await dialog.ShowDialog<bool>(Owner) : false;
        }

        protected override async Task<string> GetClipboardTextAsync()
        {
            var clipboard = Owner?.Clipboard;
            if (clipboard == null) return "";
            return await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard) ?? "";
        }

        protected override void ApplyAutoStart(bool enable) => MacAutoStart.Set(enable);
    }
}
