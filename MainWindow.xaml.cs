using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;

namespace DuckDNSClient
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        private DNSData duckDNSData;
        private string Now = "";
        private string ipInfoURL = "https://api.ipify.org";
        private string ip6InfoURL = "https://api64.ipify.org";
        private string recordIPv4 = "127.0.0.1";
        private string recordIPv6 = "[::1]";
        private string MODE_IPV4 = "4";
        private string MODE_IPV6 = "6";

        private string recordFileName = "tick.tmp";
        //private bool CloseAllowed;
        private Timer Timer_updateDNS;
        private System.Windows.Forms.NotifyIcon m_notifyIcon;
        private System.Windows.Controls.ContextMenu mContextMenu;
        public MainWindow()
        {
            InitializeComponent();
            duckDNSData = new DNSData();

            Timer_updateDNS = new System.Timers.Timer();
            Timer_updateDNS.Interval = 5000;
            Timer_updateDNS.AutoReset = true;
            Timer_updateDNS.Enabled = true;
            Timer_updateDNS.Stop();
            Timer_updateDNS.Elapsed += OnTimedEvent_UpdateDomain;
            StopTimer();
            InitTrayIcon();
            LoadRecord();
            CheckAutoStart();
        }


        private void StartTimer()
        {
            Timer_updateDNS.Stop();
            Timer_updateDNS.Interval = Int32.Parse(duckDNSData.updateInterval) * 60000;
            Timer_updateDNS.Start();

            Button_UpdateStart.IsEnabled = false;
            Button_UpdateStop.IsEnabled = true;
        }

        private void StopTimer()
        {
            Timer_updateDNS.Stop();
            Button_UpdateStart.IsEnabled = true;
            Button_UpdateStop.IsEnabled = false;
        }

        private void OnTimedEvent_UpdateDomain(object source, System.Timers.ElapsedEventArgs e)
        {
            UpdateDomain();
        }

        private void UpdateDomain()
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                Task.Run(() => UpdateDNSInfoAsync());
            }));
        }

        private void Button_UpdateStart_Click(object sender, RoutedEventArgs e)
        {
            if (TextBox_APIKey.Text.Length > 0)
            {
                duckDNSData.token = TextBox_APIKey.Text;
            }

            if (TextBox_ZoneID.Text.Length > 0)
            {
                duckDNSData.domainName = TextBox_ZoneID.Text;
            }
            if (TextBox_Interval.Text.Length > 0)
            {
                duckDNSData.updateInterval = TextBox_Interval.Text;
            }

            recordIPv4 = "";
            recordIPv6 = "";

            UpdateDomain();

            StartTimer();

            SaveRecord();
        }

        private void Button_UpdateStop_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }


        private async void UpdateDNSInfoAsync()
        {
            try
            {
                bool isIPNotChanged = true;
                Now = DateTime.Now.ToString();
                Uri uriForIPInfoV4 = new Uri(ipInfoURL);
                Uri uriForIPInfoV6 = new Uri(ip6InfoURL);

                HttpClient clientForIPInfo = new HttpClient();
                HttpResponseMessage rspForIPInfo;
                rspForIPInfo = await clientForIPInfo.GetAsync(uriForIPInfoV4);
                rspForIPInfo.EnsureSuccessStatusCode();

                var newIP = await rspForIPInfo.Content.ReadAsStringAsync();

                if (newIP == recordIPv4)
                {

                    UpdateStatsLabel(MODE_IPV4, "IP Not Changed");   
                }
                else
                {
                    recordIPv4 = newIP.ToString();

                    UpdateStatsLabel(MODE_IPV4, recordIPv4);

                    isIPNotChanged = false;
                }


                rspForIPInfo = await clientForIPInfo.GetAsync(uriForIPInfoV6);
                rspForIPInfo.EnsureSuccessStatusCode();

                newIP = await rspForIPInfo.Content.ReadAsStringAsync();

                if (newIP == recordIPv6)
                {
                    UpdateStatsLabel(MODE_IPV6, "IP Not Changed");
                }
                else if (newIP.Length > 0)
                {
                    string[] arr = newIP.Split(':');
                    if (arr.Length > 2)
                    {
                        recordIPv6 = newIP.ToString();
                        UpdateStatsLabel(MODE_IPV6, recordIPv6);
                        isIPNotChanged = false;
                    }
                    else
                    {
                        UpdateStatsLabel(MODE_IPV6, "IP Not Detect");
                    }
                }

                if (isIPNotChanged == true)
                {
                    //IP無變更，不需要更新
                    return;
                }

                StringBuilder apiURLBuilder = new StringBuilder("https://www.duckdns.org/update?");
                apiURLBuilder.AppendFormat("domains={0}&token={1}&ip={2}&ipv6={3}", duckDNSData.domainName, duckDNSData.token, recordIPv4, recordIPv6);
                string apiURL = apiURLBuilder.ToString();
                HttpClient client = new HttpClient();
                HttpResponseMessage rspn;
                Uri uriWebApi = new Uri(apiURL);
                rspn = await client.GetAsync(uriWebApi);
                rspn.EnsureSuccessStatusCode();

                UpdateStatsLabel(MODE_IPV4, "Success");
                UpdateStatsLabel(MODE_IPV6, "Success");

            }
            catch (Exception ex)
            {
                string errorMsg = "";
                if (ex.Message != null)
                    errorMsg = ex.Message.Substring(0, Math.Min(ex.Message.Length, 40));
                UpdateStatsLabel(MODE_IPV6, errorMsg);
            }
            finally
            {
            }
        }


        private void UpdateStatsLabel(string mode, string info)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (mode == MODE_IPV4)
                    Label_IPv4Status.Content = "IPv4: " + info + " " + Now;
                else
                    Label_IPv6Status.Content = "IPv6: " + info + " " + Now;
            }));
        }

        private void LoadRecord()
        {
            if (File.Exists(recordFileName))
            {
                string rawString = File.ReadAllText(recordFileName);
                duckDNSData.LoadDataFromString(rawString);
                System.Console.WriteLine(rawString);
            }
            else
            {
                duckDNSData.updateInterval = "60";
            }

            TextBox_APIKey.Text = duckDNSData.token;
            TextBox_ZoneID.Text = duckDNSData.domainName;
            TextBox_Interval.Text = duckDNSData.updateInterval;
        }

        private void CheckAutoStart()
        {
            if (duckDNSData.token != null && duckDNSData.token.Length > 0 &&
                duckDNSData.domainName != null && duckDNSData.domainName.Length > 0 &&
                duckDNSData.updateInterval != null && duckDNSData.updateInterval.Length > 0)
            {

                UpdateDomain();

                StartTimer();
            }
        }


        private void SaveRecord()
        {
            if (File.Exists(recordFileName) == true)
                File.Delete(recordFileName);
            File.WriteAllText(recordFileName, duckDNSData.StringForWrite());
        }

        #region TrayIcon

        private void InitTrayIcon()
        {
            m_notifyIcon = new System.Windows.Forms.NotifyIcon();
            m_notifyIcon.Text = "CFD";
            m_notifyIcon.Icon = Properties.Resources.godaddy;
            m_notifyIcon.MouseDoubleClick += mNotifyIcon_MouseDoubleClick;
            m_notifyIcon.MouseClick += mNotifyIcon_MouseClick;
            m_notifyIcon.Visible = true;

            mContextMenu = new System.Windows.Controls.ContextMenu();
            MenuItem itemMainWindowShow = new MenuItem();
            itemMainWindowShow.Header = "Setting";
            itemMainWindowShow.Click += new RoutedEventHandler(MainWindow_Show_Click);
            mContextMenu.Items.Add(itemMainWindowShow);
            MenuItem itemExit = new MenuItem();
            itemExit.Header = "Exit";
            itemExit.Click += new RoutedEventHandler(Exit_Click);
            mContextMenu.Items.Add(itemExit);
        }
        private void mNotifyIcon_MouseClick(object iSender, System.Windows.Forms.MouseEventArgs iEventArgs)
        {
            System.Windows.Forms.MouseEventArgs me = (System.Windows.Forms.MouseEventArgs)iEventArgs;
            if (iEventArgs.Button == System.Windows.Forms.MouseButtons.Right)
            {
                mContextMenu.IsOpen = true;
            }
        }
        private void MainWindow_Show_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
        }
        private void mNotifyIcon_MouseDoubleClick(object iSender, System.Windows.Forms.MouseEventArgs iEventArgs)
        {
            mContextMenu.IsOpen = false;
        }
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) this.Hide();
            base.OnStateChanged(e);
        }
        // Minimize to system tray when application is closed.
        protected override void OnClosing(CancelEventArgs e)
        {
            // setting cancel to true will cancel the close request
            // so the application is not closed
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        #endregion

    }


    public class DNSData
    {
        public string token;
        public string domainName;
        public string updateInterval;
        public bool enableIPv6;

        private char _splitToken = ',';
        private int tokenIndex = 0;
        private int domainNameIndex = 1;
        private int updateIntervalIndex = 2;
        public string StringForWrite()
        {
            System.Text.StringBuilder stringToWrite = new System.Text.StringBuilder();
            stringToWrite.Append(token);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(domainName);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(updateInterval);
            return stringToWrite.ToString();
        }

        public void LoadDataFromString(string input)
        {
            string[] datas = input.Split(_splitToken);
            token = datas[tokenIndex];
            domainName = datas[domainNameIndex];
            updateInterval = datas[updateIntervalIndex];
            if (Int32.Parse(updateInterval) > 35790)
                updateInterval = "35790";

        }
    }
}
