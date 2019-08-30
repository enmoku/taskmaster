﻿//
// ProcessModificationEventArgs.cs
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

using System.Diagnostics;

namespace Taskmaster.Process
{
	public delegate void ModificationDelegate(ModificationInfo ea);

	public class ModificationInfo
	{
		public ModificationInfo(Process.ProcessEx info) => Info = info;

		public Process.ProcessEx Info { get; set; }

		public ProcessPriorityClass? PriorityNew { get; set; } = null;
		public ProcessPriorityClass? PriorityOld { get; set; } = null;
		public int AffinityNew { get; set; } = -1;
		public int AffinityOld { get; set; } = -1;

		public bool AffinityFail { get; set; } = false;
		public bool PriorityFail { get; set; } = false;

		public Process.IOPriority NewIO { get; set; } = Process.IOPriority.Ignore;

		/// <summary>
		/// Text for end-users.
		/// </summary>
		public System.Text.StringBuilder? User { get; set; } = null;
	}
}
