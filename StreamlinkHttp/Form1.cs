using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
// ReSharper disable LocalizableElement

namespace StreamlinkHttp
{
    public partial class Form1 : Form
    {
        public const string ServerUrl = "http://localhost:15551/";
        private readonly Socket _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            /*if (!File.Exists("streamlink.exe"))
            {
                MessageBox.Show("未找到streamlink！请先安装streamlink.", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }*/
            new Thread(() =>
            {
                try
                {
                    _listener.Bind(new IPEndPoint(IPAddress.Any, 15551));
                    _listener.Listen(10);

                    // Start listening for connections.  
                    while (true)
                    {
                        Invoke(new Inv(() => txtLog.Text += $"[{DateTime.Now}] 服务器启动.\r\n"));
                        Console.WriteLine("Waiting for a connection...");
                        // Program is suspended while waiting for an incoming connection.  
                        var handler = _listener.Accept();
                        new Thread(() =>
                        {
                            var sid = new Random().Next(0, 10000);
                            var channelUrl = txtChannel.Text;
                            var proxy = txtProxyHost.Text;
                            var proxyPort = txtProxyPort.Text;
                            Invoke(new Inv(() => txtLog.Text += $"[{DateTime.Now}] [{sid}] 检测到客户端连接！通过Streamlink连接频道{channelUrl}，使用代理{proxy}:{proxyPort}...\r\n"));
                            
                            var recv = new byte[1024];
                            handler.Receive(recv);

                            handler.Send(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nConnection: Closed\r\n\r\n"));

                            var info = new ProcessStartInfo(txtStreamlink.Text, $"-O {channelUrl}  best --http-proxy=http://{proxy}:{proxyPort} --https-proxy=http://{proxy}:{proxyPort}");
                            info.RedirectStandardError = true;
                            info.RedirectStandardOutput = true;
                            info.UseShellExecute = false;
                            var proc = Process.Start(info);
                            proc.BeginErrorReadLine();
                            proc.ErrorDataReceived += (o, args) =>
                            {
                                Invoke(new Inv(() => txtLog.Text += $"[{DateTime.Now}] [{sid}] {args.Data}\n"));
                            };
                            var stream = proc.StandardOutput.BaseStream;
                            while (!proc.HasExited && handler.Connected)
                            {
                                var recvLen = stream.Read(recv, 0, 1024);
                                if (recvLen < 0) break;
                                handler.Send(recv, recvLen, SocketFlags.None);
                            }
                            handler.Close();
                        }).Start();
                    }
                }
                catch (Exception ex)
                {
                    Invoke(new Inv(() =>
                    {
                        txtLog.Text += $"[{DateTime.Now}] 服务器监听线程出现异常！\r\n" + ex.ToString() + "\r\n";
                        MessageBox.Show("服务器掉线！将尝试重新连接", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                    _listener.Bind(new IPEndPoint(IPAddress.Any, 15551));
                    _listener.Listen(10);
                }
            }).Start();
        }

        public delegate void Inv();

        private void txtChannel_TextChanged(object sender, EventArgs e)
        {
        }

        private void txtProxyHost_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtProxyPort_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtStreamlink_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
