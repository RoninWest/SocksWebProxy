using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Diagnostics;

namespace com.LandonKey.SocksWebProxy
{
	/// <summary>
	/// Wrapper to start or check on a Tor process
	/// </summary>
	public class TorProcess : IDisposable
	{
		readonly FileInfo _torPath;
		/// <summary>
		/// Create a tor process using path to .exe
		/// </summary>
		/// <param name="path">Path to tor browser (firefox.exe)</param>
		public TorProcess(FileInfo path)
		{
			if (path == null)
				throw new ArgumentNullException("path");
			if (!path.Exists)
				throw new FileNotFoundException(path.FullName);

			_torPath = path;
		}
		/// <summary>
		/// Crete a tor process using <see cref="Proxy.ProxyConfig.TorPath"/>
		/// </summary>
		public TorProcess() 
			: this(Proxy.ProxyConfig.Settings.TorPath)
		{
		}
		/// <summary>
		/// Create a tor process using either the root directory or path to .exe
		/// </summary>
		/// <param name="path">Root installation directory or path to .exe</param>
		public TorProcess(string path)
			: this(GetExe(path))
		{
		}
		/// <summary>
		/// Create a tor process using root installation folder
		/// </summary>
		/// <param name="path">Path to the root of tor installation</param>
		public TorProcess(DirectoryInfo path)
			: this(GetExe(path))
		{
		}

		#region CTOR helpers

		static FileInfo GetExe(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("path is missing or null");
			if (File.Exists(path))
				return new FileInfo(path);
			else if (Directory.Exists(path))
				return GetExe(new DirectoryInfo(path));
			else
				return null;
		}

		static FileInfo GetExe(DirectoryInfo path)
		{
			if (path == null)
				throw new ArgumentNullException("path");
			if (!path.Exists)
				throw new DirectoryNotFoundException(path.FullName);

			string f = Path.Combine(path.FullName, @"Browser\firefox.exe");
			if (File.Exists(f))
				return new FileInfo(f);
			else
				throw new FileNotFoundException(f);
		}

		#endregion

		~TorProcess() { Dispose(); }
		int _disposing = 0;
		/// <summary>
		/// Cleanup and delete existing process if any
		/// </summary>
		public void Dispose()
		{
			lock(_padlock)
			{
				if (_tor != null && Interlocked.CompareExchange(ref _disposing, 1, 0) == 0)
				{
					try
					{
						_tor.Kill();
					}
					catch(Exception) //double kill
					{
						IEnumerable<Process> existings = GetExisting();
						if (existings!= null && existings.Count() > 0)
						{
							Process p = existings.FirstOrDefault(o => o.Id == _tor.Id && o.SessionId == _tor.SessionId);
							if (p != null && p.HasExited)
							{
								try
								{
									p.Kill();
								}
								catch { }
							}
						}
					}
					_tor.Dispose();
				}
			}
		}

