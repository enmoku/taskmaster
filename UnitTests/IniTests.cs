using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ini = MKAh.Ini;

namespace IniFile
{
	[TestClass]
	public class Reading
	{
		public TestContext TestContext { get; set; }

		[TestMethod]
		public void Initialization()
		{
			var config = new Ini.Config();

			var section = new Ini.Section("Test");

			config.Add(section);

			section.Add(new Ini.Setting() { Key = "Key" }); // empty value

			var lines = config.GetLines();

			foreach (var line in lines)
				Debug.WriteLine(line);

			Assert.AreEqual(3, lines.Length);
			Assert.AreEqual("[Test]\n", lines[0]);
		}

		[TestMethod]
		public void LoadFromText()
		{
			var data = Properties.Resources.TestIni;

			var config = new Ini.Config();

			config.Load(data.Split('\n'));
		}

		[TestMethod]
		public void StoreValue()
		{
			try
			{
				var section = new Ini.Section("Test");

				var key0 = new Ini.Setting { Key = "Key0", Value = "#\"bob\"", Comment = "string" };
				Debug.WriteLine(key0.Value);
				Assert.AreEqual("#\"bob\"", key0.Value);

				var key1 = new Ini.Setting { Key = "Key1", Comment = "float" };
				key1.Set(0.5f);
				Debug.WriteLine(key1.Value);
				Assert.AreEqual("0.5", key1.Value);

				var intarray = new Ini.Setting { Key = "IntArray", Comment = "ints" };
				intarray.Set(new[] { 1, 2f, 3 });
				Debug.WriteLine(intarray.EscapedValue);
				Assert.AreEqual("{ 1, 2, 3 }", intarray.EscapedValue);

				var strarray = new Ini.Setting { Key = "StringArray", Comment = "strings" };
				strarray.Set(new[] { "abc", "xyz" });
				Debug.WriteLine(strarray.EscapedValue);
				Assert.AreEqual("{ abc, xyz }", strarray.EscapedValue);

				var badarray = new Ini.Setting { Key = "BadArray", Comment = "bad strings" };
				badarray.Set(new[] { "a#b#c", "x\"y\"z", "\"doop\"#", "good", "  spaced", "#bad", "#\"test\"" });
				Debug.WriteLine(badarray.EscapedValue);

				Assert.AreEqual("{ \"a#b#c\", \"x\\\"y\\\"z\", \"\\\"doop\\\"#\", good, \"  spaced\", \"#bad\", \"#\\\"test\\\"\" }", badarray.EscapedValue);

				var quotedArray = new Ini.Setting { Key="test", Value = "kakka\"bob\"" };

				section.Add(key0);
				section.Add(key1);
				section.Add(intarray);
				section.Add(strarray);
			}
			catch (Exception ex)
			{
				Assert.Fail(ex.Message);
			}
		}

		[TestMethod]
		public void Indexers()
		{
			var config = new Ini.Config();

			config["Section"]["Key"].Value = "HaHaHa";

			Assert.AreEqual(1, config.SectionCount);
			Assert.AreEqual(1, config.Sections.First().ValueCount);

			var section = config["Section"];

			Assert.AreEqual(1, section.ValueCount);

			var value = section["Key"];

			Assert.AreEqual("HaHaHa", value.Value);
		}

		public void EmptyValues()
		{

		}
	}
}
