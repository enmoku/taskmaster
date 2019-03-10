using System;
using Taskmaster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MKAh;

namespace Timing
{
	[TestClass]
	public class Time
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
		public void GetTickCountCorrection()
		{
			Assert.AreEqual(10_000L, MKAh.User.CorrectIdleTime(12_000, 22_000), "Normal");
		}

		[TestMethod]
		public void GetTickCountWrapAround()
		{
			uint diff = 5_000;
			uint lowTick = diff;
			uint highTick = uint.MaxValue - diff;

			long result = MKAh.User.CorrectIdleTime(highTick, lowTick);

			Console.WriteLine($"GetTickCount: {highTick} -> {lowTick} = {result}");

			Assert.AreEqual(diff*2L, result, "49 day wrap around failpoint");
		}
	}
}
