using com.LandonKey.SocksWebProxy;
using com.LandonKey.SocksWebProxy.Proxy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    class Program
    {
		static void Main(string[] args)
        {
			try
			{
				using (var tp = new TorProcess())
				{
					tp.Start(
						behavior: TorProcess.StartBehavior.ReturnExisting,
						windowStyle: System.Diagnostics.ProcessWindowStyle.Normal);
					if (tp.InitWait(
						retrySleep: TimeSpan.FromSeconds(3),
						maxWait: TimeSpan.FromMinutes(2)))
					{
						string html = null;
						const string url = "https://check.torproject.org/";
						using (var client = new WebClient())
						{
							client.Proxy = new SocksWebProxy();
							html = client.DownloadString(url);
						}
						var doc = new HtmlAgilityPack.HtmlDocument();
						doc.LoadHtml(html);
						var nodes = doc.DocumentNode.SelectNodes("//p/strong");
						IPAddress ip;
						foreach (var node in nodes)
						{
							if (IPAddress.TryParse(node.InnerText, out ip))
							{
								Console.WriteLine(":::::::::::::::::::::");
								if (html.Contains("Congratulations. This browser is configured to use Tor."))
									Console.WriteLine("Connected through Tor with IP: " + ip.ToString());
								else
									Console.Write("Not connected through Tor with IP: " + ip.ToString());
								Console.WriteLine(":::::::::::::::::::::");
								return;
							}
						}
						Console.WriteLine(":::::::::::::::::::::");
						Console.Write("IP not found");
						Console.WriteLine(":::::::::::::::::::::");
					}
					else
						Console.WriteLine("Can not confirm if tor is running");
				}
			}
			catch(Exception ex)
			{
				Console.Error.WriteLine(ex.Message);
			}
			finally
			{
#if DEBUG
				Console.WriteLine("Press enter to continue...");
				Console.ReadLine();				
#endif
			}
		}

    }
}
