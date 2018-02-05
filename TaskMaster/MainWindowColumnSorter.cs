//
// MainWindowColumnSorter.cs
//
// Author:
//       M.A. (enmoku) <>
//
// Copyright (c) 2018 M.A. (enmoku)
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
using System.Windows.Forms;

namespace TaskMaster
{
	public class MainWindowColumnSorter : IComparer
	{
		public MainWindowColumnSorter()
		{
			// Initialize the column to '0'
			Column = 0;

			// Initialize the sort order to 'none'
			Order = SortOrder.None;

			// Initialize the CaseInsensitiveComparer object
			ObjectCompare = new CaseInsensitiveComparer();
		}

		/// <summary>
		/// Specifies the column to be sorted
		/// </summary>
		private int Column;
		/// <summary>
		/// Specifies the order in which to sort (i.e. 'Ascending').
		/// </summary>
		private SortOrder Order;
		/// <summary>
		/// Case insensitive comparer object
		/// </summary>
		private CaseInsensitiveComparer ObjectCompare;

		/// <summary>
		/// This method is inherited from the IComparer interface.  It compares the two objects passed using a case insensitive comparison.
		/// </summary>
		/// <param name="x">First object to be compared</param>
		/// <param name="y">Second object to be compared</param>
		/// <returns>The result of the comparison. "0" if equal, negative if 'x' is less than 'y' and positive if 'x' is greater than 'y'</returns>
		public int Compare(object x, object y)
		{
			// Cast the objects to be compared to ListViewItem objects
			ListViewItem listviewX = (ListViewItem)x;
			ListViewItem listviewY = (ListViewItem)y;

			// Compare the two items
			int compareResult = ObjectCompare.Compare(listviewX.SubItems[Column].Text, listviewY.SubItems[Column].Text);

			// Calculate correct return value based on object comparison
			if (Order == SortOrder.Ascending)
			{
				// Ascending sort is selected, return normal result of compare operation
				return compareResult;
			}
			else if (Order == SortOrder.Descending)
			{
				// Descending sort is selected, return negative result of compare operation
				return (-compareResult);
			}
			else
			{
				// Return '0' to indicate they are equal
				return 0;
			}
		}
	}
}
