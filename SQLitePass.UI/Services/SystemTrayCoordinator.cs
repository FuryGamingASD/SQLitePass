using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace SQLitePass.UI.Services
{
    public sealed class SystemTrayCoordinator : IDisposable
    {
        private TaskbarIcon? _trayIcon;
        private bool _isDisposed = false;

        public SystemTrayCoordinator(
            Action onShowRequested,
            Action onPasswordGeneratorRequested,
            Action onLockRequested,
            Action onExitRequested)
        {
            Icon trayIconImage = SystemIcons.Shield;
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/app.ico");
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using var s = streamInfo.Stream;
                    trayIconImage = new Icon(s);
                }
            }
            catch { }

            _trayIcon = new TaskbarIcon
            {
                Icon = trayIconImage,
                ToolTipText = "SQLitePass - Vault Protetto"
            };

            var contextMenu = new ContextMenu();

            var itemShow = new MenuItem
            {
                Header = "Mostra SQLitePass",
                FontWeight = FontWeights.Bold
            };
            itemShow.Click += (s, e) => onShowRequested?.Invoke();
            contextMenu.Items.Add(itemShow);

            var itemGen = new MenuItem { Header = "Generatore Password..." };
            itemGen.Click += (s, e) => onPasswordGeneratorRequested?.Invoke();
            contextMenu.Items.Add(itemGen);

            contextMenu.Items.Add(new Separator());

            var itemLock = new MenuItem { Header = "Blocca Vault" };
            itemLock.Click += (s, e) => onLockRequested?.Invoke();
            contextMenu.Items.Add(itemLock);

            contextMenu.Items.Add(new Separator());

            var itemExit = new MenuItem { Header = "Esci" };
            itemExit.Click += (s, e) => onExitRequested?.Invoke();
            contextMenu.Items.Add(itemExit);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.TrayMouseDoubleClick += (s, e) => onShowRequested?.Invoke();
        }

        public void ShowBalloonTip(string title, string message, BalloonIcon icon = BalloonIcon.Info)
        {
            _trayIcon?.ShowBalloonTip(title, message, icon);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
