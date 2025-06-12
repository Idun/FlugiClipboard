using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JiebaNet.Segmenter;

namespace FlugiClipboard
{
    public partial class SplitWordsWindow : Window
    {
        private readonly string _originalText;
        private readonly JiebaSegmenter _segmenter;
        private readonly List<SegmentInfo> _segments;
        private readonly MainWindow? _parentMainWindow;
        
        // 记录窗口大小
        private static double _savedWindowWidth = 700;
        private static double _savedWindowHeight = 500;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int VK_CONTROL = 0x11;
        private const int VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private IntPtr _previousForegroundWindow;

        public SplitWordsWindow(string text, JiebaSegmenter segmenter, IntPtr previousForegroundWindow = default, MainWindow? parentMainWindow = null)
        {
            InitializeComponent();
            _originalText = text ?? throw new ArgumentNullException(nameof(text));
            _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
            _segments = new List<SegmentInfo>();
            _parentMainWindow = parentMainWindow;

            // 使用传入的前台窗口，如果没有传入则获取当前前台窗口
            _previousForegroundWindow = previousForegroundWindow != IntPtr.Zero ? previousForegroundWindow : GetForegroundWindow();

            // 确保窗口状态为Normal
            WindowState = WindowState.Normal;
            
            // 加载保存的窗口大小
            LoadSavedWindowSize();
            
            // 添加窗口大小变化事件
            SizeChanged += SplitWordsWindow_SizeChanged;

            InitializeWindow();
        }

