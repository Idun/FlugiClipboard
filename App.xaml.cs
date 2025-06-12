using System;
using System.Windows;

namespace FlugiClipboard
{
    public partial class App : Application
    {
        private bool _silentStart = false;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // 设置应用程序关闭模式，确保即使没有可见窗口也不会退出
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // 检查命令行参数是否包含静默启动标志
                if (e.Args.Length > 0)
                {
                    foreach (string arg in e.Args)
                    {
                        if (arg.Equals("/silent", System.StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("-silent", System.StringComparison.OrdinalIgnoreCase))
                        {
                            _silentStart = true;
                            break;
                        }
                    }
                }

                // 创建主窗口
                _mainWindow = new MainWindow();
                MainWindow = _mainWindow;

                if (_silentStart)
                {
                    // 静默启动：完全隐藏窗口，只在系统托盘运行
                    _mainWindow.WindowState = WindowState.Minimized;
                    _mainWindow.ShowInTaskbar = false;
                    _mainWindow.Visibility = Visibility.Hidden;

                    // 重要：即使隐藏也要调用Show()来初始化窗口，然后立即隐藏
                    _mainWindow.Show();
                    _mainWindow.Hide();

                    // 确保窗口加载后保持隐藏状态
                    _mainWindow.Loaded += (s, args) =>
                    {
                        if (_silentStart && _mainWindow != null)
                        {
                            _mainWindow.Hide();
                            _mainWindow.WindowState = WindowState.Minimized;
                            _mainWindow.ShowInTaskbar = false;
                            _mainWindow.Visibility = Visibility.Hidden;
                        }
                    };
                }
                else
                {
                    // 正常启动：显示窗口
                    _mainWindow.Show();
                }

                base.OnStartup(e);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 清理系统托盘图标
            if (_mainWindow != null)
            {
                _mainWindow.CleanupNotifyIcon();
            }
            base.OnExit(e);
        }
    }
}
