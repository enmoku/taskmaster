//
// HumanReadable.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
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

namespace Taskmaster
{
	/// <summary>
	/// String literals for various settings
	/// </summary>
	namespace HumanReadable
	{
		public static class Generic
		{
			public const string Undefined = "Undefined";

			public const string QualityOfLife = "Quality of Life";

			public const string Enabled = "Enabled";
			public const string Disabled = "Disabled";

			public const string Description = "Description";

			public const string Ignore = "Ignore";

			public const string Ellipsis = "…";
			public const string NotAvailable = "n/a";
			public const string Uninitialized = "Uninitialized";

			public const string Debug = "Debug";
			public const string Logging = "Logging";
		}

		public static class System
		{
			public static class Process
			{
				public const string Section = "Process";
				public const string Foreground = "Foreground";
				public const string Background = "Background";

				public const string Executable = "Executable";
				public const string Path = "Path";

				public const string Priority = "Priority";
				public const string PriorityClass = Priority + " class";
				public const string PriorityStrategy = Priority + " strategy";
				public const string Affinity = "Affinity";
				public const string AffinityStrategy = Affinity + " strategy";

				public const string Restart = "Restart";
				public const string Exit = "Exit";
				public const string Rescan = "Rescan";
			}
		}

		public static class Hardware
		{
			public const string Section = "Hardware";

			public const string Memory = "Memory";

			public static class CPU
			{
				// no dedicated section

				public static class Settings
				{
					public const string SampleInterval = "CPU sample interval";
					public const string SampleCount = "CPU sample count";

					public const string AffinityStyle = "Core affinity style";
				}
			}

			public static class Network
			{
				public const string Connected = "Connected";
				public const string Disconnected = "Disconnected";
			}

			public static class Audio
			{
				public const string Section = "Audio";

				public const string Volume = "Volume";
				public const string VolumeStrateg = Volume + " strategy";

				public const string Microphone = "Microphone";
			}

			public static class Monitor
			{
				public const string Section = "Monitor";
			}

			public static class Power
			{
				public const string Section = "Power";

				public const string Plan = Section + " plan";
				public const string Mode = Section + " mode";

				public const string BackgroundPowerdown = "Background powerdown";

				public const string AutoAdjust = "Auto-adjust";
				public const string RuleBased = "Rule-based";
				public const string Manual = "Manual";
			}
		}
	}
}
