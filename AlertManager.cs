//
// AlertManager.cs
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
using System.Collections.Generic;

namespace Taskmaster
{
	public class AlertManager : IDisposable
	{
		public event EventHandler onNewAlert;
		public event EventHandler onAlertCancel;

		public static AlertManager instance = null;

		public AlertManager()
		{
			if (instance != null) throw new InvalidOperationException();
			instance = this;
		}

		/// <summary>
		/// Retrieve active alerts.
		/// </summary>
		public object[] Active()
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Emit new alert.
		/// </summary>
		public void Emit(object sender, object alert)
		{
			throw new NotImplementedException();
			throw new ArgumentException();

			onNewAlert?.Invoke(sender, new EventArgs());
		}

		List<object> AlertList = new List<object>();
		HashSet<int> ActiveKeys = new HashSet<int>();
		Dictionary<int, object> KeyToAlertMap = new Dictionary<int, object>();

		/// <summary>
		/// Cancel an event.
		/// </summary>
		public void Cancel(object sender, object alert)
		{
			throw new NotImplementedException();
			throw new ArgumentException();

			onAlertCancel?.Invoke(sender, new EventArgs());
		}

		/// <summary>
		/// Checks if the defined alert is already active.
		/// </summary>
		public bool isActive(object sender, object alert)
		{
			throw new NotImplementedException();
		}

		#region IDisposable Support
		bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					AlertList.Clear();
					AlertList = null;
					ActiveKeys.Clear();
					ActiveKeys = null;
					KeyToAlertMap.Clear();
					KeyToAlertMap = null;
					instance = null;
				}

				disposed = true;
			}
		}

		public void Dispose() => Dispose(true);
		#endregion
	}

	struct AlertKey
	{
		public object Sender;
		public int Alert;
	}
}
