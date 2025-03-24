using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace myipset
{
    public partial class CPing : Window
    {
        private CancellationTokenSource _cts;

        private double aspectRatio = 16.0 / 8.2; // 设置窗口的宽高比为16:10
        public ObservableCollection<PingResult> PingResults { get; set; }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint phyAddrLen);

        public CPing()
        {
            InitializeComponent();
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
                    MAC = "",
                    Color = Brushes.WhiteSmoke,
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
            // 使用前缀初始化PingResults
            InitPingResults(prefix);
            // 清空MAC列表
            DataGridMAC.ItemsSource = null;

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

            // 群ping共256个地址，分批执行（4批，每批64个）
            for (int batch = 0; batch < 4; batch++)
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < 64; i++)
                {
                    int ipSuffix = batch * 64 + i;
                    string targetIp = $"{prefix}.{ipSuffix}";
                    tasks.Add(PingAndUpdateUI(targetIp, ipSuffix, _cts.Token));
                }
                try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { return; }
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
                        pr.MAC = GetMacAddress(pr.FullIP);
                        // 若该单元格仍是默认背景（未在线响应），则更新为紫色，并补充 MAC 信息
                        if (pr.Color == Brushes.LightGray)
                        {
                            pr.Color = Brushes.Fuchsia;
                            pr.ToolTip = "ping不通但可获取MAC地址";
                        }
                    }
                }
                // 同网段更新MAC不为空的记录
                DataGridMAC.ItemsSource = PingResults.Where(p => !string.IsNullOrEmpty(p.MAC)).ToList();
            }
            else //跨网段更新不超时的IP列表
            {
                DataGridMAC.ItemsSource = PingResults.Where(p => p.ToolTip != null).ToList();
            }
            ItemsControlCping.ItemsSource = PingResults;    //必须加在最后,不然刷新不会显示数据
            ButtonStartPing.Content = "开始群ping";
            ButtonStartPing.IsEnabled = true;
        }

        private async Task PingAndUpdateUI(string targetIp, int ipSuffix, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            PingReply reply = null;
            Ping pingSender = new Ping();
            try { reply = await pingSender.SendPingAsync(targetIp, 1500); } catch { }

            if (reply != null && reply.Status == IPStatus.Success)
            {
                long delay = reply.RoundtripTime;
                Color cellColor;
                if (delay < 10)
                    cellColor = Colors.Lime;
                else if (delay < 100)
                    cellColor = Colors.Yellow;
                else if (delay < 1000)
                    cellColor = Colors.Orange;
                else
                    cellColor = Colors.OrangeRed;

                PingResults[ipSuffix].Color = new SolidColorBrush(cellColor);
                PingResults[ipSuffix].ToolTip = $"{delay} ms";
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

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 动态调整窗口高度以保持宽高比,故不需要定义窗口的高度,会用界面元素的高度填充
            double newHeight = e.NewSize.Width / aspectRatio;
            this.Height = newHeight;
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