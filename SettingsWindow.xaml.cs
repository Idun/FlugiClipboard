using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace FlugiClipboard
{
    public partial class SettingsWindow : Window
    {
        // 快捷键相关属性
        private uint _currentModifiers = 0;
        private uint _currentKey = 0;
        private bool _isCapturingHotkey = false;

        // 智能文字交换快捷键相关属性
        private uint _currentTextSwapModifiers = 0;
        private uint _currentTextSwapKey = 0;
        private bool _isCapturingTextSwapHotkey = false;

        // AI翻译快捷键相关属性
        private uint _currentAiTranslateModifiers = 0;
        private uint _currentAiTranslateKey = 0;
        private bool _isCapturingAiTranslateHotkey = false;

        // 的地得变换快捷键相关属性
        private uint _currentDeDeDeModifiers = 0;
        private uint _currentDeDeDeKey = 0;
        private bool _isCapturingDeDeDeHotkey = false;
        
        // 记录窗口大小
        private static double _savedWindowWidth = 600;
        private static double _savedWindowHeight = 450;

        // 公共属性用于外部访问
        public bool SingleClickPaste { get; set; } = false;
        public bool DoubleClickPaste { get; set; } = true;
        public int MaxItems { get; set; } = 20;
        public bool SaveHistoryEnabled { get; set; } = false;
        public string HistoryFolderPath { get; set; } = "";
        public uint HotkeyModifiers { get; set; } = 0;
        public uint HotkeyKey { get; set; } = 0;

        // 智能文字交换功能属性
        public bool TextSwapEnabled { get; set; } = true;
        public uint TextSwapHotkeyModifiers { get; set; } = 0;
        public uint TextSwapHotkeyKey { get; set; } = 0;

        // 开机启动功能属性
        public bool StartupEnabled { get; set; } = false;

        // AI翻译功能属性
        public string AiProvider { get; set; } = "ollama";
        public string AiApiUrl { get; set; } = "http://localhost:11434";
        public string AiApiKey { get; set; } = "";
        public string AiModel { get; set; } = "";
        public string AiPrompt { get; set; } = "你是一个中英文翻译专家，将用户输入的中文翻译成英文，或将用户输入的英文翻译成中文。对于非中文内容，它将提供中文翻译结果。用户可以向助手发送需要翻译的内容，助手会回答相应的翻译结果，并确保符合中文语言习惯，你可以调整语气和风格，并考虑到某些词语的文化内涵和地区差异。同时作为翻译家，需将原文翻译成具有信达雅标准的译文。\"信\" 即忠实于原文的内容与意图；\"达\" 意味着译文应通顺易懂，表达清晰；\"雅\" 则追求译文的文化审美和语言的优美。目标是创作出既忠于原作精神，又符合目标语言文化和读者审美的翻译。";
        public uint AiTranslateHotkeyModifiers { get; set; } = 0;
        public uint AiTranslateHotkeyKey { get; set; } = 0;

        // 的地得变换功能属性
        public bool DeDeDeEnabled { get; set; } = true;
        public uint DeDeDeHotkeyModifiers { get; set; } = 0;
        public uint DeDeDeHotkeyKey { get; set; } = 0;

        // 默认快捷键值
        private const uint DEFAULT_HOTKEY_MODIFIERS = 0x0001 | 0x0002; // MOD_ALT | MOD_CONTROL
        private const uint DEFAULT_HOTKEY_KEY = 0x43; // C
        private const uint DEFAULT_TEXT_SWAP_MODIFIERS = 0x0002; // MOD_CONTROL
        private const uint DEFAULT_TEXT_SWAP_KEY = 0x51; // Q
        private const uint DEFAULT_DEDEDE_MODIFIERS = 0x0002; // MOD_CONTROL
        private const uint DEFAULT_DEDEDE_KEY = 0x47; // G

        public SettingsWindow()
        {
            InitializeComponent();
            // 确保窗口状态为Normal
            WindowState = WindowState.Normal;
            
            // 加载保存的窗口大小
            LoadSavedWindowSize();
            
            // 添加窗口大小变化事件
            SizeChanged += SettingsWindow_SizeChanged;
            
            LoadSettings();
        }
        
        private void LoadSavedWindowSize()
        {
            try
            {
                string settingsPath = GetSettingsWindowFilePath();
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
        
        private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
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
                string settingsPath = GetSettingsWindowFilePath();
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

        private void LoadSettings()
        {
            // 加载设置到界面
            MaxItemsTextBox.Text = MaxItems.ToString();
            SingleClickPasteCheckBox.IsChecked = SingleClickPaste;
            DoubleClickPasteCheckBox.IsChecked = DoubleClickPaste;

            // 设置默认快捷键 Ctrl+Alt+C（仅在没有设置时）
            if (HotkeyModifiers == 0 && HotkeyKey == 0)
            {
                HotkeyModifiers = 0x0001 | 0x0002; // MOD_ALT | MOD_CONTROL
                HotkeyKey = 0x43; // C
            }

            // 使用传入的快捷键值
            _currentModifiers = HotkeyModifiers;
            _currentKey = HotkeyKey;
            UpdateHotkeyDisplay();

            // 加载历史保存设置
            SaveHistoryCheckBox.IsChecked = SaveHistoryEnabled;
            if (string.IsNullOrEmpty(HistoryFolderPath))
            {
                HistoryFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClipboardHistory");
            }
            HistoryPathTextBox.Text = HistoryFolderPath;

            // 加载开机启动设置
            StartupEnabledCheckBox.IsChecked = StartupEnabled;

            // 加载智能文字交换设置
            TextSwapEnabledCheckBox.IsChecked = TextSwapEnabled;

            // 设置默认智能文字交换快捷键 Ctrl+Q（仅在没有设置时）
            if (TextSwapHotkeyModifiers == 0 && TextSwapHotkeyKey == 0)
            {
                TextSwapHotkeyModifiers = 0x0002; // MOD_CONTROL
                TextSwapHotkeyKey = 0x51; // Q
            }

            _currentTextSwapModifiers = TextSwapHotkeyModifiers;
            _currentTextSwapKey = TextSwapHotkeyKey;
            UpdateTextSwapHotkeyDisplay();

            // 加载AI翻译设置
            LoadAiTranslateSettings();

            // 加载的地得变换设置
            LoadDeDeDeSettings();

        }

        public void LoadSettingsToUI()
        {
            // 强制刷新界面显示当前设置
            LoadSettings();

            // 确保AI翻译设置在UI控件初始化后再次加载
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LoadAiTranslateSettings();
                LoadDeDeDeSettings();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void LoadAiTranslateSettings()
        {
            // 加载AI翻译设置
            if (AiProviderComboBox != null)
            {
                // 设置默认提供商
                string provider = AiProvider;
                if (string.IsNullOrEmpty(provider))
                {
                    provider = "ollama";
                }

                // 选择对应项
                foreach (ComboBoxItem item in AiProviderComboBox.Items)
                {
                    if (item.Tag?.ToString() == provider)
                    {
                        AiProviderComboBox.SelectedItem = item;
                        break;
                    }
                }

                // 如果没有找到匹配项，默认选择第一项
                if (AiProviderComboBox.SelectedItem == null && AiProviderComboBox.Items.Count > 0)
                {
                    AiProviderComboBox.SelectedIndex = 0;
                }
            }

            // 填充API地址和其他设置
            if (AiApiUrlTextBox != null)
            {
                AiApiUrlTextBox.Text = AiApiUrl;
            }

            if (AiApiKeyPasswordBox != null)
            {
                AiApiKeyPasswordBox.Password = AiApiKey;
            }

            if (AiPromptTextBox != null)
            {
                AiPromptTextBox.Text = AiPrompt;
            }

            // 加载模型列表
            UpdateModelList(GetCurrentProvider());

            // 确保模型选择被正确设置
            if (!string.IsNullOrEmpty(AiModel))
            {
                SelectModelInComboBox(AiModel);
            }

            // 设置默认快捷键 Ctrl+T（仅在没有设置时）
            if (AiTranslateHotkeyModifiers == 0 || AiTranslateHotkeyKey == 0)
            {
                AiTranslateHotkeyModifiers = 0x0002; // MOD_CONTROL
                AiTranslateHotkeyKey = 0x54; // T
            }

            // 使用传入的快捷键值
            _currentAiTranslateModifiers = AiTranslateHotkeyModifiers;
            _currentAiTranslateKey = AiTranslateHotkeyKey;
            
            // 更新界面显示
            UpdateAiTranslateHotkeyDisplay();
        }

        private void LoadDeDeDeSettings()
        {
            // 加载的地得变换功能开关
            if (DeDeDeEnabledCheckBox != null)
            {
                DeDeDeEnabledCheckBox.IsChecked = DeDeDeEnabled;
            }

            // 检查是否需要升级旧的快捷键设置为新的 Ctrl+G
            if ((DeDeDeHotkeyModifiers == 0x0002 && DeDeDeHotkeyKey == 0x44) || // 旧的 Ctrl+D
                (DeDeDeHotkeyModifiers == (0x0002 | 0x0004) && DeDeDeHotkeyKey == 0x44)) // 旧的 Ctrl+Shift+D
            {
                // 升级为新的默认快捷键 Ctrl+G
                DeDeDeHotkeyModifiers = DEFAULT_DEDEDE_MODIFIERS; // MOD_CONTROL
                DeDeDeHotkeyKey = DEFAULT_DEDEDE_KEY; // G
            }
            // 设置默认快捷键（仅在没有设置时）
            else if (DeDeDeHotkeyModifiers == 0 || DeDeDeHotkeyKey == 0)
            {
                DeDeDeHotkeyModifiers = DEFAULT_DEDEDE_MODIFIERS; // MOD_CONTROL
                DeDeDeHotkeyKey = DEFAULT_DEDEDE_KEY; // G
            }

            // 使用传入的快捷键值
            _currentDeDeDeModifiers = DeDeDeHotkeyModifiers;
            _currentDeDeDeKey = DeDeDeHotkeyKey;

            // 更新界面显示
            UpdateDeDeDeHotkeyDisplay();
        }

        private void UpdateHotkeyDisplay()
        {
            string hotkeyText = "";

            // 添加修饰键
            if ((_currentModifiers & 0x0001) != 0) hotkeyText += "Alt+";
            if ((_currentModifiers & 0x0002) != 0) hotkeyText += "Ctrl+";
            if ((_currentModifiers & 0x0004) != 0) hotkeyText += "Shift+";

            // 添加主键
            if (_currentKey != 0)
            {
                hotkeyText += GetKeyName(_currentKey);
            }

            if (string.IsNullOrEmpty(hotkeyText))
            {
                hotkeyText = "未设置";
            }

            HotkeyDisplayTextBlock.Text = hotkeyText;
        }

        private void UpdateTextSwapHotkeyDisplay()
        {
            string hotkeyText = "";

            // 添加修饰键
            if ((_currentTextSwapModifiers & 0x0001) != 0) hotkeyText += "Alt+";
            if ((_currentTextSwapModifiers & 0x0002) != 0) hotkeyText += "Ctrl+";
            if ((_currentTextSwapModifiers & 0x0004) != 0) hotkeyText += "Shift+";

            // 添加主键
            if (_currentTextSwapKey != 0)
            {
                hotkeyText += GetKeyName(_currentTextSwapKey);
            }

            if (string.IsNullOrEmpty(hotkeyText))
            {
                hotkeyText = "未设置";
            }

            TextSwapHotkeyDisplayTextBlock.Text = hotkeyText;
        }

        private void UpdateAiTranslateHotkeyDisplay()
        {
            string hotkeyText = "";

            // 添加修饰键 - 按照Win+Ctrl+Alt+Shift的顺序
            if ((_currentAiTranslateModifiers & 0x0008) != 0) hotkeyText += "Win+";
            if ((_currentAiTranslateModifiers & 0x0002) != 0) hotkeyText += "Ctrl+";
            if ((_currentAiTranslateModifiers & 0x0001) != 0) hotkeyText += "Alt+";
            if ((_currentAiTranslateModifiers & 0x0004) != 0) hotkeyText += "Shift+";

            // 添加主键
            if (_currentAiTranslateKey != 0)
            {
                hotkeyText += GetKeyName(_currentAiTranslateKey);
            }

            if (string.IsNullOrEmpty(hotkeyText))
            {
                hotkeyText = "未设置";
            }

            if (AiTranslateHotkeyDisplayTextBlock != null)
            {
                AiTranslateHotkeyDisplayTextBlock.Text = hotkeyText;
            }
        }

        private void UpdateDeDeDeHotkeyDisplay()
        {
            string hotkeyText = "";

            // 添加修饰键 - 按照标准顺序：Ctrl+Alt+Shift
            if ((_currentDeDeDeModifiers & 0x0002) != 0) hotkeyText += "Ctrl+";
            if ((_currentDeDeDeModifiers & 0x0001) != 0) hotkeyText += "Alt+";
            if ((_currentDeDeDeModifiers & 0x0004) != 0) hotkeyText += "Shift+";

            // 添加主键
            if (_currentDeDeDeKey != 0)
            {
                hotkeyText += GetKeyName(_currentDeDeDeKey);
            }

            if (string.IsNullOrEmpty(hotkeyText))
            {
                hotkeyText = "未设置";
            }

            if (DeDeDeHotkeyDisplayTextBlock != null)
            {
                DeDeDeHotkeyDisplayTextBlock.Text = hotkeyText;
            }
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

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // 重置所有按钮样式
                GeneralTabButton.Style = (Style)FindResource("SidebarButtonStyle");
                HotkeyTabButton.Style = (Style)FindResource("SidebarButtonStyle");
                TextSwapTabButton.Style = (Style)FindResource("SidebarButtonStyle");
                if (DeDeDeTabButton != null)
                {
                    DeDeDeTabButton.Style = (Style)FindResource("SidebarButtonStyle");
                }
                if (AiTranslateTabButton != null)
                {
                    AiTranslateTabButton.Style = (Style)FindResource("SidebarButtonStyle");
                }
                AboutTabButton.Style = (Style)FindResource("SidebarButtonStyle");

                // 设置当前按钮为选中状态
                button.Style = (Style)FindResource("SelectedSidebarButtonStyle");

                // 隐藏所有面板
                GeneralPanel.Visibility = Visibility.Collapsed;
                HotkeyPanel.Visibility = Visibility.Collapsed;
                TextSwapPanel.Visibility = Visibility.Collapsed;
                if (DeDeDePanel != null)
                {
                    DeDeDePanel.Visibility = Visibility.Collapsed;
                }
                if (AiTranslatePanel != null)
                {
                    AiTranslatePanel.Visibility = Visibility.Collapsed;
                }
                AboutPanel.Visibility = Visibility.Collapsed;

                // 显示对应面板
                string tag = button.Tag?.ToString() ?? "";
                switch (tag)
                {
                    case "General":
                        GeneralPanel.Visibility = Visibility.Visible;
                        break;
                    case "Hotkey":
                        HotkeyPanel.Visibility = Visibility.Visible;
                        break;
                    case "TextSwap":
                        TextSwapPanel.Visibility = Visibility.Visible;
                        break;
                    case "DeDeDe":
                        if (DeDeDePanel != null)
                        {
                            DeDeDePanel.Visibility = Visibility.Visible;
                        }
                        break;
                    case "AiTranslate":
                        if (AiTranslatePanel != null)
                        {
                            AiTranslatePanel.Visibility = Visibility.Visible;
                        }
                        break;
                    case "About":
                        AboutPanel.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证输入
                if (!int.TryParse(MaxItemsTextBox.Text, out int maxItems) || maxItems < 1 || maxItems > 100)
                {
                    MessageBox.Show("最大保存条数必须是1-100之间的数字", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 验证鼠标操作设置
                if (SingleClickPasteCheckBox.IsChecked != true && DoubleClickPasteCheckBox.IsChecked != true)
                {
                    MessageBox.Show("请至少选择一种鼠标操作方式", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 验证历史保存设置
                if (SaveHistoryCheckBox.IsChecked == true)
                {
                    string historyPath = HistoryPathTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(historyPath))
                    {
                        MessageBox.Show("请选择历史保存路径", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    try
                    {
                        // 尝试创建目录以验证路径有效性
                        if (!Directory.Exists(historyPath))
                        {
                            Directory.CreateDirectory(historyPath);
                        }
                    }
                    catch
                    {
                        MessageBox.Show("无法创建历史保存目录，请检查路径是否有效", "路径错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // 从UI控件更新到属性
                MaxItems = maxItems;
                SingleClickPaste = SingleClickPasteCheckBox.IsChecked ?? false;
                DoubleClickPaste = DoubleClickPasteCheckBox.IsChecked ?? true;
                SaveHistoryEnabled = SaveHistoryCheckBox.IsChecked ?? false;
                HistoryFolderPath = HistoryPathTextBox.Text.Trim();
                HotkeyModifiers = _currentModifiers;
                HotkeyKey = _currentKey;
                StartupEnabled = StartupEnabledCheckBox.IsChecked ?? false;
                TextSwapEnabled = TextSwapEnabledCheckBox.IsChecked ?? false;
                TextSwapHotkeyModifiers = _currentTextSwapModifiers;
                TextSwapHotkeyKey = _currentTextSwapKey;

                // 保存AI翻译设置
                SaveAiTranslateSettings();

                // 保存的地得变换设置
                SaveDeDeDeSettings();

                // 保存窗口尺寸
                SaveWindowSize();
                
                // 保存设置并关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveHotkey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证快捷键设置
                if (_currentModifiers == 0 || _currentKey == 0)
                {
                    MessageBox.Show("请设置一个有效的快捷键", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 保存快捷键设置
                HotkeyModifiers = _currentModifiers;
                HotkeyKey = _currentKey;

                // 保存其他设置
                if (int.TryParse(MaxItemsTextBox.Text, out int maxItems) && maxItems > 0 && maxItems <= 100)
                {
                    MaxItems = maxItems;
                }

                SingleClickPaste = SingleClickPasteCheckBox.IsChecked ?? false;
                DoubleClickPaste = DoubleClickPasteCheckBox.IsChecked ?? true;
                SaveHistoryEnabled = SaveHistoryCheckBox.IsChecked ?? false;
                HistoryFolderPath = HistoryPathTextBox.Text.Trim();
                
                // 保存智能文字交换设置
                TextSwapEnabled = TextSwapEnabledCheckBox.IsChecked ?? false;
                TextSwapHotkeyModifiers = _currentTextSwapModifiers;
                TextSwapHotkeyKey = _currentTextSwapKey;



                // 保存窗口尺寸
                SaveWindowSize();
                
                // 关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = "选择历史保存文件夹",
                    SelectedPath = HistoryPathTextBox.Text,
                    ShowNewFolderButton = true
                };

                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    HistoryPathTextBox.Text = dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择文件夹时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 快捷键相关事件处理方法
        private void HotkeyInputButton_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey = true;
            HotkeyInputButton.Content = "请按下快捷键...";
            HotkeyInputButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 220)); // 浅黄色
            HotkeyInputTextBox.Focus();
        }

        private void HotkeyInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingHotkey) return;

            e.Handled = true;

            // 获取修饰键
            uint modifiers = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers |= 0x0002; // MOD_CONTROL
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers |= 0x0001; // MOD_ALT
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers |= 0x0004; // MOD_SHIFT
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= 0x0008; // MOD_WIN

            // 获取主键（排除修饰键）
            Key key = e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return; // 忽略单独的修饰键
            }

            // 转换为虚拟键码
            uint vkCode = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 验证快捷键有效性
            if (modifiers == 0)
            {
                MessageBox.Show("请至少按下一个修饰键（Ctrl、Alt、Shift或Win）", "无效快捷键",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetHotkeyCapture();
                return;
            }

            // 保存快捷键
            _currentModifiers = modifiers;
            _currentKey = vkCode;

            UpdateHotkeyDisplay();
            ResetHotkeyCapture();
        }

        private void HotkeyInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingHotkey)
            {
                HotkeyInputButton.Content = "请按下快捷键...";
            }
        }

        private void HotkeyInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingHotkey)
            {
                ResetHotkeyCapture();
            }
        }

        private void ClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentModifiers = 0;
            _currentKey = 0;
            UpdateHotkeyDisplay();
        }

        private void ResetHotkeyCapture()
        {
            _isCapturingHotkey = false;
            HotkeyInputButton.Content = "点击此处设置快捷键";
            HotkeyInputButton.Background = System.Windows.Media.Brushes.Transparent;
        }

        private void CancelHotkey_Click(object sender, RoutedEventArgs e)
        {
            // 保存窗口尺寸
            SaveWindowSize();
            
            DialogResult = false;
            Close();
        }
        
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 保存窗口尺寸
                SaveWindowSize();
            }
            catch
            {
                // 忽略关闭时的错误
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        // 窗口控制按钮事件处理方法
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // GitHub链接点击事件处理
        private void GitHubHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                // 在默认浏览器中打开GitHub链接
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (System.Exception ex)
            {
                // 如果打开失败，显示错误信息
                System.Windows.MessageBox.Show($"无法打开链接: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // 新增重置快捷键功能
        private void ResetDefaultHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentModifiers = DEFAULT_HOTKEY_MODIFIERS;
            _currentKey = DEFAULT_HOTKEY_KEY;
            UpdateHotkeyDisplay();
        }

        // 智能文字交换相关事件处理方法
        private void TextSwapHotkeyInputButton_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingTextSwapHotkey = true;
            TextSwapHotkeyInputButton.Content = "请按下快捷键...";
            TextSwapHotkeyInputButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 220)); // 浅黄色
            TextSwapHotkeyInputTextBox.Focus();
        }

        private void TextSwapHotkeyInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingTextSwapHotkey) return;

            e.Handled = true;

            // 获取修饰键
            uint modifiers = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers |= 0x0002; // MOD_CONTROL
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers |= 0x0001; // MOD_ALT
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers |= 0x0004; // MOD_SHIFT
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= 0x0008; // MOD_WIN

            // 获取主键（排除修饰键）
            Key key = e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return; // 忽略单独的修饰键
            }

            // 转换为虚拟键码
            uint vkCode = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 验证快捷键有效性
            if (modifiers == 0)
            {
                MessageBox.Show("请至少按下一个修饰键（Ctrl、Alt、Shift或Win）", "无效快捷键",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetTextSwapHotkeyCapture();
                return;
            }

            // 保存快捷键
            _currentTextSwapModifiers = modifiers;
            _currentTextSwapKey = vkCode;

            UpdateTextSwapHotkeyDisplay();
            ResetTextSwapHotkeyCapture();
        }

        private void TextSwapHotkeyInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingTextSwapHotkey)
            {
                TextSwapHotkeyInputButton.Content = "请按下快捷键...";
            }
        }

        private void TextSwapHotkeyInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingTextSwapHotkey)
            {
                ResetTextSwapHotkeyCapture();
            }
        }

        private void ClearTextSwapHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentTextSwapModifiers = 0;
            _currentTextSwapKey = 0;
            UpdateTextSwapHotkeyDisplay();
        }

        private void ResetTextSwapHotkeyCapture()
        {
            _isCapturingTextSwapHotkey = false;
            TextSwapHotkeyInputButton.Content = "点击此处设置快捷键";
            TextSwapHotkeyInputButton.Background = System.Windows.Media.Brushes.Transparent;
        }

        private void SaveTextSwap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存智能文字交换设置
                TextSwapEnabled = TextSwapEnabledCheckBox.IsChecked ?? false;
                TextSwapHotkeyModifiers = _currentTextSwapModifiers;
                TextSwapHotkeyKey = _currentTextSwapKey;

                // 保存其他设置
                if (int.TryParse(MaxItemsTextBox.Text, out int maxItems) && maxItems > 0 && maxItems <= 100)
                {
                    MaxItems = maxItems;
                }

                SingleClickPaste = SingleClickPasteCheckBox.IsChecked ?? false;
                DoubleClickPaste = DoubleClickPasteCheckBox.IsChecked ?? true;
                SaveHistoryEnabled = SaveHistoryCheckBox.IsChecked ?? false;
                HistoryFolderPath = HistoryPathTextBox.Text.Trim();
                HotkeyModifiers = _currentModifiers;
                HotkeyKey = _currentKey;



                // 保存窗口尺寸
                SaveWindowSize();

                // 关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelTextSwap_Click(object sender, RoutedEventArgs e)
        {
            // 保存窗口尺寸
            SaveWindowSize();

            DialogResult = false;
            Close();
        }

        // 新增重置智能文字交换快捷键功能
        private void ResetDefaultTextSwapHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentTextSwapModifiers = DEFAULT_TEXT_SWAP_MODIFIERS;
            _currentTextSwapKey = DEFAULT_TEXT_SWAP_KEY;
            UpdateTextSwapHotkeyDisplay();
        }

        private string GetSettingsWindowFilePath()
        {
            // 获取程序根目录下的设置文件
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDirectory, "settingswindow_settings.ini");
        }

        private void SaveAiTranslateSettings()
        {
            try
            {
                // 保存AI翻译提供商设置
                if (AiProviderComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    AiProvider = selectedItem.Tag?.ToString() ?? "ollama";
                }

                // 保存API设置
                if (AiApiUrlTextBox != null)
                {
                    AiApiUrl = AiApiUrlTextBox.Text.Trim();
                }

                if (AiApiKeyPasswordBox != null)
                {
                    AiApiKey = AiApiKeyPasswordBox.Password;
                }

                // 保存模型名称 - 修复模型保存问题
                if (AiModelComboBox != null)
                {
                    // 优先使用选中项的值
                    if (AiModelComboBox.SelectedItem != null)
                    {
                        string selectedModel = AiModelComboBox.SelectedItem.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(selectedModel))
                        {
                            AiModel = selectedModel;
    
                        }
                    }
                    // 如果没有选中项但ComboBox有文本，使用文本值
                    else if (!string.IsNullOrWhiteSpace(AiModelComboBox.Text))
                    {
                        AiModel = AiModelComboBox.Text.Trim();

                    }
                    // 如果都没有，保持原有值不变
                    else
                    {

                    }
                }

                // 保存提示词
                if (AiPromptTextBox != null)
                {
                    AiPrompt = AiPromptTextBox.Text;
                }

                // 保存AI翻译快捷键设置
                AiTranslateHotkeyModifiers = _currentAiTranslateModifiers;
                AiTranslateHotkeyKey = _currentAiTranslateKey;

            }
            catch
            {
                throw;
            }
        }

        // AI提供商切换事件处理
        private void AiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 只有在用户主动切换时才强制更新API地址
            UpdateProviderSettings(true);
        }

        // 根据选择的提供商更新相关设置
        private void UpdateProviderSettings(bool forceUpdateApiUrl)
        {
            if (AiProviderComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                string provider = selectedItem.Tag?.ToString() ?? "ollama";

                // 更新API地址 - 在用户主动切换时总是更新为默认地址
                if (AiApiUrlTextBox != null)
                {
                    if (forceUpdateApiUrl)
                    {
                        // 用户主动切换提供商时，总是更新为对应的默认API地址
                        switch (provider)
                        {
                            case "ollama":
                                AiApiUrlTextBox.Text = "http://localhost:11434/v1/chat/completions";
                                break;
                            case "deepseek":
                                AiApiUrlTextBox.Text = "https://api.deepseek.com/v1/chat/completions";
                                break;
                            case "custom":
                                AiApiUrlTextBox.Text = "https://api.openai.com/v1/chat/completions";
                                break;
                        }
                    }
                    else
                    {
                        // 初始化时，只在API地址为空时才设置默认值
                        string currentUrl = AiApiUrlTextBox.Text?.Trim() ?? "";
                        if (string.IsNullOrEmpty(currentUrl))
                        {
                            switch (provider)
                            {
                                case "ollama":
                                    AiApiUrlTextBox.Text = "http://localhost:11434/v1/chat/completions";
                                    break;
                                case "deepseek":
                                    AiApiUrlTextBox.Text = "https://api.deepseek.com/v1/chat/completions";
                                    break;
                                case "custom":
                                    AiApiUrlTextBox.Text = "https://api.openai.com/v1/chat/completions";
                                    break;
                            }
                        }
                    }
                }

                // 控制API密钥输入框的可见性
                if (ApiKeyPanel != null)
                {
                    ApiKeyPanel.Visibility = provider == "ollama" ? Visibility.Collapsed : Visibility.Visible;
                }

                // 在用户主动切换提供商时，根据提供商类型处理API密钥
                if (forceUpdateApiUrl && AiApiKeyPasswordBox != null)
                {
                    if (provider == "ollama")
                    {
                        // ollama不需要API密钥，清空
                        AiApiKeyPasswordBox.Password = "";
                    }
                    else
                    {
                        // 其他提供商需要API密钥，如果当前为空则保持空状态让用户填写
                        // 不自动清空已有的API密钥，除非是从ollama切换过来的
                    }
                }

                // 更新模型列表
                UpdateModelList(provider);
            }
        }

        // 检查是否为默认API地址
        private bool IsDefaultApiUrl(string url)
        {
            var defaultUrls = new[]
            {
                "http://localhost:11434",
                "http://localhost:11434/v1/chat/completions",
                "https://api.deepseek.com/v1/chat/completions",
                "https://api.openai.com/v1/chat/completions"
            };
            return defaultUrls.Contains(url);
        }

        // 根据提供商更新模型列表
        private void UpdateModelList(string provider)
        {
            if (AiModelComboBox == null) return;

            // 保存当前选中的模型
            string currentSelectedModel = AiModelComboBox.SelectedItem?.ToString() ?? AiModel;
            
            // 清空当前列表
            AiModelComboBox.Items.Clear();

            // 检查是否有用户刷新的模型列表
            string cacheFilePath = GetModelCacheFilePath(provider);
            var userModels = new List<string>();

            if (File.Exists(cacheFilePath))
            {
                try
                {
                    userModels = File.ReadAllLines(cacheFilePath).ToList();
                    
                    // 如果有用户缓存的模型列表，优先使用它
                    if (userModels.Count > 0)
                    {
                        foreach (string model in userModels)
                        {
                            if (!string.IsNullOrWhiteSpace(model))
                            {
                                AiModelComboBox.Items.Add(model);
                            }
                        }
                        
                        // 尝试选择之前选中的模型
                        SelectModelInComboBox(currentSelectedModel);
                        return; // 使用用户刷新的列表后直接返回，不再加载默认列表
                    }
                }
                catch
                {
                    // 出错时继续加载默认列表
                }
            }

            // 如果没有用户缓存或缓存为空，使用默认列表
            switch (provider)
            {
                case "ollama":
                    AiModelComboBox.Items.Add("llama2");
                    AiModelComboBox.Items.Add("llama2:13b");
                    AiModelComboBox.Items.Add("codellama");
                    AiModelComboBox.Items.Add("mistral");
                    AiModelComboBox.Items.Add("qwen:7b");
                    AiModelComboBox.Items.Add("qwen:14b");
                    break;
                case "deepseek":
                    AiModelComboBox.Items.Add("deepseek-chat");
                    AiModelComboBox.Items.Add("deepseek-coder");
                    break;
                default: // 自定义OpenAI
                    AiModelComboBox.Items.Add("gpt-3.5-turbo");
                    AiModelComboBox.Items.Add("gpt-4");
                    AiModelComboBox.Items.Add("gpt-4-turbo");
                    AiModelComboBox.Items.Add("claude-3-sonnet");
                    AiModelComboBox.Items.Add("claude-3-opus");
                    break;
            }

            // 尝试选择之前选中的模型
            SelectModelInComboBox(currentSelectedModel);
        }

        // 选择指定模型到ComboBox
        private void SelectModelInComboBox(string modelName)
        {
            if (string.IsNullOrEmpty(modelName) || AiModelComboBox.Items.Count == 0)
            {
                // 如果没有指定模型或列表为空，选择第一项
                if (AiModelComboBox.Items.Count > 0)
                {
                    AiModelComboBox.SelectedIndex = 0;
                }
                return;
            }

            // 在列表中查找指定的模型
            for (int i = 0; i < AiModelComboBox.Items.Count; i++)
            {
                var item = AiModelComboBox.Items[i];
                if (item != null && item.ToString() == modelName)
                {
                    AiModelComboBox.SelectedIndex = i;
                    return;
                }
            }

            // 如果找不到指定的模型，选择第一项
            if (AiModelComboBox.Items.Count > 0)
            {
                AiModelComboBox.SelectedIndex = 0;
            }
        }

        // 获取模型缓存文件路径
        private string GetModelCacheFilePath(string provider)
        {
            string appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimpleTest");
                
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            return System.IO.Path.Combine(appDataPath, $"{provider}_models.cache");
        }

        // AI翻译快捷键相关事件处理方法
        private void AiTranslateHotkeyInputButton_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingAiTranslateHotkey = true;
            AiTranslateHotkeyInputButton.Content = "请按下快捷键...";
            AiTranslateHotkeyInputButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 220)); // 浅黄色
            AiTranslateHotkeyInputTextBox.Focus();
        }

        private void AiTranslateHotkeyInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingAiTranslateHotkey) return;

            e.Handled = true;

            // 获取修饰键
            uint modifiers = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers |= 0x0002; // MOD_CONTROL
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers |= 0x0001; // MOD_ALT
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers |= 0x0004; // MOD_SHIFT
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= 0x0008; // MOD_WIN

            // 获取主键（排除修饰键）
            Key key = e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return; // 忽略单独的修饰键
            }

            // 转换为虚拟键码
            uint vkCode = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 验证快捷键有效性 - 现在支持更多修饰符
            if (modifiers == 0)
            {
                MessageBox.Show("请至少按下一个修饰键（Ctrl、Alt、Shift或Win）", "无效快捷键",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetAiTranslateHotkeyCapture();
                return;
            }

            // 保存快捷键
            _currentAiTranslateModifiers = modifiers;
            _currentAiTranslateKey = vkCode;

            UpdateAiTranslateHotkeyDisplay();
            ResetAiTranslateHotkeyCapture();
        }

        private void AiTranslateHotkeyInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingAiTranslateHotkey)
            {
                AiTranslateHotkeyInputButton.Content = "请按下快捷键...";
            }
        }

        private void AiTranslateHotkeyInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingAiTranslateHotkey)
            {
                ResetAiTranslateHotkeyCapture();
            }
        }

        private void ResetDefaultAiTranslateHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentAiTranslateModifiers = 0x0002; // MOD_CONTROL
            _currentAiTranslateKey = 0x54; // T
            UpdateAiTranslateHotkeyDisplay();
        }

        private void ClearAiTranslateHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentAiTranslateModifiers = 0;
            _currentAiTranslateKey = 0;
            UpdateAiTranslateHotkeyDisplay();
        }

        private void ResetAiTranslateHotkeyCapture()
        {
            _isCapturingAiTranslateHotkey = false;
            AiTranslateHotkeyInputButton.Content = "点击此处设置快捷键";
            AiTranslateHotkeyInputButton.Background = System.Windows.Media.Brushes.Transparent;
        }

        private void SaveAiTranslate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存AI翻译设置
                SaveAiTranslateSettings();

                // 保存其他设置
                if (int.TryParse(MaxItemsTextBox.Text, out int maxItems) && maxItems > 0 && maxItems <= 100)
                {
                    MaxItems = maxItems;
                }

                SingleClickPaste = SingleClickPasteCheckBox.IsChecked ?? false;
                DoubleClickPaste = DoubleClickPasteCheckBox.IsChecked ?? true;
                SaveHistoryEnabled = SaveHistoryCheckBox.IsChecked ?? false;
                HistoryFolderPath = HistoryPathTextBox.Text.Trim();
                HotkeyModifiers = _currentModifiers;
                HotkeyKey = _currentKey;
                StartupEnabled = StartupEnabledCheckBox.IsChecked ?? false;
                TextSwapEnabled = TextSwapEnabledCheckBox.IsChecked ?? false;
                TextSwapHotkeyModifiers = _currentTextSwapModifiers;
                TextSwapHotkeyKey = _currentTextSwapKey;

                // 确保AI翻译快捷键也被保存到属性中
                AiTranslateHotkeyModifiers = _currentAiTranslateModifiers;
                AiTranslateHotkeyKey = _currentAiTranslateKey;



                // 保存窗口尺寸
                SaveWindowSize();

                // 关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelAiTranslate_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // 刷新模型列表按钮点击事件
        private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshModelsFromApi();
        }

        // 从API刷新模型列表
        private async Task RefreshModelsFromApi()
        {
            if (AiApiUrlTextBox == null || AiModelComboBox == null || RefreshModelsButton == null)
                return;

            string apiUrl = AiApiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiUrl))
            {
                MessageBox.Show("请先填入API地址", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string provider = GetCurrentProvider();

            // 检查是否需要API密钥
            if (provider != "ollama")
            {
                string apiKey = AiApiKeyPasswordBox?.Password?.Trim() ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("请先填入API密钥", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            // 禁用刷新按钮，显示加载状态
            RefreshModelsButton.IsEnabled = false;
            RefreshModelsButton.Content = "⏳";

            try
            {
                var models = await FetchModelsFromApi(apiUrl, provider);

                if (models != null && models.Count > 0)
                {
                    // 保存当前选中的模型
                    string currentModel = AiModelComboBox.SelectedItem?.ToString() ?? "";

                    // 清空并添加新的模型列表
                    AiModelComboBox.Items.Clear();
                    foreach (string model in models)
                    {
                        AiModelComboBox.Items.Add(model);
                    }

                    // 尝试恢复之前选中的模型
                    SelectModelInComboBox(currentModel);

                    // 将模型列表保存到缓存文件中
                    try
                    {
                        string cacheFilePath = GetModelCacheFilePath(provider);
                        File.WriteAllLines(cacheFilePath, models);
                    }
                    catch
                    {
                        // 忽略保存缓存失败
                    }

                    MessageBox.Show($"成功获取到 {models.Count} 个模型", "刷新成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("未获取到任何模型，请检查API地址和网络连接", "刷新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新模型列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

            }
            finally
            {
                // 恢复刷新按钮状态
                RefreshModelsButton.IsEnabled = true;
                RefreshModelsButton.Content = "🔄";
            }
        }

        // 获取当前选择的提供商
        private string GetCurrentProvider()
        {
            if (AiProviderComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                return selectedItem.Tag?.ToString() ?? "ollama";
            }
            return "ollama";
        }

        // 从API获取模型列表
        private async Task<List<string>> FetchModelsFromApi(string apiUrl, string provider)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                // 设置请求头 - 使用当前输入框中的API密钥
                if (provider != "ollama")
                {
                    string currentApiKey = AiApiKeyPasswordBox?.Password?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(currentApiKey))
                    {
                        httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", currentApiKey);
                    }
                }

                try
                {
                    string modelsEndpoint = GetModelsEndpoint(apiUrl, provider);

                    var response = await httpClient.GetAsync(modelsEndpoint);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        return ParseModelsFromJson(jsonContent, provider);
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"API请求失败: {response.StatusCode} - {response.ReasonPhrase}");
                    }
                }
                catch (TaskCanceledException)
                {
                    throw new Exception("请求超时，请检查网络连接和API地址");
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception($"网络请求失败: {ex.Message}");
                }
            }
        }

        // 获取模型列表的API端点
        private string GetModelsEndpoint(string apiUrl, string provider)
        {
            // 移除chat/completions后缀，添加models端点
            string baseUrl = apiUrl;
            if (baseUrl.EndsWith("/v1/chat/completions"))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - "/chat/completions".Length);
            }
            else if (baseUrl.EndsWith("/chat/completions"))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - "/chat/completions".Length);
            }

            return provider switch
            {
                "ollama" => baseUrl.Replace("/v1", "") + "/api/tags", // Ollama使用不同的端点
                _ => baseUrl + "/models" // OpenAI兼容接口
            };
        }

        // 解析JSON响应中的模型列表
        private List<string> ParseModelsFromJson(string jsonContent, string provider)
        {
            var models = new List<string>();

            try
            {
                using (JsonDocument document = JsonDocument.Parse(jsonContent))
                {
                    if (provider == "ollama")
                    {
                        // Ollama格式: {"models": [{"name": "model_name"}, ...]}
                        if (document.RootElement.TryGetProperty("models", out JsonElement modelsArray))
                        {
                            foreach (JsonElement model in modelsArray.EnumerateArray())
                            {
                                if (model.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    models.Add(nameElement.GetString() ?? "");
                                }
                            }
                        }
                    }
                    else
                    {
                        // OpenAI格式: {"data": [{"id": "model_id"}, ...]}
                        if (document.RootElement.TryGetProperty("data", out JsonElement dataArray))
                        {
                            foreach (JsonElement model in dataArray.EnumerateArray())
                            {
                                if (model.TryGetProperty("id", out JsonElement idElement))
                                {
                                    models.Add(idElement.GetString() ?? "");
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new Exception($"解析API响应失败: {ex.Message}");
            }

            return models.Where(m => !string.IsNullOrEmpty(m)).ToList();
        }

        // 的地得变换功能相关方法
        private void SaveDeDeDeSettings()
        {
            DeDeDeEnabled = DeDeDeEnabledCheckBox?.IsChecked ?? true;
            DeDeDeHotkeyModifiers = _currentDeDeDeModifiers;
            DeDeDeHotkeyKey = _currentDeDeDeKey;
        }

        // 的地得变换快捷键事件处理
        private void DeDeDeHotkeyInputButton_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingDeDeDeHotkey = true;
            DeDeDeHotkeyInputButton.Content = "请按下快捷键...";
            DeDeDeHotkeyInputButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 220)); // 浅黄色
            DeDeDeHotkeyInputTextBox.Focus();
        }

        private void DeDeDeHotkeyInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingDeDeDeHotkey) return;

            e.Handled = true;

            // 获取修饰键
            uint modifiers = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers |= 0x0002; // MOD_CONTROL
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers |= 0x0001; // MOD_ALT
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers |= 0x0004; // MOD_SHIFT
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= 0x0008; // MOD_WIN

            // 获取主键（排除修饰键）
            Key key = e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return; // 忽略单独的修饰键
            }

            // 转换为虚拟键码
            uint vkCode = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 验证快捷键有效性
            if (modifiers == 0)
            {
                MessageBox.Show("请至少按下一个修饰键（Ctrl、Alt、Shift或Win）", "无效快捷键",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetDeDeDeHotkeyCapture();
                return;
            }

            // 保存快捷键
            _currentDeDeDeModifiers = modifiers;
            _currentDeDeDeKey = vkCode;

            UpdateDeDeDeHotkeyDisplay();
            ResetDeDeDeHotkeyCapture();
        }

        private void DeDeDeHotkeyInputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingDeDeDeHotkey)
            {
                DeDeDeHotkeyInputButton.Content = "请按下快捷键...";
            }
        }

        private void DeDeDeHotkeyInputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isCapturingDeDeDeHotkey)
            {
                ResetDeDeDeHotkeyCapture();
            }
        }

        private void ClearDeDeDeHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentDeDeDeModifiers = 0;
            _currentDeDeDeKey = 0;
            UpdateDeDeDeHotkeyDisplay();
        }

        private void ResetDefaultDeDeDeHotkey_Click(object sender, RoutedEventArgs e)
        {
            _currentDeDeDeModifiers = DEFAULT_DEDEDE_MODIFIERS;
            _currentDeDeDeKey = DEFAULT_DEDEDE_KEY;
            UpdateDeDeDeHotkeyDisplay();
        }

        private void SaveDeDeDe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存的地得变换设置
                SaveDeDeDeSettings();

                // 保存其他设置
                if (int.TryParse(MaxItemsTextBox.Text, out int maxItems) && maxItems > 0 && maxItems <= 100)
                {
                    MaxItems = maxItems;
                }

                SingleClickPaste = SingleClickPasteCheckBox.IsChecked ?? false;
                DoubleClickPaste = DoubleClickPasteCheckBox.IsChecked ?? true;
                SaveHistoryEnabled = SaveHistoryCheckBox.IsChecked ?? false;
                HistoryFolderPath = HistoryPathTextBox.Text.Trim();
                HotkeyModifiers = _currentModifiers;
                HotkeyKey = _currentKey;
                StartupEnabled = StartupEnabledCheckBox.IsChecked ?? false;
                TextSwapEnabled = TextSwapEnabledCheckBox.IsChecked ?? false;
                TextSwapHotkeyModifiers = _currentTextSwapModifiers;
                TextSwapHotkeyKey = _currentTextSwapKey;

                // 保存AI翻译设置
                SaveAiTranslateSettings();

                // 保存窗口尺寸
                SaveWindowSize();

                // 关闭窗口
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelDeDeDe_Click(object sender, RoutedEventArgs e)
        {
            LoadDeDeDeSettings(); // 重新加载设置，取消更改

            // 保存窗口尺寸
            SaveWindowSize();

            DialogResult = false;
            Close();
        }

        private void ResetDeDeDeHotkeyCapture()
        {
            _isCapturingDeDeDeHotkey = false;
            DeDeDeHotkeyInputButton.Content = "点击此处设置快捷键";
            DeDeDeHotkeyInputButton.Background = System.Windows.Media.Brushes.Transparent;
        }
    }
}
