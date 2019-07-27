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
using System.Diagnostics;
using Ini = MKAh.Ini;

namespace Taskmaster.Configuration
{
	public class File : IFile, IDisposable
	{
		public Ini.Config Config { get; private set; } = null;

		public string Filename { get; } = null;

		//string Path { get; } = null;

		public int Shared { get; internal set; } = 0;

		public event EventHandler<FileEvent> OnUnload;
		public event EventHandler<FileEvent> OnSave;

		public File(Ini.Config config, string filename)
		{
			Config = config;
			Filename = filename;
			config.ResetChangeCount();
		}

		public void Save(bool force = false)
		{
			System.Diagnostics.Debug.Assert(Config != null);

			Logging.DebugMsg("ConfigFile.Save(" + Filename + ") - Forced: " + force + ", Changes: " + Config.Changes.ToString());

			if (force || Config.Changes > 0)
			{
				OnSave?.Invoke(this, new FileEvent(this));

				Config.ResetChangeCount();
			}
		}

		bool UnloadRequested = false;

		public void Unload()
		{
			if (Disposed) throw new ObjectDisposedException(nameof(File), "Configuration.File.Unload after Dispose()");

			System.Diagnostics.Debug.Assert(Config != null);

			if (Shared > 0) UnloadRequested = true;

			if (Config.Changes > 0) Save();

			OnUnload?.Invoke(this, new FileEvent(this));

			Config = null;
		}

		/// <summary>
		/// Get disposable version of the config file.
		/// </summary>
		/// <returns></returns>
		public ScopedFile AutoUnloader()
		{
			Shared++;
			return new ScopedFile(this);
		}

		internal void ScopeReturn()
		{
			if (--Shared == 0 && UnloadRequested) Unload();
		}

		internal void Stagnate()
		{
			Debug.WriteLine("Configuration.File.Stagnate(" + Filename + ")");
			Config.Stagnate();
		}

		/// <summary>
		/// Replace internal configuration.
		/// </summary>
		public void Replace(Ini.Config config)
		{
			Config = config;
		}

		#region IDisposable Support
		bool Disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (Disposed) return;

			if (disposing)
			{
				if (Config.Changes > 0) Save();
				Unload();
			}

			Disposed = true;
		}

		public void Dispose() => Dispose(true);
		#endregion
	}

	public class ScopedFile : IFile, IDisposable
	{
		public File File { get; } = null;

		public Ini.Config Config => File.Config;

		internal ScopedFile(File file) => File = file;

		#region IDisposable Support
		bool Disposed = false;

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
