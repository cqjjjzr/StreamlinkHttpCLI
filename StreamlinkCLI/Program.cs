using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using IniParser;

namespace StreamlinkCLI
{
    public class Program
    {
        private static readonly Socket Listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: StreamlinkHttpCLI <config_file>");
                return;
            }

            string channelUrl;
            string proxy;
            string streamlink;
            var port = 15551;
            try
            {
                var ini = new FileIniDataParser().ReadFile(args[0]);
                var sec = ini.Sections["streamlink"];
                streamlink = sec.GetKeyData("streamlink").Value;
                proxy = sec.GetKeyData("proxy").Value;
                channelUrl = sec.GetKeyData("url").Value;
                if (sec.ContainsKey("port"))
                    if (!int.TryParse(sec.GetKeyData("port").Value, out port))
                        Console.WriteLine("配置文件解析错误：端口号无法解析，将使用默认端口号" + port);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }
            
            try
            {
                Listener.Bind(new IPEndPoint(IPAddress.Any, port));
                Listener.Listen(10);

                Console.WriteLine($"[{DateTime.Now}] 服务器启动于端口{port}.");
                // Start listening for connections.  
                while (true)
                {
                    // Program is suspended while waiting for an incoming connection.  
                    var handler = Listener.Accept();
                    new Thread(() =>
                    {
                        var sid = new Random().Next(0, 10000);
                        Console.WriteLine($"[{DateTime.Now}] [{sid}] 检测到客户端连接！通过Streamlink连接频道{channelUrl}，使用代理{proxy}...");

                        var recv = new byte[1024];
                        handler.Receive(recv);
                        Process proc = null;
                        try
                        {
                            handler.Send(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nConnection: Closed\r\n\r\n"));

                            var info = new ProcessStartInfo(streamlink,
                                $"-O {channelUrl} best " +
                                "--hls-timeout 1200 --stream-timeout 1200 --retry-streams 5 --retry-max 100000 --retry-open 500" +
                                " --hls-segment-attempts 10 --hls-segment-timeout 60 --hls-timeout 600  --hls-segment-threads 3 --hls-live-edge 7 --hls-playlist-reload-attempts 10 " +
                                $"--http-proxy={proxy} --https-proxy={proxy}")
                            {
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,
                                UseShellExecute = false
                            };
                            proc = Process.Start(info);
                            Debug.Assert(proc != null, nameof(proc) + " != null");
                            proc.BeginErrorReadLine();
                            proc.ErrorDataReceived += (o, a) =>
                            {
                                Console.WriteLine($"[{DateTime.Now}] [{sid}] {a.Data}");
                            };
                            var stream = proc.StandardOutput.BaseStream;
                            while (!proc.HasExited && handler.Connected)
                            {
                                var recvLen = stream.Read(recv, 0, 1024);
                                if (recvLen < 0) break;
                                handler.Send(recv, recvLen, SocketFlags.None);
                            }
                            Console.WriteLine($"[{DateTime.Now}] [{sid}] Streamlink退出，连接关闭");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{DateTime.Now}] [{sid}] 转播发生异常，连接关闭！\r\n{ex}");
                        }
                        finally
                        {
                            handler.Close();
                            if (proc != null && !proc.HasExited) proc.Kill();
                        }
                        
                    }).Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] 服务器监听线程出现异常！程序退出.\r\n{ex}");
            }
        }
    }
}