		static readonly Regex TOR_OK = new Regex(@"<h1[^>]*>\s*congratulations", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static readonly Regex FF_TOR = new Regex(@"^\s*Firefox", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static readonly Regex BRW_TOR = new Regex(@"\WTor\s*Browser\W", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// Return the list of existing tor processes (doesn't matter who started it)
		/// </summary>
		public IEnumerable<Process> GetExisting()
		{
			Process[] processes = Process.GetProcesses();
			if (processes != null && processes.Count() > 0)
			{
				Process[] ffs = (from p in processes
								 where FF_TOR.IsMatch(p.ProcessName)
									&& p.MainModule != null
									&& !string.IsNullOrWhiteSpace(p.MainModule.FileName)
									&& (string.Compare(p.MainModule.FileName, _torPath.FullName, true) == 0 || 
										BRW_TOR.IsMatch(p.MainModule.FileName))
								 select p).ToArray();
				return ffs;
			}
			return new Process[0];
		}

		Process _tor = null;
		readonly object _padlock = new object();
		/// <summary>
		/// Return the current tor process started by this logic
		/// </summary>
		public Process CurrentProcess
		{
			get
			{
				lock(_padlock)
				{
					return _tor;
				}
			}
		}

		/// <summary>
		/// Start process behavior
		/// </summary>
		public enum StartBehavior
		{
			/// <summary>
			/// Throw if an existing process is already running. This is the default option.
			/// </summary>
			ThrowIfRunning,
			/// <summary>
			/// If process is already running, attempt to return one as child
			/// </summary>
			ReturnExisting,
			/// <summary>
			/// Kill existing process and start a new one
			/// </summary>
			KillExistings,
		}

		/// <summary>
		/// Attempts to kill all existing processes
		/// </summary>
		public void KillExisting()
		{
			IEnumerable<Process> existings = GetExisting();
			KillExisting(existings);
		}
		void KillExisting(IEnumerable<Process> processes)
		{
			if (processes != null && processes.Count() > 0)
			{
				foreach(Process p in processes)
				{
					try
					{
						if (p != null && !p.HasExited)
						{
							try
							{
								p.Kill();
							}
							catch { }
							p.Dispose();
						}
					}
					catch(Exception) { }
				}
			}
		}

		/// <summary>
		/// Attempts to start a tor process
		/// </summary>
		/// <param name="behavior"><see cref="StartBehavior"/></param>
		/// <param name="windowStyle"><see cref="ProcessWindowStyle"/></param>
		/// <returns><see cref="CurrentProcess"/> handle if able to start</returns>
		public Process Start(
			StartBehavior behavior = StartBehavior.ReturnExisting, 
			ProcessWindowStyle windowStyle = ProcessWindowStyle.Hidden)
		{
			IEnumerable<Process> existings = GetExisting();
			if(existings != null && existings.Count() > 0)
			{
				switch(behavior)
				{
					case StartBehavior.ReturnExisting:
						return CurrentProcess ?? existings.FirstOrDefault();
					case StartBehavior.ThrowIfRunning:
						throw new InvalidOperationException("Tor is already running");
					case StartBehavior.KillExistings:
						KillExisting(existings);
						break;
					default:
						throw new NotImplementedException(typeof(StartBehavior).Name + ":" + behavior);
				}
			}

			lock (_padlock)
			{
				if (_tor != null && !_tor.HasExited)
					return _tor;

				var tor = new Process();
				bool startOk = false;
				try
				{
					tor.StartInfo.FileName = _torPath.FullName;
					tor.StartInfo.Arguments = "-n";
					tor.StartInfo.WindowStyle = windowStyle;
					startOk = tor.Start();
					if (startOk)
						return _tor = tor;
					else {
						tor.Dispose();
						return _tor = null;
					}
				}
				catch (Exception)
				{
					tor.Dispose();
					return _tor = null;
				}
			}
		}

		/// <summary>
		/// Attempts to wait until tor process is operational using default parameters
		/// </summary>
		/// <returns>True if tor started or false if it did not</returns>
		public bool InitWait()
		{
			return InitWait(
				TimeSpan.FromSeconds(5), //default retries every 5s
				TimeSpan.FromMinutes(1)); //default max wait for 1m
		}
		/// <summary>
		/// Attempts to wait until tor process is operational
		/// </summary>
		/// <param name="retrySleep">How long to wait between attempts</param>
		/// <param name="maxWait">How long to wait in total</param>
		/// <returns>True if tor started or false if it did not</returns>
		public bool InitWait(TimeSpan retrySleep, TimeSpan maxWait)
		{
			if (retrySleep <= TimeSpan.Zero)
				retrySleep = TimeSpan.FromMilliseconds(100);
			if (maxWait <= TimeSpan.Zero || maxWait < retrySleep)
				maxWait = TimeSpan.FromSeconds(5);

			bool ok = false;
			using (var client = new WebClient())
			{
				client.Proxy = new SocksWebProxy();
				const string url = "https://check.torproject.org/";
				string html = null;
				int count = 0;
				do
				{
					try
					{
						html = null;
						if (count > 0)
							Thread.Sleep(retrySleep);

						html = client.DownloadString(url);
					}
					catch (Exception)
					{
					}
					finally
					{
						count++;
					}
				}
				while (!(ok = _disposing == 0 && !string.IsNullOrWhiteSpace(html) && TOR_OK.IsMatch(html)));
			}
			return ok;
		}
	}
}
