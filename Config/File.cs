//
// Config.File.cs
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

using System;
using Ini = MKAh.Ini;

namespace Taskmaster.Config
{
	public class File : IDisposable
	{
		public Ini.Config Config { get; private set; } = null;
		public string Filename { get; private set; } = null;
		string Path { get; set; } = null;

		bool Dirty { get; set; } = false;

		public event EventHandler onUnload;
		public event EventHandler onSave;

		public File(Ini.Config config, string filename)
		{
			Config = config;
			Filename = filename;
		}

		public void MarkDirty()
		{
			System.Diagnostics.Debug.Assert(Config != null);

			Dirty = true;
		}

		public void Save(bool force = false)
		{
			System.Diagnostics.Debug.Assert(Config != null);

			if (force) Dirty = true;

			if (!Dirty) return;

			onSave?.Invoke(this, EventArgs.Empty);

			Dirty = false;
		}

		public void Unload()
		{
			System.Diagnostics.Debug.Assert(Config != null);

			if (Dirty) Save();

			onUnload?.Invoke(this, EventArgs.Empty);

			Config = null;
		}

		#region IDisposable Support
		private bool disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					if (Dirty) Save();
					Unload();
				}

				disposed = true;
			}
		}

		public void Dispose() => Dispose(true);
		#endregion
	}
}
