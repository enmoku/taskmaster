//
// Configuration.File.cs
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

namespace Taskmaster.Configuration
{
	public class File : IFile, IDisposable
	{
		public Ini.Config Config { get; private set; } = null;
		public string Filename { get; private set; } = null;
		string Path { get; set; } = null;

		public int Shared { get; internal set; } = 0;
		bool Dirty { get; set; } = false;

		public event EventHandler<FileEvent> OnUnload;
		public event EventHandler<FileEvent> OnSave;

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

			OnSave?.Invoke(this, new FileEvent(this));

			Dirty = false;
		}

		bool UnloadRequested = false;

		public void Unload()
		{
			if (Disposed) throw new ObjectDisposedException("Configuration.File.Unload after Dispose()");

			System.Diagnostics.Debug.Assert(Config != null);

			if (Shared > 0) UnloadRequested = true;

			if (Dirty) Save();

			OnUnload?.Invoke(this, new FileEvent(this));

			Config = null;
		}

		public ScopedFile BlockUnload()
		{
			Shared++;
			return new ScopedFile(this);
		}

		internal void ScopeReturn()
		{
			if (--Shared == 0 && UnloadRequested) Unload();
		}

		#region IDisposable Support
		private bool Disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (Disposed) return;

			if (disposing)
			{
				if (Dirty) Save();
				Unload();
			}

			Disposed = true;
		}

		public void Dispose() => Dispose(true);
		#endregion
	}

	public class ScopedFile : IFile, IDisposable
	{
		public File File { get; private set; } = null;

		public Ini.Config Config => File.Config;
		public void MarkDirty() => File.MarkDirty();

		internal ScopedFile(File file) => File = file;

		#region IDisposable Support
		private bool Disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (Disposed) return;

			if (disposing)
			{
				File.ScopeReturn();
			}

			Disposed = true;
		}

		~ScopedFile() => Dispose(false);

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
