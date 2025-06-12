using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JiebaNet.Segmenter;
using WinForms = System.Windows.Forms;

namespace FlugiClipboard
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ClipboardItem> _clipboardHistory;
        private JiebaSegmenter? _segmenter; // 延迟加载
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private HwndSource? _hwndSource;
        private WinForms.NotifyIcon? _notifyIcon;
        private IntPtr _previousForegroundWindow = IntPtr.Zero;
        private bool _singleClickPaste = false;
        private bool _doubleClickPaste = true;
        private int _maxItems = 8; // 进一步减少默认最大项目数以节省内存
        private uint _hotkeyModifiers = MOD_CONTROL | MOD_ALT;
        private uint _hotkeyKey = VK_C;
        private bool _saveHistoryEnabled = false;
        private string _historyFolderPath = "";

        // 智能文字交换功能相关
        private bool _textSwapEnabled = true; // 默认启用智能文字交换功能
        private uint _textSwapHotkeyModifiers = MOD_CONTROL;
        private uint _textSwapHotkeyKey = VK_Q;
        private bool _isPerformingTextSwap = false; // 防止递归调用和剪贴板监控冲突
        private bool _disableTextSwapHotkey = false; // 完全禁用智能文字交换热键注册

        // 开机启动功能相关
        private bool _startupEnabled = false;

        // AI翻译功能相关
        private string _aiProvider = "ollama";
        private string _aiApiUrl = "http://localhost:11434/v1/chat/completions";
        private string _aiApiKey = "";
        private string _aiModel = "";
        private string _aiPrompt = "你是一个中英文翻译专家，将用户输入的中文翻译成英文，或将用户输入的英文翻译成中文。对于非中文内容，它将提供中文翻译结果。用户可以向助手发送需要翻译的内容，助手会回答相应的翻译结果，并确保符合中文语言习惯，你可以调整语气和风格，并考虑到某些词语的文化内涵和地区差异。同时作为翻译家，需将原文翻译成具有信达雅标准的译文。\"信\" 即忠实于原文的内容与意图；\"达\" 意味着译文应通顺易懂，表达清晰；\"雅\" 则追求译文的文化审美和语言的优美。目标是创作出既忠于原作精神，又符合目标语言文化和读者审美的翻译。";
        private uint _aiTranslateHotkeyModifiers = MOD_CONTROL;
        private uint _aiTranslateHotkeyKey = VK_T;
        
        // 窗口尺寸相关
        private double _savedWindowWidth = 380;
        private double _savedWindowHeight = 600;

        // 内存优化相关
        private System.Timers.Timer? _memoryCleanupTimer;
        private WeakReference<JiebaSegmenter>? _segmenterRef;

        // 历史保存相关常量
        private const int MAX_HISTORY_FILES = 500; // 减少文件数量限制
        private const long MAX_TEXT_FILE_SIZE = 512 * 1024; // 512KB
        private const long MAX_IMAGE_FILE_SIZE = 2 * 1024 * 1024; // 2MB
        private const long MAX_TOTAL_FOLDER_SIZE = 50 * 1024 * 1024; // 50MB

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPoint(IntPtr hWndParent, POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT point, uint uFlags);

        private const byte VK_CONTROL = 0x11;
        private const int VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const uint CWP_ALL = 0x0000;
        private const uint CWP_SKIPINVISIBLE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int HOTKEY_ID = 9000;
        private const int TEXT_SWAP_HOTKEY_ID = 9001;
        // AI翻译快捷键将使用新的ID和逻辑
        private const int AI_TRANSLATE_HOTKEY_NEW_ID = 9003;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008; // Windows键
        private const uint VK_C = 0x43; // C键
        private const uint VK_S = 0x53; // S键
        private const uint VK_A = 0x41; // A键
        private const uint VK_Q = 0x51; // Q键
        private const uint VK_T = 0x54; // T键
        private const uint VK_SPACE = 0x20; // 空格键
        private const uint VK_CAPITAL = 0x14; // Caps Lock键
        private const int WM_HOTKEY = 0x0312;

        public MainWindow()
        {
            InitializeComponent();
            _clipboardHistory = new ObservableCollection<ClipboardItem>();
            // _segmenter 延迟加载，不在构造函数中创建

            // 设置AI翻译快捷键默认值 - 改为Ctrl+T避免与系统快捷键冲突
            _aiTranslateHotkeyNewModifiers = MOD_CONTROL;
            _aiTranslateHotkeyNewKey = VK_T;
            _aiTranslateHotkeyModifiers = MOD_CONTROL;
            _aiTranslateHotkeyKey = VK_T;

            // 加载设置
            try
            {
                LoadSettings();
            }
            catch
            {
                // 使用默认设置继续
            }

            ClipboardItemsControl.ItemsSource = _clipboardHistory;

            // 启动剪贴板监控
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            // 添加窗口大小变化事件监听
            SizeChanged += MainWindow_SizeChanged;

            // 初始化系统托盘
            try
            {
                InitializeNotifyIcon();
            }
            catch
            {
                // 继续运行，即使托盘初始化失败
            }

            // 设置窗口状态变化事件
            StateChanged += MainWindow_StateChanged;

            // 初始化内存清理定时器
            try
            {
                InitializeMemoryCleanup();
            }
            catch
            {
                // 继续运行，即使内存清理初始化失败
            }

            // 确保窗口初始状态正确
            WindowState = WindowState.Normal;
            Topmost = true;
        }



        private void InitializeMemoryCleanup()
        {
            // 设置低优先级以减少系统资源占用
            MemoryOptimizer.SetLowPriority();

            _memoryCleanupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(3).TotalMilliseconds); // 每3分钟清理一次
            _memoryCleanupTimer.Elapsed += (s, e) => PerformMemoryCleanup();
            _memoryCleanupTimer.AutoReset = true;
            _memoryCleanupTimer.Start();
        }

        private void PerformMemoryCleanup()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 更激进的清理策略以节省内存
                    var cutoffTime = DateTime.Now.AddMinutes(-20); // 减少到20分钟
                    var itemsToRemove = _clipboardHistory
                        .Where(item => !item.IsPinned && item.Timestamp < cutoffTime)
                        .ToList();

                    // 批量删除以提高性能
                    foreach (var item in itemsToRemove)
                    {
                        _clipboardHistory.Remove(item);
                    }

                    // 如果内存使用过高，进行更激进的清理
                    if (MemoryOptimizer.IsMemoryUsageHigh(40)) // 降低阈值
                    {
                        // 清理更多旧项目，只保留最近3个
                        var recentCutoff = DateTime.Now.AddMinutes(-5);
                        var moreItemsToRemove = _clipboardHistory
                            .Where(item => !item.IsPinned && item.Timestamp < recentCutoff)
                            .Skip(3) // 只保留最近3个
                            .ToList();

                        foreach (var item in moreItemsToRemove)
                        {
                            _clipboardHistory.Remove(item);
                        }
                    }

                    // 强制限制最大项目数
                    if (_clipboardHistory.Count > _maxItems)
                    {
                        var excessItems = _clipboardHistory
                            .Where(item => !item.IsPinned)
                            .Skip(_maxItems)
                            .ToList();

                        foreach (var item in excessItems)
                        {
                            _clipboardHistory.Remove(item);
                        }
                    }
                });

                // 使用优化器进行内存清理，降低阈值
                MemoryOptimizer.MonitorAndCleanup(30);
            }
            catch
            {
                // 忽略内存清理失败
            }
        }

        private JiebaSegmenter GetSegmenter()
        {
            // 延迟加载分词器 - 优化内存使用
            if (_segmenter == null)
            {
                // 检查弱引用是否还有效
                if (_segmenterRef?.TryGetTarget(out var existingSegmenter) == true)
                {
                    _segmenter = existingSegmenter;
                }
                else
                {
                    _segmenter = new JiebaSegmenter();
                    _segmenterRef = new WeakReference<JiebaSegmenter>(_segmenter);

                    // 强制垃圾回收以清理可能的旧分词器实例
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            return _segmenter;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建窗口源用于接收剪贴板消息
                var windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (windowSource != null)
                {
                    _hwndSource = windowSource;
                    windowSource.AddHook(WndProc);

                    // 注册剪贴板监听
                    AddClipboardFormatListener(windowSource.Handle);

                    // 注册全局热键
                    RegisterGlobalHotkey();
                }
            }
            catch
            {
                // 忽略初始化错误，程序仍可正常使用
            }
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                System.Drawing.Icon? customIcon = null;
                try
                {
                    // 尝试加载自定义图标
                    string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico", "ilo.ico");
                    if (System.IO.File.Exists(iconPath))
                    {
                        customIcon = new System.Drawing.Icon(iconPath);
                    }
                }
                catch
                {
                    // 忽略图标加载失败，使用默认图标
                }

                _notifyIcon = new WinForms.NotifyIcon
                {
                    Icon = customIcon ?? System.Drawing.SystemIcons.Application,
                    Text = "FlugiClipboard 剪贴板工具",
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => ShowWindow();

                var contextMenu = new WinForms.ContextMenuStrip();
                contextMenu.Items.Add("显示窗口", null, (s, e) => ShowWindow());
                contextMenu.Items.Add("设置", null, (s, e) => OpenSettings());
                contextMenu.Items.Add("-"); // 分隔线
                contextMenu.Items.Add("退出", null, (s, e) => ExitApplication());
                _notifyIcon.ContextMenuStrip = contextMenu;

                // 显示托盘通知，确认程序已启动
                _notifyIcon.ShowBalloonTip(2000, "FlugiClipboard", "剪贴板工具已启动，双击托盘图标显示窗口", WinForms.ToolTipIcon.Info);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化系统托盘失败: {ex.Message}");
                // 即使托盘初始化失败，程序也应该继续运行
            }
        }

        // 清理系统托盘图标
        public void CleanupNotifyIcon()
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        // 安全退出应用程序
        private void ExitApplication()
        {
            try
            {
                CleanupNotifyIcon();
                System.Windows.Application.Current.Shutdown();
            }
            catch
            {
                // 强制退出
                System.Environment.Exit(0);
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _notifyIcon!.ShowBalloonTip(2000, "剪贴板工具", "程序已最小化到系统托盘", WinForms.ToolTipIcon.Info);
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 只在窗口状态为Normal时保存大小
            if (WindowState == WindowState.Normal && e.PreviousSize != e.NewSize)
            {
                _savedWindowWidth = Width;
                _savedWindowHeight = Height;

                // 保存窗口尺寸到设置
                SaveSettings();

                // 窗口尺寸已保存
            }
        }

        private void ShowWindow()
        {
            try
            {
                // 获取鼠标位置
                GetCursorPos(out POINT cursorPos);

                // 获取屏幕高度
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                
                // 判断鼠标位置在屏幕上半部分还是下半部分
                bool isInUpperHalf = cursorPos.Y < screenHeight / 2;
                
                // 计算窗口位置
                double windowLeft = cursorPos.X - Width / 2; // 水平居中对齐鼠标
                double windowTop;
                
                if (isInUpperHalf)
                {
                    // 鼠标在上半部分，窗口显示在下方
                    windowTop = cursorPos.Y + 10; // 留出10像素间距
                }
                else
                {
                    // 鼠标在下半部分，窗口显示在上方
                    windowTop = cursorPos.Y - Height - 10; // 留出10像素间距
                }
                
                // 确保窗口在屏幕范围内
                if (windowLeft + Width > SystemParameters.PrimaryScreenWidth)
                    windowLeft = SystemParameters.PrimaryScreenWidth - Width;
                if (windowTop + Height > SystemParameters.PrimaryScreenHeight)
                    windowTop = SystemParameters.PrimaryScreenHeight - Height;
                if (windowLeft < 0) windowLeft = 0;
                if (windowTop < 0) windowTop = 0;

                // 先设置窗口位置，再显示窗口
                Left = windowLeft;
                Top = windowTop;
                
                // 强制设置为置顶 - 确保窗口显示在最前端
                Topmost = true;
                
                // 先显示窗口，确保WindowState设置生效
                Show();
                
                // 强制恢复窗口状态为Normal，确保不是最小化状态
                WindowState = WindowState.Normal;

                // 确保窗口内容可见
                Visibility = Visibility.Visible;
                
                // 强制窗口显示在前台
                Activate();
                Focus();

                // 再次确保窗口状态为Normal
                WindowState = WindowState.Normal;

                // 短暂延迟后再次激活窗口以确保它显示在前台
                Task.Delay(50).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // 再次激活窗口
                            Activate();
                            Focus();
                        }
                        catch
                        {
                            // 忽略窗口激活失败
                        }
                    });
                });

                // 短暂延迟后根据置顶按钮状态决定是否取消置顶
                Task.Delay(300).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // 查找置顶按钮并检查状态
                            var pinButton = this.FindName("PinButton") as System.Windows.Controls.Button;
                            if (pinButton != null && pinButton.Content?.ToString() != "📌")
                            {
                                Topmost = false;
                            }
                        }
                        catch
                        {
                            // 如果PinButton不可用，默认不置顶
                            Topmost = false;
                        }
                    });
                });
            }
            catch
            {
                // 忽略显示窗口失败
            }
        }

        private void OpenSettings()
        {
            // 使用SettingsButton_Click来打开设置窗口，以保持代码一致性
            SettingsButton_Click(this, new RoutedEventArgs());
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                // 只有在应用程序真正退出时才清理资源
                if (System.Windows.Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
                {
                    // 停止内存清理定时器
                    _memoryCleanupTimer?.Stop();
                    _memoryCleanupTimer?.Dispose();

                    if (_hwndSource != null)
                    {
                        RemoveClipboardFormatListener(_hwndSource.Handle);
                        UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
                        UnregisterHotKey(_hwndSource.Handle, TEXT_SWAP_HOTKEY_ID);
                        // 新的AI翻译快捷键注销将在新逻辑中处理
                        UnregisterAiTranslateHotkeyNew();
                        _hwndSource.RemoveHook(WndProc);
                    }

                    CleanupNotifyIcon();

                    // 清理分词器
                    _segmenter = null;
                    _segmenterRef = null;

                    // 清理剪贴板历史
                    _clipboardHistory.Clear();

                    // 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }
            catch
            {
                // 忽略清理时的错误
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // 如果正在执行文字交换，忽略剪贴板更新以避免冲突
                if (_isPerformingTextSwap)
                {
                    return IntPtr.Zero; // 正在执行文字交换，忽略剪贴板更新
                }

                try
                {
                    ClipboardItem? newItem = null;

                    // 检查是否包含图片
                    if (System.Windows.Clipboard.ContainsImage())
                    {
                        var image = System.Windows.Clipboard.GetImage();
                        if (image != null)
                        {
                            // 检查是否已存在相同图片（简单比较尺寸）
                            if (!_clipboardHistory.Any(item => item.IsImage &&
                                item.Image?.PixelWidth == image.PixelWidth &&
                                item.Image?.PixelHeight == image.PixelHeight))
                            {
                                newItem = new ClipboardItem(image);
                            }
                        }
                    }
                    // 检查是否包含文本
                    else if (System.Windows.Clipboard.ContainsText())
                    {
                        string text = System.Windows.Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            // 优化文本长度以节省内存
                            text = MemoryOptimizer.OptimizeString(text, 2000);

                            // 检查是否已存在相同文本
                            if (!_clipboardHistory.Any(item => !item.IsImage && item.Text == text))
                            {
                                newItem = new ClipboardItem(text);
                            }
                        }
                    }

                    if (newItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _clipboardHistory.Insert(0, newItem);

                            // 限制历史记录数量，但保留固定的项目
                            while (_clipboardHistory.Count > _maxItems)
                            {
                                // 从后往前找到第一个未固定的项目并删除
                                for (int i = _clipboardHistory.Count - 1; i >= 0; i--)
                                {
                                    if (!_clipboardHistory[i].IsPinned)
                                    {
                                        _clipboardHistory.RemoveAt(i);
                                        break;
                                    }
                                }
                                // 如果所有项目都被固定，则停止删除
                                if (_clipboardHistory.All(item => item.IsPinned))
                                    break;
                            }

                            // 内容已添加到剪贴板历史
                        });

                        // 异步保存到文件
                        if (_saveHistoryEnabled)
                        {
                            Task.Run(async () => await SaveClipboardItemToFile(newItem));
                        }
                    }
                }
                catch
                {
                    // 忽略剪贴板处理错误
                }
                handled = true;
            }
            else if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();

                if (hotkeyId == HOTKEY_ID)
                {
                    // 剪贴板热键 - 显示剪贴板窗口
                    ShowAtCursorPosition();
                    handled = true;
                }
                else if (hotkeyId == TEXT_SWAP_HOTKEY_ID)
                {
                    // 智能文字交换热键 - 仅执行文字交换，不显示剪贴板窗口
                    if (_textSwapEnabled)
                    {
                        PerformTextSwap();
                    }
                    handled = true;
                }
                else if (hotkeyId == AI_TRANSLATE_HOTKEY_NEW_ID)
                {
                    // AI翻译热键处理逻辑
                    HandleAiTranslateHotkeyNew();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void RegisterGlobalHotkey()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    // 注册剪贴板快捷键
                    bool success = TryRegisterHotkey(helper.Handle, HOTKEY_ID, _hotkeyModifiers, _hotkeyKey, "剪贴板");

                    // 注册智能文字交换快捷键
                    if (_textSwapEnabled && !_disableTextSwapHotkey &&
                        _textSwapHotkeyModifiers != 0 && _textSwapHotkeyKey != 0)
                    {
                        // 确保两个热键不相同
                        bool isSameHotkey = (_hotkeyModifiers == _textSwapHotkeyModifiers && _hotkeyKey == _textSwapHotkeyKey);

                        if (!isSameHotkey)
                        {
                            TryRegisterHotkey(helper.Handle, TEXT_SWAP_HOTKEY_ID, _textSwapHotkeyModifiers, _textSwapHotkeyKey, "文字交换");
                        }
                    }

                    // 注册AI翻译快捷键
                    RegisterAiTranslateHotkeyNew();
                }
            }
            catch
            {
                // 忽略热键注册失败
            }
        }

        // 改进的热键注册方法，支持更好的错误处理
        private bool TryRegisterHotkey(IntPtr handle, int id, uint modifiers, uint key, string description)
        {
            try
            {
                // 先尝试注销可能存在的旧热键
                UnregisterHotKey(handle, id);

                // 注册新热键
                bool success = RegisterHotKey(handle, id, modifiers, key);

                // 热键注册结果处理已简化

                return success;
            }
            catch
            {
                return false;
            }
        }

        private string GetHotkeyDescriptionByValues(uint modifiers, uint key)
        {
            string result = "";
            if ((modifiers & MOD_CONTROL) != 0) result += "Ctrl+";
            if ((modifiers & MOD_ALT) != 0) result += "Alt+";
            if ((modifiers & MOD_SHIFT) != 0) result += "Shift+";
            if ((modifiers & MOD_WIN) != 0) result += "Win+";
            result += GetKeyName(key);
            return result;
        }

        private void UpdateGlobalHotkey(uint modifiers, uint key)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    // 先注销旧的热键
                    UnregisterHotKey(helper.Handle, HOTKEY_ID);

                    // 注册新的热键
                    if (RegisterHotKey(helper.Handle, HOTKEY_ID, modifiers, key))
                    {
                        _hotkeyModifiers = modifiers;
                        _hotkeyKey = key;
                    }
                    else
                    {
                        // 恢复原来的热键
                        RegisterHotKey(helper.Handle, HOTKEY_ID, _hotkeyModifiers, _hotkeyKey);
                    }
                }
            }
            catch
            {
                // 忽略热键更新失败
            }
        }

        private void UpdateTextSwapHotkey(uint modifiers, uint key)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    // 先注销旧的热键
                    UnregisterHotKey(helper.Handle, TEXT_SWAP_HOTKEY_ID);

                    // 检查是否完全禁用智能文字交换热键注册
                    if (_disableTextSwapHotkey)
                    {
                        // 智能文字交换热键注册已被用户禁用
                    }
                    // 仅在启用状态下注册新的热键
                    else if (_textSwapEnabled && modifiers != 0 && key != 0)
                    {
                        // 确保与剪贴板热键不冲突
                        bool isSameHotkey = (_hotkeyModifiers == modifiers && _hotkeyKey == key);

                        if (!isSameHotkey)
                        {
                            // 使用改进的热键注册方法，支持多键组合
                            bool textSwapSuccess = TryRegisterHotkey(helper.Handle, TEXT_SWAP_HOTKEY_ID, modifiers, key, "文字交换");
                            if (textSwapSuccess)
                            {
                                _textSwapHotkeyModifiers = modifiers;
                                _textSwapHotkeyKey = key;
                            }
                        }
                        else
                        {
                            // 智能文字交换热键与剪贴板热键相同，避免冲突
                        }
                    }
                    else
                    {
                        // 智能文字交换功能已禁用或快捷键无效
                    }
                }
            }
            catch
            {
                // 忽略智能文字交换热键更新失败
            }
        }

        private string GetHotkeyDescription()
        {
            string description = "";
            if ((_hotkeyModifiers & MOD_WIN) != 0) description += "Win+";
            if ((_hotkeyModifiers & MOD_CONTROL) != 0) description += "Ctrl+";
            if ((_hotkeyModifiers & MOD_ALT) != 0) description += "Alt+";
            if ((_hotkeyModifiers & MOD_SHIFT) != 0) description += "Shift+";
            description += GetKeyName(_hotkeyKey);
            return description;
        }

        private string GetTextSwapHotkeyDescription()
        {
            string description = "";
            if ((_textSwapHotkeyModifiers & MOD_WIN) != 0) description += "Win+";
            if ((_textSwapHotkeyModifiers & MOD_CONTROL) != 0) description += "Ctrl+";
            if ((_textSwapHotkeyModifiers & MOD_ALT) != 0) description += "Alt+";
            if ((_textSwapHotkeyModifiers & MOD_SHIFT) != 0) description += "Shift+";
            description += GetKeyName(_textSwapHotkeyKey);
            return description;
        }

        private string GetKeyName(uint keyCode)
        {
            return keyCode switch
            {
                0x41 => "A", 0x42 => "B", 0x43 => "C", 0x44 => "D", 0x45 => "E", 0x46 => "F",
                0x47 => "G", 0x48 => "H", 0x49 => "I", 0x4A => "J", 0x4B => "K", 0x4C => "L",
                0x4D => "M", 0x4E => "N", 0x4F => "O", 0x50 => "P", 0x51 => "Q", 0x52 => "R",
                0x53 => "S", 0x54 => "T", 0x55 => "U", 0x56 => "V", 0x57 => "W", 0x58 => "X",
                0x59 => "Y", 0x5A => "Z",
                0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3", 0x34 => "4",
                0x35 => "5", 0x36 => "6", 0x37 => "7", 0x38 => "8", 0x39 => "9",
                0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4", 0x74 => "F5", 0x75 => "F6",
                0x76 => "F7", 0x77 => "F8", 0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
                0x20 => "Space", // 空格键
                0x14 => "CapsLock", // Caps Lock键
                _ => $"Key{keyCode:X2}"
            };
        }

        private uint GetKeyCode(ComboBoxItem? item)
        {
            if (item?.Content?.ToString() is string key)
            {
                return key switch
                {
                    "C" => 0x43,
                    "V" => 0x56,
                    "X" => 0x58,
                    "Z" => 0x5A,
                    "A" => 0x41,
                    "S" => 0x53,
                    "D" => 0x44,
                    "F" => 0x46,
                    "G" => 0x47,
                    "H" => 0x48,
                    "Q" => 0x51,
                    _ => 0x51  // 默认改为Q，因为新的默认热键是Ctrl+Q
                };
            }
            return 0x51;  // 默认改为Q
        }

        private void LoadSettings()
        {
            try
            {
                // 这里可以实现设置的持久化加载
                // 例如从配置文件或注册表读取
                // 暂时使用默认值
                _singleClickPaste = false;
                _doubleClickPaste = true;
                _maxItems = 20;
                _hotkeyModifiers = MOD_CONTROL | MOD_ALT;
                _hotkeyKey = VK_C;
                _saveHistoryEnabled = false;
                _historyFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClipboardHistory");

                // 智能文字交换默认设置
                _textSwapEnabled = true; // 默认启用智能文字交换功能
                _textSwapHotkeyModifiers = MOD_CONTROL;
                _textSwapHotkeyKey = VK_Q;

                // 开机启动默认设置
                _startupEnabled = false;

                // 设置AI翻译默认值
                SetAiTranslateDefaults();
                
                // 加载窗口尺寸设置
                try
                {
                    string settingsPath = GetSettingsFilePath();
                    if (File.Exists(settingsPath))
                    {
                        string[] lines = File.ReadAllLines(settingsPath);
                        foreach (string line in lines)
                        {
                            string[] parts = line.Split('=');
                            if (parts.Length == 2)
                            {
                                string key = parts[0].Trim();
                                string value = parts[1].Trim();
                                
                                if (key == "WindowWidth" && double.TryParse(value, out double width))
                                {
                                    _savedWindowWidth = width;
                                    // 只在初始化时设置窗口尺寸，避免覆盖用户调整
                                    if (Width == 480) // 默认宽度
                                    {
                                        Width = width;
                                    }
                                }
                                else if (key == "WindowHeight" && double.TryParse(value, out double height))
                                {
                                    _savedWindowHeight = height;
                                    // 只在初始化时设置窗口尺寸，避免覆盖用户调整
                                    if (Height == 600) // 默认高度
                                    {
                                        Height = height;
                                    }
                                }
                                else if (key == "MaxItems" && int.TryParse(value, out int maxItems))
                                {
                                    _maxItems = maxItems;
                                }
                                else if (key == "SingleClickPaste" && bool.TryParse(value, out bool singleClick))
                                {
                                    _singleClickPaste = singleClick;
                                }
                                else if (key == "DoubleClickPaste" && bool.TryParse(value, out bool doubleClick))
                                {
                                    _doubleClickPaste = doubleClick;
                                }
                                else if (key == "SaveHistoryEnabled" && bool.TryParse(value, out bool saveHistory))
                                {
                                    _saveHistoryEnabled = saveHistory;
                                }
                                else if (key == "HistoryFolderPath")
                                {
                                    _historyFolderPath = value;
                                }
                                else if (key == "HotkeyModifiers" && uint.TryParse(value, out uint hotkeyModifiers))
                                {
                                    _hotkeyModifiers = hotkeyModifiers;
                                }
                                else if (key == "HotkeyKey" && uint.TryParse(value, out uint hotkeyKey))
                                {
                                    _hotkeyKey = hotkeyKey;
                                }
                                else if (key == "TextSwapEnabled" && bool.TryParse(value, out bool textSwapEnabled))
                                {
                                    _textSwapEnabled = textSwapEnabled;
                                }
                                else if (key == "TextSwapHotkeyModifiers" && uint.TryParse(value, out uint textSwapModifiers))
                                {
                                    _textSwapHotkeyModifiers = textSwapModifiers;
                                }
                                else if (key == "TextSwapHotkeyKey" && uint.TryParse(value, out uint textSwapKey))
                                {
                                    _textSwapHotkeyKey = textSwapKey;
                                }
                                else if (key == "StartupEnabled" && bool.TryParse(value, out bool startupEnabled))
                                {
                                    _startupEnabled = startupEnabled;
                                }
                                else if (key == "AiProvider")
                                {
                                    _aiProvider = value;
                                }
                                else if (key == "AiApiUrl")
                                {
                                    _aiApiUrl = value;
                                }
                                else if (key == "AiApiKey")
                                {
                                    _aiApiKey = value;
                                }
                                else if (key == "AiModel")
                                {
                                    _aiModel = value;
                                }
                                else if (key == "AiPrompt")
                                {
                                    // 检查是否为旧版本提示词，如果是则强制更新为新版本
                                    string newPrompt = "你是一个中英文翻译专家，将用户输入的中文翻译成英文，或将用户输入的英文翻译成中文。对于非中文内容，它将提供中文翻译结果。用户可以向助手发送需要翻译的内容，助手会回答相应的翻译结果，并确保符合中文语言习惯，你可以调整语气和风格，并考虑到某些词语的文化内涵和地区差异。同时作为翻译家，需将原文翻译成具有信达雅标准的译文。\"信\" 即忠实于原文的内容与意图；\"达\" 意味着译文应通顺易懂，表达清晰；\"雅\" 则追求译文的文化审美和语言的优美。目标是创作出既忠于原作精神，又符合目标语言文化和读者审美的翻译。";

                                    // 检查多种可能的旧版本提示词
                                    if (value == "请将以下文本翻译成中文：" ||
                                        value.Contains("请将以下文本翻译成中文") ||
                                        value.Length < 50) // 如果提示词太短，也认为是旧版本
                                    {
                                        _aiPrompt = newPrompt;
                                        // 检测到旧版本提示词，已自动更新
                                    }
                                    else
                                    {
                                        _aiPrompt = value;
                                    }
                                }
                                else if (key == "AiTranslateHotkeyModifiers" && uint.TryParse(value, out uint aiTranslateModifiers))
                                {
                                    // 加载到新的AI翻译快捷键变量
                                    _aiTranslateHotkeyNewModifiers = aiTranslateModifiers;
                                    // 保持旧变量兼容性
                                    _aiTranslateHotkeyModifiers = aiTranslateModifiers;
                                }
                                else if (key == "AiTranslateHotkeyKey" && uint.TryParse(value, out uint aiTranslateKey))
                                {
                                    // 加载到新的AI翻译快捷键变量
                                    _aiTranslateHotkeyNewKey = aiTranslateKey;
                                    // 保持旧变量兼容性
                                    _aiTranslateHotkeyKey = aiTranslateKey;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 使用默认值
                    _savedWindowWidth = 380;
                    _savedWindowHeight = 600;
                }

                // 确保AI翻译快捷键有效，无论是否从配置加载成功
                if (_aiTranslateHotkeyNewModifiers == 0 || _aiTranslateHotkeyNewKey == 0)
                {
                    // AI翻译快捷键无效，使用默认值 Ctrl+T
                    _aiTranslateHotkeyNewModifiers = MOD_CONTROL;
                    _aiTranslateHotkeyNewKey = VK_T;
                    _aiTranslateHotkeyModifiers = MOD_CONTROL;
                    _aiTranslateHotkeyKey = VK_T;
                }

                // 创建默认历史文件夹
                InitializeHistoryFolder();

            }
            catch
            {
                // 使用默认值
                _singleClickPaste = false;
                _doubleClickPaste = true;
                _maxItems = 20;
                _hotkeyModifiers = MOD_CONTROL | MOD_ALT;
                _hotkeyKey = VK_C;
                _saveHistoryEnabled = false;
                _historyFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClipboardHistory");
                _savedWindowWidth = 380;
                _savedWindowHeight = 600;

                // 智能文字交换默认设置 - 确保在异常情况下也有正确的默认值
                _textSwapEnabled = true;
                _textSwapHotkeyModifiers = MOD_CONTROL;
                _textSwapHotkeyKey = VK_Q;

                // 开机启动默认设置
                _startupEnabled = false;

                // 设置AI翻译默认值
                SetAiTranslateDefaults();
            }
        }

        private void InitializeHistoryFolder()
        {
            try
            {
                if (!Directory.Exists(_historyFolderPath))
                {
                    Directory.CreateDirectory(_historyFolderPath);
                    System.Diagnostics.Debug.WriteLine($"创建历史文件夹: {_historyFolderPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建历史文件夹失败: {ex.Message}");
            }
        }

        private async Task SaveClipboardItemToFile(ClipboardItem item)
        {
            if (!_saveHistoryEnabled || string.IsNullOrEmpty(_historyFolderPath))
                return;

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string fileName;
                string filePath = "";

                if (item.IsImage && item.Image != null)
                {
                    // 保存图片
                    fileName = $"clipboard_image_{timestamp}.png";
                    filePath = Path.Combine(_historyFolderPath, fileName);

                    // 检查文件大小限制
                    using var stream = new MemoryStream();
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(item.Image));
                    encoder.Save(stream);

                    if (stream.Length > MAX_IMAGE_FILE_SIZE)
                    {
                        System.Diagnostics.Debug.WriteLine($"图片文件过大，跳过保存: {stream.Length} bytes");
                        return;
                    }

                    await File.WriteAllBytesAsync(filePath, stream.ToArray());
                }
                else if (!string.IsNullOrEmpty(item.Text))
                {
                    // 保存文本
                    fileName = $"clipboard_text_{timestamp}.txt";
                    filePath = Path.Combine(_historyFolderPath, fileName);

                    // 检查文件大小限制
                    var textBytes = System.Text.Encoding.UTF8.GetBytes(item.Text);
                    if (textBytes.Length > MAX_TEXT_FILE_SIZE)
                    {
                        return; // 文件过大，跳过保存
                    }

                    await File.WriteAllTextAsync(filePath, item.Text, System.Text.Encoding.UTF8);
                }
                else
                {
                    return; // 没有可保存的内容
                }

                // 清理旧文件
                await CleanupHistoryFiles();
            }
            catch
            {
                // 忽略保存失败
            }
        }

        private Task CleanupHistoryFiles()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_historyFolderPath))
                        return;

                    var files = Directory.GetFiles(_historyFolderPath, "clipboard_*.*")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.CreationTime)
                        .ToArray();

                    // 检查文件数量限制
                    if (files.Length > MAX_HISTORY_FILES)
                    {
                        var filesToDelete = files.Skip(MAX_HISTORY_FILES);
                        foreach (var file in filesToDelete)
                        {
                            try
                            {
                                file.Delete();
                            }
                            catch
                            {
                                // 忽略删除失败的文件
                            }
                        }
                    }

                    // 检查总文件夹大小限制
                    long totalSize = files.Where(f => f.Exists).Sum(f => f.Length);
                    if (totalSize > MAX_TOTAL_FOLDER_SIZE)
                    {
                        var sortedFiles = files.Where(f => f.Exists).OrderBy(f => f.CreationTime);
                        foreach (var file in sortedFiles)
                        {
                            try
                            {
                                totalSize -= file.Length;
                                file.Delete();
                                if (totalSize <= MAX_TOTAL_FOLDER_SIZE * 0.8) // 保留20%的缓冲
                                    break;
                            }
                            catch
                            {
                                // 忽略删除失败的文件
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略清理失败
                }
            });
        }

        private void SaveSettings()
        {
            try
            {
                // 保存窗口尺寸等设置到配置文件
                string settingsPath = GetSettingsFilePath();
                using (StreamWriter writer = new StreamWriter(settingsPath, false))
                {
                    writer.WriteLine($"WindowWidth={_savedWindowWidth}");
                    writer.WriteLine($"WindowHeight={_savedWindowHeight}");
                    writer.WriteLine($"MaxItems={_maxItems}");
                    writer.WriteLine($"SingleClickPaste={_singleClickPaste}");
                    writer.WriteLine($"DoubleClickPaste={_doubleClickPaste}");
                    writer.WriteLine($"SaveHistoryEnabled={_saveHistoryEnabled}");
                    writer.WriteLine($"HistoryFolderPath={_historyFolderPath}");
                    writer.WriteLine($"HotkeyModifiers={_hotkeyModifiers}");
                    writer.WriteLine($"HotkeyKey={_hotkeyKey}");
                    writer.WriteLine($"TextSwapEnabled={_textSwapEnabled}");
                    writer.WriteLine($"TextSwapHotkeyModifiers={_textSwapHotkeyModifiers}");
                    writer.WriteLine($"TextSwapHotkeyKey={_textSwapHotkeyKey}");
                    writer.WriteLine($"StartupEnabled={_startupEnabled}");
                    writer.WriteLine($"AiProvider={_aiProvider}");
                    writer.WriteLine($"AiApiUrl={_aiApiUrl}");
                    writer.WriteLine($"AiApiKey={_aiApiKey}");
                    writer.WriteLine($"AiModel={_aiModel}");
                    writer.WriteLine($"AiPrompt={_aiPrompt}");
                    // 保存新的AI翻译快捷键（保持兼容性）
                    writer.WriteLine($"AiTranslateHotkeyModifiers={_aiTranslateHotkeyNewModifiers}");
                    writer.WriteLine($"AiTranslateHotkeyKey={_aiTranslateHotkeyNewKey}");
                }
                
                System.Diagnostics.Debug.WriteLine("设置已保存");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        private void ShowAtCursorPosition()
        {
            try
            {
                // 记住当前前台窗口（保持光标位置）
                _previousForegroundWindow = GetForegroundWindow();

                // 获取屏幕尺寸
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                
                // 计算窗口在屏幕中间的位置
                double windowLeft = (screenWidth - Width) / 2;
                double windowTop = (screenHeight - Height) / 2;
                
                // 确保窗口在屏幕范围内
                if (windowLeft < 0) windowLeft = 0;
                if (windowTop < 0) windowTop = 0;
                if (windowLeft + Width > screenWidth)
                    windowLeft = screenWidth - Width;
                if (windowTop + Height > screenHeight)
                    windowTop = screenHeight - Height;

                // 先设置窗口位置，再显示窗口
                Left = windowLeft;
                Top = windowTop;
                
                // 强制设置为置顶 - 确保窗口显示在最前端
                Topmost = true;
                
                // 先显示窗口，确保WindowState设置生效
                Show();
                
                // 强制恢复窗口状态为Normal，确保不是最小化状态
                WindowState = WindowState.Normal;

                // 确保窗口内容可见
                Visibility = Visibility.Visible;
                
                // 强制窗口显示在前台
                Activate();
                Focus();

                // 再次确保窗口状态为Normal
                WindowState = WindowState.Normal;

                // 短暂延迟后再次激活窗口以确保它显示在前台
                Task.Delay(50).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // 再次激活窗口
                            Activate();
                            Focus();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"激活窗口时出错: {ex.Message}");
                        }
                    });
                });

                // 短暂延迟后根据置顶按钮状态决定是否取消置顶
                CheckAndUpdateTopmostState();
            }
            catch
            {
                // 忽略显示窗口时的错误
            }
        }

        private void SetAiTranslateDefaults()
        {
            _aiProvider = "ollama";
            _aiApiUrl = "http://localhost:11434";
            _aiApiKey = "";
            _aiModel = "";
            _aiPrompt = "你是一个中英文翻译专家，将用户输入的中文翻译成英文，或将用户输入的英文翻译成中文。对于非中文内容，它将提供中文翻译结果。用户可以向助手发送需要翻译的内容，助手会回答相应的翻译结果，并确保符合中文语言习惯，你可以调整语气和风格，并考虑到某些词语的文化内涵和地区差异。同时作为翻译家，需将原文翻译成具有信达雅标准的译文。\"信\" 即忠实于原文的内容与意图；\"达\" 意味着译文应通顺易懂，表达清晰；\"雅\" 则追求译文的文化审美和语言的优美。目标是创作出既忠于原作精神，又符合目标语言文化和读者审美的翻译。";
            // 修复默认快捷键为单一Ctrl+T，避免多键组合问题
            _aiTranslateHotkeyNewModifiers = MOD_CONTROL;
            _aiTranslateHotkeyNewKey = VK_T;
            _aiTranslateHotkeyModifiers = MOD_CONTROL;
            _aiTranslateHotkeyKey = VK_T;
        }

        private void CheckAndUpdateTopmostState()
        {
            Task.Delay(300).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var pinButton = this.FindName("PinButton") as System.Windows.Controls.Button;
                        if (pinButton != null && pinButton.Content?.ToString() != "📌")
                        {
                            Topmost = false;
                        }
                    }
                    catch
                    {
                        Topmost = false;
                    }
                });
            });
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            if (sender is System.Windows.Controls.Button pinButton)
            {
                pinButton.Content = Topmost ? "📌" : "📍";
                pinButton.ToolTip = Topmost ? "取消置顶" : "置顶";
            }
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                System.Diagnostics.Process.Start("explorer.exe", programDirectory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打开程序目录失败: {ex.Message}");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 点击关闭按钮时隐藏到系统托盘，而不是退出程序
            WindowState = WindowState.Minimized;
            Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, "FlugiClipboard", "程序已隐藏到系统托盘，双击托盘图标可重新显示", WinForms.ToolTipIcon.Info);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is ClipboardItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        private void SplitWords_Click(object sender, RoutedEventArgs e)
        {
            ClipboardItem? clipboardItem = null;

            if (sender is System.Windows.Controls.Button button && button.DataContext is ClipboardItem buttonItem)
            {
                clipboardItem = buttonItem;
            }
            else if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ClipboardItem menuItemData)
            {
                clipboardItem = menuItemData;
            }

            if (clipboardItem != null)
            {
                try
                {
                    // 使用已保存的前台窗口（在ShowAtCursorPosition中保存的）
                    IntPtr targetWindow = _previousForegroundWindow;

                    // 如果没有保存的前台窗口，则获取当前前台窗口（但排除自己）
                    if (targetWindow == IntPtr.Zero)
                    {
                        targetWindow = GetForegroundWindow();
                        var thisWindowHandle = new WindowInteropHelper(this).Handle;
                        if (targetWindow == thisWindowHandle)
                        {
                            // 如果当前前台窗口是自己，尝试获取之前的窗口
                            targetWindow = IntPtr.Zero;
                        }
                    }

                    // 获取鼠标位置
                    GetCursorPos(out POINT cursorPos);

                    // 打开拆分选词窗口
                    var splitWindow = new SplitWordsWindow(clipboardItem.Text, GetSegmenter(), targetWindow, this);
                    splitWindow.Owner = this;
                    splitWindow.Topmost = true;

                    // 设置窗口位置到鼠标附近
                    splitWindow.Left = cursorPos.X - splitWindow.Width / 2;
                    splitWindow.Top = cursorPos.Y - splitWindow.Height / 2;

                    // 确保窗口在屏幕范围内
                    if (splitWindow.Left < 0) splitWindow.Left = 0;
                    if (splitWindow.Top < 0) splitWindow.Top = 0;
                    if (splitWindow.Left + splitWindow.Width > SystemParameters.PrimaryScreenWidth)
                        splitWindow.Left = SystemParameters.PrimaryScreenWidth - splitWindow.Width;
                    if (splitWindow.Top + splitWindow.Height > SystemParameters.PrimaryScreenHeight)
                        splitWindow.Top = SystemParameters.PrimaryScreenHeight - splitWindow.Height;

                    splitWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"打开拆分选词窗口时出错: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteText_Click(object sender, RoutedEventArgs e)
        {
            ClipboardItem? clipboardItem = null;

            if (sender is Button button && button.DataContext is ClipboardItem buttonItem)
            {
                clipboardItem = buttonItem;
            }
            else if (sender is MenuItem menuItem && menuItem.DataContext is ClipboardItem menuItemData)
            {
                clipboardItem = menuItemData;
            }

            if (clipboardItem != null)
            {
                _clipboardHistory.Remove(clipboardItem);
                // StatusTextBlock.Text = "已删除选中的文本"; // 暂时注释掉，避免引用错误
            }
        }

        /// <summary>
        /// 清空所有非固定的剪贴板项目
        /// </summary>
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var itemsToRemove = _clipboardHistory.Where(item => !item.IsPinned).ToList();
            foreach (var item in itemsToRemove)
            {
                _clipboardHistory.Remove(item);
            }

        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;

                // 设置当前的设置选项
                settingsWindow.SingleClickPaste = _singleClickPaste;
                settingsWindow.DoubleClickPaste = _doubleClickPaste;
                settingsWindow.MaxItems = _maxItems;
                settingsWindow.SaveHistoryEnabled = _saveHistoryEnabled;
                settingsWindow.HistoryFolderPath = _historyFolderPath;
                settingsWindow.HotkeyModifiers = _hotkeyModifiers;
                settingsWindow.HotkeyKey = _hotkeyKey;
                settingsWindow.StartupEnabled = _startupEnabled;
                settingsWindow.TextSwapEnabled = _textSwapEnabled;
                settingsWindow.TextSwapHotkeyModifiers = _textSwapHotkeyModifiers;
                settingsWindow.TextSwapHotkeyKey = _textSwapHotkeyKey;
                settingsWindow.AiProvider = _aiProvider;
                settingsWindow.AiApiUrl = _aiApiUrl;
                settingsWindow.AiApiKey = _aiApiKey;
                settingsWindow.AiModel = _aiModel;
                settingsWindow.AiPrompt = _aiPrompt;
                // 传递新的AI翻译快捷键给设置窗口
                settingsWindow.AiTranslateHotkeyModifiers = _aiTranslateHotkeyNewModifiers;
                settingsWindow.AiTranslateHotkeyKey = _aiTranslateHotkeyNewKey;

                // 确保界面显示当前设置
                settingsWindow.LoadSettingsToUI();

                if (settingsWindow.ShowDialog() == true)
                {
                    // 应用设置
                    _singleClickPaste = settingsWindow.SingleClickPaste;
                    _doubleClickPaste = settingsWindow.DoubleClickPaste;
                    _maxItems = settingsWindow.MaxItems;
                    _saveHistoryEnabled = settingsWindow.SaveHistoryEnabled;
                    _historyFolderPath = settingsWindow.HistoryFolderPath;
                    _hotkeyModifiers = settingsWindow.HotkeyModifiers;
                    _hotkeyKey = settingsWindow.HotkeyKey;
                    _startupEnabled = settingsWindow.StartupEnabled;
                    _textSwapEnabled = settingsWindow.TextSwapEnabled;
                    _textSwapHotkeyModifiers = settingsWindow.TextSwapHotkeyModifiers;
                    _textSwapHotkeyKey = settingsWindow.TextSwapHotkeyKey;
                    _aiProvider = settingsWindow.AiProvider;
                    _aiApiUrl = settingsWindow.AiApiUrl;
                    _aiApiKey = settingsWindow.AiApiKey;
                    _aiModel = settingsWindow.AiModel;
                    _aiPrompt = settingsWindow.AiPrompt;
                    // 更新新的AI翻译快捷键变量
                    _aiTranslateHotkeyNewModifiers = settingsWindow.AiTranslateHotkeyModifiers;
                    _aiTranslateHotkeyNewKey = settingsWindow.AiTranslateHotkeyKey;
                    // 保持旧变量兼容性
                    _aiTranslateHotkeyModifiers = settingsWindow.AiTranslateHotkeyModifiers;
                    _aiTranslateHotkeyKey = settingsWindow.AiTranslateHotkeyKey;

                    // 确保至少有一个选项被选中
                    if (!_singleClickPaste && !_doubleClickPaste)
                    {
                        _doubleClickPaste = true;
                    }

                    if (_saveHistoryEnabled && !string.IsNullOrEmpty(_historyFolderPath))
                    {
                        InitializeHistoryFolder();
                    }

                    // 应用快捷键设置
                    uint newModifiers = settingsWindow.HotkeyModifiers;
                    uint newKey = settingsWindow.HotkeyKey;

                    if (newModifiers != 0 && newKey != 0)
                    {
                        // 更新快捷键
                        UpdateGlobalHotkey(newModifiers, newKey);
                    }

                    // 处理开机启动设置
                    try
                    {
                        SetStartupEnabled(_startupEnabled);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"设置开机启动失败: {ex.Message}");
                    }

                    // 更新智能文字交换设置
                    _textSwapEnabled = settingsWindow.TextSwapEnabled;

                    // 重新注册智能文字交换快捷键
                    try
                    {
                        UpdateTextSwapHotkey(settingsWindow.TextSwapHotkeyModifiers, settingsWindow.TextSwapHotkeyKey);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"重新注册智能文字交换快捷键失败: {ex.Message}");
                    }

                    // 重新注册AI翻译快捷键（新实现）
                    try
                    {
                        UpdateAiTranslateHotkeyNew(settingsWindow.AiTranslateHotkeyModifiers, settingsWindow.AiTranslateHotkeyKey);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"重新注册AI翻译快捷键失败: {ex.Message}");
                    }

                    // 保存设置到配置文件
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开设置窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            ClipboardItem? clipboardItem = null;

            if (sender is System.Windows.Controls.Button button && button.DataContext is ClipboardItem buttonItem)
            {
                clipboardItem = buttonItem;
            }
            else if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ClipboardItem menuItemData)
            {
                clipboardItem = menuItemData;
            }

            if (clipboardItem != null)
            {
                clipboardItem.IsPinned = !clipboardItem.IsPinned;
                // 不显示状态信息，避免StatusTextBlock错误
            }
        }

        private void ClipboardItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ClipboardItem item)
            {
                // 根据设置判断是单击还是双击触发粘贴
                bool shouldPaste = false;

                if (_singleClickPaste && e.ClickCount == 1)
                {
                    shouldPaste = true;
                }
                else if (_doubleClickPaste && e.ClickCount == 2)
                {
                    shouldPaste = true;
                }

                if (shouldPaste)
                {
                    try
                    {
                        // 使用记住的前台窗口（保持光标位置）
                        IntPtr targetWindow = _previousForegroundWindow != IntPtr.Zero ? _previousForegroundWindow : GetForegroundWindow();

                        if (item.IsImage && item.Image != null)
                        {
                            // 复制图片到剪贴板
                            System.Windows.Clipboard.SetImage(item.Image);

                            // 隐藏剪贴板窗口
                            Hide();

                            // 短暂延迟后粘贴
                            Task.Delay(100).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    // 恢复到原来的前台窗口
                                    SetForegroundWindow(targetWindow);

                                    // 再次短暂延迟确保窗口切换完成
                                    Task.Delay(50).ContinueWith(__ =>
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            // 模拟 Ctrl+V 粘贴
                                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                                            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                                            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                        });
                                    });
                                });
                            });
                        }
                        else if (!string.IsNullOrEmpty(item.Text))
                        {
                            // 复制文本到剪贴板
                            System.Windows.Clipboard.SetText(item.Text);

                            // 隐藏剪贴板窗口
                            Hide();

                            // 短暂延迟后粘贴
                            Task.Delay(100).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    // 恢复到原来的前台窗口
                                    SetForegroundWindow(targetWindow);

                                    // 再次短暂延迟确保窗口切换完成
                                    Task.Delay(50).ContinueWith(__ =>
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            // 模拟 Ctrl+V 粘贴
                                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                                            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                                            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                        });
                                    });
                                });
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"粘贴失败: {ex.Message}");
                    }
                }
            }
        }

        // 智能文字交换功能 - 简化版本，避免状态管理问题
        private async void PerformTextSwap()
        {
            // 使用简单的锁机制，避免复杂的状态管理
            if (_isPerformingTextSwap)
            {
                return;
            }

            _isPerformingTextSwap = true;

            try
            {
                // 记录当前窗口可见状态
                bool wasVisible = IsVisible;

                // 简化的文本获取和交换逻辑
                await Task.Run(() =>
                {
                    try
                    {
                        // 发送Ctrl+C获取选中文本
                        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)VK_C, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                        // 等待复制完成
                        System.Threading.Thread.Sleep(200);

                        // 获取剪贴板内容
                        string selectedText = "";
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (System.Windows.Clipboard.ContainsText())
                                {
                                    selectedText = System.Windows.Clipboard.GetText();
                                }
                            }
                            catch { }
                        });

                        if (!string.IsNullOrEmpty(selectedText))
                        {
                            // 执行文字交换
                            string swappedText = SwapText(selectedText);
                            if (swappedText != selectedText)
                            {
                                // 设置交换后的文本到剪贴板
                                Dispatcher.Invoke(() =>
                                {
                                    try
                                    {
                                        System.Windows.Clipboard.SetText(swappedText);
                                    }
                                    catch { }
                                });

                                // 发送Ctrl+V粘贴
                                System.Threading.Thread.Sleep(100);
                                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                                keybd_event((byte)VK_V, 0, 0, UIntPtr.Zero);
                                keybd_event((byte)VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略所有异常
                    }
                });

                // 确保窗口状态没有被意外改变
                if (!wasVisible && IsVisible)
                {
                    Hide();
                }
            }
            catch
            {
                // 忽略所有异常
            }
            finally
            {
                // 立即重置状态，避免延迟
                _isPerformingTextSwap = false;
            }
        }

        private string GetSelectedText()
        {
            try
            {
                // 保存当前剪贴板内容
                string originalClipboard = "";
                bool hasOriginalContent = false;
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        originalClipboard = System.Windows.Clipboard.GetText();
                        hasOriginalContent = true;
                    }
                }
                catch
                {
                    // 忽略剪贴板访问错误
                }

                // 模拟 Ctrl+C 复制选中文本
                keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(0x43, 0, 0, UIntPtr.Zero); // C键
                keybd_event(0x43, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // 短暂延迟等待复制完成
                System.Threading.Thread.Sleep(150);

                // 获取剪贴板内容
                string selectedText = "";
                if (System.Windows.Clipboard.ContainsText())
                {
                    selectedText = System.Windows.Clipboard.GetText();
                }

                // 如果获取到的文本与原剪贴板内容相同，说明没有选中文本
                if (hasOriginalContent && selectedText == originalClipboard)
                {
                    System.Diagnostics.Debug.WriteLine("没有检测到新的选中文本");
                    return "";
                }

                System.Diagnostics.Debug.WriteLine($"获取到选中文本: {selectedText}");
                return selectedText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取选中文本失败: {ex.Message}");
            }
            return "";
        }

        private void ReplaceSelectedText(string newText)
        {
            try
            {
                // 将新文本放入剪贴板
                System.Windows.Clipboard.SetText(newText);

                // 短暂延迟
                System.Threading.Thread.Sleep(100);

                // 模拟 Ctrl+V 粘贴
                keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(0x56, 0, 0, UIntPtr.Zero); // V键
                keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"替换选中文本失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能文字交换功能 - 基于用户友好的交换规则
        /// </summary>
        private string SwapText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = text.Trim();

            // 如果是两个字符，直接交换
            if (text.Length == 2)
            {
                return new string(new char[] { text[1], text[0] });
            }

            // 使用智能分割规则进行交换
            var swapResult = PerformIntelligentSwap(text);
            return swapResult ?? text; // 如果交换失败，返回原文本
        }

        /// <summary>
        /// 执行智能交换 - 实现用户友好的交换规则
        /// </summary>
        private string? PerformIntelligentSwap(string text)
        {
            try
            {
                // 规则1: 优先级最高 - 逗号分割
                var commaResult = TrySwapByComma(text);
                if (commaResult != null) return commaResult;

                // 规则2: 优先级中等 - "的、地、得"分割
                var auxiliaryResult = TrySwapByAuxiliaryWords(text);
                if (auxiliaryResult != null) return auxiliaryResult;

                // 规则3: 中英文混合处理
                var mixedResult = TrySwapMixedText(text);
                if (mixedResult != null) return mixedResult;

                // 规则4: 其他标点符号分割
                var punctuationResult = TrySwapByOtherPunctuation(text);
                if (punctuationResult != null) return punctuationResult;

                // 规则5: 智能分词交换（兜底方案）
                var segmentResult = TrySwapBySegmentation(text);
                if (segmentResult != null) return segmentResult;

                return null; // 无法交换
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 规则1: 按逗号分割交换（优先级最高）
        /// 示例: "今天，明天" → "明天，今天"
        /// </summary>
        private string? TrySwapByComma(string text)
        {
            if (!text.Contains('，') && !text.Contains(','))
                return null;

            // 优先处理中文逗号
            char delimiter = text.Contains('，') ? '，' : ',';
            var parts = text.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2)
            {
                string left = parts[0].Trim();
                string right = parts[1].Trim();
                return $"{right}{delimiter}{left}";
            }

            return null;
        }

        /// <summary>
        /// 规则2: 按"的、地、得"分割交换（优先级中等）
        /// 示例: "我的书" → "书的我", "红色的苹果" → "苹果的红色"
        /// </summary>
        private string? TrySwapByAuxiliaryWords(string text)
        {
            string[] auxiliaryWords = { "的", "地", "得" };

            foreach (string auxiliary in auxiliaryWords)
            {
                if (text.Contains(auxiliary))
                {
                    var parts = text.Split(new string[] { auxiliary }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        string left = parts[0].Trim();
                        string right = parts[1].Trim();
                        return $"{right}{auxiliary}{left}";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 规则3: 中英文混合文本处理
        /// 示例: "hello世界" → "世界hello", "我love你" → "你love我"
        /// </summary>
        private string? TrySwapMixedText(string text)
        {
            // 检查是否包含中英文混合
            bool hasChinese = ContainsChinese(text);
            bool hasEnglish = text.Any(c => char.IsLetter(c) && c < 128);

            if (!hasChinese || !hasEnglish)
                return null;

            // 使用智能分割中英文部分
            var parts = SplitChineseEnglish(text);

            // 处理两部分的情况：如 "hello世界" → "世界hello"
            if (parts.Count == 2)
            {
                return $"{parts[1]}{parts[0]}";
            }

            // 处理三部分的情况：如 "我love你" → "你love我"
            if (parts.Count == 3)
            {
                // 检查中间是否为英文单词
                bool firstIsChinese = ContainsChinese(parts[0]);
                bool middleIsEnglish = parts[1].All(c => char.IsLetter(c) && c < 128);
                bool lastIsChinese = ContainsChinese(parts[2]);

                if (firstIsChinese && middleIsEnglish && lastIsChinese)
                {
                    // 交换第一部分和第三部分，保持中间英文不变
                    return $"{parts[2]}{parts[1]}{parts[0]}";
                }
            }

            return null;
        }

        /// <summary>
        /// 规则4: 其他标点符号分割
        /// 处理其他常见标点符号
        /// </summary>
        private string? TrySwapByOtherPunctuation(string text)
        {
            char[] punctuations = { '。', '！', '？', '；', '：', '.', '!', '?', ';', ':' };

            foreach (char punct in punctuations)
            {
                if (text.Contains(punct))
                {
                    var parts = text.Split(punct, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        string left = parts[0].Trim();
                        string right = parts[1].Trim();
                        return $"{right}{punct}{left}";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 规则5: 智能分词交换（兜底方案）
        /// 使用jieba分词进行智能交换
        /// </summary>
        private string? TrySwapBySegmentation(string text)
        {
            try
            {
                // 如果包含中文，使用分词
                if (ContainsChinese(text))
                {
                    return SwapChineseTextBySegmentation(text);
                }

                // 如果是英文，按空格分割
                if (text.Contains(' '))
                {
                    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 2)
                    {
                        return $"{words[1]} {words[0]}";
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查文本是否包含中文字符
        /// </summary>
        private bool ContainsChinese(string text)
        {
            return text.Any(c => c >= 0x4e00 && c <= 0x9fff);
        }

        /// <summary>
        /// 分割中英文混合文本
        /// </summary>
        private List<string> SplitChineseEnglish(string text)
        {
            var parts = new List<string>();
            var currentPart = new StringBuilder();
            bool lastWasChinese = false;
            bool firstCharProcessed = false;

            foreach (char c in text)
            {
                bool isChinese = c >= 0x4e00 && c <= 0x9fff;
                bool isEnglish = char.IsLetter(c) && c < 128;

                if (!firstCharProcessed)
                {
                    currentPart.Append(c);
                    lastWasChinese = isChinese;
                    firstCharProcessed = true;
                }
                else if ((isChinese && !lastWasChinese) || (isEnglish && lastWasChinese))
                {
                    // 语言类型切换，保存当前部分
                    if (currentPart.Length > 0)
                    {
                        parts.Add(currentPart.ToString().Trim());
                        currentPart.Clear();
                    }
                    currentPart.Append(c);
                    lastWasChinese = isChinese;
                }
                else
                {
                    currentPart.Append(c);
                }
            }

            // 添加最后一部分
            if (currentPart.Length > 0)
            {
                parts.Add(currentPart.ToString().Trim());
            }

            // 过滤空字符串
            return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        /// <summary>
        /// 使用分词进行中文文本交换
        /// </summary>
        private string? SwapChineseTextBySegmentation(string text)
        {
            try
            {
                var segmenter = GetSegmenter();
                var segments = segmenter.Cut(text, cutAll: false).ToList();

                // 过滤掉空白和标点符号，保留有意义的词
                var meaningfulSegments = segments.Where(s => !string.IsNullOrWhiteSpace(s) &&
                    s.Any(c => char.IsLetterOrDigit(c) || (c >= 0x4e00 && c <= 0x9fff))).ToList();

                if (meaningfulSegments.Count == 2)
                {
                    // 如果正好有两个有意义的词，交换它们
                    return $"{meaningfulSegments[1]}{meaningfulSegments[0]}";
                }
                else if (meaningfulSegments.Count > 2)
                {
                    // 如果有多个词，交换前两个
                    var result = new List<string>(segments);
                    int firstIndex = segments.IndexOf(meaningfulSegments[0]);
                    int secondIndex = segments.IndexOf(meaningfulSegments[1]);

                    if (firstIndex >= 0 && secondIndex >= 0)
                    {
                        result[firstIndex] = meaningfulSegments[1];
                        result[secondIndex] = meaningfulSegments[0];
                        return string.Join("", result);
                    }
                }
            }
            catch
            {
                // 忽略分词失败
            }

            return null;
        }

        /// <summary>
        /// 测试智能文字交换功能的各种场景
        /// </summary>
        private void TestIntelligentSwap()
        {
            var testCases = new Dictionary<string, string>
            {
                // 测试"的、地、得"规则
                { "我的书", "书的我" },
                { "红色的苹果", "苹果的红色" },
                { "快速地跑", "跑地快速" },
                { "写得好", "好得写" },

                // 测试逗号规则
                { "今天，明天", "明天，今天" },
                { "苹果，香蕉", "香蕉，苹果" },

                // 测试中英文混合
                { "hello世界", "世界hello" },
                { "我love你", "你love我" },
                { "good morning", "morning good" },

                // 测试两字符交换
                { "你我", "我你" },
                { "AB", "BA" },

                // 测试其他标点符号
                { "开始！结束", "结束！开始" },
                { "问题？答案", "答案？问题" }
            };

            // 测试智能文字交换功能（仅在调试模式下运行）
            #if DEBUG
            foreach (var testCase in testCases)
            {
                string input = testCase.Key;
                string expected = testCase.Value;
                string actual = SwapText(input);
                // 测试结果可在调试器中查看
            }
            #endif
        }

        private string GetSettingsFilePath()
        {
            // 获取程序根目录下的设置文件
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDirectory, "settings.ini");
        }

        // 开机启动相关方法
        private void SetStartupEnabled(bool enabled)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        string appName = "ClipboardTool";
                        string appPath = System.AppContext.BaseDirectory + "FlugiClipboard.exe";

                        if (enabled)
                        {
                            // 添加静默启动参数，使程序在开机时后台运行
                            key.SetValue(appName, $"\"{appPath}\" /silent");
                        }
                        else
                        {
                            if (key.GetValue(appName) != null)
                            {
                                key.DeleteValue(appName);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略设置开机启动失败
            }
        }

        // ========== 全新的AI翻译快捷键实现 ==========

        // 新的AI翻译快捷键变量 - 使用Ctrl+T避免冲突
        private uint _aiTranslateHotkeyNewModifiers = MOD_CONTROL | MOD_SHIFT;
        private uint _aiTranslateHotkeyNewKey = VK_T;
        private bool _aiTranslateHotkeyNewRegistered = false;

        /// <summary>
        /// 注册新的AI翻译快捷键
        /// </summary>
        private void RegisterAiTranslateHotkeyNew()
        {
            try
            {
                if (_hwndSource?.Handle == IntPtr.Zero || _hwndSource == null)
                {
                    return;
                }

                // 先注销旧的快捷键（如果已注册）
                UnregisterAiTranslateHotkeyNew();

                // 检查快捷键是否有效
                if (_aiTranslateHotkeyNewModifiers == 0 || _aiTranslateHotkeyNewKey == 0)
                {
                    // 如果快捷键无效，设置为默认值 Ctrl+T
                    _aiTranslateHotkeyNewModifiers = MOD_CONTROL;
                    _aiTranslateHotkeyNewKey = VK_T;
                    // AI翻译快捷键无效，已重置为默认值
                    // 注意：不要在这里返回，而是继续使用默认值进行注册
                }

                // 检查是否与其他快捷键冲突
                bool conflictWithClipboard = (_hotkeyModifiers == _aiTranslateHotkeyNewModifiers && _hotkeyKey == _aiTranslateHotkeyNewKey);
                bool conflictWithTextSwap = (_textSwapHotkeyModifiers == _aiTranslateHotkeyNewModifiers && _textSwapHotkeyKey == _aiTranslateHotkeyNewKey);

                if (conflictWithClipboard)
                {
                    return; // 与剪贴板快捷键冲突
                }

                if (conflictWithTextSwap)
                {
                    return; // 与文字交换快捷键冲突
                }

                // 注册新的快捷键
                bool success = RegisterHotKey(_hwndSource.Handle, AI_TRANSLATE_HOTKEY_NEW_ID, _aiTranslateHotkeyNewModifiers, _aiTranslateHotkeyNewKey);
                if (success)
                {
                    _aiTranslateHotkeyNewRegistered = true;
                }
                else
                {
                    // 如果注册失败，尝试使用默认值 Ctrl+T 再次注册
                    if (_aiTranslateHotkeyNewModifiers != MOD_CONTROL || _aiTranslateHotkeyNewKey != VK_T)
                    {
                        _aiTranslateHotkeyNewModifiers = MOD_CONTROL;
                        _aiTranslateHotkeyNewKey = VK_T;
                        // 尝试使用默认值重新注册
                        success = RegisterHotKey(_hwndSource.Handle, AI_TRANSLATE_HOTKEY_NEW_ID, _aiTranslateHotkeyNewModifiers, _aiTranslateHotkeyNewKey);
                        _aiTranslateHotkeyNewRegistered = success;
                    }
                    else
                    {
                        _aiTranslateHotkeyNewRegistered = false;
                    }
                }
            }
            catch
            {
                _aiTranslateHotkeyNewRegistered = false;
            }
        }

        /// <summary>
        /// 注销新的AI翻译快捷键
        /// </summary>
        private void UnregisterAiTranslateHotkeyNew()
        {
            try
            {
                if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero && _aiTranslateHotkeyNewRegistered)
                {
                    UnregisterHotKey(_hwndSource.Handle, AI_TRANSLATE_HOTKEY_NEW_ID);
                }
                _aiTranslateHotkeyNewRegistered = false;
            }
            catch
            {
                _aiTranslateHotkeyNewRegistered = false;
            }
        }

        /// <summary>
        /// 更新AI翻译快捷键
        /// </summary>
        private void UpdateAiTranslateHotkeyNew(uint modifiers, uint key)
        {
            try
            {
                // 更新快捷键值
                _aiTranslateHotkeyNewModifiers = modifiers;
                _aiTranslateHotkeyNewKey = key;

                // 重新注册快捷键
                RegisterAiTranslateHotkeyNew();
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// 获取AI翻译快捷键描述
        /// </summary>
        private string GetAiTranslateHotkeyNewDescription()
        {
            try
            {
                if (_aiTranslateHotkeyNewModifiers == 0 || _aiTranslateHotkeyNewKey == 0)
                {
                    return "未设置";
                }
                return GetHotkeyDescriptionNew(_aiTranslateHotkeyNewModifiers, _aiTranslateHotkeyNewKey);
            }
            catch
            {
                return "未知";
            }
        }

        /// <summary>
        /// 获取快捷键描述的通用方法
        /// </summary>
        private string GetHotkeyDescriptionNew(uint modifiers, uint key)
        {
            string description = "";
            if ((modifiers & MOD_WIN) != 0) description += "Win+";
            if ((modifiers & MOD_CONTROL) != 0) description += "Ctrl+";
            if ((modifiers & MOD_ALT) != 0) description += "Alt+";
            if ((modifiers & MOD_SHIFT) != 0) description += "Shift+";
            description += GetKeyName(key);
            return description;
        }

        /// <summary>
        /// 处理AI翻译快捷键触发 - 简化版本
        /// </summary>
        private async void HandleAiTranslateHotkeyNew()
        {
            try
            {
                // 简化的AI翻译逻辑，避免复杂的剪贴板操作
                string selectedText = await GetSelectedTextSimple();

                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    ShowAiTranslateNoTextMessage();
                    return;
                }

                // 显示AI翻译结果窗口
                ShowAiTranslateResult(selectedText);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI翻译功能出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 简化的文本获取方法，避免剪贴板残留
        private async Task<string> GetSelectedTextSimple()
        {
            try
            {
                // 保存原剪贴板内容
                string originalClipboard = "";
                bool hasOriginalContent = false;

                if (System.Windows.Clipboard.ContainsText())
                {
                    originalClipboard = System.Windows.Clipboard.GetText();
                    hasOriginalContent = true;
                }

                // 发送Ctrl+C获取选中文本
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event((byte)VK_C, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event((byte)VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // 等待复制完成
                await Task.Delay(200);

                string selectedText = "";
                // 获取剪贴板内容
                if (System.Windows.Clipboard.ContainsText())
                {
                    selectedText = System.Windows.Clipboard.GetText().Trim();
                }

                // 立即恢复原剪贴板内容，避免残留
                if (hasOriginalContent)
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(originalClipboard);
                    }
                    catch
                    {
                        // 如果恢复失败，至少清空剪贴板
                        try { System.Windows.Clipboard.Clear(); } catch { }
                    }
                }
                else
                {
                    // 如果原来没有内容，清空剪贴板
                    try { System.Windows.Clipboard.Clear(); } catch { }
                }

                return selectedText;
            }
            catch
            {
                // 忽略所有异常
                return "";
            }
        }

        /// <summary>
        /// 专门为AI翻译获取选中文本的方法
        /// </summary>
        private string GetSelectedTextForAiTranslate()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("开始获取选中文本用于AI翻译");

                // 保存当前剪贴板内容
                string originalClipboard = "";
                bool hasOriginalContent = false;
                
                // 首先检查剪贴板中是否已有文本内容
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        originalClipboard = System.Windows.Clipboard.GetText();
                        hasOriginalContent = true;
                        
                        // 如果剪贴板中已有非空文本，直接使用它，避免不必要的Ctrl+C操作
                        if (!string.IsNullOrWhiteSpace(originalClipboard))
                        {
                            System.Diagnostics.Debug.WriteLine($"剪贴板中已有文本内容，直接使用: {originalClipboard[..Math.Min(50, originalClipboard.Length)]}...");
                            return originalClipboard.Trim();
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"保存原剪贴板内容：{originalClipboard[..Math.Min(50, originalClipboard.Length)]}...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"保存原剪贴板内容失败：{ex.Message}");
                }

                // 清空剪贴板以确保能检测到新内容
                try
                {
                    ClearClipboardThoroughly(); // 使用彻底清空剪贴板的方法
                    System.Threading.Thread.Sleep(100); // 增加等待时间
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"清空剪贴板失败：{ex.Message}");
                }

                // 模拟 Ctrl+C 复制选中文本
                System.Diagnostics.Debug.WriteLine("发送 Ctrl+C 复制命令");
                keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(0x43, 0, 0, UIntPtr.Zero); // C键
                System.Threading.Thread.Sleep(50); // 确保按键被系统捕获
                keybd_event(0x43, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // 等待复制完成（增加等待时间，确保复制操作完成）
                System.Threading.Thread.Sleep(600); // 从500ms增加到600ms

                // 获取剪贴板内容
                string selectedText = "";
                try
                {
                    // 尝试多次获取剪贴板内容，增加成功率
                    for (int i = 0; i < 5; i++) // 从3次增加到5次尝试
                    {
                        if (System.Windows.Clipboard.ContainsText())
                        {
                            selectedText = System.Windows.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(selectedText))
                            {
                                System.Diagnostics.Debug.WriteLine($"获取到剪贴板内容：{selectedText[..Math.Min(50, selectedText.Length)]}...");
                                break;
                            }
                        }
                        System.Threading.Thread.Sleep(150); // 从100ms增加到150ms
                    }
                    
                    if (string.IsNullOrWhiteSpace(selectedText))
                    {
                        System.Diagnostics.Debug.WriteLine("剪贴板中没有文本内容");
                        
                        // 如果无法获取新的选中文本，但原剪贴板有内容，则尝试使用原内容
                        if (hasOriginalContent && !string.IsNullOrWhiteSpace(originalClipboard))
                        {
                            System.Diagnostics.Debug.WriteLine("尝试使用原剪贴板内容作为选中文本");
                            return originalClipboard.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"获取剪贴板内容失败：{ex.Message}");
                    // 如果获取失败但原剪贴板有内容，尝试使用原内容
                    if (hasOriginalContent && !string.IsNullOrWhiteSpace(originalClipboard))
                    {
                        return originalClipboard.Trim();
                    }
                    return "";
                }

                // 检查是否获取到文本
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    System.Diagnostics.Debug.WriteLine("没有获取到选中文本");
                    // 恢复原剪贴板内容
                    if (hasOriginalContent && !string.IsNullOrEmpty(originalClipboard))
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(originalClipboard);
                        }
                        catch { }
                    }
                    return "";
                }

                // 恢复原剪贴板内容（延迟恢复，避免影响翻译功能）
                if (hasOriginalContent && !string.IsNullOrEmpty(originalClipboard))
                {
                    Task.Delay(1500).ContinueWith(_ => // 从1000ms增加到1500ms
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(originalClipboard);
                            System.Diagnostics.Debug.WriteLine("已恢复原剪贴板内容");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"恢复原剪贴板内容失败：{ex.Message}");
                        }
                    });
                }

                System.Diagnostics.Debug.WriteLine($"成功获取选中文本：{selectedText.Trim()}");
                return selectedText.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取选中文本失败：{ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 显示没有选中文本的提示
        /// </summary>
        private void ShowAiTranslateNoTextMessage()
        {
            try
            {
                MessageBox.Show("请先选中要翻译的文本，然后按快捷键进行AI翻译。", "AI翻译", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                // 忽略显示提示失败
            }
        }

        // 从拆分词组窗口调用的AI翻译功能
        public void OpenAiTranslateWithText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                // 显示AI翻译结果窗口
                ShowAiTranslateResult(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI翻译功能出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ShowAiTranslateResult(string text)
        {
            try
            {
                // 检查配置是否完整
                if (string.IsNullOrEmpty(_aiApiUrl) || string.IsNullOrEmpty(_aiModel))
                {
                    string message = $"原文：{text}\n\n提供商：{_aiProvider}\nAPI地址：{_aiApiUrl}\n模型：{_aiModel}\n提示词：{_aiPrompt}\n\n注意：请先在设置中配置完整的AI翻译参数。";
                    MessageBox.Show(message, "AI翻译", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 清除剪切板中的选中内容，避免翻译后剪切板中残留选中的文本
                ClearClipboardThoroughly();
                
                // 再次清除，确保彻底清除
                Task.Delay(100).Wait();
                ClearClipboardThoroughly();

                // 显示加载提示
                string loadingMessage = $"原文：{text}\n\n正在翻译中，请稍候...";
                var loadingWindow = new Window
                {
                    Title = "AI翻译",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    Content = new TextBlock
                    {
                        Text = loadingMessage,
                        Margin = new Thickness(20),
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                loadingWindow.Show();

                try
                {
                    // 调用AI API进行翻译
                    string translatedText = await CallAiTranslateApi(text);

                    // 关闭加载窗口
                    loadingWindow.Close();

                    // 显示翻译结果
                    ShowTranslationResultWindow(text, translatedText);
                }
                catch (Exception apiEx)
                {
                    // 关闭加载窗口
                    loadingWindow.Close();

                    // 显示错误信息
                    string errorMessage = $"原文：{text}\n\n翻译失败：{apiEx.Message}\n\n提供商：{_aiProvider}\nAPI地址：{_aiApiUrl}\n模型：{_aiModel}";
                    MessageBox.Show(errorMessage, "AI翻译", MessageBoxButton.OK, MessageBoxImage.Error);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI翻译功能出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 清除剪贴板的强化方法
        private void ClearClipboardThoroughly()
        {
            try
            {
                // 使用多种方法清空剪贴板
                System.Windows.Clipboard.Clear();

                // 向剪贴板写入一个空字符串然后再清空
                System.Windows.Clipboard.SetText(" ");
                System.Windows.Clipboard.Clear();

                // 再次写入空字符串并清空，确保完全清除
                System.Windows.Clipboard.SetText("  ");
                System.Windows.Clipboard.Clear();

                // 使用WinForms的剪贴板API
                System.Windows.Forms.Clipboard.Clear();

                // 添加延迟，确保系统有足够时间完成剪贴板清理
                Task.Delay(150).Wait();

                // 最后再次清空
                System.Windows.Clipboard.Clear();
                System.Windows.Forms.Clipboard.Clear();
            }
            catch
            {
                // 忽略清除失败
            }
        }

        // 调用AI API进行翻译
        private async Task<string> CallAiTranslateApi(string text)
        {
            using (var httpClient = new HttpClient())
            {
                // 增加超时时间到120秒，适应大模型的响应时间
                httpClient.Timeout = TimeSpan.FromSeconds(120);

                // 设置认证头（如果需要）
                if (!string.IsNullOrEmpty(_aiApiKey) && _aiProvider != "ollama")
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _aiApiKey);
                }

                // 构建请求体
                object requestBody;
                
                // 针对不同的提供商构建不同的请求格式
                if (_aiProvider == "ollama")
                {
                    // Ollama API格式
                    requestBody = new
                    {
                        model = _aiModel,
                        messages = new[]
                        {
                            new { role = "user", content = $"{_aiPrompt}\n\n{text}" }
                        },
                        stream = false
                    };
                    
                    // 确保ollama API地址正确
                    if (!_aiApiUrl.Contains("/v1/chat/completions"))
                    {
                        if (_aiApiUrl.EndsWith("/"))
                        {
                            _aiApiUrl += "v1/chat/completions";
                        }
                        else
                        {
                            _aiApiUrl += "/v1/chat/completions";
                        }
                    }
                }
                else
                {
                    // OpenAI兼容格式
                    requestBody = new
                    {
                        model = _aiModel,
                        messages = new[]
                        {
                            new { role = "user", content = $"{_aiPrompt}\n\n{text}" }
                        },
                        temperature = 0.3,
                        max_tokens = 1000
                    };
                }

                string jsonRequest = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(_aiApiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    return ParseAiResponse(jsonResponse);
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API请求失败: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
        }

        // 解析AI API响应
        private string ParseAiResponse(string jsonResponse)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(jsonResponse))
                {
                    // 尝试解析OpenAI格式的响应
                    if (document.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out JsonElement message) &&
                            message.TryGetProperty("content", out JsonElement content))
                        {
                            return content.GetString() ?? "翻译结果为空";
                        }
                    }

                    // 如果没有找到标准格式，返回原始响应
                    return $"无法解析翻译结果，原始响应：{jsonResponse}";
                }
            }
            catch (JsonException ex)
            {
                throw new Exception($"解析AI响应失败: {ex.Message}");
            }
        }

        // 显示精致的翻译结果窗口
        private void ShowTranslationResultWindow(string originalText, string translatedText)
        {
            try
            {
                // 清除剪切板中的选中内容，避免翻译后剪切板中残留选中的文本
                ClearClipboardThoroughly();

                Task.Delay(50).ContinueWith(_ => {
                    Dispatcher.Invoke(() => {
                        try
                        {
                            var resultWindow = new Window
                            {
                                Title = "AI翻译",
                                Width = 600,
                                Height = 500,
                                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                Topmost = true,
                                ResizeMode = ResizeMode.CanResize,
                                MinWidth = 400,
                                MinHeight = 300
                            };

                            // 创建主容器
                            var mainGrid = new Grid();
                            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                            // 原文标题
                            var originalLabel = new TextBlock
                            {
                                Text = "原文：",
                                FontWeight = FontWeights.Bold,
                                FontSize = 14,
                                Margin = new Thickness(15, 15, 15, 5),
                                Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64))
                            };
                            Grid.SetRow(originalLabel, 0);
                            mainGrid.Children.Add(originalLabel);

                            // 原文内容（只读）
                            var originalTextBox = new TextBox
                            {
                                Text = originalText,
                                Margin = new Thickness(15, 0, 15, 10),
                                Padding = new Thickness(10),
                                IsReadOnly = true,
                                TextWrapping = TextWrapping.Wrap,
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                                BorderThickness = new Thickness(1),
                                FontSize = 13
                            };
                            Grid.SetRow(originalTextBox, 0);
                            mainGrid.Children.Add(originalTextBox);

                            // 分隔线
                            var separator = new Border
                            {
                                Height = 1,
                                Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                                Margin = new Thickness(15, 5, 15, 5)
                            };
                            Grid.SetRow(separator, 1);
                            mainGrid.Children.Add(separator);

                            // 译文标题
                            var translatedLabel = new TextBlock
                            {
                                Text = "译文：",
                                FontWeight = FontWeights.Bold,
                                FontSize = 14,
                                Margin = new Thickness(15, 10, 15, 5),
                                Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64))
                            };
                            Grid.SetRow(translatedLabel, 2);
                            mainGrid.Children.Add(translatedLabel);

                            // 译文内容（可编辑）
                            var translatedTextBox = new TextBox
                            {
                                Text = translatedText,
                                Margin = new Thickness(15, 0, 15, 10),
                                Padding = new Thickness(10),
                                TextWrapping = TextWrapping.Wrap,
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                AcceptsReturn = true,
                                Background = Brushes.White,
                                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                                BorderThickness = new Thickness(1),
                                FontSize = 13
                            };
                            Grid.SetRow(translatedTextBox, 2);
                            mainGrid.Children.Add(translatedTextBox);

                            // 信息栏
                            var infoPanel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Margin = new Thickness(15, 5, 15, 10),
                                HorizontalAlignment = HorizontalAlignment.Left
                            };

                            var providerInfo = new TextBlock
                            {
                                Text = $"提供商：{_aiProvider}",
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                                Margin = new Thickness(0, 0, 20, 0)
                            };
                            infoPanel.Children.Add(providerInfo);

                            var modelInfo = new TextBlock
                            {
                                Text = $"模型：{_aiModel}",
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
                            };
                            infoPanel.Children.Add(modelInfo);

                            Grid.SetRow(infoPanel, 3);
                            mainGrid.Children.Add(infoPanel);

                            // 按钮栏
                            var buttonPanel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Margin = new Thickness(15, 10, 15, 15)
                            };

                            // 复制译文按钮
                            var copyButton = new Button
                            {
                                Content = "复制译文",
                                Width = 80,
                                Height = 30,
                                Margin = new Thickness(0, 0, 10, 0),
                                Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                                Foreground = Brushes.White,
                                BorderThickness = new Thickness(0),
                                FontSize = 12
                            };
                            copyButton.Click += (s, e) =>
                            {
                                try
                                {
                                    Clipboard.SetText(translatedTextBox.Text);
                                    copyButton.Content = "已复制";
                                    Task.Delay(1500).ContinueWith(_ =>
                                    {
                                        Dispatcher.Invoke(() => copyButton.Content = "复制译文");
                                    });
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            };
                            buttonPanel.Children.Add(copyButton);

                            // 关闭按钮
                            var closeButton = new Button
                            {
                                Content = "关闭",
                                Width = 60,
                                Height = 30,
                                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                                Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                                BorderThickness = new Thickness(1),
                                FontSize = 12
                            };
                            closeButton.Click += (s, e) => resultWindow.Close();
                            buttonPanel.Children.Add(closeButton);

                            Grid.SetRow(buttonPanel, 4);
                            mainGrid.Children.Add(buttonPanel);

                            resultWindow.Content = mainGrid;

                            // 设置窗口图标（如果有的话）
                            try
                            {
                                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico", "ilo.ico");
                                if (System.IO.File.Exists(iconPath))
                                {
                                    resultWindow.Icon = new BitmapImage(new Uri(iconPath));
                                }
                            }
                            catch
                            {
                                // 忽略图标加载错误
                            }

                            // 显示窗口
                            resultWindow.ShowDialog();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"创建翻译结果窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI翻译功能出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }

    /// <summary>
    /// 剪贴板项目数据模型
    /// </summary>
    public class ClipboardItem : INotifyPropertyChanged
    {
        private string _text;
        private DateTime _timestamp;
        private bool _isExpanded;
        private bool _isPinned;
        private System.Windows.Media.Imaging.BitmapSource? _image;

        public ClipboardItem(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _timestamp = DateTime.Now;
            _isExpanded = false;
            _isPinned = false;
            _image = null;
        }

        public ClipboardItem(System.Windows.Media.Imaging.BitmapSource image)
        {
            _text = "[图片]";
            _timestamp = DateTime.Now;
            _isExpanded = false;
            _isPinned = false;
            _image = CompressImage(image ?? throw new ArgumentNullException(nameof(image)));
        }

        private static System.Windows.Media.Imaging.BitmapSource CompressImage(System.Windows.Media.Imaging.BitmapSource source)
        {
            try
            {
                // 更激进的压缩以节省内存
                const int maxWidth = 400;  // 减小最大宽度
                const int maxHeight = 300; // 减小最大高度

                if (source.PixelWidth <= maxWidth && source.PixelHeight <= maxHeight)
                {
                    // 即使不需要缩放，也要冻结以减少内存使用
                    if (!source.IsFrozen)
                    {
                        source.Freeze();
                    }
                    return source;
                }

                // 计算缩放比例
                double scaleX = (double)maxWidth / source.PixelWidth;
                double scaleY = (double)maxHeight / source.PixelHeight;
                double scale = Math.Min(scaleX, scaleY);

                // 创建缩放后的图片
                var transformedBitmap = new System.Windows.Media.Imaging.TransformedBitmap(source,
                    new System.Windows.Media.ScaleTransform(scale, scale));

                // 冻结以提高性能并减少内存使用
                transformedBitmap.Freeze();
                return transformedBitmap;
            }
            catch
            {
                // 如果压缩失败，至少尝试冻结原图
                try
                {
                    if (!source.IsFrozen)
                    {
                        source.Freeze();
                    }
                }
                catch
                {
                    // 忽略冻结失败
                }
                return source;
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardPreview));
                    OnPropertyChanged(nameof(ShowExpandButton));
                }
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TimeDisplay));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardPreview));
                    OnPropertyChanged(nameof(ExpandButtonText));
                    OnPropertyChanged(nameof(MaxPreviewHeight));
                }
            }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PinMenuText));
                    OnPropertyChanged(nameof(PinIcon));
                    OnPropertyChanged(nameof(PinTooltip));
                }
            }
        }

        public System.Windows.Media.Imaging.BitmapSource? Image
        {
            get => _image;
            set
            {
                if (_image != value)
                {
                    _image = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsImage));
                    OnPropertyChanged(nameof(CardPreview));
                }
            }
        }

        public bool IsImage => _image != null;

        public string PinMenuText => _isPinned ? "取消固定" : "固定";

        public string PinIcon => _isPinned ? "📌" : "📍";

        public string PinTooltip => _isPinned ? "取消固定" : "固定";

        public string CardPreview
        {
            get
            {
                if (IsImage)
                    return "[图片]";

                if (string.IsNullOrEmpty(Text))
                    return "(空内容)";

                if (IsExpanded)
                    return Text;

                const int maxLength = 120;
                if (Text.Length <= maxLength)
                    return Text;

                // 使用更高效的字符串截取方法
                return string.Concat(Text.AsSpan(0, maxLength), "...");
            }
        }

        public string ExpandButtonText => IsExpanded ? "收起" : "展开";

        public Visibility ShowExpandButton => Text.Length > 120 ? Visibility.Visible : Visibility.Collapsed;

        public double MaxPreviewHeight => IsExpanded ? double.PositiveInfinity : 54;

        public string TimeDisplay
        {
            get
            {
                var now = DateTime.Now;
                var diff = now - Timestamp;

                if (diff.TotalMinutes < 1)
                    return "刚刚";
                else if (diff.TotalHours < 1)
                    return $"{(int)diff.TotalMinutes} 分钟前";
                else if (diff.TotalDays < 1)
                    return $"{(int)diff.TotalHours} 小时前";
                else if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} 天前";
                else
                    return Timestamp.ToString("MM/dd HH:mm");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
