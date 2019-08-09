//
// Process.Legacy.cs
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

namespace Taskmaster.Process
{
	public enum LegacyLevel
	{
		Undefined,
		/// <summary>
		/// <para>No legacy support required.</para>
		/// </summary>
		None,
		/// <summary>
		/// <para>95, 98, ME</para>
		/// <para>Single core only.</para>
		/// </summary>
		Win95,
		/// <summary>
		/// <para>2000</para>
		/// <para>Single core may be preferable.</para>
		/// </summary>
		Win2k,
		/// <summary>
		/// <para>Multi-core fairly assured. Single core unnecessary.</para>
		/// </summary>
		Win7,
		/// <summary>
		/// <para>Single core completely unnecessary.</para>
		/// </summary>
		Win10,
	}
}
