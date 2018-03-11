//
// SelfAwareness.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using Serilog;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TaskMaster
{
	/// <summary>
	/// Module for monitoring the app itself.
	/// </summary>
	public class SelfAwareness
	{
		static readonly object AwarenessMap_lock = new object();
		static Dictionary<int, Awareness> AwarenessMap;

		static Queue<int> FreeKeys = new Queue<int>();
		static int NextKey = 1;
		static readonly object FreeKeys_lock = new object();

		System.Threading.Timer AwarenessTicker;

		public SelfAwareness()
		{
			AwarenessMap = new Dictionary<int, Awareness>();

			NextDue = DateTime.Now.AddSeconds(5);
			AwarenessTicker = new System.Threading.Timer(Assess, null, 5 * 1000, 15 * 1000);
		}

		public static Awareness Mind(string message, DateTime due, Action<object> callback = null, object callbackObject = null,
							   [CallerMemberName] string method = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			var awn = new Awareness(message, due, callback, callbackObject, method, file, line);
			awn.Start = DateTime.Now;

			AddAwareness(awn);

			return awn;
		}

		public static void Unmind(int key)
		{
			RemoveAwareness(key);
		}

		static void AddAwareness(Awareness awn)
		{
			lock (AwarenessMap_lock)
			{
				Debug.Assert(AwarenessMap.ContainsKey(awn.Key) == false, string.Format("Id:{0} (Count:{1}) :: {2}", awn.Key, AwarenessMap.Count, awn.Method));

				AwarenessMap.Add(awn.Key, awn);
			}
		}

		static void RemoveAwareness(int key)
		{
			lock (AwarenessMap_lock)
			{
				AwarenessMap.Remove(key);
			}
		}

		static internal int GetKey()
		{
			int rv = 0;
			lock (FreeKeys_lock)
			{
				if (FreeKeys.Count > 0)
					rv = FreeKeys.Dequeue();
				else
					rv = NextKey++;
			}
			return rv;
		}

		static internal void FreeKey(int key)
		{
			lock (FreeKeys_lock)
			{
				FreeKeys.Enqueue(key);
			}
		}

		DateTime NextDue = DateTime.MinValue;
		void Assess(object state)
		{
			DateTime now = DateTime.Now;
			Stack<int> clearList = new Stack<int>();

			lock (AwarenessMap_lock)
			{
				if (AwarenessMap.Count > 0)
				{
					foreach (var awn in AwarenessMap)
					{
						if (awn.Value.Due <= now)
						{
							Log.Fatal("<<Self-Awareness>> {Method}:{Line} :: {Message} ({File})",
									  awn.Value.Method, awn.Value.Line,
									  awn.Value.Message,
									  awn.Value.File);
							Log.Fatal("<<Self-Awareness>> Due:{Due} – Now:{Now} – Late: {Late}s", awn.Value.Due, now, (now - awn.Value.Due).TotalSeconds);

							awn.Value.Callback?.Invoke(awn.Value.UserObject);

							clearList.Push(awn.Key);
						}
					}
				}
			}

			while (clearList.Count > 0)
			{
				int key = clearList.Pop();
				RemoveAwareness(key);
			}
		}
	}

	public class Awareness : IDisposable
	{
		public int Key;

		public DateTime Start;
		public DateTime Due;

		public string Message;

		public string File;
		public string Method;
		public int Line;

		public Action<object> Callback;
		public object UserObject;

		public Awareness(string message, DateTime due, Action<object> callback = null, object callbackObject = null,
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
		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			//base.Dispose(disposing);

			if (disposing)
			{
				if (Key > 0)
				{
					SelfAwareness.Unmind(Key);
					SelfAwareness.FreeKey(Key);
				}
				Key = -1;
			}

			disposed = true;
		}
	}
}
