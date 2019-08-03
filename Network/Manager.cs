//
// Network.Manager.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2019 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using MKAh;
using Serilog;
using Windows = MKAh.Wrapper.Windows;

namespace Taskmaster.Network
{
	using static Taskmaster;

	public class TrafficDelta
	{
		public float Input = float.NaN;
		public float Output = float.NaN;
		public float Queue = float.NaN;
		public float Packets = float.NaN;
	}

	public class TrafficEventArgs : EventArgs
	{
		public TrafficDelta Delta = null;
	}

	[Component(RequireMainThread = false)]
	public class Manager : Component, IDisposal
	{
		public static bool ShowNetworkErrors { get; set; } = false;

		bool DebugNet { get; set; } = false;

		bool DebugDNS { get; set; } = false;

		public event EventHandler<InternetStatus> InternetStatusChange;
		public event EventHandler IPChanged;
		public event EventHandler<Status> NetworkStatusChange;

		public event EventHandler<TrafficEventArgs> NetworkTraffic;

		readonly Windows.PerformanceCounter NetInTrans = null;
		readonly Windows.PerformanceCounter NetOutTrans = null;
		readonly Windows.PerformanceCounter NetPackets = null;
		readonly Windows.PerformanceCounter NetQueue = null;

		string dnstestaddress = "google.com"; // should be fine, www is omitted to avoid deeper DNS queries

		int DeviceTimerInterval = 15 * 60;

		/// <summary>
		/// Seconds.
		/// </summary>
		int PacketStatTimerInterval = 2; // second
		int ErrorReportLimit = 5;

		readonly System.Timers.Timer SampleTimer;

		public event EventHandler<Network.DeviceTrafficEventArgs> DeviceSampling;

		const string NetConfigFilename = "Net.ini";

		bool DynamicDNS = false;
		bool DynamicDNSForcedUpdate = false;

		Uri DynamicDNSHost = null; // BUG: Insecure. Contains secrets.
		TimeSpan DynamicDNSFrequency = TimeSpan.FromMinutes(15d);
		DateTimeOffset DynamicDNSLastUpdate = DateTimeOffset.MinValue;
		IPAddress DNSOldIPv4 = IPAddress.None, DNSOldIPv6 = IPAddress.IPv6None;

