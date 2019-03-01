using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using IniParser;

namespace StreamlinkHttpCLI
{
    public class Program
    {
        private static readonly Socket Listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        public static void Main(string[] args)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Console.Title = "Streamlink Http CLI v" + version + " by Charlie Jiang";
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: StreamlinkHttpCLI <config_file>");
                return;
            }

            string liveUrl;
            string proxy = null;
            string streamlink;
            var port = 15551;

            string channelUrl = null;
            try
            {
                var ini = new FileIniDataParser().ReadFile(args[0]);
                var sec = ini.Sections["streamlink"];
                if (!sec.ContainsKey("streamlink"))
                    WriteLineWithColor("配置文件解析错误：未定义streamlink位置", ConsoleColor.Red);
                streamlink = sec["streamlink"];
                if (sec.ContainsKey("proxy"))
                    proxy = sec.GetKeyData("proxy").Value;

                liveUrl = sec["url"];

                if (sec.ContainsKey("port"))
                    if (!int.TryParse(sec.GetKeyData("port").Value, out port))
                        WriteLineWithColor("配置文件解析错误：端口号无法解析，将使用默认端口号" + port, ConsoleColor.Yellow);

                if (ini.Sections.ContainsSection("youtube"))
                {
                    var sec2 = ini.Sections["youtube"];
                    channelUrl = sec2["channel"];
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }

            if (!File.Exists(streamlink))
            {
                WriteLineWithColor("初始化失败：Streamlink未在指定路径中找到！", ConsoleColor.Red);
                return;
            }

            try
            {
                if (channelUrl != null)
                {
                    Console.WriteLine($"已指定YouTube频道地址{channelUrl}，尝试获取直播间地址...");
                    if (channelUrl.EndsWith("/") || !channelUrl.Contains("/"))
                    {
                        WriteLineWithColor($"无效地址{channelUrl}，正确的格式：https://www.youtube.com/channel/UCIaC5td9nGG6JeKllWLwFLA", ConsoleColor.Red);
                        return;
                    }
                    var id = channelUrl.Substring(channelUrl.LastIndexOf('/') + 1);
                    liveUrl = FetchYouTubeLiveUrl(id, proxy);
                    if (!string.IsNullOrWhiteSpace(liveUrl))
                        WriteLineWithColor($"获取到直播间地址：{liveUrl}", ConsoleColor.Green);
                    else
                    {
                        WriteLineWithColor("获取失败！", ConsoleColor.Red);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                WriteLineWithColor("初始化失败：获取YouTube直播间地址失败！" + e, ConsoleColor.Red);
                return;
            }
            
            try
            {
                Listener.Bind(new IPEndPoint(IPAddress.Any, port));
                Listener.Listen(10);

                WriteLineWithColor($"[{DateTime.Now}] 服务器启动于端口{port}.", ConsoleColor.Green);
                // Start listening for connections.  
                while (true)
                {
                    // Program is suspended while waiting for an incoming connection.  
                    var handler = Listener.Accept();
                    new Thread(() =>
                    {
                        var sid = new Random().Next(0, 10000);
                        WriteLineWithColor($"[{DateTime.Now}] [{sid}] 检测到客户端连接！通过Streamlink连接频道{liveUrl}，使用代理{proxy}...", ConsoleColor.Blue);

                        var recv = new byte[1024];
                        handler.Receive(recv);
                        Process proc = null;
                        try
                        {
                            handler.Send(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nConnection: Closed\r\n\r\n"));

                            var arg = $"-O {liveUrl} best " +
                                      "--hls-timeout 1200 --stream-timeout 1200 --retry-streams 5 --retry-max 100000 --retry-open 500" +
                                      " --hls-segment-attempts 10 --hls-segment-timeout 60 --hls-timeout 600  --hls-segment-threads 3 --hls-live-edge 7 --hls-playlist-reload-attempts 10";
                            if (!string.IsNullOrWhiteSpace(proxy))
                            {
                                arg += $" --http-proxy={proxy} --https-proxy={proxy}";
                            }
                            var info = new ProcessStartInfo(streamlink, arg)
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
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now}] 服务器监听线程出现异常！程序退出.\r\n{ex}");
                Console.BackgroundColor = ConsoleColor.Black;
            }
        }

        private static readonly Regex YouTubeUrlRegex = new Regex("<link rel=\"canonical\" href=\"(.+)\">");
        public static string FetchYouTubeLiveUrl(string id, string proxy)
        {
            var url = "https://www.youtube.com/embed/live_stream?channel=" + id;
            var req = (HttpWebRequest) WebRequest.Create(url);
            if (proxy != null)
                req.Proxy = new WebProxy(new Uri(proxy));
            req.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
            using (var resp = req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("无法获取响应流")))
                {
                    var html = reader.ReadToEnd();
                    var matches = YouTubeUrlRegex.Matches(html);
                    if (matches.Count == 0) throw new Exception("未找到有效的直播间地址，HTML数据：" + html + "\r\n\r\n");
                    var liveUrl = matches[0].Groups[1].Value;
                    return liveUrl;
                }
        }

        public static void WriteLineWithColor(string str, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(str);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
