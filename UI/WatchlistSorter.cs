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
using MKAh;

namespace Taskmaster
{
	public class WatchlistSorter : IComparer
	{
		public int Column { get; set; } = 0;
		public SortOrder Order { get; set; } = SortOrder.Ascending;
		public bool Number { get; set; } = false;
		public bool Priority { get; set; } = false;
		public bool SortPower { get; set; } = false;

		readonly CaseInsensitiveComparer Comparer = new CaseInsensitiveComparer();

		readonly int[] NumberColumns = Array.Empty<int>();
		readonly int PriorityColumn = -1;
		readonly int PowerColumn = -1;

		public WatchlistSorter(int[] numberColumns = null, int priorityColumn = -1, int powerColumn = -1)
		{
			if (numberColumns != null)
				NumberColumns = numberColumns;
			if (priorityColumn > 0)
				PriorityColumn = priorityColumn;
			if (powerColumn > 0)
				PowerColumn = powerColumn;
		}

		public int Compare(object x, object y)
		{
			var lix = (ListViewItem)x;
			var liy = (ListViewItem)y;

			Number = NumberColumns.Contains(Column);
			Priority = PriorityColumn == Column;
			SortPower = PowerColumn == Column;

			int result;
			if (Priority)
			{
				int lixp = lix.SubItems[Column].Text.Length > 0 ? MKAh.Utility.ProcessPriority(lix.SubItems[Column].Text).ToInt32() : -1;
				int liyp = liy.SubItems[Column].Text.Length > 0 ? MKAh.Utility.ProcessPriority(liy.SubItems[Column].Text).ToInt32() : -1;
				result = Comparer.Compare(lixp, liyp);
			}
			else if (SortPower)
			{
				string lixs = lix.SubItems[Column].Text;
				string liys = liy.SubItems[Column].Text;

				int lixp = lixs.Length > 0 ? (int)Power.Utility.GetModeByName(lixs) : (int)Power.Mode.Undefined;
				int liyp = liys.Length > 0 ? (int)Power.Utility.GetModeByName(liys) : (int)Power.Mode.Undefined;
				result = Comparer.Compare(lixp, liyp);
			}
			else if (Number)
				result = Comparer.Compare(Convert.ToInt64(lix.SubItems[Column].Text), Convert.ToInt64(liy.SubItems[Column].Text));
			else
				result = Comparer.Compare(lix.SubItems[Column].Text, liy.SubItems[Column].Text);

			return Order == SortOrder.Ascending ? result : -result;
		}
	}
}
