//
// Settings.Global.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019–2020 M.A.
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
using System.Diagnostics;

namespace Taskmaster
{
	public class PathCacheSettings
	{
		public int MaxItems { get; set; } = 200;
		public TimeSpan MaxAge { get; set; } = new TimeSpan(30, 0, 0);
	}

	public static partial class Application
	{
		public static bool NoLogging { get; set; } = false;

		public static bool ShowSplash { get; set; } = true;

		public static bool VisualStyling { get; set; } = true;

		public static bool ShowProcessAdjusts { get; set; } = true;
		public static bool ShowSessionActions { get; set; } = true;

		public static bool DebugAudio { get; set; } = false;

		public static bool DebugForeground { get; set; } = false;

		public static bool DebugPower { get; set; } = false;
		public static bool DebugMonitor { get; set; } = false;

		public static bool DebugSession { get; set; } = false;
		public static bool SaveDebugSettings { get; set; } = false;

		public static bool DebugMemory { get; set; } = false;

		public static bool Trace { get; set; } = false;
		public static bool ShowInaction { get; set; } = false;
		public static bool ShowAgency { get; set; } = false;

		// EXPERIMENTAL FEATURES
		public static bool TempMonitorEnabled { get; private set; } = false;
		public static bool LastModifiedList { get; private set; } = false;
		public static TimeSpan? RecordAnalysis { get; set; } = null;

		// DEBUG INFO
		public static bool DebugCache { get; private set; } = false;

		public static bool ShowOnStart { get; private set; } = false;
		public static bool ShowVolOnStart { get; private set; } = false;

		public static bool SelfOptimize { get; private set; } = true;
		public static ProcessPriorityClass SelfPriority { get; private set; } = ProcessPriorityClass.BelowNormal;
		public static bool SelfOptimizeBGIO { get; private set; } = false;
		public static int SelfAffinity { get; private set; } = 0;

		// public static bool LowMemory { get; private set; } = true; // low memory mode; figure out way to auto-enable this when system is low on memory

		public static int TempRescanDelay { get; set; } = 60 * 60_000; // 60 minutes
		public static int TempRescanThreshold { get; set; } = 1000;

		public static bool ExitConfirmation { get; set; } = true;
		public static bool GlobalHotkeys { get; set; } = false;

		internal static bool RestartElevated { get; set; } = false;
		internal static int RestartCounter { get; set; } = 0;

		public static BitmaskStyle LogBitmask { get; set; } = BitmaskStyle.Bits;

		public static BitmaskStyle AffinityStyle { get; set; } = BitmaskStyle.Bits;

		public enum BitmaskStyle
		{
			Bits = 0,
			Decimal = 1,
			Mixed = 2,
		}
	}
}
