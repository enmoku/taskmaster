using System;
using MKAh.Logic;
using NUnit.Framework;

namespace Processes
{
	[TestFixture]
	public class ProcessController
	{
		[Test]
		[TestOf(nameof(Bit.Count))]
		public void AffinityBitCountTests()
		{
			const int testSource = 192;
			const int testTarget = 240;
			Console.WriteLine("Source: " + testSource.ToString());
			Console.WriteLine("Target: " + testTarget.ToString());

			Assert.AreEqual(2, Bit.Count(testSource));
			Assert.AreEqual(4, Bit.Count(testTarget));

			int excesscores = Bit.Count(testTarget) - Bit.Count(testSource);
			Assert.AreEqual(2, excesscores);
		}

		[Test]
		[TestOf(nameof(Bit.Unset))]
		public void AffinityBitManipTests()
		{
			int mask = 0b11110000; // 240
			const int expected = 0b110000; // 48

			mask = Bit.Unset(mask, 7);
			mask = Bit.Unset(mask, 6);

			Assert.AreEqual(expected, mask);
		}

		[Test]
		[TestOf(nameof(Bit.Count))]
		public void CPUMaskTests()
		{
			const int fakecpucount = 8;
			const int expectedoffset = 23; // 0 to 23, not 1 to 24
			int offset = Bit.Count(int.MaxValue) - fakecpucount;
			Assert.AreEqual(expectedoffset, offset);
		}

		/// <summary>
		/// Makes sure Limit affinity strategy does not increase number of assigned cores from source.
		/// </summary>
		/// <param name="cores"></param>
		[Test]
		[TestOf(nameof(Taskmaster.Process.Utility.ApplyAffinityStrategy))]
		public void AffinityStrategyLimitActual()
		{
			int testSource, testTarget;

			switch (Environment.ProcessorCount)
			{
				case 8:
					testSource = 0b11000000;
					testTarget = 0b11110000;
					break;
				case 6:
					testSource = 0b100000;
					testTarget = 0b111000;
					break;
				case 4:
					testSource = 0b0100;
					testTarget = 0b1100;
					break;
				default:
					throw new NotImplementedException("Test not implemented for core count of " + Environment.ProcessorCount.ToString());
			}

			Taskmaster.Process.Manager.DebugProcesses = true;
			System.Diagnostics.Debug.AutoFlush = true;
			System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());

			Console.WriteLine("Source: " + Convert.ToString(testSource, 2));
			Console.WriteLine("Target: " + Convert.ToString(testTarget, 2));

			int testProduct = Taskmaster.Process.Utility.ApplyAffinityStrategy(
				testSource, testTarget, Taskmaster.Process.AffinityStrategy.Limit);

			Console.WriteLine("Result: " + Convert.ToString(testProduct, 2));

			Assert.AreEqual(Bit.Count(testSource), Bit.Count(testProduct));
		}

		[Test]
		[TestOf(nameof(Bit))]
		public void AffinityTests()
		{
			const int target = 240;
			const int source = 192;
			int testmask = target;

			const int testcpucount = 8;

			int excesscores = Bit.Count(target) - Bit.Count(source);
			TestContext.WriteLine("Excess: " + excesscores.ToString());
			if (excesscores > 0)
			{
				TestContext.WriteLine("Mask Base: " + Convert.ToString(testmask, 2));
				for (int i = 0; i < testcpucount; i++)
				{
					if (Bit.IsSet(testmask, i))
					{
						testmask = Bit.Unset(testmask, i);
						TestContext.WriteLine("Mask Modified: " + Convert.ToString(testmask, 2));
						if (--excesscores <= 0) break;
					}
					else
						TestContext.WriteLine("Bit not set: " + i.ToString());
				}
				TestContext.WriteLine("Mask Final: " + Convert.ToString(testmask, 2));
			}

			Assert.AreEqual(source, testmask);
		}
	}
}
