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

	public sealed class TrafficDelta
	{
		public float Input = float.NaN;
		public float Output = float.NaN;
		public float Queue = float.NaN;
		public float Packets = float.NaN;
	}

	public sealed class TrafficEventArgs : EventArgs
	{
		public TrafficDelta Delta = null;
	}

	sealed public class Manager : IDisposal, IDisposable
	{
		public static bool ShowNetworkErrors { get; set; } = false;

		bool DebugNet { get; set; } = false;

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

		void LoadConfig()
		{
			using (var netcfg = Config.Load(NetConfigFilename).BlockUnload())
			{
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
			}

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				var logsec = corecfg.Config[HumanReadable.Generic.Logging];
				ShowNetworkErrors = logsec.GetOrSet("Show network errors", true)
					.InitComment("Show network errors on each sampling.")
					.Bool;

				var dbgsec = corecfg.Config[HumanReadable.Generic.Debug];
				DebugNet = dbgsec.Get("Network")?.Bool ?? false;
			}

			if (Trace) Log.Debug("<Network> Traffic sample frequency: " + PacketStatTimerInterval + "s");
		}

		public Manager()
		{
			var now = DateTimeOffset.UtcNow;

			InvalidateInterfaceList();

			UptimeRecordStart = now;
			LastUptimeStart = now;

			LoadConfig();

			InterfaceInitialization();

			SampleTimer = new System.Timers.Timer(PacketStatTimerInterval * 1_000);
			SampleTimer.Elapsed += AnalyzeTrafficBehaviour;
			//SampleTimer.Elapsed += DeviceSampler;
			SampleTimer.Start();

			AnalyzeTrafficBehaviour(this, EventArgs.Empty); // initialize, not really needed

			/*
			// Reset time could be used for initial internet start time as it is the only even remotely relevant one
			// ... but it's not honestly truly indicative of it.
			using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DeviceID,TimeOfLastReset FROM Win32_NetworkAdapter"))
			{
				foreach (ManagementObject mo in searcher.Get())
				{
					string netreset = mo["TimeOfLastReset"] as string;
					var reset = ManagementDateTimeConverter.ToDateTime(netreset);
					Console.WriteLine("NET RESET: " + reset);
				}
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

			if (DebugNet) Log.Information("<Network> Component loaded.");

			RegisterForExit(this);
			DisposalChute.Push(this);
		}

		public TrafficDelta GetTraffic
			=> new TrafficDelta()
			{
				Input = NetInTrans?.Value ?? float.NaN,
				Output = NetOutTrans?.Value ?? float.NaN,
				Queue = NetQueue?.Value ?? float.NaN,
				Packets = NetPackets?.Value ?? float.NaN,
			};

		void DeviceSampler(object sender, System.Timers.ElapsedEventArgs e)
		{
			if (DisposedOrDisposing) return;

			RecordUptimeState(InternetAvailable, false);
		}

		public string GetDeviceData(string devicename)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "GetDeviceData called after NetManager was disposed.");

			foreach (var device in CurrentInterfaceList.Value)
			{
				if (device.Name.Equals(devicename))
				{
					return devicename + " – " + device.IPv4Address.ToString() + " [" + IPv6Address.ToString() + "]" +
						" – " + (device.Incoming.Bytes / 1_000_000) + " MB in, " + (device.Outgoing.Bytes / 1_000_000) + " MB out, " +
						(device.Outgoing.Errors + device.Incoming.Errors) + " errors";
				}
			}

			return null;
		}

		readonly LinearMeter PacketWarning = new LinearMeter(15); // UNUSED
		readonly LinearMeter ErrorReports = new LinearMeter(5, 4);

		Lazy<List<Device>> CurrentInterfaceList = null;

		int TrafficAnalysisLimiter = 0;
		TrafficData nout, nin, oldout, oldin;

		long errorsSinceLastReport = 0;
		DateTimeOffset lastErrorReport = DateTimeOffset.MinValue;

		void AnalyzeTrafficBehaviour(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			if (!Atomic.Lock(ref TrafficAnalysisLimiter)) return;

			try
			{
				//PacketWarning.Drain();

				var oldifaces = CurrentInterfaceList.Value;
				InvalidateInterfaceList(); // force refresh
				var ifaces = CurrentInterfaceList.Value;

				if (!oldifaces.Count.Equals(ifaces.Count))
				{
					if (DebugNet) Log.Warning("<Network> Interface count mismatch (" + oldifaces.Count + " vs " + ifaces.Count + "), skipping analysis.");
					return;
				}

				if ((ifaces?.Count ?? 0) == 0) return; // no interfaces, just quit

				for (int index = 0; index < ifaces.Count; index++)
				{
					nout = ifaces[index].Outgoing;
					nin = ifaces[index].Incoming;
					oldout = oldifaces[index].Outgoing;
					oldin = oldifaces[index].Incoming;

					long totalerrors = nout.Errors + nin.Errors,
						totaldiscards = nout.Errors + nin.Errors,
						totalunicast = nout.Errors + nin.Errors,
						errorsInSample = (nin.Errors - oldin.Errors) + (nout.Errors - oldout.Errors),
						discards = (nin.Discards - oldin.Discards) + (nout.Discards - oldout.Discards),
						packets = (nin.Unicast - oldin.Unicast) + (nout.Unicast - oldout.Unicast);

					errorsSinceLastReport += errorsInSample;

					bool reportErrors = false;

					// TODO: Better error limiter.
					// Goals:
					// - Show initial error.
					// - Show errors in increasing rarity
					// - Reset the increased rarity once it becomes too rare

					//Logging.DebugMsg($"NETWORK - Errors: +{errorsInSample}, NotPeaked: {!ErrorReports.Peaked}, Level: {ErrorReports.Level}/{ErrorReports.Peak}");

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
					TimeSpan period = lastErrorReport.TimeTo(now);
					double pmins = period.TotalHours < 24 ? period.TotalMinutes : double.NaN; // NaN-ify too large periods

					if (reportErrors)
					{
						var sbs = new StringBuilder().Append(ifaces[index].Name).Append(" is suffering from traffic errors! (");

						bool longProblem = (errorsSinceLastReport > errorsInSample);

						if (longProblem) sbs.Append("+").Append(errorsSinceLastReport).Append(" errors, ").Append(errorsInSample).Append(" in last sample");
						else sbs.Append("+").Append(errorsInSample).Append(" errors in last sample");

						if (!double.IsNaN(pmins)) sbs.Append($"; {pmins:N1}").Append(" minutes since last report");
						sbs.Append(")");

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

		DeviceTraffic LastTraffic = new DeviceTraffic();
		public DeviceTraffic GetCurrentTraffic => LastTraffic;

		public UI.TrayAccess Tray { get; set; } = null; // bad design

		public bool NetworkAvailable { get; private set; } = false;
		public bool InternetAvailable { get; private set; } = false;

		readonly int MaxSamples = 20;
		readonly List<double> UptimeSamples = new List<double>(20);
		DateTimeOffset UptimeRecordStart; // since we started recording anything
		DateTimeOffset LastUptimeStart; // since we last knew internet to be initialized
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
				lock (uptime_lock)
				{
					return UptimeSamples.Count > 0 ? UptimeSamples.Average() : double.PositiveInfinity;
				}
			}
		}

		bool InternetAvailableLast = false;

		Stopwatch Downtime = null;
		void ReportCurrentUpstate()
		{
			if (InternetAvailable != InternetAvailableLast) // prevent spamming available message
			{
				if (InternetAvailable)
				{
					Downtime?.Stop();

					double downtime = Downtime?.Elapsed.TotalMinutes ?? double.NaN;
					Downtime = null;

					Log.Information("<Network> Internet available." + (double.IsNaN(downtime) ? "" : $"{downtime:N1} minutes downtime."));
				}
				else
				{
					Log.Warning("<Network> Internet unavailable.");
					Downtime = Stopwatch.StartNew();
				}

				InternetAvailableLast = InternetAvailable;
			}
		}

		void ReportUptime()
		{
			var sbs = new StringBuilder().Append("<Network> Average uptime: ");

			lock (uptime_lock)
			{
				var currentUptime = DateTimeOffset.UtcNow.TimeSince(LastUptimeStart).TotalMinutes;

				int cnt = UptimeSamples.Count;
				sbs.Append($"{(UptimeSamples.Sum() + currentUptime) / (cnt + 1):N1}").Append(" minutes");

				if (cnt >= 3)
					sbs.Append(" (").Append($"{(UptimeSamples.GetRange(cnt - 3, 3).Sum() / 3f):N1}").Append(" minutes for last 3 samples");
			}

			sbs.Append(" since: ").Append(UptimeRecordStart)
			   .Append(" (").Append($"{(DateTimeOffset.UtcNow - UptimeRecordStart).TotalHours:N2}").Append("h ago)")
			   .Append(".");

			Log.Information(sbs.ToString());
		}

		bool lastOnlineState = false;
		int DeviceStateRecordLimiter = 0;

		DateTimeOffset LastUptimeSample = DateTimeOffset.MinValue;

		void RecordUptimeState(bool online_state, bool address_changed)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "RecordUptimeState called after NetManager was disposed.");

			if (!Atomic.Lock(ref DeviceStateRecordLimiter)) return;

			try
			{
				var now = DateTimeOffset.UtcNow;
				if (LastUptimeSample.TimeTo(now).TotalMinutes < DeviceTimerInterval)
					return;

				LastUptimeSample = now;
				if (online_state != lastOnlineState)
				{
					lastOnlineState = online_state;

					if (online_state)
					{
						LastUptimeStart = now;

						Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(x => ReportCurrentUpstate());
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

		bool Notified = false;

		int InetCheckLimiter; // = 0;

		// TODO: Fix internet status checking.
		bool CheckInet(bool address_changed = false)
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "CheckInet called after NetManager was disposed.");

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
							Dns.GetHostEntry(dnstestaddress); // FIXME: There should be some other method than DNS testing
							InternetAvailable = true;
							Notified = false;
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
									timeout = true;
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

					if (Trace) RecordUptimeState(InternetAvailable, address_changed);

					if (oldInetAvailable != InternetAvailable)
					{
						InvalidateInterfaceList();
						ReportNetAvailability();
					}
					else
					{
						if (timeout)
							Log.Information("<Network> Internet availability test inconclusive, assuming connected.");

						if (!Notified && NetworkAvailable)
						{
							if (interrupt)
								Log.Warning("<Network> Internet check interrupted. Potential hardware/driver issues.");

							if (dnsfail)
								Log.Warning("<Network> DNS test failed, test host unreachable. Test host may be down.");

							Notified = dnsfail || interrupt;
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

		readonly List<IPAddress> AddressList = new List<IPAddress>(2);
		// List<NetworkInterface> PublicInterfaceList = new List<NetworkInterface>(2);
		IPAddress IPv4Address = IPAddress.None;
		NetworkInterface IPv4Interface;
		IPAddress IPv6Address = IPAddress.IPv6None;
		NetworkInterface IPv6Interface;

		void InterfaceInitialization()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "InterfaceInitialization called after NetManager was disposed.");

			bool ipv4 = false, ipv6 = false;
			NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface n in adapters)
			{
				if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
					continue;

				// TODO: Implement smarter looping; Currently allows getting address across NICs.

				IPAddress[] ipa = n.GetAddresses();
				foreach (IPAddress ip in ipa)
				{
					switch (ip.AddressFamily)
					{
						case System.Net.Sockets.AddressFamily.InterNetwork:
							IPv4Address = ip;
							IPv4Interface = n;
							ipv4 = true;
							// PublicInterfaceList.Add(n);
							break;
						case System.Net.Sockets.AddressFamily.InterNetworkV6:
							IPv6Address = ip;
							IPv6Interface = n;
							ipv6 = true;
							// PublicInterfaceList.Add(n);
							break;
					}

					if (ipv4 && ipv6) goto FoundAddresses;
				}

				if (ipv4 && ipv6) break;
			}

		FoundAddresses:;
		}

		readonly object interfaces_lock = new object();
		int InterfaceUpdateLimiter = 0;

		void InvalidateInterfaceList() => CurrentInterfaceList = new Lazy<List<Device>>(RecreateInterfaceList, false);

		List<Device> RecreateInterfaceList()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "UpdateInterfaces called after NetManager was disposed.");

			var ifacelistt = new List<Device>();

			try
			{
				if (DebugNet) Log.Verbose("<Network> Enumerating network interfaces...");

				// var ifacelist = new List<string[]>();

				var index = 0;
				NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface dev in adapters)
				{
					var ti = index++;
					if (dev.NetworkInterfaceType == NetworkInterfaceType.Loopback || dev.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
						continue;

					var stats = dev.GetIPStatistics();

					bool found4 = false, found6 = false;
					IPAddress _ipv4 = IPAddress.None, _ipv6 = IPAddress.None;
					foreach (UnicastIPAddressInformation ip in dev.GetIPProperties().UnicastAddresses)
					{
						switch (ip.Address.AddressFamily)
						{
							case System.Net.Sockets.AddressFamily.InterNetwork:
								_ipv4 = ip.Address;
								found4 = true;
								break;
							case System.Net.Sockets.AddressFamily.InterNetworkV6:
								_ipv6 = ip.Address;
								found6 = true;
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

					devi.Incoming.From(stats, true);
					devi.Outgoing.From(stats, false);
					// devi.PrintStats();
					ifacelistt.Add(devi);

					if (DebugNet) Log.Verbose("<Network> Interface: " + dev.Name);
				}
			}
			finally
			{

			}

			return ifacelistt;
		}

		public List<Device> GetInterfaces()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "GetInterfaces called after NetManager was disposed.");

			InvalidateInterfaceList();
			return CurrentInterfaceList.Value;
		}

		async void NetAddrChanged(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			var now = DateTimeOffset.UtcNow;

			bool AvailabilityChanged = InternetAvailable;

			await Task.Delay(0).ConfigureAwait(false); // asyncify

			CheckInet(address_changed: true);
			AvailabilityChanged = AvailabilityChanged != InternetAvailable;

			if (InternetAvailable)
			{
				IPAddress oldV6Address = IPv6Address;
				IPAddress oldV4Address = IPv4Address;

				InterfaceInitialization(); // Update IPv4Address & IPv6Address

				bool ipv4changed, ipv6changed, ipchanged = false;
				ipchanged |= ipv4changed = !oldV4Address.Equals(IPv4Address);

				var sbs = new StringBuilder();

				if (AvailabilityChanged)
				{
					sbs.AppendLine("Internet restored.");
					Log.Information("<Network> Internet connection restored.");
				}

				ipchanged |= ipv6changed = !oldV6Address.Equals(IPv6Address);

				if (ipchanged)
				{
					if (ipv4changed)
					{
						sbs.AppendLine("IPv4 address changed.").AppendLine(IPv4Address.ToString());
						Log.Information($"<Network> IPv4 address changed: {oldV4Address} → {IPv4Address}");
					}
					if (ipv6changed)
					{
						sbs.AppendLine("IPv6 address changed.").AppendLine(IPv6Address.ToString());
						Log.Information($"<Network> IPv6 address changed: {oldV6Address} → {IPv6Address}");
					}

					Tray.Tooltip(4000, sbs.ToString(), Name, System.Windows.Forms.ToolTipIcon.Warning);

					// bad since if it's not clicked, we react to other tooltip clicks, too
					// TODO: Need replaceable callback or something.
					//Tray.TrayTooltipClicked += (s, e) => { /* something */ };

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

			NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
			NetworkChange.NetworkAddressChanged += NetAddrChanged;

			// CheckInet().Wait(); // unnecessary?
		}

		bool LastReportedNetAvailable = false;
		bool LastReportedInetAvailable = false;

		void ReportNetAvailability()
		{
			if (DisposedOrDisposing) throw new ObjectDisposedException(nameof(Manager), "ReportNetAvailability called after NetManager was disposed.");

			bool changed = (LastReportedInetAvailable != InternetAvailable) || (LastReportedNetAvailable != NetworkAvailable);
			if (!changed) return; // bail out if nothing has changed

			var sbs = new StringBuilder()
				.Append("<Network> Status: ")
				.Append(NetworkAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(", Internet: ")
				.Append(InternetAvailable ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected)
				.Append(" - ");

			if (NetworkAvailable && !InternetAvailable) sbs.Append("Route problems");
			else if (!NetworkAvailable) sbs.Append("Cable unplugged or router/modem down");
			else sbs.Append("All OK");

			if (!NetworkAvailable || !InternetAvailable) Log.Warning(sbs.ToString());
			else Log.Information(sbs.ToString());

			LastReportedInetAvailable = InternetAvailable;
			LastReportedNetAvailable = NetworkAvailable;
		}

		/// <summary>
		/// Non-blocking lock for NetworkChanged event output
		/// </summary>
		int NetworkChangeAntiFlickerLock = 0;
		/// <summary>
		/// For tracking how many times NetworkChanged is triggered
		/// </summary>
		int NetworkChangeCounter = 4; // 4 to force fast inet check on start
		/// <summary>
		/// Last time NetworkChanged was triggered
		/// </summary>
		DateTimeOffset LastNetworkChange = DateTimeOffset.MinValue;
		async void NetworkChanged(object _, EventArgs _ea)
		{
			if (DisposedOrDisposing) return;

			var oldNetAvailable = NetworkAvailable;
			bool available = NetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

			LastNetworkChange = DateTimeOffset.UtcNow;

			NetworkChangeCounter++;

			NetworkStatusChange?.Invoke(this, new Status { Available = available });

			// do stuff only if this is different from last time
			if (oldNetAvailable != available)
			{
				if (Atomic.Lock(ref NetworkChangeAntiFlickerLock))
				{
					try
					{
						await Task.Delay(0).ConfigureAwait(false);

						int loopbreakoff = 0;
						while (LastNetworkChange.TimeTo(DateTimeOffset.UtcNow).TotalSeconds < 5)
						{
							if (loopbreakoff++ >= 3) break; // arbitrary end based on double reconnect behaviour of some routers
							if (NetworkChangeCounter >= 4) break; // break off in case NetworkChanged event is received often enough
							await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
						}

						CheckInet();
						NetworkChangeCounter = 0;
						ReportNetAvailability();
					}
					finally
					{
						Atomic.Unlock(ref NetworkChangeAntiFlickerLock);
					}
				}

				ReportNetAvailability();
			}
			else
			{
				if (DebugNet) Log.Debug("<Net> Network changed but still as available as before.");
			}
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs> OnDisposed;

		public void Dispose() => Dispose(true);

		bool DisposedOrDisposing = false;
		void Dispose(bool disposing)
		{
			if (DisposedOrDisposing) return;
			DisposedOrDisposing = true;

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
			}

			OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
			OnDisposed = null;
		}

		public void ShutdownEvent(object sender, EventArgs ea)
		{
			SampleTimer?.Stop();
		}
		#endregion
	}
}