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

using System;
using System.IO;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Taskmaster
{
	sealed public class LogEventArgs : EventArgs
	{
		public readonly string Message;
		public readonly LogEventLevel Level;
		public readonly LogEvent Internal;

		public LogEventArgs(string message, LogEventLevel level, LogEvent ev)
		{
			Message = message;
			Level = level;
			Internal = ev;
		}
	}

	static class MemoryLog
	{
		public static MemorySink MemorySink = null;
	}

	sealed class MemorySink : Serilog.Core.ILogEventSink, IDisposable
	{
		public event EventHandler<LogEventArgs> onNewEvent;

		readonly TextWriter p_output;
		readonly object sinklock = new object();
		readonly IFormatProvider p_formatProvider;
		readonly ITextFormatter p_textFormatter;
		public LoggingLevelSwitch LevelSwitch;
		const string p_DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

		public MemorySink(IFormatProvider formatProvider, string outputTemplate = p_DefaultOutputTemplate, LoggingLevelSwitch levelSwitch = null)
		{
			Logs = new System.Collections.Generic.List<LogEventArgs>(Max);

			p_formatProvider = formatProvider;
			p_textFormatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter(
				outputTemplate ?? p_DefaultOutputTemplate,
				p_formatProvider
			);
			p_output = new System.IO.StringWriter();
			LevelSwitch = levelSwitch;

			MemoryLog.MemorySink = this;
		}

		public int Max = 50;
		readonly object LogLock = new object();
		public System.Collections.Generic.List<LogEventArgs> Logs = null;

		//public LoggingLevelSwitch LevelSwitch;

		public void Clear() { lock (LogLock) Logs.Clear(); }

		public void ExcludeDebug() => LevelSwitch.MinimumLevel = LogEventLevel.Information;
		public void ExcludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Debug;
		public void IncludeTrace() => LevelSwitch.MinimumLevel = LogEventLevel.Verbose;

		public void Emit(LogEvent e)
		{
			if (e.Level == LogEventLevel.Fatal) Statistics.FatalErrors++;

			if ((int)e.Level < (int)LevelSwitch.MinimumLevel) return;

			string formattedtext = string.Empty;

			lock (sinklock) // is lock faster than repeated new? Probably should try spinlock?
			{
				try
				{
					p_textFormatter.Format(e, p_output);
					formattedtext = p_output.ToString();
				}
				catch (OutOfMemoryException) { throw; }
				catch
				{
					return; // ignore, kinda
				}
				finally
				{
					((System.IO.StringWriter)p_output).GetStringBuilder().Clear(); // empty, weird results if not done.
				}
			}

			Emit(this, new LogEventArgs(formattedtext, e.Level, e));
		}

		void Emit(object _, LogEventArgs ea)
		{
			lock (LogLock)
			{
				if (Logs.Count > Max) Logs.RemoveAt(0);
				Logs.Add(ea);
			}

			onNewEvent?.Invoke(_, ea);
		}

		public LogEventArgs[] ToArray()
		{
			lock (LogLock) return Logs.ToArray();
		}

		public void Dispose() => Dispose(true);

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace)
					Log.Verbose("Disposing memory sink...");

				onNewEvent = null;

				p_output?.Dispose();
			}

			MemoryLog.MemorySink = null;

			disposed = true;
		}
	}

	public static class MemorySinkExtensions
	{
		public static Serilog.LoggerConfiguration MemorySink(
			this LoggerSinkConfiguration logConf,
			IFormatProvider formatProvider = null,
			string outputTemplate = null,
			LoggingLevelSwitch levelSwitch = null
		)
		{
			return logConf.Sink(new MemorySink(formatProvider, outputTemplate, levelSwitch));
		}
	}
}