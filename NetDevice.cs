//
// NetDevice.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2017-2018 M.A.
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
using System.Net;
using System.Net.NetworkInformation;

namespace Taskmaster
{
	public struct NetDeviceTraffic
	{
		public int Index { get; set; }
		public NetTraffic Delta { get; set; }
		public NetTraffic Total { get; set; }
	}

	public sealed class NetDeviceTrafficEventArgs : EventArgs
	{
		public NetDeviceTraffic Traffic;
	}

	public struct NetTraffic
	{
		/// <summary>
		/// Unicast packets
		/// </summary>
		public long Unicast { get; set; }
		/// <summary>
		/// Broadcast and Multicast packets
		/// </summary>
		public long NonUnicast { get; set; }

		public long Discards { get; set; }
		public long Errors { get; set; }

		/// <summary>
		/// Unknown packets, only for incoming data.
		/// </summary>
		public long Unknown { get; set; }

		public void From(IPInterfaceStatistics stats, bool incoming = true)
		{
			if (incoming)
			{
				Unicast = stats.UnicastPacketsReceived;
				NonUnicast = stats.NonUnicastPacketsReceived;
				Discards = stats.IncomingPacketsDiscarded;
				Errors = stats.IncomingPacketsWithErrors;
				Unknown = stats.IncomingUnknownProtocolPackets;
			}
			else
			{
				Unicast = stats.UnicastPacketsSent;
				NonUnicast = stats.NonUnicastPacketsSent;
				Discards = stats.OutgoingPacketsDiscarded;
				Errors = stats.OutgoingPacketsWithErrors;
			}
		}
	}

	sealed public class NetDevice
	{
		public int Index { get; set; }

		public string Name { get; set; } = string.Empty;
		public NetworkInterfaceType Type { get; set; } = NetworkInterfaceType.Unknown;
		public OperationalStatus Status { get; set; } = OperationalStatus.NotPresent;
		public long Speed { get; set; } = 0;
		public IPAddress IPv4Address { get; set; } = null;
		public IPAddress IPv6Address { get; set; } = null;

		// Stats
		public NetTraffic Outgoing;
		public NetTraffic Incoming;

		public void PrintStats()
		{
			Console.WriteLine(string.Format("Incoming: {0}(+{1}) Err({2}) Dis({3}) Unk({4})",
											Incoming.Unicast, Incoming.NonUnicast, Incoming.Errors, Incoming.Discards, Incoming.Unknown));
			Console.WriteLine(string.Format("Outgoing: {0}(+{1}) Err({2}) Dis({3})",
											Outgoing.Unicast, Outgoing.NonUnicast, Outgoing.Errors, Outgoing.Discards));
		}
	}
}