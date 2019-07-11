using System.Collections;
using System.Collections.Generic;

namespace Taskmaster
{
	/// <summary>
	/// Because dictionary compares byte arrays for reference equality when determining if they're unique keys...
	/// </summary>
	// Credit: https://stackoverflow.com/a/5601068/6761963
	public class StructuralEqualityComparer<T> : IEqualityComparer<T>
	{
		public bool Equals(T x, T y)
			=> StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

		public int GetHashCode(T obj)
			=> StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);

		public static StructuralEqualityComparer<T> Default { get; } = new StructuralEqualityComparer<T>();
	}
}
