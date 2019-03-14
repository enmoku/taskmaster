using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NUnit;
using NUnit.Framework;

using Ini = MKAh.Ini;

namespace IniFile
{
	[TestFixture]
	public class Reading
	{
		[Test]
		public void Initialization()
		{
			var config = new Ini.Config();

			var section = new Ini.Section("Test");

			config.Add(section);

			section.Add(new Ini.Setting() { Name = "Key" }); // empty value

			var lines = config.GetLines();

			foreach (var line in lines)
				Debug.WriteLine(line);

			Assert.AreEqual(3, lines.Length);
			Assert.AreEqual("[Test]\n", lines[0]);
		}

		[Test]
		public void LoadFromText()
		{
			var data = Properties.Resources.TestIni;

			var config = new Ini.Config();

			config.Load(data.Split('\n'));
		}

		[Test]
		public void StoreValue()
		{
			try
			{
				var section = new Ini.Section("Test");

				var key0 = new Ini.Setting { Name = "Key0", Value = "#\"bob\"", Comment = "string" };
				Debug.WriteLine(key0.Value);
				Assert.AreEqual("#\"bob\"", key0.Value);

				var key1 = new Ini.Setting { Name = "Key1", Comment = "float" };
				key1.Set(0.5f);
				Debug.WriteLine(key1.Value);
				Assert.AreEqual("0.5", key1.Value);

				var intarray = new Ini.Setting { Name = "IntArray", Comment = "ints" };
				intarray.Set(new[] { 1, 2f, 3 });
				Debug.WriteLine(intarray.EscapedValue);
				Assert.AreEqual("{ 1, 2, 3 }", intarray.EscapedValue);

				var strarray = new Ini.Setting { Name = "StringArray", Comment = "strings" };
				strarray.Set(new[] { "abc", "xyz" });
				Debug.WriteLine(strarray.EscapedValue);
				Assert.AreEqual("{ abc, xyz }", strarray.EscapedValue);

				var badarray = new Ini.Setting { Name = "BadArray", Comment = "bad strings" };
				badarray.Set(new[] { "a#b#c", "x\"y\"z", "\"doop\"#", "good", "  spaced", "#bad", "#\"test\"" });
				Debug.WriteLine(badarray.EscapedValue);

				Assert.AreEqual("{ \"a#b#c\", \"x\\\"y\\\"z\", \"\\\"doop\\\"#\", good, \"  spaced\", \"#bad\", \"#\\\"test\\\"\" }", badarray.EscapedValue);

				var quotedArray = new Ini.Setting { Name="test", Value = "kakka\"bob\"" };

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

		[Test]
		public void Indexers()
		{
			var config = new Ini.Config();

			config["Section"]["Key"].Value = "HaHaHa";

			Assert.AreEqual(1, config.ItemCount);
			Assert.AreEqual(1, config.Items.First().ItemCount);

			var section = config["Section"];

			Assert.AreEqual(1, section.ItemCount);

			var value = section["Key"];

			Assert.AreEqual("HaHaHa", value.Value);
		}

		public void EmptyValues()
		{

		}

		[Test]
		public void SectionEnumerator()
		{
			var config = new Ini.Config();

			config.Add(new Ini.Section("Test1", 5));
			config.Add(new Ini.Section("Test2", 72));

			var results = new List<string>();
			var resultsInt = new List<int>();

			foreach (Ini.Section section in config)
			{
				results.Add(section.Name);
				resultsInt.Add(section.Index);
			}

			Assert.AreEqual("Test1", results[0]);
			Assert.AreEqual(5, resultsInt[0]);
			Assert.AreEqual("Test2", results[1]);
			Assert.AreEqual(72, resultsInt[1]);
		}

		[Test]
		public void SettingEnumerator()
		{
			var section = new Ini.Section("TestSection");

			section.Add(new Ini.Setting() { Name = "Test1", IntValue = 5 });
			section.Add(new Ini.Setting() { Name = "Test2", IntValue = 72 });

			var results = new List<string>();
			var resultInts = new List<string>();

			foreach (Ini.Setting setting in section)
			{
				results.Add(setting.Name);
				resultInts.Add(setting.Value);
			}

			Assert.AreEqual("Test1", results[0]);
			Assert.AreEqual("5", resultInts[0]);
			Assert.AreEqual("Test2", results[1]);
			Assert.AreEqual("72", resultInts[1]);
		}
	}
}
