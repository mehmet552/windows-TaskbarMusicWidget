using System;
using System.IO;
using System.Windows;
using System.Drawing;
using System.Threading.Tasks;

namespace TaskbarMusicWidget
{
    public partial class App : Application 
    {
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            InitNotifyIcon();

            AppDomain.CurrentDomain.UnhandledException += (s, args) => 
            {
                LogException(args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) => 
            {
                LogException(args.Exception);
                args.Handled = true; 
            };

            TaskScheduler.UnobservedTaskException += (s, args) => 
            {
                LogException(args.Exception);
                args.SetObserved();
            };
        }

        private void InitNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "Taskbar Music Widget";
            _notifyIcon.Visible = true;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                try { _notifyIcon.Icon = new Icon(iconPath); }
                catch { _notifyIcon.Icon = SystemIcons.Application; }
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Çıkış Yap");
            exitItem.Click += (s, args) => 
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                Current.Shutdown();
            };
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnExit(e);
        }

        private void LogException(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(logFile, $"[{DateTime.Now}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch { }
        }
    }
}