using System;
using Taskmaster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MKAh;

namespace Unit_Tests
{
	[TestClass]
	public class TimeTests
	{
		public TestContext TestContext { get; set; }

		[TestMethod]
		public void AffinityBitCountTests()
		{
			uint eticks = (uint)Environment.TickCount;
			uint pticks = NativeMethods.GetTickCount();

			Console.WriteLine("C# Env:   " + eticks);
			Console.WriteLine("P/Invoke: " + pticks);

			Assert.AreEqual(eticks/1000, pticks/1000);
		}

		[TestMethod]
		public void GetTickCountWrapAround()
		{
			uint diff = 5_000;
			uint lowTick = diff;
			uint highTick = uint.MaxValue - diff;

			long result = MKAh.User.CorrectIdleTime(highTick, lowTick);

			Console.WriteLine($"GetTickCount: {highTick} -> {lowTick} = {result}");

			Assert.AreEqual(diff*2L, result);
		}
	}
}
