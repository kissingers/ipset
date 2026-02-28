using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace myipset
{
    public partial class CPing : Window
    {
        private CancellationTokenSource _cts;
        private readonly double aspectRatio = 16.0 / 9;
        private readonly Brush BackColor = Brushes.WhiteSmoke;
        public ObservableCollection<PingResult> PingResults { get; set; }

        public CPing()
        {
            InitializeComponent();
            InitPingResults("0.0.0");
            ItemsControlCping.ItemsSource = PingResults;
            // keep TextBoxDelay showing default based on current IP input
            try
            {
                TextBoxCping.LostFocus += TextBoxCping_LostFocus;
                TextBoxCping.KeyDown += TextBoxCping_KeyDown;
                TextBoxDelay.TextChanged += TextBoxDelay_TextChanged;
                UpdateDelayTextBox();
                _lastIpText = TextBoxCping.Text?.Trim();
                this.Loaded += (s, e) => UpdateDelayTextBox();
                this.PreviewMouseDown += Window_PreviewMouseDown;
            }
            catch { }
        }

        private void TextBoxCping_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var current = TextBoxCping.Text?.Trim();
                if (string.IsNullOrEmpty(_lastIpText) || !_lastIpText.Equals(current))
                {
                    // IP changed -> reset user edit flag so default can be applied
                    _delayUserEdited = false;
                    UpdateDelayTextBox();
                    _lastIpText = current;
                }
            }
            catch { }
        }

        private void TextBoxCping_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    var current = TextBoxCping.Text?.Trim();
                    if (string.IsNullOrEmpty(_lastIpText) || !_lastIpText.Equals(current))
                    {
                        _delayUserEdited = false;
                        UpdateDelayTextBox();
                        _lastIpText = current;
                    }
                }
                catch { }
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // if TextBoxCping currently has focus and click is outside it, treat as confirm
                if (TextBoxCping.IsFocused)
                {
                    DependencyObject src = e.OriginalSource as DependencyObject;
                    // walk up visual tree to see if click target is inside TextBoxCping
                    while (src != null && src != TextBoxCping)
                        src = VisualTreeHelper.GetParent(src);
                    if (src == null)
                    {
                        var current = TextBoxCping.Text?.Trim();
                        if (string.IsNullOrEmpty(_lastIpText) || !_lastIpText.Equals(current))
                        {
                            _delayUserEdited = false;
                            UpdateDelayTextBox();
                            _lastIpText = current;
                        }
                    }
                }
            }
            catch { }
        }

        private bool _delayUserEdited = false;
        private bool _suppressDelayTextChanged = false;
        private string _lastIpText;

        private void TextBoxDelay_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressDelayTextChanged) return;
            _delayUserEdited = true;
        }

        // Update TextBoxDelay to show default inter-delay according to current TextBoxCping value
        private void UpdateDelayTextBox()
        {
            try
            {
                string txt = TextBoxCping?.Text?.Trim();
                if (string.IsNullOrEmpty(txt))
                {
                    TextBoxDelay.Text = "5";
                    return;
                }
                IPAddress ip;
                if (!IPAddress.TryParse(txt, out ip))
                {
                    TextBoxDelay.Text = "5";
                    return;
                }
                int def = GetDefaultDelayForIp(ip);
                TextBoxDelay.Text = def.ToString();
                TextBoxDelay.ToolTip = "扫描延时 毫秒";
                System.Windows.Controls.ToolTipService.SetInitialShowDelay(TextBoxDelay, 0);
                System.Windows.Controls.ToolTipService.SetShowDuration(TextBoxDelay, 5000);
            }
            catch { }
        }

        private int GetDefaultDelayForIp(IPAddress baseIp)
        {
            try
            {
                var nic = GetMatchingNetworkInterface(baseIp);
                if (nic == null) return 5; // not same subnet or unknown
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return 200;
                return 25; // assume wired otherwise
            }
            catch { return 5; }
        }

        private void InitPingResults(string prefix)
        {
            PingResults = new ObservableCollection<PingResult>();
            for (int i = 0; i < 256; i++)
            {
                PingResults.Add(new PingResult
                {
                    DisplayIP = i.ToString(),
                    FullIP = $"{prefix}.{i}",
                    MAC = "",
                    Color = BackColor,
                    ToolTip = null
                });
            }
        }

        private List<Tuple<IPAddress, IPAddress>> GetLocalIPv4Addresses()
        {
            var addresses = new List<Tuple<IPAddress, IPAddress>>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        addresses.Add(new Tuple<IPAddress, IPAddress>(ip.Address, ip.IPv4Mask));
                    }
                }
            }
            return addresses;
        }

        // Return the NetworkInterface whose unicast address places baseIp in the same subnet, or null.
        private NetworkInterface GetMatchingNetworkInterface(IPAddress baseIp)
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        if (IsInSameSubnet(baseIp, ip.Address, ip.IPv4Mask))
                            return adapter;
                    }
                }
            }
            return null;
        }

        private bool IsInSameSubnet(IPAddress address, IPAddress subnetAddress, IPAddress subnetMask)
        {
            byte[] addressBytes = address.GetAddressBytes();
            byte[] subnetAddressBytes = subnetAddress.GetAddressBytes();
            byte[] subnetMaskBytes = subnetMask.GetAddressBytes();
            if (addressBytes.Length != subnetAddressBytes.Length || addressBytes.Length != subnetMaskBytes.Length)
                return false;
            for (int i = 0; i < addressBytes.Length; i++)
            {
                if ((addressBytes[i] & subnetMaskBytes[i]) != (subnetAddressBytes[i] & subnetMaskBytes[i]))
                    return false;
            }
            return true;
        }

        private async void ButtonStartPing_Click(object sender, RoutedEventArgs e)
        {
            // 取消上一次扫描
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            string ipText = TextBoxCping.Text.Trim();
            if (!IPAddress.TryParse(ipText, out IPAddress baseIp))
            {
                MessageBox.Show("无效的IP地址！");
                return;
            }

            string[] parts = baseIp.ToString().Split('.');
            if (parts.Length != 4)
            {
                MessageBox.Show("IP地址格式错误！");
                return;
            }

            // 获取前三个octet形成 /24 网段前缀
            string prefix = $"{parts[0]}.{parts[1]}.{parts[2]}";
            // 使用前缀初始化PingResults
            InitPingResults(prefix);
            //立即更新ItemsSource以清空界面
            ItemsControlCping.ItemsSource = PingResults;
            DataGridMAC.ItemsSource = null;

            // 检查是否同网段
            bool sameNetwork = false;
            var localAddresses = GetLocalIPv4Addresses();
            foreach (var addr in localAddresses)
            {
                if (IsInSameSubnet(baseIp, addr.Item1, addr.Item2))
                {
                    sameNetwork = true;
                    break;
                }
            }

            TextBoxCping.Background = sameNetwork ? Brushes.LightGreen : Brushes.LightSalmon;
            ButtonStartPing.Content = "正在群ping...";
            ButtonStartPing.IsEnabled = false;
            ButtonSaveMac.IsEnabled = false;
            // disable delay textbox while scanning
            try { TextBoxDelay.IsEnabled = false; } catch { }

            // 严格控制并发（避免网络栈缓冲）
            var semaphore = new SemaphoreSlim(256);
            var pingTasks = new List<Task>();

            try
            {
                // interDelay is read from TextBoxDelay (filled by UpdateDelayTextBox or user)
                int interDelay = 5;
                try
                {
                    int userDelay;
                    if (int.TryParse(TextBoxDelay.Text, out userDelay) && userDelay > 0)
                        interDelay = userDelay;
                }
                catch { }

                //随机ping方式,供参考
                //var indices = Enumerable.Range(0, 256).OrderBy(x => Guid.NewGuid()).ToList();
                //foreach (var i in indices)

                for (int i = 0; i < 256; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    string targetIp = $"{prefix}.{i}";
                    // 精确控制启动间隔
                    pingTasks.Add(PingAndUpdateUI(targetIp, i, sameNetwork, _cts.Token, semaphore));
                    await Task.Delay(interDelay, _cts.Token);
                }
                await Task.WhenAll(pingTasks);

                // After pings finish, when same subnet, read arp table and populate MACs and mark unreachable-with-MAC as purple
                if (sameNetwork && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var arpEntries = GetArpTableEntries();
                        foreach (var entry in arpEntries)
                        {
                            if (entry.Key.StartsWith(prefix + "."))
                            {
                                int lastOctet;
                                if (!int.TryParse(entry.Key.Split('.')[3], out lastOctet)) continue;
                                if (lastOctet < 0 || lastOctet >= PingResults.Count) continue;
                                var pr = PingResults[lastOctet];
                                // entry.Value already formatted mac
                                pr.MAC = entry.Value;
                                // if cell still default (no ICMP reply), mark purple
                                if (pr.Color == BackColor)
                                {
                                    pr.Color = Brushes.Fuchsia;
                                    pr.ToolTip = "ping不通但可获取MAC地址";
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { }  // 用户取消，静默处理
            catch (Exception ex) { MessageBox.Show($"扫描异常: {ex.Message}"); }
            finally { semaphore.Dispose(); }

            // 仅当未取消时更新结果
            if (!_cts.Token.IsCancellationRequested)
            {
                DataGridMAC.ItemsSource = sameNetwork
                    ? PingResults.Where(p => !string.IsNullOrEmpty(p.MAC)).ToList()
                    : PingResults.Where(p => p.ToolTip != null && p.Color != BackColor).ToList();

                ButtonStartPing.Content = "开始群ping";
                ButtonStartPing.IsEnabled = true;
                ButtonSaveMac.IsEnabled = true;
                try { TextBoxDelay.IsEnabled = true; } catch { }
            }
        }

        private async Task PingAndUpdateUI(string targetIp, int idx, bool isSameSubnet, CancellationToken token, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested || idx < 0 || idx >= PingResults.Count)
                    return;

                // 执行Ping
                PingReply reply = null;
                try
                {
                    using (var ping = new Ping())
                    {
                        reply = await ping.SendPingAsync(targetIp, 1600);
                    }
                }
                catch (PingException) { /* 静默处理Ping异常 */ }
                catch (TaskCanceledException) { return; }

                // 安全更新UI（.NET Framework 4.8兼容：同步Invoke）
                Dispatcher.Invoke(new Action(() =>
                {
                    if (idx >= PingResults.Count) return;
                    var item = PingResults[idx];

                    // Ping成功：按延迟着色
                    if (reply?.Status == IPStatus.Success)
                    {
                        long rt = reply.RoundtripTime;
                        item.Color = new SolidColorBrush(rt < 10 ? Colors.Lime : rt < 100 ? Colors.Yellow : rt < 1000 ? Colors.Orange : Colors.OrangeRed);
                        item.ToolTip = $"{rt} ms";
                    }
                }), DispatcherPriority.Background);
            }
            finally { semaphore.Release(); }
        }

        private Dictionary<string, string> GetArpTableEntries()
        {
            var entries = new Dictionary<string, string>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("-"))
                        {
                            var tokens = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 2)
                            {
                                string ip = tokens[0];
                                string mac = tokens[1].Replace('-', ':').ToUpper();
                                if (mac == "FF:FF:FF:FF:FF:FF" || mac == "00:00:00:00:00:00")
                                    continue;
                                entries[ip] = mac;
                            }
                        }
                    }
                }
            }
            catch { }
            return entries;
        }

        private void ButtonSaveMac_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                Title = "保存IP/MAC表"
            };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var pr in PingResults)
                    {
                        if (!string.IsNullOrEmpty(pr.MAC))
                        {
                            sb.AppendLine($"{pr.FullIP},{pr.MAC}");
                        }
                    }
                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double newHeight = e.NewSize.Width / aspectRatio;
            this.Height = newHeight;
        }
    }

    // 实现INotifyPropertyChanged以支持实时UI更新（.NET Framework 4.8必需）
    public class PingResult : INotifyPropertyChanged
    {
        private string _displayIP;
        private string _fullIP;
        private string _mac;
        private Brush _color;
        private string _toolTip;

        public string DisplayIP
        {
            get => _displayIP;
            set { _displayIP = value; OnPropertyChanged(nameof(DisplayIP)); }
        }

        public string FullIP
        {
            get => _fullIP;
            set { _fullIP = value; OnPropertyChanged(nameof(FullIP)); }
        }

        public string MAC
        {
            get => _mac;
            set { _mac = value; OnPropertyChanged(nameof(MAC)); }
        }

        public Brush Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        public string ToolTip
        {
            get => _toolTip;
            set { _toolTip = value; OnPropertyChanged(nameof(ToolTip)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}