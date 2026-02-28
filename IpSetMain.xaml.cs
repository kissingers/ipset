using Microsoft.Win32;
using myipset;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ipset
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<string> traceMessage = new ObservableCollection<string>();
        private readonly List<string> netWorkList = new List<string>();
        private readonly double aspectRatio = 16.0 / 10.0; // 设置窗口的宽高比为16:10

        public MainWindow()
        {
            InitializeComponent();
            ReadConfig();
            ShowAdapterInfo();
            ListNetWork();
            IpClass.PShellOk = IsNetworkPowerShellSupported();
        }

        public static bool IsNetworkPowerShellSupported()
        {
            try
            {
                // 执行 PowerShell 命令：检查是否能加载 NetTCPIP 模块并调用 Get-NetIPInterface
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"Get-Module -ListAvailable -Name NetTCPIP | Out-Null; if (Get-Command Set-NetIPInterface -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public void AddMessage(string message)
        {
            TextBox_Message.AppendText(message + Environment.NewLine);
            TextBox_Message.ScrollToEnd();
        }

        public void SetColor(Color UsedColor, bool EditEnable)
        {
            TextBox_IP1.Background = new SolidColorBrush(UsedColor);
            TextBox_Mask1.Background = new SolidColorBrush(UsedColor);
            TextBox_GateWay.Background = new SolidColorBrush(UsedColor);
            TextBox_DNS1.Background = new SolidColorBrush(UsedColor);
            TextBox_DNS2.Background = new SolidColorBrush(UsedColor);
            TextBox_IP2.Background = new SolidColorBrush(UsedColor);
            TextBox_Mask2.Background = new SolidColorBrush(UsedColor);
            TextBox_IP1.IsEnabled = EditEnable;
            TextBox_Mask1.IsEnabled = EditEnable;
            TextBox_GateWay.IsEnabled = EditEnable;
            TextBox_DNS1.IsEnabled = EditEnable;
            TextBox_DNS2.IsEnabled = EditEnable;
            TextBox_IP2.IsEnabled = EditEnable;
            TextBox_Mask2.IsEnabled = EditEnable;
        }

        // 网卡列表,这个方法只显示真的的物理网卡列表
        public void ListNetWork()
        {
            ComboBox_NetCard.ItemsSource = "";
            netWorkList.Clear();
            string qry = "SELECT * FROM MSFT_NetAdapter WHERE Virtual=False";
            if (IpClass.ShowVirtualCard)
            {
                qry = "SELECT * FROM MSFT_NetAdapter";
                AddMessage("不可更改IP的网卡不会列出，可参考程序启动时候调试信息栏的适配器信息");
            }
            ManagementScope scope = new ManagementScope(@"\\.\ROOT\StandardCimv2");
            ObjectQuery query = new ObjectQuery(qry);
            ManagementObjectSearcher mos = new ManagementObjectSearcher(scope, query);
            ManagementObjectCollection moc = mos.Get();

            foreach (ManagementObject mo in moc.Cast<ManagementObject>())
            {
                netWorkList.Add(mo["Name"]?.ToString());
                if (IpClass.NicDefaultName == "")
                    IpClass.NicDefaultName = mo["Name"]?.ToString();    //确保有一个网卡选中

                uint ConnectState = Convert.ToUInt32(mo["MediaConnectState"] ?? 0);
                if (ConnectState == 1)    //网卡连接状态0未知  1已连接 2断开
                    IpClass.NicDefaultName = mo["Name"]?.ToString();    //选中最后一个点亮的网卡
            }
            ComboBox_NetCard.ItemsSource = netWorkList;
            ComboBox_NetCard.SelectedItem = IpClass.NicDefaultName;    //默认选取预定义网卡,最后点亮的物理网卡匹配优先,如果都没有,就默认第一个.
        }

        //显示网卡信息
        public void ShowAdapterInfo()
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            AddMessage("主机名字：" + Dns.GetHostName());
            AddMessage("网卡个数：" + adapters.Length);
            int index = 0;
            foreach (NetworkInterface adapter in adapters)
            {
                //显示网络适配器描述信息、名称、类型、速度、MAC 地址
                index++;
                AddMessage("--------------------第" + index + "个适配器信息--------------------");
                AddMessage("网卡名字：" + adapter.Name);
                AddMessage("网卡描述：" + adapter.Description);
                AddMessage("网卡标识：" + adapter.Id);
                AddMessage("网卡类型：" + adapter.NetworkInterfaceType);
                AddMessage("点亮情况：" + adapter.OperationalStatus);
                AddMessage("网卡地址：" + adapter.GetPhysicalAddress());
                AddMessage("网卡速度：" + adapter.Speed / 1000 / 1000 + "MB");

                IPInterfaceProperties ip = adapter.GetIPProperties();

                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    AddMessage("网卡类型：有线网卡");
                    AddMessage("自动获取：" + ip.GetIPv4Properties().IsDhcpEnabled);
                    AddMessage("IPV4MTU：" + ip.GetIPv4Properties().Mtu);
                    AddMessage("IPV6MTU：" + ip.GetIPv6Properties().Mtu);
                }

                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    AddMessage("网卡类型：无线网卡");
                    AddMessage("自动获取：" + ip.GetIPv4Properties().IsDhcpEnabled);
                    AddMessage("IPV4MTU：" + ip.GetIPv4Properties().Mtu);
                    AddMessage("IPV6MTU：" + ip.GetIPv6Properties().Mtu);
                }

                UnicastIPAddressInformationCollection netIpAdds = ip.UnicastAddresses;
                foreach (UnicastIPAddressInformation ipadd in netIpAdds)
                {
                    if (ipadd.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        AddMessage("IPV4地址：" + ipadd.Address);
                    if (ipadd.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        AddMessage("IPV6地址：" + ipadd.Address);
                }

                IPAddressCollection dnsServers = ip.DnsAddresses;
                foreach (IPAddress dns in dnsServers)
                {
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        AddMessage("IPV4域名：" + dns);
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        AddMessage("IPV6域名：" + dns);
                }
            }
            AddMessage("-------------------适配器信息输出结束---------------------");
        }

        // 选择网卡下拉列表时候显示对应的网卡
        public void SelectNetCard()
        {
            if (ComboBox_NetCard.SelectedValue == null || !GetNetWork(ComboBox_NetCard.SelectedValue.ToString()))
            {
                ListNetWork();
                MessageBox.Show("网卡名字不正确，已重新刷新网卡信息，选择的网卡可能变了，请重新选择...");
            }

            IpClass.NiceEnable = false;
            IpClass.UseDhcp = false;
            IpClass.NicConnect = false;
            IpClass.Use2Ip = false;

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.Name != ComboBox_NetCard.SelectedValue.ToString())
                    continue;        //处理下拉列表,和前面读取的表项比较如果不匹配就继续匹配

                IpClass.NiceEnable = true;    //匹配成功说明网卡起来了,读取网卡信息
                IPInterfaceProperties ip = adapter.GetIPProperties();
                UnicastIPAddressInformationCollection netIpAdds = ip.UnicastAddresses;
                GatewayIPAddressInformationCollection gatewayIpAdds = ip.GatewayAddresses;
                IPAddressCollection dnsServers = ip.DnsAddresses;

                IpClass.NicName = adapter.Name;                 //如果匹配先保存网卡名字和描述到ip临时表
                IpClass.NicDescript = adapter.Description;
                this.Title = "修改器  " + IpClass.NicDescript;

                TextBox_MAC.Text = adapter.GetPhysicalAddress().ToString();
                TextBox_MTU.Text = ip.GetIPv4Properties().Mtu.ToString();
                IpClass.UseDhcp = ip.GetIPv4Properties().IsDhcpEnabled;
                if (adapter.OperationalStatus == OperationalStatus.Up)
                {
                    IpClass.NicConnect = true;
                }

                //处理IP和掩码,最多2组IPv4
                int index1 = 0;
                TextBox_IP1.Text = "";
                TextBox_Mask1.Text = "";
                TextBox_IP2.Text = "";
                TextBox_Mask2.Text = "";
                TextBox_GateWay.Text = "";
                TextBox_DNS1.Text = "";
                TextBox_DNS2.Text = "";
                foreach (UnicastIPAddressInformation ipadd in netIpAdds)
                {
                    if (ipadd.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) //判断ipV4
                    {
                        index1++;   //多个ip处理
                        if (index1 == 1)
                        {
                            TextBox_IP1.Text = ipadd.Address.ToString();
                            TextBox_Mask1.Text = ipadd.IPv4Mask.ToString();
                        }
                        if (index1 == 2)
                        {
                            IpClass.Use2Ip = true;
                            TextBox_IP2.Text = ipadd.Address.ToString();
                            TextBox_Mask2.Text = ipadd.IPv4Mask.ToString();
                        }
                    }
                }

                //处理网关
                foreach (GatewayIPAddressInformation gateway in gatewayIpAdds)
                {
                    // 仅使用 IPv4 网关地址，忽略 IPv6（本程序仅处理 IPv4）
                    if (gateway.Address != null && gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        TextBox_GateWay.Text = gateway.Address.ToString();
                        break; // 使用第一个 IPv4 网关即可
                    }
                }

                //处理DNS服务器地址,最多2组
                int index2 = 0;
                foreach (IPAddress dns in dnsServers)
                {
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) //判断ipv4的dns
                    {
                        index2++;   //多个dns处理
                        if (index2 == 1) TextBox_DNS1.Text = dns.ToString();
                        if (index2 == 2) TextBox_DNS2.Text = dns.ToString();
                    }
                }
            }
        }

        // 设置网卡ip地址
        public bool SetNetworkAdapter()
        {
            //检查合格保存当前网卡状态，以备可以回退一次
            if (!Savelastip())
            {
                MessageBox.Show("保存历史IP信息不成功，请刷新网卡信息后再尝试");
                return false;
            }

            //如果是地址是自动获取的,上面已经修改为dhcp模式了,完成任务直接结束
            if (IpClass.UseDhcp)
            {
                AddMessage("netsh interface ip set address name=\"" + IpClass.NicName + "\" source=dhcp");
                AddMessage("netsh interface ip set dns name=\"" + IpClass.NicName + "\" source=dhcp");
                RunNetshCommand("interface ip set address name=\"" + IpClass.NicName + "\" source=dhcp");
                RunNetshCommand("interface ip set dns name=\"" + IpClass.NicName + "\" source=dhcp");
                AddMessage("-------------修改网卡动态获取地址结束---------------\r\n");
                SelectNetCard();
                ChangeUI();
                return true;
            }

            //网卡不是dhcp则检查是否激活,不激活直接退出
            if (!IpClass.NicConnect)
            {
                MessageBox.Show("当前网卡未激活，请激活网卡后再设置静态IP,否则会变成169开头的地址！");
                return false;
            }

            //不是动态则检查IP是否合法，不合法直接退出
            if (!Checkinput())
            {
                AddMessage("-----------需要修改的IP不符合规范,更改IP不成功-----------\r\n");
                return false;
            }

            //处理第一组IP掩码和网关,有变化才改变,避免不必要的更改IP导致网络暂时中断
            if (IpClass.lastUseDhcp || IpClass.setip1 != IpClass.lastArray[1] || IpClass.setmask1 != IpClass.lastArray[2] || IpClass.setgw != IpClass.lastArray[3])
            {
                //如果ip、掩码和网关都不为空,则设置ip地址和子网掩码和网关
                if (!string.IsNullOrEmpty(IpClass.setip1) && !string.IsNullOrEmpty(IpClass.setmask1) && !string.IsNullOrEmpty(IpClass.setgw))
                {
                    AddMessage("netsh interface ipv4 set address \"" + IpClass.NicName + "\" static " + IpClass.setip1 + " " + IpClass.setmask1 + " " + IpClass.setgw);
                    RunNetshCommand("interface ipv4 set address \"" + IpClass.NicName + "\" static " + IpClass.setip1 + " " + IpClass.setmask1 + " " + IpClass.setgw);
                }

                //如果ip和掩码都不为空，但是没网关，则设置ip地址和子网掩码
                if (!string.IsNullOrEmpty(IpClass.setip1) && !string.IsNullOrEmpty(IpClass.setmask1) && string.IsNullOrEmpty(IpClass.setgw))
                {
                    AddMessage("netsh interface ipv4 set address \"" + IpClass.NicName + "\" static " + IpClass.setip1 + " " + IpClass.setmask1);
                    RunNetshCommand("interface ipv4 set address \"" + IpClass.NicName + "\" static " + IpClass.setip1 + " " + IpClass.setmask1);
                }
            }

            //处理第二组IP掩码
            if (IpClass.Use2Ip)
            {
                if (IpClass.lastUseDhcp || !IpClass.lastUse2Ip || IpClass.setip2 != IpClass.lastArray[6] || IpClass.setmask2 != IpClass.lastArray[7])
                {
                    //如果有第二个IP和掩码且不为空，则加入第二个IP和掩码
                    if ((IpClass.Use2Ip) && !string.IsNullOrEmpty(IpClass.setip2) && !string.IsNullOrEmpty(IpClass.setmask2))
                    {
                        AddMessage("netsh interface ipv4 add address \"" + IpClass.NicName + "\" " + IpClass.setip2 + " " + IpClass.setmask2);
                        RunNetshCommand("interface ipv4 add address \"" + IpClass.NicName + "\" " + IpClass.setip2 + " " + IpClass.setmask2);
                    }
                }
            }
            else
            {
                if (IpClass.lastUse2Ip)
                {
                    //如果有第二个IP和掩码且不为空，则加入第二个IP和掩码

                    AddMessage("netsh interface ipv4 delete address \"" + IpClass.NicName + "\" " + IpClass.lastArray[6]);
                    RunNetshCommand("interface ipv4 delete address \"" + IpClass.NicName + "\" " + IpClass.lastArray[6]);
                }
            }

            //处理DNS
            if (IpClass.lastUseDhcp || IpClass.setdns1 != IpClass.lastArray[4] || IpClass.setdns2 != IpClass.lastArray[5])
            {
                //如果任意一个DNS非空,那么设置DNS
                if (!string.IsNullOrEmpty(IpClass.setdns1))
                {
                    AddMessage("netsh interface ipv4 set dns \"" + IpClass.NicName + "\" static " + IpClass.setdns1 + " register=primary");
                    RunNetshCommand("interface ipv4 set dns \"" + IpClass.NicName + "\" static " + IpClass.setdns1 + " register=primary");
                }
                else
                {
                    AddMessage("netsh interface ipv4 delete dns \"" + IpClass.NicName + "\" all");
                    RunNetshCommand("interface ipv4 delete dns \"" + IpClass.NicName + "\" all");
                }

                if (!string.IsNullOrEmpty(IpClass.setdns2))
                {
                    string DNS2Command = $"interface ipv4 add dns \"{IpClass.NicName}\" {IpClass.setdns2}";
                    AddMessage("netsh " + DNS2Command);
                    RunNetshCommand(DNS2Command);
                }
            }
            AddMessage("---------------修改网卡结束-----------------\r\n");
            SelectNetCard();
            ChangeUI();
            return true;
        }

        public bool CheckIP(string ip)
        {
            // 尝试解析IP地址
            if (!IPAddress.TryParse(ip, out IPAddress address))
            {
                AddMessage("无效的IP地址：" + ip);
                return false;
            }
            // 仅接受IPv4地址
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                AddMessage("无效的IPv4地址：" + ip);
                return false;
            }
            // 判断第一段必须在1到223之间
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] < 1 || bytes[0] > 223)
            {
                AddMessage("IP地址首段必须在1到223之间：" + ip);
                return false;
            }
            AddMessage("这是合法的IP网关DNS地址：" + ip);
            return true;
        }

        // 验证子网掩码正确性,最后一个1后面应该是全0
        public bool CheckMask(string mask)
        {
            // 尝试解析子网掩码
            if (!IPAddress.TryParse(mask, out IPAddress subnet))
            {
                AddMessage("无效的网络掩码：" + mask);
                return false;
            }
            // 仅接受IPv4掩码
            if (subnet.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                AddMessage("无效的IPv4网络掩码：" + mask);
                return false;
            }

            byte[] bytes = subnet.GetAddressBytes();
            // 将4字节转为一个32位整数（大端序）
            uint maskValue = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | ((uint)bytes[3]);
            if (maskValue == 0)
            {
                AddMessage("无效的网络掩码，掩码不能全为0：" + mask);
                return false;
            }
            // 检查掩码连续性：从最高位开始连续为1，后面必须全为0
            bool foundZero = false;
            for (int i = 31; i >= 0; i--)
            {
                if ((maskValue & (1u << i)) != 0)
                {
                    if (foundZero)
                    {
                        AddMessage("无效的网络掩码，掩码中1和0不连续：" + mask);
                        return false;
                    }
                }
                else
                {
                    foundZero = true;
                }
            }
            AddMessage("这是合法的网络掩码 地址：" + mask);
            return true;
        }

        // 获得网络地址
        public static string GetNetSegment(string ipAddress, string subnetMask)
        {
            byte[] ip = IPAddress.Parse(ipAddress).GetAddressBytes();
            byte[] sub = IPAddress.Parse(subnetMask).GetAddressBytes();

            // 网络地址=子网按位与IP地址
            for (int i = 0; i < ip.Length; i++)
            {
                ip[i] = (byte)((sub[i]) & ip[i]);
            }
            return new IPAddress(ip).ToString();
        }

        // 把条框中显示的保存到ip临时表中,并返回是否成功审核
        public bool Checkinput()
        {
            // 验证IP1
            if (!CheckIP(TextBox_IP1.Text))
            {
                MessageBox.Show("无效的IP地址, 确保输入的地址正确 (IP1: " + TextBox_IP1.Text + ")");
                AddMessage("错误的 IP1：" + TextBox_IP1.Text);
                return false;
            }
            IpClass.setip1 = TextBox_IP1.Text;

            // 验证子网掩码1
            if (!CheckMask(TextBox_Mask1.Text))
            {
                MessageBox.Show("无效的网络掩码, 确保输入的地址正确 (Mask1: " + TextBox_Mask1.Text + ")");
                AddMessage("错误的掩码1：" + TextBox_Mask1.Text);
                return false;
            }
            IpClass.setmask1 = TextBox_Mask1.Text;

            // 验证网关 (允许为空)
            if (string.IsNullOrWhiteSpace(TextBox_GateWay.Text))
                IpClass.setgw = "";
            else if (!CheckIP(TextBox_GateWay.Text))
            {
                MessageBox.Show("无效的网关IP地址: " + TextBox_GateWay.Text);
                AddMessage("错误的网关：" + TextBox_GateWay.Text);
                return false;
            }
            else
                IpClass.setgw = TextBox_GateWay.Text;

            // 验证DNS1 (允许为空)
            if (string.IsNullOrWhiteSpace(TextBox_DNS1.Text))
                IpClass.setdns1 = "";
            else if (!CheckIP(TextBox_DNS1.Text))
            {
                MessageBox.Show("无效的DNS地址: " + TextBox_DNS1.Text);
                AddMessage("错误的 DNS1：" + TextBox_DNS1.Text);
                return false;
            }
            else
                IpClass.setdns1 = TextBox_DNS1.Text;

            // 验证DNS2 (允许为空，但DNS2非空时，DNS1也必须非空)
            if (string.IsNullOrWhiteSpace(TextBox_DNS2.Text))
            {
                IpClass.setdns2 = "";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TextBox_DNS1.Text))
                {
                    MessageBox.Show("如果设置了第二DNS地址，首选DNS地址必须不为空！");
                    AddMessage("错误: 设置了DNS2但DNS1为空");
                    return false;
                }
                if (!CheckIP(TextBox_DNS2.Text))
                {
                    MessageBox.Show("无效的第二DNS地址: " + TextBox_DNS2.Text);
                    AddMessage("错误的 DNS2：" + TextBox_DNS2.Text);
                    return false;
                }
                IpClass.setdns2 = TextBox_DNS2.Text;
            }

            // 验证第二IP和第二子网掩码（如果启用）
            if (IpClass.Use2Ip)
            {
                if (!CheckIP(TextBox_IP2.Text))
                {
                    MessageBox.Show("无效的第二IP地址: " + TextBox_IP2.Text);
                    AddMessage("错误的 IP2：" + TextBox_IP2.Text);
                    return false;
                }
                IpClass.setip2 = TextBox_IP2.Text;

                if (!CheckMask(TextBox_Mask2.Text))
                {
                    MessageBox.Show("无效的第二网络掩码: " + TextBox_Mask2.Text);
                    AddMessage("错误的掩码2：" + TextBox_Mask2.Text);
                    return false;
                }
                IpClass.setmask2 = TextBox_Mask2.Text;
            }
            else
            {
                IpClass.setip2 = "";
                IpClass.setmask2 = "";
            }

            // 检查网关是否与IP在同一个网络段（只在网关不为空时验证）
            if (!string.IsNullOrWhiteSpace(IpClass.setgw))
            {
                bool validSubnet = false;
                // 使用IP1和其掩码检查
                string ip1Segment = GetNetSegment(IpClass.setip1, IpClass.setmask1);
                string gwSegment1 = GetNetSegment(IpClass.setgw, IpClass.setmask1);
                if (ip1Segment == gwSegment1)
                {
                    validSubnet = true;
                }
                // 如果启用了第二IP且第一项不匹配，则尝试使用IP2
                else if (IpClass.Use2Ip && !string.IsNullOrWhiteSpace(IpClass.setip2) && !string.IsNullOrWhiteSpace(IpClass.setmask2))
                {
                    string ip2Segment = GetNetSegment(IpClass.setip2, IpClass.setmask2);
                    string gwSegment2 = GetNetSegment(IpClass.setgw, IpClass.setmask2);
                    if (ip2Segment == gwSegment2)
                        validSubnet = true;
                }

                if (!validSubnet)
                {
                    MessageBox.Show("无效的网关地址：网关与IP地址不在同一网段！");
                    return false;
                }
            }

            return true;
        }

        // 得到指定网卡
        public static ManagementObject NetWork(string networkname)
        {
            string netState = "SELECT * From Win32_NetworkAdapter  where PhysicalAdapter=1";

            ManagementObjectSearcher searcher = new ManagementObjectSearcher(netState);
            ManagementObjectCollection collection = searcher.Get();

            foreach (ManagementObject manage in collection.Cast<ManagementObject>())
            {
                if (manage["NetConnectionID"].ToString() == networkname)
                {
                    return manage;
                }
            }
            return null;
        }

        public void ChangeUI()
        {
            if (IpClass.NiceEnable)         //如果网卡可用,改禁用启用的颜色
            {
                Button_TurnCardOnOff.Content = "停用";
                Button_TurnCardOnOff.Background = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                Button_TurnCardOnOff.Content = "启用";
                Button_TurnCardOnOff.Background = new SolidColorBrush(Color.FromRgb(128, 0, 0));
            }

            if (!IpClass.NicConnect)          //如果网卡没联网,输入界面变粉色,且不可编辑
            {
                SetColor(Color.FromRgb(255, 128, 128), false);
            }
            else if (IpClass.UseDhcp)  //如果网卡联网且是DHCP,输入界面变绿色,且不可编辑
            {
                CheckBox_DHCP.IsChecked = true;
                SetColor(Color.FromRgb(128, 255, 128), false);
            }
            else //如果网卡联网且是静态IP,输入界面变白色,且可编辑
            {
                CheckBox_DHCP.IsChecked = false;
                SetColor(Color.FromRgb(255, 255, 255), true);
            }

            if (IpClass.Use2Ip)
            {
                CheckBox_Enable2IP.IsChecked = true;
                TextBox_IP2.Visibility = Visibility.Visible;
                TextBox_Mask2.Visibility = Visibility.Visible;
                Lable_IP2.Visibility = Visibility.Visible;
                Lable_Mask2.Visibility = Visibility.Visible;
            }
            else
            {
                CheckBox_Enable2IP.IsChecked = false;
                TextBox_IP2.Visibility = Visibility.Hidden;
                TextBox_Mask2.Visibility = Visibility.Hidden;
                Lable_IP2.Visibility = Visibility.Hidden;
                Lable_Mask2.Visibility = Visibility.Hidden;
            }
        }

        private void RunNetshCommand(string command)
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "netsh.exe";
                process.StartInfo.Arguments = command;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("Error: " + error);
                }
                else
                {
                    Console.WriteLine("Output: " + output);
                }
            }
        }

        //读取配置文件 config.cfg 然后生成一个配置方案的下拉集合
        public void ReadConfig()
        {
            IpClass.netConfigDict = new Dictionary<string, NetConfig>();

            if (!File.Exists("config.cfg"))
            {
                //File.Create("config.cfg").Close();    //创建文件. 后改为如果没有不创建,以没有方案文件运行
                return;
            }

            using (StreamReader sr = File.OpenText("config.cfg"))
            {
                IpClass.configfile = sr.ReadToEnd();
            }

            //去掉回车和换行符
            IpClass.configfile = IpClass.configfile.Replace("\n", "").Replace("\r", "");

            //每个方案用|隔开，每个方案的具体地IP用#隔开，用分隔符读取多个方案
            string[] configArray = IpClass.configfile.Split('|');

            foreach (string config in configArray)
            {
                if (config.Length > 0)
                {
                    NetConfig nc = new NetConfig(config);
                    IpClass.netConfigDict.Add(nc.Name, nc);
                    AddMessage($"========== 方案：{nc.Name} ==========");
                    AddMessage($"IP1 地址\t\t{(nc.IP1 == "" ? "无" : nc.IP1)}");
                    AddMessage($"IP1 掩码\t\t{(nc.Mask1 != "" ? nc.Mask1 : "无")}");
                    AddMessage($"网关地址\t{(nc.Gateway != "" ? nc.Gateway : "无")}");
                    AddMessage($"DNS1地址\t{(nc.DNS1 != "" ? nc.DNS1 : "无")}");
                    AddMessage($"DNS2地址\t{(nc.DNS2 != "" ? nc.DNS2 : "无")}");
                    AddMessage($"IP2 地址\t\t{(nc.IP2 != "" ? nc.IP2 : "无")}");
                    AddMessage($"IP2 掩码\t\t{(nc.Mask2 != "" ? nc.Mask2 : "无")}");
                }
            }
            ListBox_FangAn.ItemsSource = IpClass.netConfigDict.Keys;
        }

        public void SaveConfig()
        {
            if (!File.Exists("config.cfg"))
            {
                File.Create("config.cfg").Close();    //创建文件. 后改为如果没有不创建,以没有方案文件运行
            }

            FileStream fs = new FileStream("config.cfg", FileMode.Truncate, FileAccess.Write);
            StreamWriter sw = new StreamWriter(fs);

            foreach (NetConfig config in IpClass.netConfigDict.Values)
            {
                string saveString = config.Writebackfile();
                AddMessage("写入  " + saveString);
                sw.WriteLine(saveString);
            }
            sw.Close();
            AddMessage("已保存配置方案");
        }

        private void DisplayHistoryRecord(string[] record)
        {
            CheckBox_DHCP.IsChecked = false;
            TextBox_IP1.Text = record[1];
            TextBox_Mask1.Text = record[2];
            TextBox_GateWay.Text = record[3];
            TextBox_DNS1.Text = record[4];
            TextBox_DNS2.Text = record[5];
            TextBox_IP2.Text = record[6];
            TextBox_Mask2.Text = record[7];

            // 根据记录中的第二IP启用标记显示或隐藏第二IP的文本框
            if (record[8] == "true")
            {
                TextBox_IP2.Visibility = Visibility.Visible;
                TextBox_Mask2.Visibility = Visibility.Visible;
                Lable_IP2.Visibility = Visibility.Visible;
                Lable_Mask2.Visibility = Visibility.Visible;
                CheckBox_Enable2IP.IsChecked = true;
            }
            else
            {
                TextBox_IP2.Visibility = Visibility.Hidden;
                TextBox_Mask2.Visibility = Visibility.Hidden;
                Lable_IP2.Visibility = Visibility.Hidden;
                Lable_Mask2.Visibility = Visibility.Hidden;
                CheckBox_Enable2IP.IsChecked = false;
            }
            SetColor(Colors.Yellow, true);
        }

        public bool Savelastip()
        {
            if (ComboBox_NetCard.SelectedValue == null || !GetNetWork(ComboBox_NetCard.SelectedValue.ToString()))
            {
                return false;
            }

            IpClass.lastUseDhcp = false;
            IpClass.lastUse2Ip = false;

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                if (!(adapter.Name == ComboBox_NetCard.SelectedValue.ToString()))
                    continue;
                IPInterfaceProperties ip = adapter.GetIPProperties();
                UnicastIPAddressInformationCollection netIpAdds = ip.UnicastAddresses;
                GatewayIPAddressInformationCollection gatewayIpAdds = ip.GatewayAddresses;
                IPAddressCollection dnsServers = ip.DnsAddresses;

                IpClass.lastUseDhcp = ip.GetIPv4Properties().IsDhcpEnabled;

                int index1 = 0;
                Array.Clear(IpClass.lastArray, 0, IpClass.lastArray.Length);
                foreach (UnicastIPAddressInformation ipadd in netIpAdds)
                {
                    if (ipadd.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        index1++;
                        if (index1 == 1)
                        {
                            IpClass.lastUse2Ip = false;
                            IpClass.lastArray[1] = ipadd.Address.ToString();
                            IpClass.lastArray[2] = ipadd.IPv4Mask.ToString();
                            IpClass.lastArray[8] = "false";
                        }
                        if (index1 == 2)
                        {
                            IpClass.lastUse2Ip = true;
                            IpClass.lastArray[6] = ipadd.Address.ToString();
                            IpClass.lastArray[7] = ipadd.IPv4Mask.ToString();
                            IpClass.lastArray[8] = "true";
                        }
                    }
                }

                foreach (GatewayIPAddressInformation gateway in gatewayIpAdds)
                {
                    // 仅保存 IPv4 网关，忽略 IPv6 或空条目
                    if (gateway.Address != null && gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        IpClass.lastArray[3] = gateway.Address.ToString();
                        break; // 记录第一个 IPv4 网关
                    }
                }

                int index2 = 0;
                foreach (IPAddress dns in dnsServers)
                {
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        index2++;
                        if (index2 == 1) IpClass.lastArray[4] = dns.ToString();
                        if (index2 == 2) IpClass.lastArray[5] = dns.ToString();
                    }
                }
            }

            // 保存本次记录到历史记录中
            string[] currentRecord = new string[IpClass.lastArray.Length];
            Array.Copy(IpClass.lastArray, currentRecord, IpClass.lastArray.Length);
            IpClass.HistoryRecords.Add(currentRecord);
            return true;
        }

        // 生成随机MAC地址
        public string CreateNewMacAddress()
        {
            while (true)
            {
                Random random = new Random();
                byte[] macBytes = new byte[6];
                // 生成随机字节数组
                random.NextBytes(macBytes);
                // 将字节数组转换为MAC地址字符串
                string macAddress = BitConverter.ToString(macBytes).Replace("-", "").ToUpper();
                // 检查MAC地址是否合法
                if (CheckMacAddress(macAddress))
                    return macAddress;
            }
        }

        //检查mac地址
        public bool CheckMacAddress(string MAC)
        {
            // 定义正则表达式
            string pattern = @"^(?i)(?!0{12}$)([0-9a-f][048c])(?:[0-9a-f]{2}){5}$";
            Regex regex = new Regex(pattern);
            // 验证 MAC 地址格式
            return regex.IsMatch(MAC);
        }

        // 禁用网卡
        public static bool DisableNetWork(ManagementObject network)
        {
            try
            {
                network.InvokeMethod("Disable", null);
                IpClass.NiceEnable = false;
                return true;
            }
            catch
            {
                IpClass.NiceEnable = true;
                return false;
            }
        }

        // 启用网卡
        public static bool EnableNetWork(ManagementObject network)
        {
            try
            {
                network.InvokeMethod("Enable", null);
                IpClass.NiceEnable = true;
                return true;
            }
            catch
            {
                IpClass.NiceEnable = false;
                return false;
            }
        }

        private bool ConfirmMacChange()
        {
            MessageBoxResult result = MessageBox.Show("并非所有MAC地址都能绑定到网卡，以实际是否生效为准，确认更改网卡MAC地址？", "警告", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            return result == MessageBoxResult.OK;
        }

        private async Task ChangeMacAddressAsync(string newMac)
        {
            //用此方法获取注册表内物理网卡的ID
            string DeviceId = "";
            string netState = "SELECT * From Win32_NetworkAdapter  where PhysicalAdapter=1";
            if (IpClass.ShowVirtualCard)
            {
                netState = "SELECT * From Win32_NetworkAdapter";
            }
            string cardName = ComboBox_NetCard.SelectedValue.ToString();
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(netState);
            ManagementObjectCollection collection = searcher.Get();
            foreach (ManagementObject manage in collection.Cast<ManagementObject>())
            {
                if (manage["NetConnectionID"]?.ToString() == cardName)  //直接用列表名匹配
                {
                    DeviceId = int.Parse(manage["DeviceId"].ToString()).ToString("D4");
                    //MessageBox.Show(DeviceId);
                }
            }
            if (DeviceId == "")
            {
                return;
            }

            //所有网卡物理信息所在位置
            RegistryKey NetaddaptRegistry = Registry.LocalMachine.OpenSubKey("SYSTEM")
                .OpenSubKey("CurrentControlSet")
                .OpenSubKey("Control")
                .OpenSubKey("Class")
                .OpenSubKey("{4D36E972-E325-11CE-BFC1-08002bE10318}")
                .OpenSubKey(DeviceId, true);
            if (string.IsNullOrEmpty(newMac))
            {
                try { NetaddaptRegistry.DeleteValue("NetworkAddress"); } catch (Exception) { }
            }
            else
            {
                NetaddaptRegistry.SetValue("NetworkAddress", newMac);
                AddMessage("随机生产的MAC地址为: " + newMac);
            }
            NetaddaptRegistry.Close();

            MessageBoxResult result = MessageBox.Show("是否直接重启网卡？IP可能会改变，你也可以手动禁用启用网卡以使得更改生效。\n\n确认更改？", "是否立即生效?", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result == MessageBoxResult.OK)
            {
                DisableNetWork(NetWork(cardName));
                EnableNetWork(NetWork(cardName));
                await Task.Delay(12000);     //等待12秒差不多可以重启网卡,并刷新DHCP的IP
            }
        }

        // 网卡状态
        public static bool GetNetWork(string netWorkName)
        {
            string netState = "SELECT * From Win32_NetworkAdapter  where PhysicalAdapter=1";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(netState);
            ManagementObjectCollection collection = searcher.Get();
            foreach (ManagementObject manage in collection.Cast<ManagementObject>())
            {
                if (manage["NetConnectionID"].ToString() == netWorkName)
                {
                    return true;
                }
            }
            return false;
        }

        private void Button_TurnCardOnOff_Click(object sender, RoutedEventArgs e)
        {
            string cardName = ComboBox_NetCard.SelectedValue.ToString();
            IpClass.NicDefaultName = cardName;       //记住上次选择的网卡
            if (Button_TurnCardOnOff.Content.ToString() == "启用")
                if (GetNetWork(cardName))
                {
                    if (EnableNetWork(NetWork(cardName)))
                    { MessageBox.Show("开启网卡成功，请稍后手动刷新IP地址!"); }
                    else
                    { MessageBox.Show("开启网卡失败!"); }
                }
                else { MessageBox.Show("网卡未找到!"); }

            if (Button_TurnCardOnOff.Content.ToString() == "停用")
                if (GetNetWork(cardName))
                {
                    if (DisableNetWork(NetWork(cardName)))
                    { MessageBox.Show("禁用网卡成功!"); }
                    else
                    { MessageBox.Show("禁用网卡失败!"); }
                }
                else
                { MessageBox.Show("网卡未找到!"); }
            Thread.Sleep(1000);
            SelectNetCard();
            Thread.Sleep(1000);
            ChangeUI();
        }

        private void Button_ChangeCardName_Click(object sender, RoutedEventArgs e)
        {
            string ChangeNameCommand = $"interface set interface name=\"{IpClass.NicName}\" newname=\"{ComboBox_NetCard.Text}\"";
            AddMessage("netsh " + ChangeNameCommand);
            RunNetshCommand(ChangeNameCommand);
            ListNetWork();
        }

        private void Button_FindRouting_Click(object sender, RoutedEventArgs e)
        {
            //显示当前路由表
            AddMessage("目的地址  \t掩码\t\t下一跳\t\t接口\t代价");
            ManagementClass isrouteClass = new ManagementClass("Win32_IP4RouteTable");
            ManagementObjectCollection routeColl = isrouteClass.GetInstances();
            foreach (ManagementObject mor in routeColl.Cast<ManagementObject>())
            {
                string routemessage = mor["Destination"] + "\t";
                if (mor["Destination"].ToString().Length < 9)
                    routemessage += "\t";

                routemessage += mor["Mask"] + "\t";
                if (mor["Mask"].ToString().Length < 9)
                    routemessage += "\t";

                routemessage += mor["NextHop"] + "\t";
                if (mor["NextHop"].ToString().Length < 9)
                    routemessage += "\t";

                routemessage += mor["InterfaceIndex"] + "\t" + mor["Metric1"];
                AddMessage(routemessage);
            }
            AddMessage("-------------------------------------------------------");
        }

        private async void Botton_RandomMAC_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmMacChange())
            {
                await ChangeMacAddressAsync(CreateNewMacAddress());
                SelectNetCard();
                ChangeUI();
            }
        }

        private async void Botton_ManualMAC_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckMacAddress(TextBox_MAC.Text))
            {
                MessageBox.Show("输入的MAC地址不合法，本次更改无效");
                return;
            }
            if (ConfirmMacChange())
            {
                await ChangeMacAddressAsync(TextBox_MAC.Text);
                SelectNetCard();
                ChangeUI();
            }
        }

        private async void Botton_DefaultMAC_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmMacChange())
            {
                await ChangeMacAddressAsync("");
                SelectNetCard();
                ChangeUI();
            }
        }

        private void Botton_ManualMTU_Click(object sender, RoutedEventArgs e)
        {
            // 尝试将文本转换为整数
            if (int.TryParse(TextBox_MTU.Text, out int mtuValue))
            {
                // 检查 MTU 值是否在 64 到 9600 之间
                if (mtuValue >= 64 && mtuValue <= 9600)
                {
                    // 设置 IPv4 MTU
                    string ipv4Command = $"interface ipv4 set subinterface \"{IpClass.NicName}\" mtu={mtuValue} store=persistent";
                    AddMessage("netsh " + ipv4Command);
                    RunNetshCommand(ipv4Command);

                    // 设置 IPv6 MTU
                    string ipv6Command = $"interface ipv6 set subinterface \"{IpClass.NicName}\" mtu={mtuValue} store=persistent";
                    AddMessage("netsh " + ipv6Command);
                    RunNetshCommand(ipv6Command);
                    SelectNetCard();
                    ChangeUI();
                }
                else
                {
                    MessageBox.Show("MTU 值必须在 64 到 9600 之间。");
                }
            }
            else
            {
                MessageBox.Show("请输入一个有效的整数作为 MTU 值。");
            }
        }

        private void Botton_DefaultMTU_Click(object sender, RoutedEventArgs e)
        {
            string ipv4Command = $"interface ipv4 set subinterface \"{IpClass.NicName}\" mtu=1500 store=persistent";
            AddMessage("netsh " + ipv4Command);
            RunNetshCommand(ipv4Command);

            // 设置 IPv6 MTU
            string ipv6Command = $"interface ipv6 set subinterface \"{IpClass.NicName}\" mtu=1500 store=persistent";
            AddMessage("netsh " + ipv6Command);
            RunNetshCommand(ipv6Command);
            SelectNetCard();
            ChangeUI();
        }

        private void Botton_Cping_Click(object sender, RoutedEventArgs e)
        {
            CPing cpingWindow = new CPing
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            cpingWindow.TextBoxCping.Text = TextBox_IP1.Text;
            cpingWindow.ShowDialog();
        }

        private void CheckBox_VirtualCard_Checked(object sender, RoutedEventArgs e)
        {
            IpClass.ShowVirtualCard = true;
            ListNetWork();
        }

        private void CheckBox_VirtualCard_Unchecked(object sender, RoutedEventArgs e)
        {
            IpClass.ShowVirtualCard = false;
            ListNetWork();
        }

        private void CheckBox_DHCP_Checked(object sender, RoutedEventArgs e)
        {
            IpClass.UseDhcp = true;
            ChangeUI();
        }

        private void CheckBox_DHCP_Unchecked(object sender, RoutedEventArgs e)
        {
            IpClass.UseDhcp = false;
            ChangeUI();
        }

        private void CheckBox_Enable2IP_Checked(object sender, RoutedEventArgs e)
        {
            IpClass.Use2Ip = true;
            ChangeUI();
        }

        private void CheckBox_Enable2IP_Unchecked(object sender, RoutedEventArgs e)
        {
            IpClass.Use2Ip = false;
            ChangeUI();
        }

        private void Botton_RefreshConfig_Click(object sender, RoutedEventArgs e)
        {
            SelectNetCard();
            ChangeUI();
            if (IpClass.HistoryRecords.Count > 0)
            {
                IpClass.HistoryCurrentIndex = IpClass.HistoryRecords.Count - 1; //手动刷新时候更改历史为最新
                AddMessage("历史记录已切到最新，序号: " + IpClass.HistoryCurrentIndex);
            }
        }

        private void Botton_ApplyConfig_Click(object sender, RoutedEventArgs e)
        {
            SetNetworkAdapter();
        }

        private void Botton_PreventIP_Click(object sender, RoutedEventArgs e)
        {
            if (IpClass.HistoryRecords.Count == 0)
            {
                MessageBox.Show("还没有历史记录");
                return;
            }
            if (IpClass.HistoryCurrentIndex > 0)
            {
                IpClass.HistoryCurrentIndex--;
                DisplayHistoryRecord(IpClass.HistoryRecords[IpClass.HistoryCurrentIndex]);
                AddMessage("显示前一条历史记录，序号: " + IpClass.HistoryCurrentIndex);
            }
            else
            {
                DisplayHistoryRecord(IpClass.HistoryRecords[0]);
                AddMessage("显示前一条历史记录，序号: " + IpClass.HistoryCurrentIndex);
                MessageBox.Show("没有更多历史记录了");
            }
        }

        private void Botton_NextIP_Click(object sender, RoutedEventArgs e)
        {
            if (IpClass.HistoryRecords.Count == 0)
            {
                MessageBox.Show("还没有历史记录");
                return;
            }
            if (IpClass.HistoryCurrentIndex < IpClass.HistoryRecords.Count - 1)
            {
                IpClass.HistoryCurrentIndex++;
                DisplayHistoryRecord(IpClass.HistoryRecords[IpClass.HistoryCurrentIndex]);
                AddMessage("显示后一条历史记录，序号: " + IpClass.HistoryCurrentIndex);
            }
            else
            {
                DisplayHistoryRecord(IpClass.HistoryRecords[IpClass.HistoryCurrentIndex]);
                AddMessage("已是最近的历史记录，序号: " + IpClass.HistoryCurrentIndex);
                MessageBox.Show("已是最近的历史记录，要显示当前IP，请点击刷新网卡信息");
            }
        }

        // Add a private field to store the last selected net card name
        private string _lastNetCardSelection = string.Empty;

        // Modify the ComboBox_NetCard_SelectionChanged event handler
        private void ComboBox_NetCard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string newSelection = ComboBox_NetCard.SelectedItem as string;
            if (string.IsNullOrEmpty(newSelection) || newSelection == _lastNetCardSelection)
                return;

            _lastNetCardSelection = newSelection;
            SelectNetCard();
            ChangeUI();
        }

        private void ListBox_FangAn_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            NetConfig config = IpClass.netConfigDict[name];
            AddMessage($"========== 方案：{name} ==========");
            if (!string.IsNullOrEmpty(config.IP1)) AddMessage("IP1 地址\t\t" + config.IP1);
            if (!string.IsNullOrEmpty(config.Mask1)) AddMessage("IP1 掩码\t\t" + config.Mask1);
            if (!string.IsNullOrEmpty(config.Gateway)) AddMessage("网关地址\t" + config.Gateway);
            if (!string.IsNullOrEmpty(config.DNS1)) AddMessage("DNS1地址\t" + config.DNS1);
            if (!string.IsNullOrEmpty(config.DNS2)) AddMessage("DNS2地址\t" + config.DNS2);
            if (!string.IsNullOrEmpty(config.IP2)) AddMessage("IP2 地址\t\t" + config.IP2);
            if (!string.IsNullOrEmpty(config.Mask2)) AddMessage("IP2 掩码\t\t" + config.Mask2);
        }

        private void Botton_SaveFangAn_Click(object sender, RoutedEventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            MessageBoxResult AF = MessageBox.Show("确定用当前界面的IP覆盖选中的配置方案?\n\n你可以鼠标右键方案栏新建一个全新方案", "已有方案覆盖确认", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (AF == MessageBoxResult.OK)
            {
                NetConfig config = IpClass.netConfigDict[name];
                config.IP1 = TextBox_IP1.Text;
                config.Mask1 = TextBox_Mask1.Text;
                config.Gateway = TextBox_GateWay.Text;
                config.DNS1 = TextBox_DNS1.Text;
                config.DNS2 = TextBox_DNS2.Text;
                config.IP2 = TextBox_IP2.Text;
                config.Mask2 = TextBox_Mask2.Text;
                SaveConfig();
            }
            else
            {
                ListBox_FangAn.UnselectAll();
                return;
            }
        }

        private void FanAn_MenuItem_Apply_Click(object sender, EventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            NetConfig config = IpClass.netConfigDict[name];
            AddMessage("已选择" + config.Name + "\r\n");
            CheckBox_DHCP.IsChecked = false;
            TextBox_IP1.Text = config.IP1;
            TextBox_Mask1.Text = config.Mask1;
            TextBox_GateWay.Text = config.Gateway;
            TextBox_DNS1.Text = config.DNS1;
            TextBox_DNS2.Text = config.DNS2;
            if (config.IP2 != "" && config.Mask2 != "")
                CheckBox_Enable2IP.IsChecked = true;
            else
                CheckBox_Enable2IP.IsChecked = false;
            TextBox_IP2.Text = config.IP2;
            TextBox_Mask2.Text = config.Mask2;

            SetNetworkAdapter();
        }

        private void FanAn_MenuItem_Edit_Click(object sender, EventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            NetConfig existingScheme = IpClass.netConfigDict[name];
            EditSchemeWindow editWindow = new EditSchemeWindow(existingScheme);
            if (editWindow.ShowDialog() == true)
            {
                NetConfig updatedScheme = editWindow.Scheme;
                IpClass.netConfigDict[name] = updatedScheme;
                SaveConfig();
                ReadConfig();
            }
        }

        private void FanAn_MenuItem_Refer_Click(object sender, EventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            NetConfig config = IpClass.netConfigDict[name];
            CheckBox_DHCP.IsChecked = false;
            TextBox_IP1.Text = config.IP1;
            TextBox_Mask1.Text = config.Mask1;
            TextBox_GateWay.Text = config.Gateway;
            TextBox_DNS1.Text = config.DNS1;
            TextBox_DNS2.Text = config.DNS2;
            TextBox_IP2.Text = config.IP2;
            TextBox_Mask2.Text = config.Mask2;
            if (config.IP2 != "" && config.Mask2 != "")
                CheckBox_Enable2IP.IsChecked = true;
            else
                CheckBox_Enable2IP.IsChecked = false;
            SetColor(Colors.Yellow, true);  //要写在后面，因为CheckBox_DHCP.IsChecked 和 CheckBox_Enable2IP 会触发ChangeUI()
        }

        private void FanAn_MenuItem_New_Click(object sender, EventArgs e)
        {
            // 将当前主窗口显示的方案数据封装到NetConfig对象中
            var defaultConfig = new NetConfig(string.Empty)
            {
                Name = "时间点:" + DateTime.Now.ToString(),
                IP1 = TextBox_IP1.Text,
                Mask1 = TextBox_Mask1.Text,
                Gateway = TextBox_GateWay.Text,
                DNS1 = TextBox_DNS1.Text,
                DNS2 = TextBox_DNS2.Text,
                IP2 = TextBox_IP2.Text,
                Mask2 = TextBox_Mask2.Text
            };

            // 把当前数据传递给编辑窗口
            EditSchemeWindow editWindow = new EditSchemeWindow(defaultConfig);
            if (editWindow.ShowDialog() == true)
            {
                NetConfig newScheme = editWindow.Scheme;
                IpClass.netConfigDict.Add(newScheme.Name, newScheme);
                SaveConfig();
                ReadConfig();
            }
        }

        private void FanAn_MenuItem_Delete_Click(object sender, EventArgs e)
        {
            if (ListBox_FangAn.SelectedItem == null)
                return;
            string name = ListBox_FangAn.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name))
                return;
            if (!IpClass.netConfigDict.ContainsKey(name))
                return;

            IpClass.netConfigDict.Remove(name);
            SaveConfig();
            ReadConfig();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 动态调整窗口高度以保持宽高比,故不需要定义窗口的高度,会用界面元素的高度填充
            double newHeight = e.NewSize.Width / aspectRatio;
            this.Height = newHeight;

            // 获取窗口的外部尺寸
            double windowWidth = this.Width;
            double windowHeight = this.Height;

            // 获取窗口的客户区尺寸
            double viewWidth = MainViewbox.ActualWidth;
            double viewtHeight = MainViewbox.ActualHeight;

            // 获取窗口的客户区尺寸
            double gridWidth = MainGrid.ActualWidth;
            double gridHeight = MainGrid.ActualHeight;

            // 输出尺寸信息
            AddMessage($"程序宽:{windowWidth}, 高:{windowHeight}; 界面宽:{viewWidth}, 高:{viewtHeight}; 网格宽:{gridWidth}, 高:{gridHeight}");
        }
    }
}