﻿//
// Utility.cs
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
		static void AppendStacktace(Exception ex, ref List<string> output)
		{
			var projectdir = Properties.Resources.ProjectDirectory.Trim();
			var trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
			output.Add("");
			output.Add("----- Stacktrace -----");
			output.Add(trace);
		}

		public static void Stacktrace(Exception ex, bool crashsafe = false, [CallerMemberName] string method = "")
		{
			if (!crashsafe)
			{
				var projectdir = Properties.Resources.ProjectDirectory.Trim();
				var trace = ex.StackTrace.Replace(projectdir, HumanReadable.Generic.Ellipsis + System.IO.Path.DirectorySeparatorChar);
				Serilog.Log.Fatal($"Exception: {ex.GetType().Name} : {ex.Message} – Reported at {method}\n{trace}");
			}
			else
			{
				try
				{
					if (!System.IO.Directory.Exists(Taskmaster.logpath)) System.IO.Directory.CreateDirectory(Taskmaster.logpath);

					string logfilename = Taskmaster.UniqueCrashLogs ? $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")}.log" : "crash.log";
					var logfile = System.IO.Path.Combine(Taskmaster.logpath, logfilename);

					var now = DateTime.Now;

					var logcontents = new List<string>
					{
						"Datetime:     " + now.ToLongDateString() + " " + now.ToLongTimeString(),
						"",
						"Command line: " + Environment.CommandLine,
						"",
						"Exception:    " + ex.GetType().Name,
						"Message:      " + ex.Message
					};

					AppendStacktace(ex, ref logcontents);

					if (ex.InnerException != null)
						AppendStacktace(ex.InnerException, ref logcontents);

					System.IO.File.WriteAllLines(logfile, logcontents, System.Text.Encoding.Unicode);
					Debug.WriteLine("Crash log written to " + logfile);
				}
				catch
				{
					throw; // nothing to be done, we're already crashing and burning by this point
				}
			}
		}
	}
}