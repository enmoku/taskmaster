//
// Process.Options.cs
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskmaster.Process
{
	public enum PathVisibilityOptions : int
	{
		/// <summary>
		/// Process name. Usually executable name without extension.
		/// </summary>
		Process = -1,
		/// <summary>
		/// Partial path removes some basic elements that seem redundant.
		/// </summary>
		Partial = 1,
		/// <summary>
		/// Smart reduction of full path. Not always as smart as desirable.
		/// </summary>
		Smart = 2,
		/// <summary>
		/// Complete path.
		/// </summary>
		Full = 3,
		/// <summary>
		/// Invalid.
		/// </summary>
		Invalid = 0,
	}

	public enum IOPriority : int
	{
		Ignore = -1,
		Background = 0,
		Low = 1,
		Normal = 2,
	}
}
