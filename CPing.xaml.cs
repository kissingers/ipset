using ipset;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.IO;

namespace myipset
{
    public partial class CPing : Window
    {
        private CancellationTokenSource _cts;
        public ObservableCollection<PingResult> PingResults { get; set; }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint phyAddrLen);

        public CPing()
        {
            InitializeComponent();
            // 从主窗口取当前IP1文本自动填入
            var mainWindow = (MainWindow)Application.Current.MainWindow;
            TextBoxCping.Text = mainWindow.TextBox_IP1.Text;
            // 使用默认前缀初始化表格，确保左侧固定显示0-255
            InitPingResults("0.0.0");
            ItemsControlCping.ItemsSource = PingResults;
        }


        // 新的初始化方法，根据传入的 prefix 构造完整 IP 与显示值
        private void InitPingResults(string prefix)
        {
            PingResults = new ObservableCollection<PingResult>();
            for (int i = 0; i < 256; i++)
            {
                PingResults.Add(new PingResult
                {
                    DisplayIP = i.ToString(),
                    FullIP = $"{prefix}.{i}",
                    Color = Brushes.LightGray,
                    ToolTip = ""
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

        private string GetMacAddress(string ipAddress)
        {
            try
            {
                uint destIp = BitConverter.ToUInt32(IPAddress.Parse(ipAddress).GetAddressBytes(), 0);
                byte[] macAddr = new byte[6];
                uint macAddrLen = (uint)macAddr.Length;
                int result = SendARP(destIp, 0, macAddr, ref macAddrLen);
                if (result != 0)
                    return "";
                return string.Join(":", macAddr.Select(b => b.ToString("X2")));
            }
            catch
            {
                return "";
            }
        }

        private async void ButtonStartPing_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            DataGridMAC.ItemsSource = null; // 清空MAC列表

            // 解析基准IP，即TextBoxCping中填写的IP
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
            // 使用前缀初始化PingResults（左侧显示最后一位，右侧保存完整IP）
            InitPingResults(prefix);
            ItemsControlCping.ItemsSource = PingResults;

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


            // 群ping共256个地址，分批执行（8批，每批32个）
            List<Task> tasks = new List<Task>();
            for (int batch = 0; batch < 8; batch++)
            {
                for (int i = 0; i < 32; i++)
                {
                    int ipSuffix = batch * 32 + i;
                    string targetIp = $"{prefix}.{ipSuffix}";
                    tasks.Add(PingAndUpdateUI(targetIp, ipSuffix, sameNetwork, _cts.Token));
                }
            }
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // 任务全部完成后，根据是否同网段分别处理
            if (sameNetwork)
            {
                // 获取 ARP 表中的记录，补充那些ping不通但能获取到MAC地址的单元格
                var arpEntries = GetArpTableEntries();
                foreach (var entry in arpEntries)
                {
                    if (entry.Key.StartsWith(prefix + "."))
                    {
                        int lastOctet = int.Parse(entry.Key.Split('.')[3]);
                        var pr = PingResults[lastOctet];
                        // 若该单元格仍是默认背景（未在线响应），则更新为紫色，并补充 MAC 信息
                        if (pr.Color == Brushes.LightGray)
                        {
                            pr.Color = Brushes.Purple;
                            pr.ToolTip = "MAC:" + entry.Value;
                            pr.MAC = entry.Value;
                        }
                        // 如果在线（颜色不为默认或紫色），则仅更新 MAC 信息
                        else if (pr.Color != Brushes.Purple)
                        {
                            pr.MAC = entry.Value;
                        }
                    }
                }
                // 更新右侧MAC列表，仅显示MAC不为空的记录
                var macList = PingResults.Where(p => !string.IsNullOrEmpty(p.MAC)).ToList();
                DataGridMAC.ItemsSource = macList;
            }
            else
            {
                // 不在同网段时，右侧表格只显示IP部分，因此将MAC置空
                DataGridMAC.ItemsSource = PingResults.Select(p => new PingResult { FullIP = p.FullIP, MAC = "" }).ToList();

            }


            ButtonStartPing.Content = "开始群ping";
            ButtonStartPing.IsEnabled = true;
        }

        private async Task PingAndUpdateUI(string targetIp, int ipSuffix, bool sameNetwork, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            Ping pingSender = new Ping();
            PingReply reply = null;
            try
            {
                reply = await pingSender.SendPingAsync(targetIp, 1200);
            }
            catch
            {
            }

            Color cellColor = Colors.LightGray;
            bool online = false;
            long delay = 0;
            if (reply != null && reply.Status == IPStatus.Success)
            {
                online = true;
                delay = reply.RoundtripTime;
                if (delay < 10)
                    cellColor = Colors.Lime;
                else if (delay < 100)
                    cellColor = Colors.Yellow;
                else if (delay < 1000)
                    cellColor = Colors.Orange;
                else
                    cellColor = Colors.OrangeRed;
            }

            if (!token.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    PingResults[ipSuffix].Color = new SolidColorBrush(cellColor);
                    PingResults[ipSuffix].ToolTip = online ? $"{delay} ms" : "ping超时";
                    if (online && sameNetwork)
                    {
                        PingResults[ipSuffix].MAC = GetMacAddress(targetIp);
                    }
                });
            }
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
                                if (mac == "FF:FF:FF:FF:FF:FF")
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            base.OnClosing(e);
        }

        private void ButtonSaveMac_Click(object sender, RoutedEventArgs e)
        {
            // 使用SaveFileDialog选择保存路径
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                Title = "保存IP/MAC表"
            };
            if (sfd.ShowDialog() == true)
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
                MessageBox.Show("保存成功！");
            }
        }
    }

    public class PingResult
    {
        public string DisplayIP { get; set; }  // 用于左侧16*16网格，显示最后一位
        public string FullIP { get; set; }     // 用于右侧表格，显示完整IP
        public string MAC { get; set; }
        public Brush Color { get; set; }
        public string ToolTip { get; set; }
    }

}