		void LoadConfig()
		{
			try
			{
				using var netcfg = Config.Load(NetConfigFilename);
				var monsec = netcfg.Config["Monitor"];
				dnstestaddress = monsec.GetOrSet("DNS test", "www.google.com").Value;

				var devsec = netcfg.Config["Devices"];
				DeviceTimerInterval = devsec.GetOrSet("Check frequency", 15)
					.InitComment("Minutes")
					.Int.Constrain(1, 30) * 60;

				var pktsec = netcfg.Config["Traffic"];
				PacketStatTimerInterval = pktsec.GetOrSet("Sample rate", 15)
					.InitComment("Seconds")
					.Int.Constrain(1, 60);
				PacketWarning.Peak = PacketStatTimerInterval;

				ErrorReports.Peak = ErrorReportLimit = pktsec.GetOrSet("Error report limit", 5).Int.Constrain(1, 60);

				var dnssec = netcfg.Config[Constants.DNSUpdating];
				DynamicDNS = dnssec.GetOrSet("Enabled", false)
					.Bool;
				DynamicDNSFrequency = TimeSpan.FromMinutes(Convert.ToDouble(dnssec.GetOrSet("Frequency", 600)
					.InitComment("In minutes.")
					.Int.Min(15)));
				DynamicDNSForcedUpdate = dnssec.GetOrSet("Force", false)
					.InitComment("Force performing the update even if no IP update is detected.")
					.Bool;

				if (DynamicDNS)
				{
					string host = dnssec.Get(Constants.Host).String;
					bool tooshort = (host?.Length ?? 0) < 10; // HACK: Arbitrary size limit
					bool http = host?.StartsWith("http://") ?? false;
					bool https = host?.StartsWith("https://") ?? false;
					if (https) http = true;

					//bool afraidorg = (host?.IndexOf("sync.afraid.org/", StringComparison.InvariantCultureIgnoreCase) ?? -1) > 0;
					if (!https) Log.Warning("<Net:DynDNS> Host string does not use secure HTTP.");

					if (!http)
						Log.Error("<Net:DynDNS> Unrecognized protocol in host address.");
					else if (tooshort)
						Log.Error("<Net:DynDNS> Host URL too short.");

					try
					{
						if (tooshort || !http) throw new UriFormatException("Base qualifications failed");
						{
							DynamicDNS = false;
							DynamicDNSHost = null;
						}
						DynamicDNSHost = new Uri(host, UriKind.Absolute);
						if (!https) Log.Warning("<Net:DynDNS> Host string does not use secure HTTP.");
						Log.Information($"<Net:DynDNS> Enabled (frequency: {DynamicDNSFrequency:g})");
					}
					catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException)
					{
						DynamicDNS = false;
						DynamicDNSHost = null;
					}
				}
				else
					Log.Information("<Net:DynDNS> Disabled");

				using var corecfg = Config.Load(CoreConfigFilename);
				var logsec = corecfg.Config[HumanReadable.Generic.Logging];
				ShowNetworkErrors = logsec.GetOrSet("Show network errors", true)
					.InitComment("Show network errors on each sampling.")
					.Bool;

				var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
				DebugNet = dbgsec.Get(Network.Constants.Network)?.Bool ?? false;
				DebugDNS = dbgsec.Get(Network.Constants.DynDNS)?.Bool ?? false;

				if (Trace) Log.Debug("<Network> Traffic sample frequency: " + PacketStatTimerInterval.ToString() + "s");
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		public Manager()
		{
			var now = DateTimeOffset.UtcNow;

			InvalidateInterfaceList();

			UptimeRecordStart = now;
			LastUptimeStart = now;

			LoadConfig();

			_ = CurrentInterfaceList.Value;

			SampleTimer = new System.Timers.Timer(PacketStatTimerInterval * 1_000);
			SampleTimer.Elapsed += AnalyzeTrafficBehaviour;
			//SampleTimer.Elapsed += DeviceSampler;
			SampleTimer.Start();

			AnalyzeTrafficBehaviour(this, EventArgs.Empty); // initialize, not really needed

			/*
			// Reset time could be used for initial internet start time as it is the only even remotely relevant one
			// ... but it's not honestly truly indicative of it.
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DeviceID,TimeOfLastReset FROM Win32_NetworkAdapter");
				foreach (ManagementObject mo in searcher.Get())
				{
					string netreset = mo["TimeOfLastReset"] as string;
					var reset = ManagementDateTimeConverter.ToDateTime(netreset);
					Console.WriteLine("NET RESET: " + reset);
				}
			*/

			// TODO: SUPPORT MULTIPLE NICS
			const string CategoryName = "Network Interface";
			var firstnic = new PerformanceCounterCategory(CategoryName).GetInstanceNames()[1]; // 0 = loopback
			NetInTrans = new Windows.PerformanceCounter(CategoryName, "Bytes Received/sec", firstnic);
			NetOutTrans = new Windows.PerformanceCounter(CategoryName, "Bytes Sent/sec", firstnic);
			NetQueue = new Windows.PerformanceCounter(CategoryName, "Output Queue Length", firstnic);
			NetPackets = new Windows.PerformanceCounter(CategoryName, "Packets/sec", firstnic);

			lastErrorReport = DateTimeOffset.UtcNow; // crude

			StopUpdateNetworkState(); // initialize

			StartDynDNSUpdates();

			if (DebugNet) Log.Information("<Network> Component loaded.");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		System.Threading.Timer DynDNSTimer = null;

		async Task StartDynDNSUpdates()
		{
			if (!DynamicDNS) return;

			using var netcfg = Config.Load(NetConfigFilename);
			var dns = netcfg.Config[Constants.DNSUpdating];
			IPAddress.TryParse(dns.Get(Constants.LastKnownIPv4)?.String ?? string.Empty, out DNSOldIPv4);
			IPAddress.TryParse(dns.Get(Constants.LastKnownIPv6)?.String ?? string.Empty, out DNSOldIPv6);

			var TimerStartDelay = TimeSpan.FromSeconds(10d);
			if (DateTimeOffset.TryParse(dns.Get(Constants.LastAttempt)?.String ?? string.Empty, out DynamicDNSLastUpdate)
				&& DynamicDNSLastUpdate.To(DateTimeOffset.UtcNow).TotalMinutes < 15d)
			{
				Log.Debug("<Net:DynDNS> Delaying update timer.");
				TimerStartDelay = TimeSpan.FromMinutes(15d);
			}
			else
				if (DebugDNS) Log.Debug("<Net:DynDNS> Starting update timer.");

			DynDNSTimer = new System.Threading.Timer(DynDNSTimer_Elapsed, null, TimerStartDelay, DynamicDNSFrequency);
		}

		int DynDNSFailures = 0;

		async void DynDNSTimer_Elapsed(object _)
		{
			if (DynDNSTimer is null) return;
			if (!InternetAvailable) return;

			IPAddress curIPv4, curIPv6;
			lock (address_lock)
			{
				curIPv4 = IPv4Address;
				curIPv6 = IPv6Address;
			}

			try
			{
				bool updateIPs = false;
				if (!DynamicDNSForcedUpdate)
				{
					if (!DNSOldIPv4.Equals(curIPv4) || !DNSOldIPv6.Equals(curIPv6))
					{
						updateIPs = true;
						DNSOldIPv4 = curIPv4;
						DNSOldIPv6 = curIPv6;
					}
					else
					{
						if (DebugDNS) Log.Debug("<Net:DynDNS> IP has not changed, forgoing update.");
						return;
					}
				}

				using var netcfg = Config.Load(NetConfigFilename);
				var dns = netcfg.Config[Constants.DNSUpdating];

				bool success = await DynamicDNSUpdate().ConfigureAwait(false);
				if (success)
				{
					DynDNSFailures = 0;
					try
					{
						DynamicDNSLastUpdate = DateTimeOffset.UtcNow;
						dns[Constants.LastAttempt].String = DynamicDNSLastUpdate.ToString("u");
						if (updateIPs)
						{
							if (!DNSOldIPv4.Equals(curIPv4))
							{
								dns[Constants.LastKnownIPv4].String = curIPv4.ToString();
								DNSOldIPv4 = curIPv4;
							}
							if (!DNSOldIPv6.Equals(curIPv6))
							{
								dns["Last IPv6"].String = curIPv6.ToString();
								DNSOldIPv6 = curIPv6;
							}
						}
					}
					catch { }
				}
				else if (DynDNSFailures++ > 3)
				{
					Log.Error("<Net:DynDNS> Update failed too many times, stopping updates.");
					DynDNSTimer?.Dispose();
					DynDNSTimer = null;
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		async Task<bool> DynamicDNSUpdate()
		{
			Log.Debug("<Net:DynDNS> Updating...");

			try
			{
				// Afraid.org dynamic DNS update v2 style query

				// TODO: Load the host string only here and discard it after?

				var rq = System.Net.WebRequest.CreateHttp(DynamicDNSHost);
				rq.Method = "GET";
				rq.MaximumAutomaticRedirections = 1;
				rq.ContentType = "json";
				rq.UserAgent = "Taskmaster/DynDNS.alpha.1";
				rq.Timeout = 30_000;
				using var rs = await rq.GetResponseAsync();
				if (rs.ContentLength > 0)
				{
					//var sbs = new StringBuilder(512);
					using var dat = rs.GetResponseStream();
					int len = Convert.ToInt32(rs.ContentLength);
					byte[] buffer = new byte[len];
					await dat.ReadAsync(buffer, 0, len).ConfigureAwait(false);
					Logging.DebugMsg(buffer.ToString());
				}
				rs.Close();
			}
			catch (WebException ex)
			{
				Log.Debug($"<Net:DynDNS> Response: {ex.Status.ToString()}");
				switch (ex.Status)
				{
					case WebExceptionStatus.Success:
						// OK, rinse and repeat in case IP changes
						return true;
					case WebExceptionStatus.Timeout:
						// TODO: Increase delay
						break;
					default:
						// Assume user error
						break;
				}

				return false;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return true;
		}

		public TrafficDelta GetTraffic
			=> new TrafficDelta()
			{
				Input = NetInTrans?.Value ?? float.NaN,
				Output = NetOutTrans?.Value ?? float.NaN,
				Queue = NetQueue?.Value ?? float.NaN,
				Packets = NetPackets?.Value ?? float.NaN,
			};

		public string GetDeviceData(string devicename)
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "GetDeviceData called after NetManager was disposed.");

			foreach (var device in CurrentInterfaceList.Value)
			{
				if (device.Name.Equals(devicename))
				{
					return devicename + " – " + device.IPv4Address.ToString() + " [" + IPv6Address.ToString() + "]" +
						" – " + (device.Incoming.Bytes / 1_000_000).ToString() + " MB in, " + (device.Outgoing.Bytes / 1_000_000).ToString() + " MB out, " +
						(device.Outgoing.Errors + device.Incoming.Errors).ToString() + " errors";
				}
			}

			return null;
		}

		readonly LinearMeter PacketWarning = new LinearMeter(15); // UNUSED
		readonly LinearMeter ErrorReports = new LinearMeter(5, 4);

		Lazy<List<Device>> CurrentInterfaceList = null;

		int TrafficAnalysisLimiter = 0;

		long errorsSinceLastReport = 0;
		DateTimeOffset lastErrorReport;

		void AnalyzeTrafficBehaviour(object _, EventArgs _ea)
		{
			if (disposed) return;

			if (!Atomic.Lock(ref TrafficAnalysisLimiter)) return;

			try
			{
				//PacketWarning.Drain();

				var oldifaces = CurrentInterfaceList.Value;
				InvalidateInterfaceList(); // force refresh
				var ifaces = CurrentInterfaceList.Value;

				if (!oldifaces.Count.Equals(ifaces.Count))
				{
					if (DebugNet) Log.Warning("<Network> Interface count mismatch (" + oldifaces.Count.ToString() + " vs " + ifaces.Count.ToString() + "), skipping analysis.");
					return;
				}

				if ((ifaces?.Count ?? 0) == 0) return; // no interfaces, just quit

				for (int index = 0; index < ifaces.Count; index++)
				{
					var nout = ifaces[index].Outgoing;
					var nin = ifaces[index].Incoming;
					var oldout = oldifaces[index].Outgoing;
					var oldin = oldifaces[index].Incoming;

					long totalerrors = nout.Errors + nin.Errors,
						totaldiscards = nout.Errors + nin.Errors,
						totalunicast = nout.Errors + nin.Errors,
						errorsInSample = (nin.Errors - oldin.Errors) + (nout.Errors - oldout.Errors),
						discards = (nin.Discards - oldin.Discards) + (nout.Discards - oldout.Discards),
						packets = (nin.Unicast - oldin.Unicast) + (nout.Unicast - oldout.Unicast);

					errorsSinceLastReport += errorsInSample;

					// TODO: Better error limiter.
					// Goals:
					// - Show initial error.
					// - Show errors in increasing rarity
					// - Reset the increased rarity once it becomes too rare

					//Logging.DebugMsg($"NETWORK - Errors: +{errorsInSample}, NotPeaked: {!ErrorReports.Peaked}, Level: {ErrorReports.Level}/{ErrorReports.Peak}");

					bool reportErrors;
					if (ShowNetworkErrors // user wants to see this
						&& errorsInSample > 0 // only if errors
						&& !ErrorReports.Peaked // we're not waiting for report counter to go down
						&& ErrorReports.Pump(errorsInSample)) // error reporting not full
					{
						reportErrors = true;
					}
					else
					{
						// no error reporting until the meter goes down, giving ErrorReports.Peak worth of samples to ignore for error reporting
						ErrorReports.Drain();
						reportErrors = (ErrorReports.IsEmpty && errorsSinceLastReport > 0);
					}

					var now = DateTimeOffset.UtcNow;
					TimeSpan period = lastErrorReport.To(now);
					double pmins = period.TotalHours < 24 ? period.TotalMinutes : double.NaN; // NaN-ify too large periods

					if (reportErrors)
					{
						var sbs = new StringBuilder(ifaces[index].Name, 256).Append(" is suffering from traffic errors! (");

						bool longProblem = (errorsSinceLastReport > errorsInSample);

						if (longProblem) sbs.Append('+').Append(errorsSinceLastReport).Append(" errors, ").Append(errorsInSample).Append(" in last sample");
						else sbs.Append('+').Append(errorsInSample).Append(" errors in last sample");

						if (!double.IsNaN(pmins)) sbs.Append("; ").AppendFormat("{0:N1}", pmins).Append(" minutes since last report");
						sbs.Append(')');

						Log.Warning(sbs.ToString());

						errorsSinceLastReport = 0;
						lastErrorReport = now;

						// TODO: Slow down reports if they're excessively frequent

						if (pmins < 1) ErrorReports.Peak += 5; // this slows down some reporting, but not in a good way
					}
					else
					{
						if (period.TotalMinutes > 5 && errorsSinceLastReport > 0) // report anyway
						{
							Log.Warning($"<Network> {ifaces[index].Name} had some traffic errors (+{errorsSinceLastReport}; period: {pmins:N1} minutes)");
							errorsSinceLastReport = 0;
							lastErrorReport = now;

							ErrorReports.Peak = 5; // reset
						}
					}

					DeviceSampling?.Invoke(this, new DeviceTrafficEventArgs
					{
						Traffic =
						new DeviceTraffic
						{
							Index = index,
							Delta = new TrafficData { Unicast = packets, Errors = errorsInSample, Discards = discards },
							Total = new TrafficData { Unicast = totalunicast, Errors = totalerrors, Discards = totaldiscards, Bytes = nin.Bytes + nout.Bytes },
						}
					});
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref TrafficAnalysisLimiter);
			}
		}

		public DeviceTraffic GetCurrentTraffic { get; } = new DeviceTraffic();

		public UI.TrayAccess Tray { get; set; } = null; // HACK: bad design

		public bool NetworkAvailable { get; private set; } = false;
		public bool InternetAvailable { get; private set; } = false;

		readonly int MaxSamples = 20;
		readonly List<double> UptimeSamples = new List<double>(20);

		DateTimeOffset UptimeRecordStart; // since we started recording anything

		DateTimeOffset LastUptimeStart, // since we last knew internet to be initialized
			LastDowntimeStart;  // since we last knew internet to go down

		readonly object uptime_lock = new object();

		/// <summary>
		/// Current uptime in minutes.
		/// </summary>
		/// <value>The uptime.</value>
		public TimeSpan Uptime => InternetAvailable ? (DateTimeOffset.UtcNow - LastUptimeStart) : TimeSpan.Zero;

		/// <summary>
		/// Returns uptime in minutes or positive infinite if no average is known
		/// </summary>
		public double UptimeMean
		{
			get
			{
				lock (uptime_lock) return UptimeSamples.Count > 0 ? UptimeSamples.Average() : double.PositiveInfinity;
			}
		}

		Stopwatch Downtime = null;

		void ReportUptime()
		{
			var sbs = new StringBuilder("<Network> Average uptime: ", 128);

			lock (uptime_lock)
			{
				var currentUptime = DateTimeOffset.UtcNow.Since(LastUptimeStart).TotalMinutes;

				int cnt = UptimeSamples.Count;
				sbs.AppendFormat("{0:N1}", (UptimeSamples.Sum() + currentUptime) / (cnt + 1)).Append(" minutes");

				if (cnt >= 3)
					sbs.Append(" (").AppendFormat("{0:N1}", UptimeSamples.GetRange(cnt - 3, 3).Sum() / 3f).Append(" minutes for last 3 samples");
			}

			sbs.Append(" since: ").Append(UptimeRecordStart)
			   .Append(" (").AppendFormat("{0:N2}", (DateTimeOffset.UtcNow - UptimeRecordStart).TotalHours).Append("h ago)")
			   .Append('.');

			Log.Information(sbs.ToString());
		}

		bool lastOnlineState = false;
		int DeviceStateRecordLimiter = 0;

		DateTimeOffset LastUptimeSample = DateTimeOffset.MinValue;

		void RecordUptimeState(bool online_state, bool address_changed)
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "RecordUptimeState called after NetManager was disposed.");

			if (!Atomic.Lock(ref DeviceStateRecordLimiter)) return;

			try
			{
				var now = DateTimeOffset.UtcNow;
				if (LastUptimeSample.To(now).TotalMinutes < DeviceTimerInterval)
					return;

				LastUptimeSample = now;
				if (online_state != lastOnlineState)
				{
					lastOnlineState = online_state;

					if (online_state)
					{
						// NOP
					}
					else // went offline
					{
						lock (uptime_lock)
						{
							var newUptime = (now - LastUptimeStart).TotalMinutes;
							UptimeSamples.Add(newUptime);

							if (UptimeSamples.Count > MaxSamples)
								UptimeSamples.RemoveAt(0);
						}

						//ReportUptime();
					}

					return;
				}
				else if (address_changed)
				{
					// same state but address change was detected
					Log.Verbose("<Network> Address changed but internet connectivity unaffected.");
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
			finally
			{
				Atomic.Unlock(ref DeviceStateRecordLimiter);
			}
		}

		bool InternetChangeNotifyDone = false;

		int InetCheckLimiter; // = 0;

		// TODO: Needs to call itself in case of failure but network connected to detect when internet works.
		async Task<bool> CheckInet()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "CheckInet called after NetManager was disposed.");

			// TODO: Figure out how to get Actual start time of internet connectivity.
			// Probably impossible.

			if (Atomic.Lock(ref InetCheckLimiter))
			{
				if (Trace) Log.Verbose("<Network> Checking internet connectivity...");

				try
				{
					var oldInetAvailable = InternetAvailable;
					bool timeout = false;
					bool dnsfail = false;
					bool interrupt = false;
					if (NetworkAvailable)
					{
						try
						{
							// FIXME: There should be some other method than DNS testing
							await Dns.GetHostEntryAsync(dnstestaddress).ConfigureAwait(false);
							InternetAvailable = true;
							InternetChangeNotifyDone = false;
							// TODO: Don't rely on DNS?
						}
						catch (System.Net.Sockets.SocketException ex)
						{
							InternetAvailable = false;
							switch (ex.SocketErrorCode)
							{
								case System.Net.Sockets.SocketError.AccessDenied:
								case System.Net.Sockets.SocketError.SystemNotReady:
									break;
								case System.Net.Sockets.SocketError.TryAgain:
								case System.Net.Sockets.SocketError.TimedOut:
								default:
									//timeout = true;
									InternetAvailable = true;
									return InternetAvailable;
								case System.Net.Sockets.SocketError.SocketError:
								case System.Net.Sockets.SocketError.Interrupted:
								case System.Net.Sockets.SocketError.Fault:
									interrupt = true;
									break;
								case System.Net.Sockets.SocketError.HostUnreachable:
									break;
								case System.Net.Sockets.SocketError.HostNotFound:
								case System.Net.Sockets.SocketError.HostDown:
									dnsfail = true;
									break;
								case System.Net.Sockets.SocketError.NetworkDown:
								case System.Net.Sockets.SocketError.NetworkReset:
								case System.Net.Sockets.SocketError.NetworkUnreachable:
									break;
							}
						}
					}
					else
						InternetAvailable = false;

					if (InternetAvailable) LastUptimeStart = DateTimeOffset.UtcNow;
					else LastDowntimeStart = DateTimeOffset.UtcNow;

					if (!InternetAvailable)
					{
						// TODO: Schedule another test.
						await CheckInet().ConfigureAwait(false);
					}

					if (oldInetAvailable != InternetAvailable)
						InvalidateInterfaceList();
					else
					{
						if (timeout)
							Log.Information("<Network> Internet availability test inconclusive, assuming connected.");

						if (!InternetChangeNotifyDone && NetworkAvailable)
						{
							if (interrupt)
								Log.Warning("<Network> Internet check interrupted. Potential hardware/driver issues.");

							if (dnsfail)
								Log.Warning("<Network> DNS test failed, test host unreachable. Test host may be down.");

							InternetChangeNotifyDone = dnsfail || interrupt;
						}

						if (Trace) Log.Verbose("<Network> Connectivity unchanged.");
					}
				}
				finally
				{
					Atomic.Unlock(ref InetCheckLimiter);
				}
			}

			InternetStatusChange?.Invoke(this, new InternetStatus { Available = InternetAvailable, Start = LastUptimeStart, Uptime = Uptime });

			return InternetAvailable;
		}

		//readonly List<IPAddress> AddressList = new List<IPAddress>(2);
		// List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);

		readonly object address_lock = new object();

		IPAddress IPv4Address = IPAddress.None, IPv6Address = IPAddress.IPv6None;
		//NetworkInterface IPv4Interface, IPv6Interface;

		/// <summary>
		/// Resets <see cref="CurrentInterfaceList"/>.
		/// </summary>
		void InvalidateInterfaceList() => CurrentInterfaceList = new Lazy<List<Device>>(RecreateInterfaceList, false);

		public List<Device> GetInterfaces()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "GetInterfaces called after NetManager was disposed.");

			InvalidateInterfaceList();
			return CurrentInterfaceList.Value;
		}

