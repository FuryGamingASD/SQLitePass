using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services
{
    public sealed class SystemTrayCoordinator : IDisposable
    {
        private TaskbarIcon? _trayIcon;
        private bool _isDisposed = false;
        private void LanguageChanged(object? sender, EventArgs e)
        { if (_trayIcon != null) _trayIcon.ToolTipText = LocalizationManager.GetString("TrayTooltip"); }

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
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or ExternalException)
            {
                AppLogger.LogWarning("Tray icon could not be loaded; the system fallback icon is active (" + exception.GetType().Name + ").");
            }

            _trayIcon = new TaskbarIcon
            {
                Icon = trayIconImage,
                ToolTipText = LocalizationManager.GetString("TrayTooltip")
            };

            var contextMenu = new ContextMenu();

            var itemShow = new MenuItem
            {
                FontWeight = FontWeights.Bold
            };
            itemShow.SetResourceReference(HeaderedItemsControl.HeaderProperty, "TrayShow");
            itemShow.Click += (s, e) => onShowRequested?.Invoke();
            contextMenu.Items.Add(itemShow);

            var itemGen = new MenuItem(); itemGen.SetResourceReference(HeaderedItemsControl.HeaderProperty, "GeneratorTitle");
            itemGen.Click += (s, e) => onPasswordGeneratorRequested?.Invoke();
            contextMenu.Items.Add(itemGen);

            contextMenu.Items.Add(new Separator());

            var itemLock = new MenuItem(); itemLock.SetResourceReference(HeaderedItemsControl.HeaderProperty, "TbLock");
            itemLock.Click += (s, e) => onLockRequested?.Invoke();
            contextMenu.Items.Add(itemLock);

            contextMenu.Items.Add(new Separator());

            var itemExit = new MenuItem(); itemExit.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Exit");
            itemExit.Click += (s, e) => onExitRequested?.Invoke();
            contextMenu.Items.Add(itemExit);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.TrayMouseDoubleClick += (s, e) => onShowRequested?.Invoke();
            LocalizationManager.LanguageChanged += LanguageChanged;
        }

        public void ShowBalloonTip(string title, string message, BalloonIcon icon = BalloonIcon.Info)
        {
            _trayIcon?.ShowBalloonTip(title, message, icon);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            LocalizationManager.LanguageChanged -= LanguageChanged;

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
