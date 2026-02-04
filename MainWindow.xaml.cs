using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json; // 需引用 System.Text.Json
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Timers;

namespace DuckDNSClient
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        // 使用靜態 HttpClient 以避免 Socket 耗盡
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private DNSData _duckDNSData;
        private const string IpInfoUrl = "https://api.ipify.org";
        private const string Ip6InfoUrl = "https://api64.ipify.org";
        private const string ConfigFileName = "settings.json";

        private string _currentIPv4 = "";
        private string _currentIPv6 = "";

        private Timer _updateTimer;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private ContextMenu _contextMenu;

        public MainWindow()
        {
            InitializeComponent();
            _duckDNSData = new DNSData();

            InitTimer();
            InitTrayIcon();
            LoadRecord();
            CheckAutoStart();
        }

        private void InitTimer()
        {
            _updateTimer = new Timer();
            _updateTimer.AutoReset = true;
            _updateTimer.Elapsed += OnTimedEvent_UpdateDomain;
        }

        private void StartTimer()
        {
            _updateTimer.Stop();
            // 嘗試解析間隔，預設 5 分鐘
            if (int.TryParse(_duckDNSData.UpdateInterval, out int interval))
            {
                _updateTimer.Interval = Math.Max(1, interval) * 60000; // 至少 1 分鐘
            }
            else
            {
                _updateTimer.Interval = 300000;
            }

            _updateTimer.Start();

            Button_UpdateStart.IsEnabled = false;
            Button_UpdateStop.IsEnabled = true;
        }

        private void StopTimer()
        {
            _updateTimer.Stop();
            Button_UpdateStart.IsEnabled = true;
            Button_UpdateStop.IsEnabled = false;
        }

        // Timer 事件是在 ThreadPool 執行，使用 async void 是被允許的 (Event Handler)
        private async void OnTimedEvent_UpdateDomain(object source, ElapsedEventArgs e)
        {
            await UpdateDNSInfoAsync();
        }

        private async void Button_UpdateStart_Click(object sender, RoutedEventArgs e)
        {
            _duckDNSData.Token = TextBox_APIKey.Text;
            _duckDNSData.DomainName = TextBox_ZoneID.Text;
            _duckDNSData.UpdateInterval = TextBox_Interval.Text;

            // 重置紀錄以強制更新
            _currentIPv4 = "";
            _currentIPv6 = "";

            SaveRecord();

            // 立即執行一次
            await UpdateDNSInfoAsync();

            StartTimer();
        }

        private void Button_UpdateStop_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        private async Task UpdateDNSInfoAsync()
        {
            try
            {
                string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                bool needsUpdate = false;
                string newIPv4 = "";
                string newIPv6 = "";

                // 1. 取得 IPv4
                try
                {
                    newIPv4 = await _httpClient.GetStringAsync(IpInfoUrl);
                    if (newIPv4 != _currentIPv4)
                    {
                        _currentIPv4 = newIPv4;
                        needsUpdate = true;
                        UpdateUI("IPv4", _currentIPv4, nowStr);
                    }
                    else
                    {
                        UpdateUI("IPv4", "IP Not Changed", nowStr);
                    }
                }
                catch
                {
                    UpdateUI("IPv4", "Detect Failed", nowStr);
                }

                // 2. 取得 IPv6
                try
                {
                    string rawIPv6 = await _httpClient.GetStringAsync(Ip6InfoUrl);
                    // 簡單驗證是否為 IPv6 格式 (包含冒號)
                    if (!string.IsNullOrWhiteSpace(rawIPv6) && rawIPv6.Contains(":"))
                    {
                        newIPv6 = rawIPv6;
                        if (newIPv6 != _currentIPv6)
                        {
                            _currentIPv6 = newIPv6;
                            needsUpdate = true;
                            UpdateUI("IPv6", _currentIPv6, nowStr);
                        }
                        else
                        {
                            UpdateUI("IPv6", "IP Not Changed", nowStr);
                        }
                    }
                    else
                    {
                        UpdateUI("IPv6", "Not Detected", nowStr);
                    }
                }
                catch
                {
                    UpdateUI("IPv6", "Detect Failed", nowStr);
                }

                // 3. 如果 IP 有變更，更新 DuckDNS
                if (needsUpdate)
                {
                    var url = $"https://www.duckdns.org/update?domains={_duckDNSData.DomainName}&token={_duckDNSData.Token}&ip={_currentIPv4}&ipv6={_currentIPv6}";

                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    string result = await response.Content.ReadAsStringAsync();

                    if (result == "OK")
                    {
                        UpdateUI("IPv4", $"Success ({_currentIPv4})", nowStr);
                        UpdateUI("IPv6", $"Success ({_currentIPv6})", nowStr);
                    }
                    else
                    {
                        UpdateUI("IPv4", $"DuckDNS Error: {result}", nowStr);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = ex.Message.Length > 40 ? ex.Message.Substring(0, 40) : ex.Message;
                UpdateUI("Error", errorMsg, DateTime.Now.ToString());
            }
        }

        private void UpdateUI(string type, string info, string time)
        {
            // 確保在 UI 執行緒上執行
            Dispatcher.Invoke(() =>
            {
                if (type == "IPv4")
                    Label_IPv4Status.Content = $"IPv4: {info} ({time})";
                else if (type == "IPv6")
                    Label_IPv6Status.Content = $"IPv6: {info} ({time})";
                else if (type == "Error")
                {
                    // 可以選擇顯示在哪個 Label
                    Label_IPv6Status.Content = $"Err: {info} ({time})";
                }
            });
        }

        #region Data Persistence (Json)

        private void LoadRecord()
        {
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    string jsonString = File.ReadAllText(ConfigFileName);
                    _duckDNSData = JsonSerializer.Deserialize<DNSData>(jsonString) ?? new DNSData();
                }
                catch
                {
                    _duckDNSData = new DNSData { UpdateInterval = "60" };
                }
            }
            else
            {
                _duckDNSData.UpdateInterval = "60";
            }

            TextBox_APIKey.Text = _duckDNSData.Token;
            TextBox_ZoneID.Text = _duckDNSData.DomainName;
            TextBox_Interval.Text = _duckDNSData.UpdateInterval;
        }

        private void SaveRecord()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_duckDNSData, options);
                File.WriteAllText(ConfigFileName, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}");
            }
        }

        private void CheckAutoStart()
        {
            if (!string.IsNullOrEmpty(_duckDNSData.Token) &&
                !string.IsNullOrEmpty(_duckDNSData.DomainName) &&
                !string.IsNullOrEmpty(_duckDNSData.UpdateInterval))
            {
                // 使用非同步呼叫，不等待結果以避免卡住建構函式
                _ = UpdateDNSInfoAsync();
                StartTimer();
            }
        }

        #endregion

        #region TrayIcon & System

        private void InitTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "DuckDNSClient",
                Icon = Properties.Resources.godaddy, // 確保 Resources 中有此圖示
                Visible = true
            };

            _notifyIcon.MouseDoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; };
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Right) _contextMenu.IsOpen = true;
            };

            _contextMenu = new ContextMenu();

            MenuItem itemShow = new MenuItem { Header = "Setting" };
            itemShow.Click += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; };

            MenuItem itemExit = new MenuItem { Header = "Exit" };
            itemExit.Click += Exit_Click;

            _contextMenu.Items.Add(itemShow);
            _contextMenu.Items.Add(itemExit);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) this.Hide();
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        #endregion
    }

    // 使用屬性 (Properties) 配合 JSON 序列化
    public class DNSData
    {
        public string Token { get; set; } = "";
        public string DomainName { get; set; } = "";
        public string UpdateInterval { get; set; } = "60";
        public bool EnableIPv6 { get; set; } = true;
    }
}

