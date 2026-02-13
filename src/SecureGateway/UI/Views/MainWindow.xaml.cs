using System;
using System.ComponentModel;
using System.Windows;
using SecureGateway.UI.ViewModels;

namespace SecureGateway.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private bool _forceClose;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Auto-scroll log
            _viewModel.LogEntries.CollectionChanged += (s, e) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            };
        }

        public MainWindow(bool startMinimized) : this()
        {
            if (startMinimized)
            {
                WindowState = WindowState.Minimized;
                if (_viewModel.MinimizeToTray)
                    Hide();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (_viewModel.AutoConnect)
            {
                // Auto-connect after a brief delay
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    _viewModel.ToggleConnectionCommand.Execute(null);
                });
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (!_forceClose && _viewModel.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _viewModel.Dispose();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
                Hide();
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
