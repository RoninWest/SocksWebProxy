using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using Org.Mentalis.Network.ProxySocket;

namespace com.LandonKey.SocksWebProxy.Proxy
{
	public interface IProxyConfig
	{
		int HttpPort { get; }
		string HttpAddress { get; }
		int SocksPort { get; }
		string SocksAddress { get; }
		SocksVersion Version { get; }
		string Username { get; }
		string Password { get; }
		//ProxyTypes ProxyType { get; set; }
		string TorPath { get; }
	}
}
