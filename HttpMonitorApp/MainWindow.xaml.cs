using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HttpMonitor.Services;
using HttpMonitor.Models;
using System.Windows.Shell;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace HttpMonitor
{
    public partial class MainWindow : Window
    {
        private HttpServerService? _serverService;
        private HttpClientService? _clientService;
        private DispatcherTimer? _updateTimer;
        private bool _isServerRunning = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
            SetupUpdateTimer();
            StateChanged += MainWindow_StateChanged;
            UpdateChart();
            UpdateStatsTable();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeIcon();
        }

        private void UpdateMaximizeIcon()
        {
            if (MaximizeIcon != null)
            {
                object icon;
                if (WindowState == WindowState.Maximized)
                {
                    icon = FindResource("RestoreIcon");
                }
                else
                {
                    icon = FindResource("MaximizeIcon");
                }
                MaximizeIcon.Content = icon;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void InitializeServices()
        {
            _serverService = new HttpServerService();
            _clientService = new HttpClientService();

            _serverService.RequestProcessed += (log) =>
            {
                Dispatcher.Invoke(() => AddLogEntry(log));
            };

            _serverService.StatisticsUpdated += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatistics();
                    UpdateChart();
                    UpdateStatsTable();
                });
            };
        }

        private void SetupUpdateTimer()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromSeconds(1);
            _updateTimer.Tick += (s, e) => UpdateUptime();
            _updateTimer.Start();
        }

        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isServerRunning && _serverService != null)
            {
                if (int.TryParse(PortTextBox.Text, out int port))
                {
                    try
                    {
                        await _serverService.StartAsync(port);
                        _isServerRunning = true;
                        StartServerButton.Content = "Остановить";
                        AddLogEntry($"Сервер запущен на порту {port}");
                        UpdateChart();
                        UpdateStatsTable();

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка запуска сервера: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Введите корректный номер порта", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (_serverService != null)
            {
                _serverService.Stop();
                _isServerRunning = false;
                StartServerButton.Content = "Запустить";
                AddLogEntry("Сервер остановлен");
                
                UpdateChart();
            }
        }

        private async void SendRequestButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text;
            string method = ((System.Windows.Controls.ComboBoxItem)MethodComboBox.SelectedItem)?.Content.ToString() ?? "GET";

            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Введите URL", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResponseTextBox.Text = "Отправка запроса...";
            StatusBarText.Text = $"Отправка {method} запроса на {url}";

            try
            {
                string response = method switch
                {
                    "GET" when _clientService != null => await _clientService.SendGetAsync(url),
                    "POST" when _clientService != null => await _clientService.SendPostAsync(url, RequestBodyTextBox.Text),
                    "PUT" when _clientService != null => await _clientService.SendPutAsync(url, RequestBodyTextBox.Text),
                    "DELETE" when _clientService != null => await _clientService.SendDeleteAsync(url),
                    _ => "Клиент не инициализирован или метод не поддерживается"
                };

                ResponseTextBox.Text = response;
                StatusBarText.Text = "Запрос выполнен успешно";
            }
            catch (Exception ex)
            {
                ResponseTextBox.Text = $"Ошибка: {ex.Message}";
                StatusBarText.Text = "Ошибка выполнения запроса";
            }
        }

        private void AddLogEntry(HttpRequestLog log)
        {
            string logEntry = $"[{log.Timestamp:HH:mm:ss}] {log.Method} {log.Url} - {log.ResponseStatus} ({log.ProcessingTimeMs}ms)";
            LogListBox?.Items.Insert(0, logEntry);
        }

        private void AddLogEntry(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogListBox?.Items.Insert(0, logEntry);
        }

        private void UpdateStatistics()
        {
            if (_serverService != null)
            {
                var stats = _serverService.GetStatistics();
                TotalRequestsText.Text = stats.TotalRequests.ToString();
                GetRequestsText.Text = stats.GetRequests.ToString();
                PostRequestsText.Text = stats.PostRequests.ToString();
                AvgTimeText.Text = $"{stats.AverageProcessingTime:F0} мс";
            }
        }

        private void UpdateChart()
        {
            if (_serverService != null && _isServerRunning)
            {
                var loadPoints = _serverService.GetLoadPoints().ToList();

                if (!loadPoints.Any())
                {
                    var demoData = new List<object>();
                    var now = DateTime.Now;
                    for (int i = 0; i < 12; i++)
                    {
                        demoData.Add(new
                        {
                            Time = now.AddMinutes(-(11 - i)).ToString("HH:mm"),
                            RequestCount = 0,
                            Height = 2.0
                        });
                    }
                    LoadChartItems.ItemsSource = demoData;
                    return;
                }

                var recentPoints = loadPoints.TakeLast(12).ToList();

                var maxLoad = recentPoints.Max(x => x.RequestCount);
                if (maxLoad == 0) maxLoad = 1;

                var chartData = recentPoints.Select(x => new
                {
                    Time = x.Time.ToString("HH:mm"),
                    RequestCount = x.RequestCount,
                    Height = Math.Max((x.RequestCount * 120.0) / maxLoad, 2.0)
                }).ToList();

                LoadChartItems.ItemsSource = chartData;
            }
            else
            {
                var demoData = new List<object>();
                var now = DateTime.Now;
                for (int i = 0; i < 12; i++)
                {
                    demoData.Add(new
                    {
                        Time = now.AddMinutes(-(11 - i)).ToString("HH:mm"),
                        RequestCount = 0,
                        Height = 2.0
                    });
                }
                LoadChartItems.ItemsSource = demoData;
            }
        }

        private void UpdateStatsTable()
        {
            if (_serverService != null)
            {
                var stats = _serverService.GetStatistics();
                var total = stats.TotalRequests;

                var tableData = new ObservableCollection<StatRow>
                {
                    new StatRow { Method = "GET", Count = stats.GetRequests, Percentage = total > 0 ? (double)stats.GetRequests / total * 100 : 0 },
                    new StatRow { Method = "POST", Count = stats.PostRequests, Percentage = total > 0 ? (double)stats.PostRequests / total * 100 : 0 },
                    new StatRow { Method = "PUT", Count = stats.PutRequests, Percentage = total > 0 ? (double)stats.PutRequests / total * 100 : 0 },
                    new StatRow { Method = "DELETE", Count = stats.DeleteRequests, Percentage = total > 0 ? (double)stats.DeleteRequests / total * 100 : 0 }
                };

                StatsDataGrid.ItemsSource = null;
                StatsDataGrid.ItemsSource = tableData;
            }
        }

        private void UpdateUptime()
        {
            if (_isServerRunning && _serverService != null)
            {
                var stats = _serverService.GetStatistics();
                UptimeText.Text = stats.Uptime.ToString(@"hh\:mm\:ss");
            }
        }

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            var filter = ((System.Windows.Controls.ComboBoxItem)FilterComboBox.SelectedItem)?.Content.ToString();
            var logs = _serverService?.GetLogs(filter == "Все" ? null : filter);

            LogListBox?.Items.Clear();
            if (logs != null)
            {
                foreach (var log in logs.Take(100))
                {
                    AddLogEntry(log);
                }
            }
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            LogListBox?.Items.Clear();
        }

        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"http_monitor_logs_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var logs = _serverService?.GetLogs() ?? new List<HttpRequestLog>();
                    var sb = new StringBuilder();

                    sb.AppendLine("Timestamp,Method,URL,ResponseStatus,ProcessingTimeMs");

                    foreach (var log in logs)
                    {
                        sb.AppendLine($"{log.Timestamp:yyyy-MM-dd HH:mm:ss},{log.Method},{log.Url},{log.ResponseStatus},{log.ProcessingTimeMs}");
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                    MessageBox.Show($"Логи успешно экспортированы в:\n{dialog.FileName}",
                        "Экспорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExampleStats_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "http://localhost:8080/stats";
            MethodComboBox.SelectedIndex = 0;
            RequestBodyTextBox.Text = "";
        }

        private void ExampleExternalApi_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "https://jsonplaceholder.typicode.com/posts/1";
            MethodComboBox.SelectedIndex = 0;
            RequestBodyTextBox.Text = "";
        }

        private void ExamplePost_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "http://localhost:8080/";
            MethodComboBox.SelectedIndex = 1;
            RequestBodyTextBox.Text = "{ \"message\": \"Тестовое сообщение\" }";
        }

        private void ExamplePut_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "https://jsonplaceholder.typicode.com/posts/1";
            MethodComboBox.SelectedIndex = 2;
            RequestBodyTextBox.Text = "{ \"id\": 1, \"title\": \"updated title\", \"body\": \"updated body\", \"userId\": 1 }";
        }

        private void ExampleDelete_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "https://jsonplaceholder.typicode.com/posts/1";
            MethodComboBox.SelectedIndex = 3;
            RequestBodyTextBox.Text = "";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _serverService?.Dispose();
            _clientService?.Dispose();
            base.OnClosing(e);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;

            UpdateMaximizeIcon();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class StatRow
    {
        public string Method { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string PercentageDisplay => $"{Percentage:F1}%";
    }
}