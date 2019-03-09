using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Taskmaster;

namespace Types
{
	/// <summary>
	/// Summary description for UnitTest1
	/// </summary>
	[TestClass]
	public class Extensions
	{
		public Extensions()
		{
			//
			// TODO: Add constructor logic here
			//
		}

		readonly TestContext testContextInstance;

		/// <summary>
		///Gets or sets the test context which provides
		///information about and functionality for the current test run.
		///</summary>
		public TestContext TestContext { get; set; }

		#region Additional test attributes
		//
		// You can use the following additional attributes as you write your tests:
		//
		// Use ClassInitialize to run code before running the first test in the class
		// [ClassInitialize()]
		// public static void MyClassInitialize(TestContext testContext) { }
		//
		// Use ClassCleanup to run code after all tests in a class have run
		// [ClassCleanup()]
		// public static void MyClassCleanup() { }
		//
		// Use TestInitialize to run code before running each test
		// [TestInitialize()]
		// public void MyTestInitialize() { }
		//
		// Use TestCleanup to run code after each test has run
		// [TestCleanup()]
		// public void MyTestCleanup() { }
		//
		#endregion

		[TestMethod]
		public void ContrainIntTest()
		{
			int itest = 7;
			Assert.AreEqual(5, itest.Constrain(0, 5));
		}

		[TestMethod]
		public void ContrainLongTest()
		{
			long itest = 7L;
			Assert.AreEqual(5L, itest.Constrain(0L, 5L));
		}

		[TestMethod]
		public void ContrainFloatTest()
		{
			float ftest = 7.0f;
			Assert.AreEqual(5.0f, ftest.Constrain(0, 5.0f));
		}

		[TestMethod]
		public void ContrainDoubleTest()
		{
			double dtest = 7.0D;
			Assert.AreEqual(5.0D, dtest.Constrain(0.0D, 5.0D));
		}

		[TestMethod]
		public void ContrainDecimalTest()
		{
			decimal dectest = 7.0M;
			Assert.AreEqual(5.0M, dectest.Constrain(0, 5.0M));
		}
	}
}