		List<Device> RecreateInterfaceList()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "UpdateInterfaces called after NetManager was disposed.");

			var ifacelistt = new List<Device>(2);

			try
			{
				if (DebugNet) Log.Verbose("<Network> Enumerating network interfaces...");

				// var ifacelist = new List<string[]>();

				lock (address_lock)
				{
					var index = 0;
					var adapters = NetworkInterface.GetAllNetworkInterfaces();
					foreach (NetworkInterface dev in adapters)
					{
						var ti = index++;
						if (dev.NetworkInterfaceType == NetworkInterfaceType.Loopback || dev.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
							continue;

						var stats = dev.GetIPStatistics();

						bool found4 = false, found6 = false;
						IPAddress _ipv4 = IPAddress.None, _ipv6 = IPAddress.IPv6None;
						foreach (UnicastIPAddressInformation ip in dev.GetIPProperties().UnicastAddresses)
						{
							switch (ip.Address.AddressFamily)
							{
								case System.Net.Sockets.AddressFamily.InterNetwork:
									var address = ip.Address.GetAddressBytes();
									if (!(address[0] == 169 && address[1] == 254)) // ignore link-local
									{
										_ipv4 = ip.Address;
										found4 = true;
									}
									break;
								case System.Net.Sockets.AddressFamily.InterNetworkV6:
									if (!ip.Address.IsIPv6LinkLocal && !ip.Address.IsIPv6SiteLocal)
									{
										_ipv6 = ip.Address;
										found6 = true;
									}
									break;
							}

							if (found4 && found6) break; // kinda bad, but meh
						}

						var devi = new Device
						{
							Index = ti,
							Id = Guid.Parse(dev.Id),
							Name = dev.Name,
							Type = dev.NetworkInterfaceType,
							Status = dev.OperationalStatus,
							Speed = dev.Speed,
							IPv4Address = _ipv4,
							IPv6Address = _ipv6,
						};

						if (_ipv4 != IPAddress.None)
						{
							IPv4Address = _ipv4;
							//IPv4Interface = dev;
						}

						if (_ipv6 != IPAddress.IPv6None)
						{
							IPv6Address = _ipv6;
							//IPv6Interface = dev;
						}

						devi.Incoming.From(stats, true);
						devi.Outgoing.From(stats, false);
						// devi.PrintStats();
						ifacelistt.Add(devi);

						if (DebugNet) Log.Verbose("<Network> Interface: " + dev.Name);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}

			return ifacelistt;
		}

		// 
		async void NetAddrChanged(object _, EventArgs _ea)
		{
			if (disposed) return;

			bool AvailabilityChanged = InternetAvailable;

			await CheckInet(/*address_changed: true*/).ConfigureAwait(false);

			AvailabilityChanged = AvailabilityChanged != InternetAvailable;

			if (InternetAvailable)
			{
				IPAddress oldV6Address = IPv6Address;
				IPAddress oldV4Address = IPv4Address;

				GetInterfaces();
				IPAddress newIPv4 = IPv4Address, newIPv6 = IPv6Address;

				bool ipv4changed, ipv6changed, ipchanged = false;
				ipchanged |= ipv4changed = !oldV4Address.Equals(newIPv4);

				var sbs = new StringBuilder(128);

				if (AvailabilityChanged)
				{
					sbs.AppendLine("Internet restored.");
					Log.Information("<Network> Internet connection restored.");
				}

				ipchanged |= ipv6changed = !oldV6Address.Equals(newIPv6);

				if (ipchanged)
				{
					if (ipv4changed)
					{
						sbs.AppendLine("IPv4 address changed.").AppendLine(oldV4Address.ToString());
						Log.Information($"<Network> IPv4 address changed: {oldV4Address} → {newIPv4}");
					}
					if (ipv6changed)
					{
						sbs.AppendLine("IPv6 address changed.").AppendLine(oldV6Address.ToString());
						Log.Information($"<Network> IPv6 address changed: {oldV6Address} → {newIPv6}");
					}

					Tray.Tooltip(4000, sbs.ToString(), Name, System.Windows.Forms.ToolTipIcon.Warning);

					// bad since if it's not clicked, we react to other tooltip clicks, too
					// TODO: Need replaceable callback or something.
					//Tray.TrayTooltipClicked += (_, _ea) => { /* something */ };

					IPChanged?.Invoke(this, EventArgs.Empty);
				}
			}
			else
			{
				if (AvailabilityChanged)
				{
					//Log.Warning("<Network> Unstable connectivity detected.");

					Tray.Tooltip(2000, "Unstable internet connection detected!", Name,
						System.Windows.Forms.ToolTipIcon.Warning);
				}
			}

			//NetworkChanged(null,null);
		}

		public void SetupEventHooks()
		{
			NetworkChanged(this, EventArgs.Empty); // initialize event handler's initial values
			CheckInet().ConfigureAwait(false); // initialize

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;
		}

		bool LastReportedNetAvailable = false;
		bool LastReportedInetAvailable = false;

		void ReportNetAvailability()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Manager), "ReportNetAvailability called after NetManager was disposed.");

