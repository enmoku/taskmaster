//
// Utility.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016-2018 M.A.
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
using System.Runtime.CompilerServices;

namespace Taskmaster
{
	/// <summary>
	/// Simplified wrappers for System.Threading.Interlocked stuff.
	/// </summary>
	public static class Atomic
	{
		/// <summary>
		/// Lock the specified lockvalue.
		/// Performs simple check and set swap of 0 and 1.
		/// 0 is unlocked, 1 is locked.
		/// Simplifies basic use of System.Threading.Interlocked.CompareExchange
		/// </summary>
		/// <returns>If lock was successfully acquired.</returns>
		/// <param name="lockvalue">Variable used as the lock.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Lock(ref int lockvalue)
		{
			Debug.Assert(lockvalue == 0 || lockvalue == 1);
			return (System.Threading.Interlocked.CompareExchange(ref lockvalue, 1, 0) == 0);
		}

		/// <summary>
		/// Release the lock.
		/// </summary>
		/// <param name="lockvalue">Variable used as the lock.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Unlock(ref int lockvalue)
		{
			Debug.Assert(lockvalue != 0);
			lockvalue = 0;
		}
	}

	public enum Trinary
	{
		False = 0,
		True = 1,
		Nonce = -1,
	}

	public static class TrinaryExtensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool True(this Trinary tri) => (tri == Trinary.True);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool False(this Trinary tri) => (tri == Trinary.False);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Nonce(this Trinary tri) => (tri == Trinary.Nonce);
	}

	public enum Timescale
	{
		Seconds,
		Minutes,
		Hours,
	}

	public static class Utility
	{
		public static void Swap<T>(ref T a, ref T b)
		{
			var temp = a;
			a = b;
			b = temp;
		}

		public static void Dispose<T>(ref T obj) where T : IDisposable
		{
			try
			{
				obj?.Dispose();
				obj = default(T);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		public static string TimescaleString(Timescale t)
		{
			switch (t)
			{
				case Timescale.Seconds:
					return "second(s)";
				case Timescale.Minutes:
					return "minute(s)";
				case Timescale.Hours:
					return "hour(s)";
			}

			return string.Empty;
		}

		public static double SimpleTime(double seconds, out Timescale scale)
		{
			Debug.Assert(seconds >= 0);

			double time = seconds;

			if (time > 7200.0)
			{
				time /= 3600.0;
				scale = Timescale.Hours;
			}
			else if (time > 120.0)
			{
				time /= 60.0;
				scale = Timescale.Minutes;
			}
			else
			{
				scale = Timescale.Seconds;
			}

			return time;
		}

		public static void LogAndDiscardException(Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}

	public static class Bit
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Set(int dec, int index) => Or(dec, (1 << index));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsSet(int dec, int index) => And(dec, (1 << index)) != 0;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Unset(int dec, int index) => And(dec, ~(1 << index));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Or(int dec1, int dec2) => dec1 | dec2;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int And(int dec1, int dec2) => dec1 & dec2;

		public static int Count(int i)
		{
			i = i - ((i >> 1) & 0x55555555);
			i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
			return (((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
		}

		public static int Fill(int num, int mask, int maxbits)
		{
			var bits = Count(num);

			for (int i = 0; i < 32; i++)
			{
				if (IsSet(mask, i))
				{
					if (!IsSet(num, i))
					{
						num = Set(num, i);
						bits++;
					}
				}
			}

			return num;
		}
	}

	public static class Logging
	{
		public static void Log(string text, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			Console.WriteLine($"{System.IO.Path.GetFileName(file)}_{member}({line}): {text}");
		}

		public static void Warn(string text, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			Serilog.Log.Warning($"{System.IO.Path.GetFileName(file)}_{member}({line}): {text}");
		}

		public static void Stacktrace(Exception ex, bool crashsafe = false, [CallerMemberName] string method = "")
		{
			if (!crashsafe)
			{
				Serilog.Log.Fatal($"{ex.GetType().Name} : {ex.Message}{Environment.NewLine}Reported at {method}{Environment.NewLine}{ex.StackTrace}");
			}
			else
			{
				try
				{
					string logpath = System.IO.Path.Combine(Taskmaster.datapath, "Logs");
					if (!System.IO.Directory.Exists(logpath)) System.IO.Directory.CreateDirectory(logpath);
					var logfile = System.IO.Path.Combine(logpath, "crash.log");

					var now = DateTime.Now;
					var logcontents = new System.Collections.Generic.List<string>
					{
						"Date:         " + now.ToLongDateString(),
						"Time:         " + now.ToLongTimeString(),
						"",
						"Command line: " + Environment.CommandLine,
						"",
						"Exception:    " + ex.GetType().Name,
						"Message:      " + ex.Message,
						"Site:         " + method,
						"",
						"----- Stacktrace -----",
						ex.StackTrace
					};

					if (ex.InnerException != null)
					{
						logcontents.Add("");
						logcontents.Add("------ Stacktrace -----");
						logcontents.Add(ex.InnerException.StackTrace);
					}

					System.IO.File.WriteAllLines(logfile, logcontents, System.Text.Encoding.Unicode);
					Console.WriteLine("Crash log written to " + logfile);
				}
				catch
				{
					throw; // nothing to be done, we're already crashing and burning by this point
				}
			}
		}
	}

	public static class User
	{
		/// <summary>
		/// Pass this to IdleFor(uint).
		/// </summary>
		/// <returns>Ticks since boot.</returns>
		public static uint LastActive()
		{
			var info = new NativeMethods.LASTINPUTINFO();
			info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
			info.dwTime = 0;
			bool rv = NativeMethods.GetLastInputInfo(ref info);
			if (rv) return info.dwTime;

			return uint.MinValue;
		}

		/// <summary>
		/// Should be called in same thread as LastActive. Odd behaviour expected if the code runs on different core.
		/// </summary>
		/// <param name="lastActive">Last active time, as returned by LastActive</param>
		/// <returns>Seconds for how long user has been idle</returns>
		public static double IdleFor(uint lastActive)
		{
			double eticks = Convert.ToDouble(Environment.TickCount);
			double uticks = Convert.ToDouble(lastActive);
			return (eticks - uticks) / 1000f;
		}
	}
}