        private async void InitializeWindow()
        {
            try
            {
                // 开始分词
                StatusTextBlock.Text = "正在分词...";
                await PerformSegmentation();

                StatusTextBlock.Text = $"分词完成，共 {_segments.Count} 个词块";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"初始化错误: {ex.Message}";
                MessageBox.Show($"初始化窗口时出错: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task PerformSegmentation()
        {
            await Task.Run(() =>
            {
                try
                {
                    _segments.Clear();
                    var processedRanges = new List<(int start, int end)>();

                    // 先处理英文单词（包括连续的英文字符）
                    var englishRegex = new Regex(@"[a-zA-Z]+");
                    var englishMatches = englishRegex.Matches(_originalText);

                    foreach (Match match in englishMatches)
                    {
                        _segments.Add(new SegmentInfo
                        {
                            Text = match.Value,
                            StartIndex = match.Index,
                            EndIndex = match.Index + match.Length,
                            IsSelected = false
                        });
                        processedRanges.Add((match.Index, match.Index + match.Length));
                    }

                    // 处理数字
                    var numberRegex = new Regex(@"\d+");
                    var numberMatches = numberRegex.Matches(_originalText);

                    foreach (Match match in numberMatches)
                    {
                        if (!IsRangeProcessed(match.Index, match.Index + match.Length, processedRanges))
                        {
                            _segments.Add(new SegmentInfo
                            {
                                Text = match.Value,
                                StartIndex = match.Index,
                                EndIndex = match.Index + match.Length,
                                IsSelected = false
                            });
                            processedRanges.Add((match.Index, match.Index + match.Length));
                        }
                    }

                    // 中文分词（处理未被英文和数字占用的中文部分）
                    var chineseRegex = new Regex(@"[\u4e00-\u9fff]+");
                    var chineseMatches = chineseRegex.Matches(_originalText);

                    foreach (Match match in chineseMatches)
                    {
                        if (!IsRangeProcessed(match.Index, match.Index + match.Length, processedRanges))
                        {
                            var chineseSegments = _segmenter.Cut(match.Value, cutAll: false);
                            int currentIndex = match.Index;

                            foreach (var segment in chineseSegments)
                            {
                                if (!string.IsNullOrWhiteSpace(segment))
                                {
                                    int segmentIndex = _originalText.IndexOf(segment, currentIndex);
                                    if (segmentIndex >= 0 && !IsRangeProcessed(segmentIndex, segmentIndex + segment.Length, processedRanges))
                                    {
                                        _segments.Add(new SegmentInfo
                                        {
                                            Text = segment,
                                            StartIndex = segmentIndex,
                                            EndIndex = segmentIndex + segment.Length,
                                            IsSelected = false
                                        });
                                        processedRanges.Add((segmentIndex, segmentIndex + segment.Length));
                                        currentIndex = segmentIndex + segment.Length;
                                    }
                                }
                            }
                        }
                    }

                    // 处理标点符号和特殊字符
                    var punctuationRegex = new Regex(@"[^\w\s\u4e00-\u9fff]+");
                    var punctuationMatches = punctuationRegex.Matches(_originalText);

                    foreach (Match match in punctuationMatches)
                    {
                        if (!IsRangeProcessed(match.Index, match.Index + match.Length, processedRanges))
                        {
                            _segments.Add(new SegmentInfo
                            {
                                Text = match.Value,
                                StartIndex = match.Index,
                                EndIndex = match.Index + match.Length,
                                IsSelected = false
                            });
                            processedRanges.Add((match.Index, match.Index + match.Length));
                        }
                    }

                    // 按位置排序
                    _segments.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = $"分词错误: {ex.Message}";
                    });
                }
            });

            // 在UI线程中创建词块按钮
            Dispatcher.Invoke(() =>
            {
                CreateSegmentButtons();
                UpdateSelectedText();
            });
        }

        private bool IsRangeProcessed(int start, int end, List<(int start, int end)> processedRanges)
        {
            return processedRanges.Any(range => 
                (start >= range.start && start < range.end) ||
                (end > range.start && end <= range.end) ||
                (start <= range.start && end >= range.end));
        }

        private void CreateSegmentButtons()
        {
            SegmentsPanel.Children.Clear();

            foreach (var segment in _segments)
            {
                var button = new Button
                {
                    Content = segment.Text,
                    Style = (Style)FindResource("SegmentButtonStyle"),
                    Tag = segment
                };

                button.Click += SegmentButton_Click;
                button.MouseDoubleClick += SegmentButton_DoubleClick;
                SegmentsPanel.Children.Add(button);
            }
        }

        private void SegmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SegmentInfo segment)
            {
                // 切换选择状态
                segment.IsSelected = !segment.IsSelected;

                // 更新按钮样式
                if (segment.IsSelected)
                {
                    button.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // 蓝色
                    button.Foreground = Brushes.White;
                }
                else
                {
                    button.Background = Brushes.White;
                    button.Foreground = new SolidColorBrush(Color.FromRgb(50, 49, 48));
                }

                UpdateSelectedText();
            }
        }

