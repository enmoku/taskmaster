//
// UI.HiddenWindow.cs
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

namespace Taskmaster
{
    using System;
    using System.Threading.Tasks;

	public static partial class Application
	{
		public static HiddenWindow hiddenwindow;
	}

	/// <summary>
	/// Core window for TM.
	/// </summary>
	sealed public class HiddenWindow : System.Windows.Forms.Form
	{
		public HiddenWindow()
		{
			CreateHandle(); // HACK
			_ = Handle; // just to make sure the above happens
			TopLevel = true;

			//Text = $"<{{[ {Taskmaster.Name} ]}}>";

			Logging.DebugMsg("HiddenWindow initialized");
		}

		public async Task InvokeAsync(Action action)
		{
			await Task.Delay(5).ConfigureAwait(false);

			try
			{
				if (InvokeRequired)
					BeginInvoke(action);
				else
					action();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		~HiddenWindow()
		{
			Logging.DebugMsg("HiddenWindow finalizer");
			System.GC.SuppressFinalize(this);
			Dispose(true);
		}
	}
}
