﻿using System;
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
		[Test]
		[TestOf(nameof(Taskmaster.Process.Utility.ApplyAffinityStrategy))]
		[TestCase(4, 0b0100, 0b1100, ExpectedResult = 1)]
		[TestCase(6, 0b100000, 0b111000, ExpectedResult = 1)]
		[TestCase(8, 0b11000000, 0b11110000, ExpectedResult = 2)]
		[TestCase(24, 0b111001110000000000000000, 0b000001010000000000000000, ExpectedResult = 2)]
		public long AffinityStrategyLimitActual(int cores, int source, int target)
		{
			//int cores = Taskmaster.Hardware.Utility.ProcessorCount;

			Taskmaster.Process.Utility.Debug = true;

			System.Diagnostics.Debug.AutoFlush = true;
			System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());

			Console.WriteLine("Source: " + Convert.ToString(source, 2).PadLeft(cores, '0'));
			Console.WriteLine("Target: " + Convert.ToString(target, 2).PadLeft(cores, '0'));

			long product = Taskmaster.Process.Utility.ApplyAffinityStrategy(
				source, target, Taskmaster.Process.AffinityStrategy.Limit);

			Console.WriteLine("Result: " + Convert.ToString(product, 2).PadLeft(cores, '0'));

			return Bit.Count(product);
		}

		[Test]
		[TestOf(nameof(Taskmaster.Process.Utility.ApplyAffinityStrategy))]
		[TestCase(0b0001, 0b0001, 0b0001, ExpectedResult = 0b0001)] // nop
		[TestCase(0b0001, 0b1101, 0b0001, ExpectedResult = 0b0001)] // unset
		[TestCase(0b1101, 0b1100, 0b1100, ExpectedResult = 0b1100)] // unset; this can fail if the core freeing method is changed
		[TestCase(0b1100, 0b0101, 0b0101, ExpectedResult = 0b0101)] // move 1
		[TestCase(0b1110, 0b0001, 0b0001, ExpectedResult = 0b0001)] // move and reduce
		public long AffinityLimit1(int source, int mask, int expected)
		{
			Taskmaster.Process.Utility.Debug = true;

			System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());

			var product = Taskmaster.Process.Utility.ApplyAffinityStrategy(source, mask, Taskmaster.Process.AffinityStrategy.Limit);

			Assert.AreEqual(expected, product,
				"{0} does not match expected {1}",
				Convert.ToString(product, 2).PadLeft(4, '0'), Convert.ToString(expected, 2).PadLeft(4, '0'));

			return product;
		}

		[Test]
		[TestOf(nameof(Taskmaster.Process.Utility.ApplyAffinityStrategy))]
		[TestCase(0b1101, 0b1001, ExpectedResult = 0b1001)]
		[TestCase(0b1100, 0b0011, ExpectedResult = 0b0011)]
		[TestCase(0b0110, 0b1001, ExpectedResult = 0b1001)]
		public long AffinityForce1(int source, int mask)
		{
			Taskmaster.Process.Utility.Debug = true;

			System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());

			var product = Taskmaster.Process.Utility.ApplyAffinityStrategy(source, mask, Taskmaster.Process.AffinityStrategy.Force);

			Assert.AreEqual(mask, product);

			return product;
		}

		[Test]
		[Ignore("Malformed due to other changes.")]
		[TestOf(nameof(Bit))]
		[TestCase(192, 240, 8, ExpectedResult = 240)]
		public int AffinityTests(int sourcemask, int targetmask, int testcpus)
		{
			int result = sourcemask;

			//Taskmaster.Process.Utility.CPUCount = testcpus;

			int excesscores = Bit.Count(targetmask) - Bit.Count(sourcemask);

			TestContext.WriteLine("Excess: " + excesscores.ToString());
			if (excesscores > 0)
			{
				TestContext.WriteLine("Mask Base: " + Convert.ToString(targetmask, 2).PadLeft(testcpus, '0'));
				for (int i = 0; i < testcpus; i++)
				{
					if (Bit.IsSet(targetmask, i))
					{
						result = Bit.Unset(result, i);
						TestContext.WriteLine("Mask Modified: " + Convert.ToString(targetmask, 2).PadLeft(testcpus, '0'));
						if (--excesscores <= 0) break;
					}
					else
						TestContext.WriteLine("Bit not set: " + i.ToString());
				}
				TestContext.WriteLine("Mask Final: " + Convert.ToString(targetmask, 2).PadLeft(testcpus, '0'));
			}

			Assert.AreEqual(sourcemask, targetmask,
				"{0} does not match {1}",
				Convert.ToString(sourcemask, 2).PadLeft(testcpus, '0'), Convert.ToString(targetmask, 2).PadLeft(testcpus, '0'));

			return result;
		}
	}
}