			if (!((LastReportedInetAvailable != InternetAvailable) || (LastReportedNetAvailable != NetworkAvailable)))
				return; // bail out if nothing has changed since last report

			LastReportedInetAvailable = InternetAvailable;
			LastReportedNetAvailable = NetworkAvailable;

			var sbs = new StringBuilder(128)
				.Append("<Network> Status: ")
				.Append(NetworkAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(", Internet: ")
				.Append(InternetAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(" - ");

			if (NetworkAvailable && !InternetAvailable)
			{
				sbs.Append("Route problems");
				if (LastUptimeStart != DateTimeOffset.MinValue)
					sbs.Append("– Downtime: ").Append(LastDowntimeStart.To(LastUptimeStart).ToString());
			}
			else if (!NetworkAvailable)
			{
				sbs.Append("Cable unplugged or router/modem down");
				if (LastUptimeStart != DateTimeOffset.MinValue)
					sbs.Append("– Downtime: ").Append(LastDowntimeStart.To(LastUptimeStart).ToString());
			}
			else
			{
				sbs.Append("All OK");
				if (LastDowntimeStart != DateTimeOffset.MinValue)
					sbs.Append("– Downtime: ").Append(LastDowntimeStart.To(LastUptimeStart).ToString());
			}

			if (!NetworkAvailable || !InternetAvailable) Log.Warning(sbs.ToString());
			else Log.Information(sbs.ToString());
		}

		/// <summary>
		/// For tracking how many times NetworkChanged is triggered
		/// </summary>
		int NetworkChangeCounter = 4; // 4 to force fast inet check on start

		/*
		/// <summary>
		/// Last time NetworkChanged was triggered
		/// </summary>
		DateTimeOffset LastNetworkChange = DateTimeOffset.MinValue;
		*/

		readonly object NetworkStatus_lock = new object();
		System.Threading.Timer NetworkStatusReport = null;

		TimeSpan NetworkReportDelay = TimeSpan.FromSeconds(2.2d);
		DateTimeOffset LastNetworkReport = DateTimeOffset.MinValue;

		bool ReportOngoing = false;

		void UpdateNetworkState(object state)
		{
			if (disposed) return;

			try
			{
				var now = DateTimeOffset.UtcNow;

				lock (NetworkStatus_lock)
					if (LastNetworkReport.To(now).TotalSeconds < 5d) IncreaseReportDelay();

				ReportOngoing = true;
				LastNetworkReport = now;
				ReportNetAvailability();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				ReportOngoing = false;
			}
		}

		/// <summary>
		/// Don't use without <a cref="NetworkStatus_lock">locking</a>.
		/// </summary>
		void IncreaseReportDelay() => NetworkReportDelay = NetworkReportDelay.Add(TimeSpan.FromMilliseconds(220));

		void StartUpdateNetworkState()
		{
			if (disposed) return;

			lock (NetworkStatus_lock)
			{
				if (ReportOngoing) IncreaseReportDelay();

				NetworkStatusReport?.Change(NetworkReportDelay, System.Threading.Timeout.InfiniteTimeSpan);
			}
		}

		void StopUpdateNetworkState()
		{
			lock (NetworkStatus_lock)
				NetworkStatusReport = new System.Threading.Timer(UpdateNetworkState, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
		}

		// TODO: Make network status reporting more centralized
		void NetworkChanged(object _, EventArgs _ea)
		{
			if (disposed) return;

			//LastNetworkChange = DateTimeOffset.UtcNow;

			var oldNetAvailable = NetworkAvailable;
			bool available = NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			NetworkChangeCounter++;

			NetworkStatusChange?.Invoke(this, new Status { Available = available });

			// do stuff only if this is different from last time
			if (oldNetAvailable != available)
				StartUpdateNetworkState();
			else if (DebugNet)
				Log.Debug("<Net> Network changed but still as available as before.");
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		bool disposed = false;

		public override void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			// base.Dispose(disposing);

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing network monitor...");

				DeviceSampling = null;
				InternetStatusChange = null;
				IPChanged = null;
				NetworkStatusChange = null;

				ReportUptime();

				SampleTimer?.Dispose();

				NetInTrans?.Dispose();
				NetOutTrans?.Dispose();
				NetPackets?.Dispose();
				NetQueue?.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			SampleTimer?.Stop();
		}
		#endregion
	}
}