        private void SegmentButton_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Button button && button.Tag is SegmentInfo segment)
            {
                // 双击直接输入该词块
                AutoInputText(segment.Text);
            }
        }

        private void AutoInputText(string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                {
                    // 将文本放入剪贴板
                    System.Windows.Clipboard.SetText(text);

                    // 更新状态
                    StatusTextBlock.Text = $"正在输入: {text}";

                    // 关闭窗口
                    Close();

                    // 通知MainWindow最小化到系统托盘
                    if (_parentMainWindow != null)
                    {
                        _parentMainWindow.Dispatcher.Invoke(() =>
                        {
                            _parentMainWindow.WindowState = WindowState.Minimized;
                        });
                    }

                    // 异步执行输入操作 - 使用与MainWindow相同的简单可靠方案
                    Task.Run(async () =>
                    {
                        try
                        {
                            // 短暂延迟等待窗口关闭
                            await Task.Delay(200);

                            // 简单激活目标窗口（不使用ShowWindow避免窗口状态改变）
                            if (_previousForegroundWindow != IntPtr.Zero)
                            {
                                SetForegroundWindow(_previousForegroundWindow);
                                await Task.Delay(100);
                            }

                            // 使用简单可靠的keybd_event方法（与MainWindow保持一致）
                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                            await Task.Delay(50);
                            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                            await Task.Delay(50);
                            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            await Task.Delay(50);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                        }
                        catch
                        {
                            // 忽略输入失败
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"输入失败: {ex.Message}";
            }
        }

        private void UpdateSelectedText()
        {
            var selectedSegments = _segments.Where(s => s.IsSelected).OrderBy(s => s.StartIndex);
            // 去掉空格，直接连接词块
            var selectedText = string.Join("", selectedSegments.Select(s => s.Text));

            SelectedTextBlock.Text = string.IsNullOrEmpty(selectedText) ? "(未选择任何词块)" : $"已选择: {selectedText}";

            // 更新状态栏
            int selectedCount = selectedSegments.Count();
            StatusTextBlock.Text = $"已选择 {selectedCount} 个词块，共 {_segments.Count} 个";
        }

        private void InputSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedSegments = _segments.Where(s => s.IsSelected).OrderBy(s => s.StartIndex);
                // 去掉空格，直接连接词块
                var selectedText = string.Join("", selectedSegments.Select(s => s.Text));

                if (string.IsNullOrEmpty(selectedText))
                {
                    MessageBox.Show("请先选择要输入的词块", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 执行自动输入到目标窗口
                AutoInputText(selectedText);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"输入失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var segment in _segments)
            {
                segment.IsSelected = false;
            }

            // 更新所有按钮样式
            foreach (Button button in SegmentsPanel.Children.OfType<Button>())
            {
                button.Background = Brushes.White;
                button.Foreground = new SolidColorBrush(Color.FromRgb(50, 49, 48));
            }

            UpdateSelectedText();
        }

        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedSegments = _segments.Where(s => s.IsSelected).OrderBy(s => s.StartIndex);
                // 去掉空格，直接连接词块
                var selectedText = string.Join("", selectedSegments.Select(s => s.Text));

                if (string.IsNullOrEmpty(selectedText))
                {
                    MessageBox.Show("请先选择要翻译的词块", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 关闭当前窗口
                Close();

                // 通知MainWindow打开AI翻译面板并发送文本
                if (_parentMainWindow != null)
                {
                    _parentMainWindow.Dispatcher.Invoke(() =>
                    {
                        _parentMainWindow.OpenAiTranslateWithText(selectedText);
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"翻译失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LoadSavedWindowSize()
        {
            try
            {
                string settingsPath = GetSplitWindowFilePath();
                if (System.IO.File.Exists(settingsPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(settingsPath);
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
                                Width = width;
                            }
                            else if (key == "WindowHeight" && double.TryParse(value, out double height))
                            {
                                _savedWindowHeight = height;
                                Height = height;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 使用默认值
                Width = _savedWindowWidth;
                Height = _savedWindowHeight;
            }
        }
        
        private void SplitWordsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 只在窗口状态为Normal时保存大小
            if (WindowState == WindowState.Normal)
            {
                _savedWindowWidth = Width;
                _savedWindowHeight = Height;
                
                // 保存到设置文件
                SaveWindowSize();
            }
        }
        
        private void SaveWindowSize()
        {
            try
            {
                string settingsPath = GetSplitWindowFilePath();
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(settingsPath, false))
                {
                    writer.WriteLine($"WindowWidth={_savedWindowWidth}");
                    writer.WriteLine($"WindowHeight={_savedWindowHeight}");
                }
            }
            catch
            {
                // 忽略保存失败
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 保存窗口尺寸
                SaveWindowSize();
                
                // 清理资源
                _segments.Clear();
                SegmentsPanel.Children.Clear();
            }
            catch
            {
                // 忽略清理时的错误
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        private string GetSplitWindowFilePath()
        {
            // 获取程序根目录下的设置文件
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDirectory, "splitwindow_settings.ini");
        }
    }

    /// <summary>
    /// 词块信息
    /// </summary>
    public class SegmentInfo
    {
        public string Text { get; set; } = "";
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public bool IsSelected { get; set; }
    }
}
