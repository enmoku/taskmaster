﻿//
// UI.Extensions.Buffered.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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

using System.Windows.Forms;

namespace Taskmaster.UI.Extensions
{
	public class Button : System.Windows.Forms.Button
	{
		public Button()
		{
			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}

	public class TabPage : System.Windows.Forms.TabPage
	{
		public TabPage(string name)
			: base(name)
		{
			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}

	public class TabControl : System.Windows.Forms.TabControl
	{
		public TabControl()
		{
			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}

	public class TableLayoutPanel : System.Windows.Forms.TableLayoutPanel
	{
		public TableLayoutPanel()
		{
			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}

	public class TextBox : System.Windows.Forms.TextBox
	{
		public TextBox()
		{
			DoubleBuffered = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}

	public class Label : System.Windows.Forms.Label
	{
		public Label()
		{
			DoubleBuffered = true;
			AutoSize = true;

			SetStyle(
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage,
				true);
			UpdateStyles();
		}
	}
}
