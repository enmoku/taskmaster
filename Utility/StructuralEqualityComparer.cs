using System.Collections;
using System.Collections.Generic;

namespace Taskmaster
{
	/// <summary>
	/// Because dictionary compares byte arrays for reference equality when determining if they're unique keys...
	/// </summary>
	// Credit: https://stackoverflow.com/a/5601068/6761963
	public sealed class StructuralEqualityComparer<T> : IEqualityComparer<T>
	{
		public bool Equals(T x, T y)
		{
			return StructuralComparisons.StructuralEqualityComparer.Equals(x, y);
		}

		public int GetHashCode(T obj)
		{
			return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
		}

		private static StructuralEqualityComparer<T> defaultComparer;
		public static StructuralEqualityComparer<T> Default
		{
			get
			{
				StructuralEqualityComparer<T> comparer = defaultComparer;
				if (comparer == null)
				{
					comparer = new StructuralEqualityComparer<T>();
					defaultComparer = comparer;
				}
				return comparer;
			}
		}
	}
}
