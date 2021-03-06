﻿//
// UniForm.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2020 M.A.
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
using System.Windows.Forms;

namespace Taskmaster.UI
{
	public partial class UniForm : Form
	{
		readonly protected Padding BigPadding = new Padding(6);
		readonly protected Padding SmallPadding = new Padding(3);
		readonly protected Padding LeftSubPadding = new Padding(12, 3, 3, 3);

		static readonly protected Lazy<System.Drawing.Font> _BoldFont = new Lazy<System.Drawing.Font>(() => new System.Drawing.Font(new Control().Font, System.Drawing.FontStyle.Bold), false);
		static protected System.Drawing.Font BoldFont => _BoldFont.Value;

		public UniForm(bool centerOnScreen = false)
		{
			//_ = Handle; // forces handle creation

			AutoScaleMode = AutoScaleMode.Dpi;

			AllowTransparency = false;
			DoubleBuffered = true;

			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
			//SetStyle(ControlStyles.CacheText, true); // performance
			UpdateStyles();

			Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

			MinimizeBox = false;
			MaximizeBox = false;

			StartPosition = centerOnScreen ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;

			//Padding = CustomPadding;

			AutoSize = true;
		}

		bool disposed;

		protected override void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Icon?.Dispose();
				Icon = null;
			}

			base.Dispose(disposing);
		}

		public bool DialogOK => DialogResult == DialogResult.OK;


		// static finalizer
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Static finalizer")]
		static readonly Finalizer finalizer = new Finalizer();

		sealed class Finalizer
		{
			~Finalizer()
			{
				Logging.DebugMsg("UniForm static finalization");
				if (_BoldFont.IsValueCreated)
					_BoldFont.Value.Dispose(); // unnecessary
			}
		}
	}
}
