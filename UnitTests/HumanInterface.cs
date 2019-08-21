using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;
using Taskmaster;

namespace HumanInterface
{
	[TestFixture]
	public class Numbers
	{
		[Test]
		[TestCase(1_000, ExpectedResult = "+1,000 B")]
		[TestCase(10_000, ExpectedResult = "+9.77 kiB")]
		[TestCase(100_000, ExpectedResult = "+97.66 kiB")]
		[TestCase(1_000_000, ExpectedResult = "+976.6 kiB")]
		[TestCase(1_200_000_000, ExpectedResult = "+1,144.4 MiB")]
		[TestCase(1_300_000_000_000, ExpectedResult = "+1,210.7 GiB")]
		public string ByteString(long bytes)
		{
			var result = Taskmaster.HumanInterface.ByteString(bytes, positivesign: true, iec:true);

			return result;
		}
	}
}

