//
// SelfAwareness.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Serilog;

namespace Taskmaster
{
	/// <summary>
	/// Module for monitoring the app itself.
	/// </summary>
	sealed public class SelfAwareness : IDisposable
	{
		static readonly object AwarenessMap_lock = new object();
		readonly static ConcurrentDictionary<int, Awareness> AwarenessMap = new ConcurrentDictionary<int, Awareness>();

		static ConcurrentQueue<int> FreeKeys = new ConcurrentQueue<int>();
		static int NextKey = 1;
		DateTime NextDue = DateTime.MinValue;
		static readonly object FreeKeys_lock = new object();

		readonly System.Timers.Timer AwarenessTicker;

		public SelfAwareness()
		{
			NextDue = DateTime.Now.AddSeconds(5);
			AwarenessTicker = new System.Timers.Timer(15 * 1000);
			AwarenessTicker.Elapsed += Assess;
		}

		/// <summary>
		/// Create a minder that will be raised if it still exists past due time. Unmind to no longer track it.
		/// Best used by wrapping this around using block around minded object.
		/// </summary>
		/// <returns>The mind.</returns>
		/// <param name="message">Message to post when things go wrong.</param>
		/// <param name="due">Due date/time.</param>
		/// <param name="callback">Callback.</param>
		/// <param name="callbackObject">Object given to the callback.</param>
		/// <param name="method">Autofilled by CallerMemberName.</param>
		/// <param name="file">Autofilled by CallerFilePath.</param>
		/// <param name="line">Autofilled by CallerLineNumber.</param>
		public static Awareness Mind(DateTime due, string message = null, Action<object> callback = null, object callbackObject = null,
							   [CallerMemberName] string method = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			var awn = new Awareness(due, message, callback, callbackObject, method, file, line);

			AddAwareness(awn);

			return awn;
		}

		public static void Unmind(int key) => RemoveAwareness(key);

		static void AddAwareness(Awareness awn)
		{
			lock (AwarenessMap_lock)
			{
				if (AwarenessMap.ContainsKey(awn.Key))
				{
					Log.Fatal("Id:{0} (Count:{1}) :: {2}", awn.Key, AwarenessMap.Count, awn.Method);
					return;
				}

				AwarenessMap.TryAdd(awn.Key, awn);

				// Log.Debug("<<Self-Awareness>> Added [{Key}] {Method}", awn.Key, awn.Method);
			}
		}

		static void RemoveAwareness(int key)
		{
			lock (AwarenessMap_lock)
			{
				if (AwarenessMap.TryRemove(key, out Awareness awn))
				{
					// Log.Debug("<<Self-Awareness>> Removing [{Key}] {Method}", awn.Key, awn.Method);
				}
			}
		}

		static internal int GetKey()
		{
			var rv = 0;
			lock (FreeKeys_lock)
			{
				if (FreeKeys.Count > 0 && FreeKeys.TryDequeue(out rv))
				{
					// Log.Debug("<<Self-Awareness>> GetKey: FreeKeys.Dequeue() = {rv}", rv);
				}
				else
				{
					rv = NextKey++;
					// Log.Debug("<<Self-Awareness>> GetKey: NextKey++ = {rv}", rv);
				}
			}

			return rv;
		}

		static internal void FreeKey(int key)
		{
			lock (FreeKeys_lock)
				FreeKeys.Enqueue(key);

		}

		void Assess(object sender, EventArgs ev)
		{
			var now = DateTime.Now;
			Stack<int> clearList = new Stack<int>();

			lock (AwarenessMap_lock)
			{
				if (AwarenessMap.Count > 0)
				{
					foreach (var awnPair in AwarenessMap)
					{
						var awn = awnPair.Value;

						if (awn.Due <= now)
						{
							awn.Tick++;

							TimeSpan late = (now - awn.Due);

							if (awn.Tick == 1)
							{
								awn.Overdue = true;
								awn.Due = awn.Due.AddSeconds(5);

								if (awn.Message != null)
								{
									Log.Fatal("<<Self-Awareness>> {Method} hung [Late: {Seconds}s, Tick: {Tick}] – {Message}",
											  awn.Method, string.Format("{0:N1}", late.TotalSeconds), awn.Tick, awn.Message);
								}
								else
								{
									Log.Fatal("<<Self-Awareness>> {Method} hung [Late: {Seconds}s, Tick: {Tick}]",
											  awn.Method, string.Format("{0:N1}", late.TotalSeconds), awn.Tick);
								}

								if (awn.Callback != null)
									awn.Callback.Invoke(awn.UserObject);
							}

							if (awn.Tick >= 3) clearList.Push(awn.Key);
						}
						else
						{
							// Log.Debug("<<Self-Awareness>> Checked [{Key}] {Method} – due in: {Sec}", awn.Key, awn.Value.Method, (now - awn.Value.Due).TotalSeconds);
						}

						awn = null;
					}
				}
			}

			while (clearList.Count > 0)
			{
				var key = clearList.Pop();
				if (AwarenessMap.TryGetValue(key, out Awareness awn))
				{
					RemoveAwareness(key);
				}
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed = false;
		void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing)
			{
				if (Taskmaster.Trace)
					Log.Verbose("Disposing self-awareness...");

				AwarenessTicker?.Dispose();
			}

			disposed = true;
		}
	}

	sealed public class Awareness : IDisposable
	{
		public int Key;

		public int Tick = 0;

		public bool Overdue = false;

		public DateTime Start;
		public DateTime Due;

		public string Message;

		public string File;
		public string Method;
		public int Line;

		public bool LogEnd = false;

		public Action<object> Callback;
		public object UserObject;

		public Awareness(DateTime due, string message = null, Action<object> callback = null, object callbackObject = null,
							   [CallerMemberName] string method = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			Key = SelfAwareness.GetKey();

			Start = DateTime.Now;
			Message = message;
			Due = due;
			Callback = callback;
			UserObject = callbackObject;
			Method = method;
			File = file;
			Line = line;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		bool disposed; // = false;
		void Dispose(bool disposing)
		{
			if (disposed)
			{
				TimeSpan late = (Start - DateTime.Now);
				Log.Debug("<<Self-Awareness>> Redispose: {Method} [After: {Seconds}s]",
					Key, Method, string.Format("{0:N1}", late.TotalSeconds));
				return;
			}

			// base.Dispose(disposing);

			if (disposing)
			{
				if (Overdue && Tick <= 3)
				{
					TimeSpan late = (DateTime.Now - Start);
					Log.Fatal("<<Self-Awareness>> {Method} recovered [After: {Seconds}s]",
						Method, string.Format("{0:N1}", late.TotalSeconds));
				}

				// Log.Debug("<<Self-Awareness>> Dispose [{Key}] {Method} [Time: {N}s]", Key, Method, string.Format("{0:N2}", (DateTime.Now - Start).TotalSeconds));
				SelfAwareness.Unmind(Key);
				SelfAwareness.FreeKey(Key);
			}

			disposed = true;
		}
	}
}