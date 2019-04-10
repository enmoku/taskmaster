using System;
using NUnit.Framework;

namespace Power
{
	[TestFixture]
	public class AutoAdjust
	{
		public TestContext TestContext { get; set; }

		[Test]
		public void CPULoadHandlerTests()
		{
			/*
			var power = new Taskmaster.PowerManager(); // null reference

			power.onAutoAdjustAttempt += (_, ea) =>
			{
				Assert.AreEqual(ea.Reaction, Taskmaster.Power.Manager.Reaction.Low, "Reaction: " + ea.Reaction.ToString());
			};

			power.CPULoadHandler(null, new Taskmaster.ProcessorLoadEventArgs()
			{
				Current = 0,
				High = 0,
				Low = 0,
				Mean = 0,
				Queue = 1
			});
			*/
		}
	}
}
