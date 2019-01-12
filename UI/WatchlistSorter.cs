//
// WatchlistSorter.cs
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
using System.Collections;
using System.Linq;
using System.Windows.Forms;

namespace Taskmaster
{
	sealed public class WatchlistSorter : IComparer
	{
		public int Column { get; set; } = 0;
		public SortOrder Order { get; set; } = SortOrder.Ascending;
		public bool Number { get; set; } = false;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		readonly int[] NumberColumns = new int[] { };

		public WatchlistSorter(int[] numberColumns = null)
		{
			if (numberColumns != null)
				NumberColumns = numberColumns;
		}

		public int Compare(object x, object y)
		{
			var lix = (ListViewItem)x;
			var liy = (ListViewItem)y;
			var result = 0;

			Number = NumberColumns.Any(item => item == Column);

			if (!Number)
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);
			else
				result = Comparer.Compare(Convert.ToInt64(lix.SubItems[Column].Text), Convert.ToInt64(liy.SubItems[Column].Text));

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}
