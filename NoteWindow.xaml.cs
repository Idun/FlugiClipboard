using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;

namespace FlugiClipboard
{
    public partial class NoteWindow : Window
    {
        private string _noteFilePath;
        private bool _hasUnsavedChanges = false;
        private MainWindow _parentWindow;

        public NoteWindow(MainWindow parentWindow)
        {
            InitializeComponent();
            _parentWindow = parentWindow;

            // 设置记事文件路径
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlugiClipboard");
            Directory.CreateDirectory(appDataPath);
            _noteFilePath = Path.Combine(appDataPath, "notes.txt");

            // 默认置顶
            Topmost = true;

            InitializeWindow();
            LoadNoteContent();
        }

        private void InitializeWindow()
        {
            // 设置窗口事件
            this.Closing += NoteWindow_Closing;
            this.KeyDown += NoteWindow_KeyDown;
            
            // 设置文本框事件
            NoteTextBox.TextChanged += NoteTextBox_TextChanged;
            NoteTextBox.Focus();
            
            // 更新字符计数
            UpdateCharCount();
            
            // 设置状态
            StatusTextBlock.Text = "随时记录您的想法和重要信息 | Ctrl+S 保存 | Esc 关闭";
        }

        private void LoadNoteContent()
        {
            try
            {
                if (File.Exists(_noteFilePath))
                {
                    string content = File.ReadAllText(_noteFilePath);
                    NoteTextBox.Text = content;
                    _hasUnsavedChanges = false;
                    UpdateTitle();
                    
                    // 显示最后保存时间
                    var lastWriteTime = File.GetLastWriteTime(_noteFilePath);
                    LastSavedTextBlock.Text = $"最后保存: {lastWriteTime:MM-dd HH:mm}";
                }
                else
                {
                    NoteTextBox.Text = "";
                    LastSavedTextBlock.Text = "新建记事";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载记事内容失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void SaveNoteContent()
        {
            try
            {
                await File.WriteAllTextAsync(_noteFilePath, NoteTextBox.Text);
                _hasUnsavedChanges = false;
                UpdateTitle();
                
                // 更新最后保存时间
                LastSavedTextBlock.Text = $"最后保存: {DateTime.Now:MM-dd HH:mm}";
                
                // 显示保存成功提示
                StatusTextBlock.Text = "保存成功！";
                await Task.Delay(2000);
                StatusTextBlock.Text = "随时记录您的想法和重要信息 | Ctrl+S 保存 | Esc 关闭";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存记事内容失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCharCount()
        {
            int charCount = NoteTextBox.Text.Length;
            CharCountTextBlock.Text = $"字符数: {charCount}";
        }

        private void UpdateTitle()
        {
            string title = "随手记事 - FlugiClipboard";
            if (_hasUnsavedChanges)
            {
                title += " *";
            }
            this.Title = title;
        }

        // 事件处理方法
        private void NoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _hasUnsavedChanges = true;
            UpdateTitle();
            UpdateCharCount();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveNoteContent();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // 切换置顶状态
            Topmost = !Topmost;

            // 更新按钮显示
            if (PinButton != null)
            {
                PinButton.Content = Topmost ? "📌" : "📍";
                PinButton.ToolTip = Topmost ? "取消置顶" : "置顶";
            }
        }

        // 标题栏事件处理
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void NoteWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+S 保存
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveNoteContent();
                e.Handled = true;
            }
            // Esc 关闭
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void NoteWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 如果有未保存的更改，询问用户
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "您有未保存的更改，是否要保存？",
                    "确认关闭",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveNoteContent();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // 不恢复主窗口显示，让它保持在后台静默状态
            // 这符合用户的关闭行为逻辑：关闭记事面板就是完全关闭，不应该弹出其他窗口
        }
    }
}
