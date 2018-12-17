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
}