using MKAh;
using System;
using System.Text;
using System.Collections.Generic;
using Taskmaster;
using NUnit.Framework;

namespace Types
{
	/// <summary>
	/// Summary description for UnitTest1
	/// </summary>
	[TestFixture]
	public class Extensions
	{
		public Extensions()
		{
			//
			// TODO: Add constructor logic here
			//
		}

		[Test]
		public void ContrainIntTest()
		{
			int itest = 7;
			Assert.AreEqual(5, itest.Constrain(0, 5));
		}

		[Test]
		public void ContrainLongTest()
		{
			long itest = 7L;
			Assert.AreEqual(5L, itest.Constrain(0L, 5L));
		}

		[Test]
		public void ContrainFloatTest()
		{
			float ftest = 7.0f;
			Assert.AreEqual(5.0f, ftest.Constrain(0, 5.0f));
		}

		[Test]
		public void ContrainDoubleTest()
		{
			double dtest = 7.0D;
			Assert.AreEqual(5.0D, dtest.Constrain(0.0D, 5.0D));
		}

		[Test]
		public void ContrainDecimalTest()
		{
			decimal dectest = 7.0M;
			Assert.AreEqual(5.0M, dectest.Constrain(0, 5.0M));
		}
	}
}
