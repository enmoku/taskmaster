using System;

namespace Taskmaster
{
	public static partial class IntPtrExtensions
	{
		public static long ToInt(this IntPtr value) => value.ToInt64();
	}
}
