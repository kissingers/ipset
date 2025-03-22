using myipset;
using System;
using System.Net;
using System.Windows;

namespace ipset
{
    public partial class EditSchemeWindow : Window
    {
        public NetConfig Scheme { get; private set; }

        public EditSchemeWindow(NetConfig scheme = null)
        {
            InitializeComponent();
            if (scheme != null)
            {
                // 编辑已有方案时传入数据
                Scheme = scheme;
                TextBox_SchemeName.Text = scheme.Name;
                TextBox_IP1.Text = scheme.IP1;
                TextBox_Mask1.Text = scheme.Mask1;
                TextBox_Gateway.Text = scheme.Gateway;
                TextBox_DNS1.Text = scheme.DNS1;
                TextBox_DNS2.Text = scheme.DNS2;

                // 根据第二IP是否有值设置显示内容
                if (!string.IsNullOrEmpty(scheme.IP2) || !string.IsNullOrEmpty(scheme.Mask2))
                {
                    CheckBox_Enable2IP.IsChecked = true;
                    Panel_SecondIP.Visibility = Visibility.Visible;
                    TextBox_IP2.Text = scheme.IP2;
                    TextBox_Mask2.Text = scheme.Mask2;
                }
                else
                {
                    CheckBox_Enable2IP.IsChecked = false;
                    Panel_SecondIP.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // 新建方案时默认传入主窗口当前显示的内容
                TextBox_SchemeName.Text = "方案:" + DateTime.Now.ToString();
                // 其他文本框默认留空，由主窗口传入初始值时可覆盖
            }
        }

        private void CheckBox_Enable2IP_Checked(object sender, RoutedEventArgs e)
        {
            Panel_SecondIP.Visibility = Visibility.Visible;
        }

        private void CheckBox_Enable2IP_Unchecked(object sender, RoutedEventArgs e)
        {
            Panel_SecondIP.Visibility = Visibility.Collapsed;
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            // 先校验输入合法性
            if (!Checkinput())
            {
                return;
            }

            // 根据是否启用第二IP保存对应的字段
            Scheme = new NetConfig(string.Empty)
            {
                Name = TextBox_SchemeName.Text,
                IP1 = TextBox_IP1.Text,
                Mask1 = TextBox_Mask1.Text,
                Gateway = TextBox_Gateway.Text,
                DNS1 = TextBox_DNS1.Text,
                DNS2 = TextBox_DNS2.Text,
                IP2 = CheckBox_Enable2IP.IsChecked == true ? TextBox_IP2.Text : "",
                Mask2 = CheckBox_Enable2IP.IsChecked == true ? TextBox_Mask2.Text : ""
            };
            DialogResult = true;
            Close();
        }

        private void Button_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // 检查输入合法性，包括IP、掩码、网关、DNS和第二IP校验
        private bool Checkinput()
        {
            // 验证IP1
            if (!CheckIP(TextBox_IP1.Text))
            {
                MessageBox.Show("无效的IP地址 (IP1): " + TextBox_IP1.Text);
                return false;
            }

            // 验证子网掩码1
            if (!CheckMask(TextBox_Mask1.Text))
            {
                MessageBox.Show("无效的网络掩码 (Mask1): " + TextBox_Mask1.Text);
                return false;
            }

            // 验证网关 (允许为空)
            if (string.IsNullOrWhiteSpace(TextBox_Gateway.Text))
            {
                // 空网关设置为空
            }
            else if (!CheckIP(TextBox_Gateway.Text))
            {
                MessageBox.Show("无效的网关IP地址: " + TextBox_Gateway.Text);
                return false;
            }

            // 验证DNS1 (允许为空)
            if (string.IsNullOrWhiteSpace(TextBox_DNS1.Text))
            {
                // DNS1为空
            }
            else if (!CheckIP(TextBox_DNS1.Text))
            {
                MessageBox.Show("无效的DNS地址 (DNS1): " + TextBox_DNS1.Text);
                return false;
            }

            // 验证DNS2 (允许为空，但DNS2非空时，DNS1也必须非空)
            if (string.IsNullOrWhiteSpace(TextBox_DNS2.Text))
            {
                // DNS2为空
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TextBox_DNS1.Text))
                {
                    MessageBox.Show("设置了第二DNS地址，首选DNS地址必须不为空！");
                    return false;
                }
                if (!CheckIP(TextBox_DNS2.Text))
                {
                    MessageBox.Show("无效的第二DNS地址 (DNS2): " + TextBox_DNS2.Text);
                    return false;
                }
            }

            // 验证第二IP和子网掩码（如果启用）
            if (CheckBox_Enable2IP.IsChecked == true)
            {
                if (!CheckIP(TextBox_IP2.Text))
                {
                    MessageBox.Show("无效的第二IP地址: " + TextBox_IP2.Text);
                    return false;
                }
                if (!CheckMask(TextBox_Mask2.Text))
                {
                    MessageBox.Show("无效的第二网络掩码: " + TextBox_Mask2.Text);
                    return false;
                }
            }

            // 检查网关是否与IP在同一网络段（只在网关不为空时验证）
            if (!string.IsNullOrWhiteSpace(TextBox_Gateway.Text))
            {
                bool validSubnet = false;
                // 校验IP1与网关是否同网段
                string ip1Segment = GetNetSegment(TextBox_IP1.Text, TextBox_Mask1.Text);
                string gwSegment1 = GetNetSegment(TextBox_Gateway.Text, TextBox_Mask1.Text);
                if (ip1Segment == gwSegment1)
                {
                    validSubnet = true;
                }
                // 如果启用了第二IP且IP1校验未通过，则尝试使用IP2与网关
                else if (CheckBox_Enable2IP.IsChecked == true && !string.IsNullOrWhiteSpace(TextBox_IP2.Text) && !string.IsNullOrWhiteSpace(TextBox_Mask2.Text))
                {
                    string ip2Segment = GetNetSegment(TextBox_IP2.Text, TextBox_Mask2.Text);
                    string gwSegment2 = GetNetSegment(TextBox_Gateway.Text, TextBox_Mask2.Text);
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

        // 验证IP地址格式，仅接受IPv4
        private bool CheckIP(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;
            IPAddress address;
            if (!IPAddress.TryParse(ip, out address))
                return false;
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;
            return true;
        }

        // 验证子网掩码正确性：要求连续1之后全为0
        private bool CheckMask(string mask)
        {
            if (string.IsNullOrWhiteSpace(mask))
                return false;
            IPAddress subnet;
            if (!IPAddress.TryParse(mask, out subnet))
                return false;
            if (subnet.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;
            byte[] bytes = subnet.GetAddressBytes();
            uint maskValue = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | (uint)bytes[3];
            if (maskValue == 0)
                return false;
            bool foundZero = false;
            for (int i = 31; i >= 0; i--)
            {
                if ((maskValue & (1u << i)) != 0)
                {
                    if (foundZero)
                        return false;
                }
                else
                {
                    foundZero = true;
                }
            }
            return true;
        }

        // 根据IP地址与子网掩码计算网络段
        private string GetNetSegment(string ipAddress, string subnetMask)
        {
            byte[] ip = IPAddress.Parse(ipAddress).GetAddressBytes();
            byte[] sub = IPAddress.Parse(subnetMask).GetAddressBytes();
            for (int i = 0; i < ip.Length; i++)
            {
                ip[i] = (byte)(ip[i] & sub[i]);
            }
            return new IPAddress(ip).ToString();
        }
    }
}
