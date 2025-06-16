using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QRCoder;
using System.Windows.Threading;

namespace FlugiClipboard
{
    public partial class QRCodeWindow : Window
    {
        private Bitmap? _currentQRCode;
        private DispatcherTimer? _textChangeTimer;

        public QRCodeWindow()
        {
            InitializeComponent();

            // 初始化文本变化定时器（防抖动）
            _textChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 500ms延迟
            };
            _textChangeTimer.Tick += (s, e) =>
            {
                _textChangeTimer.Stop();
                GenerateQRCode();
            };

            // 设置初始焦点到输入框
            Loaded += (s, e) => InputTextBox.Focus();
        }

        public QRCodeWindow(string initialText) : this()
        {
            InputTextBox.Text = initialText;

            // 如果有初始文本，自动生成二维码
            if (!string.IsNullOrWhiteSpace(initialText))
            {
                // 延迟一点时间确保UI完全加载
                Loaded += (s, e) =>
                {
                    Dispatcher.BeginInvoke(() => GenerateQRCode(), DispatcherPriority.Loaded);
                };
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
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

        private void InputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // 重启定时器，实现防抖动
            _textChangeTimer?.Stop();
            _textChangeTimer?.Start();
        }

        private void GenerateQRCode()
        {
            try
            {
                string text = InputTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(text))
                {
                    // 清空二维码显示
                    QRCodeImage.Source = null;
                    SaveButton.IsEnabled = false;
                    CopyImageButton.IsEnabled = false;
                    return;
                }

                // 生成二维码
                using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                {
                    QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
                    using (QRCode qrCode = new QRCode(qrCodeData))
                    {
                        // 生成高质量的二维码图片
                        _currentQRCode = qrCode.GetGraphic(20, Color.Black, Color.White, true);
                        
                        // 转换为WPF可用的BitmapImage
                        BitmapImage bitmapImage = ConvertBitmapToBitmapImage(_currentQRCode);
                        QRCodeImage.Source = bitmapImage;
                        
                        // 启用操作按钮
                        SaveButton.IsEnabled = true;
                        CopyImageButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成二维码失败: {ex.Message}");
            }
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                
                return bitmapImage;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQRCode == null)
            {
                return;
            }

            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PNG图片|*.png|JPEG图片|*.jpg|BMP图片|*.bmp",
                    DefaultExt = "png",
                    FileName = $"QRCode_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    ImageFormat format = ImageFormat.Png;
                    string extension = Path.GetExtension(saveFileDialog.FileName).ToLower();
                    
                    switch (extension)
                    {
                        case ".jpg":
                        case ".jpeg":
                            format = ImageFormat.Jpeg;
                            break;
                        case ".bmp":
                            format = ImageFormat.Bmp;
                            break;
                    }

                    _currentQRCode.Save(saveFileDialog.FileName, format);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存二维码失败: {ex.Message}");
            }
        }

        private void CopyImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQRCode == null)
            {
                return;
            }

            try
            {
                // 将Bitmap转换为剪贴板可用的格式
                BitmapImage bitmapImage = ConvertBitmapToBitmapImage(_currentQRCode);
                System.Windows.Clipboard.SetImage(bitmapImage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"复制二维码失败: {ex.Message}");
            }
        }


    }
}
