using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Taskmaster.Process;

namespace Paths
{
	[TestFixture]
	public class Formatting
	{
		[Test]
		[TestCase(@"c:\Abdc\testify\Beep\place\folder\test.exe", ExpectedResult = @"c:\Abdc\testify\Beep\…\test.exe")]
		[TestCase(@"c:\Abdc\Beep\testing\test.exe", ExpectedResult = @"c:\Abdc\Beep\testing\test.exe")]
		[TestOf(nameof(Taskmaster.Process.Controller.FormatPathName))]
		public string BasicSmartPath(string input)
		{
			var prc = new Taskmaster.Process.Controller("Test");
			prc.PathVisibility = Taskmaster.Process.PathVisibilityOptions.Smart;
			prc.Repair();
			Assert.AreEqual(0, prc.PathElements); // no path matching, so this should be zero

			var info = new ProcessEx(-1, DateTimeOffset.UtcNow) { Name = "test", Path = input };

			var output = prc.FormatPathName(info);

			return output;
		}

		[Test]
		[TestCase(@"c:\Program Files (x86)\brand\test\hoolahoo\bin32\test.exe", ExpectedResult = @"…\brand\…\hoolahoo\bin32\test.exe")]
		[TestCase(@"c:\Program Files (x86)\brand\test\hoolahoo\bin\test\bin32\test.exe", ExpectedResult = @"…\brand\…\hoolahoo\…\bin32\test.exe")]
		[TestOf(nameof(Taskmaster.Process.Controller.FormatPathName))]
		public string CutSmartPath(string input)
		{
			var prc = new Taskmaster.Process.Controller("Test");
			prc.PathVisibility = Taskmaster.Process.PathVisibilityOptions.Smart;
			prc.Path = @"c:\Program Files (x86)\";
			prc.Repair();
			Assert.AreEqual(2, prc.PathElements); // no path matching, so this should be zero

			var info = new ProcessEx(-1, DateTimeOffset.UtcNow) { Name = "test", Path = input };

			var output = prc.FormatPathName(info);

			return output;
		}
	}
}
