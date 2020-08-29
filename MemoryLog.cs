//
// MemoryLog.cs
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

using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Taskmaster
{
	public delegate void LogEventDelegate(string message, LogEventLevel level, LogEvent ev);

	public class LogEventArgs : EventArgs
	{
		public readonly string Message;
		public readonly LogEventLevel Level;
		public readonly LogEvent Internal;
		public readonly ulong ID = LastID++;

		static ulong LastID;

		public LogEventArgs(string message, LogEventLevel level, LogEvent ev)
		{
			Message = message;
			Level = level;
			Internal = ev;
		}
	}

	static class MemoryLog
	{
		public static MemorySink? MemorySink;
	}

	class MemorySink : Serilog.Core.ILogEventSink, IDisposable
	{
		public event EventHandler<LogEventArgs>? OnNewEvent;

		readonly StringWriter Output;
		readonly object sinklock = new object();
		//readonly IFormatProvider p_formatProvider;
		readonly Serilog.Formatting.Display.MessageTemplateTextFormatter p_textFormatter;
		public LoggingLevelSwitch LevelSwitch;
		const string p_DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

		public int Max = 200;
		readonly object LogLock = new object();

		System.Collections.Generic.List<LogEventArgs> Logs { get; set; } = new System.Collections.Generic.List<LogEventArgs>(200);

		//ConcurrentDictionary<ulong, LogEventArgs> LogMap { get; set; } = new ConcurrentDictionary<ulong, LogEventArgs>(2, 200);

		public MemorySink(IFormatProvider formatProvider, string outputTemplate = p_DefaultOutputTemplate, LoggingLevelSwitch? levelSwitch = null)
		{
			//p_formatProvider = formatProvider;
			p_textFormatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter(
				outputTemplate ?? p_DefaultOutputTemplate,
				formatProvider
			);
			Output = new System.IO.StringWriter();
			LevelSwitch = levelSwitch;

			MemoryLog.MemorySink = this;
		}

#if DEBUG
		readonly List<TraceListener> listeners = new List<TraceListener>();
#endif

		[Conditional("DEBUG")]
		public void SetListener(TraceListener trace) => listeners.Add(trace);

		//public LoggingLevelSwitch LevelSwitch;

		public void Clear() { lock (LogLock) Logs.Clear(); }

		public void ExcludeDebug() => LevelSwitch.MinimumLevel = LogEventLevel.Information;
		public void ExcludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Debug;
		public void IncludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Verbose;

		public void Emit(LogEvent e)
		{
			if (e is null) throw new ArgumentNullException(nameof(e));

			if (e.Level == LogEventLevel.Fatal) Statistics.FatalErrors++;

			if ((int)e.Level < (int)LevelSwitch.MinimumLevel) return;

			string formattedtext = string.Empty;

			lock (sinklock) // is lock faster than repeated new? Probably should try spinlock?
			{
				try
				{
					p_textFormatter.Format(e, Output);
					formattedtext = Output.ToString();
				}
				catch (OutOfMemoryException) { throw; }
				catch
				{
					return; // ignore, kinda
				}
				finally
				{
					Output.GetStringBuilder().Clear(); // empty, weird results if not done.
				}
			}

#if DEBUG
			foreach (var listener in listeners)
				listener.Write(formattedtext);
#endif

			Emit(this, new LogEventArgs(formattedtext, e.Level, e));
		}

		void Emit(MemorySink sender, LogEventArgs ea)
		{
			lock (LogLock)
			{
				if (Logs.Count > Max) Logs.RemoveAt(0);
				Logs.Add(ea);
			}

			OnNewEvent?.Invoke(sender, ea);
		}

		public LogEventArgs[] ToArray()
		{
			lock (LogLock) return Logs.ToArray();
		}

		#region IDisposable
		private bool isdisposed;

		protected virtual void Dispose(bool disposing)
		{
			if (!isdisposed)
			{
				if (disposing)
				{
					Output?.Dispose();
				}

				OnNewEvent = null;
				MemoryLog.MemorySink = null;

				isdisposed = true;
			}
		}

		~MemorySink() => Dispose(disposing: false);

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}

	public static class MemorySinkExtensions
	{
		public static Serilog.LoggerConfiguration MemorySink(
			this LoggerSinkConfiguration logConf,
			IFormatProvider? formatProvider = null,
			string? outputTemplate = null,
			LoggingLevelSwitch? levelSwitch = null)
			=> logConf.Sink(new MemorySink(formatProvider, outputTemplate, levelSwitch));
	}
}