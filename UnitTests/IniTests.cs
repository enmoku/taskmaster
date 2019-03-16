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

		[Test]
		[TestCase(3)]
		public void RecursiveSaveAndLoad(int repeats)
		{
			if (repeats < 2) repeats = 2;
			if (repeats > 30) repeats = 30;

			var config = new Ini.Config();

			var section1Name = "Number Tests";
			var section2Name = "String Tests";
			var section1Var1Name = "IntArray";
			var section1Var2Name = "DoubleValue";
			var section2Var1Name = "StringArray";
			var section2Var2Name = "String";

			var s1var1value = new int[] { 1, 2, 3 };
			var s1var1valueEscaped = "{ 1, 2, 3 }";
			var s1var2value = 0.5d;
			var s1var2valueFormatted = "0.5";
			var s2var1value = new string[] { "abc", "xyz" };
			var s2var1valueEscaped = "{ abc, xyz }";
			var s2var2value = "Bad\"#";
			var s2var2valueEscaped = "\"Bad\\\"#\"";

			var section1 = new Ini.Section(section1Name);
			section1.Add(new Ini.Setting() { Name = section1Var1Name, IntArray = s1var1value });
			section1.Add(new Ini.Setting() { Name = section1Var2Name, DoubleValue = s1var2value });
			config.Add(section1);

			var section2 = new Ini.Section(section2Name);
			section2.Add(new Ini.Setting() { Name = section2Var1Name, Array = s2var1value });
			section2.Add(new Ini.Setting() { Name = section2Var2Name, Value = s2var2value });
			config.Add(section2);

			var data = config.GetLines();

			for (int i = 0; i < repeats; i++)
			{
				config = Ini.Config.FromData(data); // read written config

				Assert.AreEqual(2, config.ItemCount);
				var s1 = config.Get(section1Name);
				var s2 = config.Get(section2Name);

				Assert.IsNotNull(s1);
				Assert.IsNotNull(s2);

				var s1v1 = s1.Get(section1Var1Name);
				var s1v2 = s1.Get(section1Var2Name);
				var s2v1 = s2.Get(section2Var1Name);
				var s2v2 = s2.Get(section2Var2Name);

				Assert.IsNotNull(s1v1);
				Assert.IsNotNull(s1v2);
				Assert.IsNotNull(s2v1);
				Assert.IsNotNull(s2v2);

				Assert.AreEqual(s1var1valueEscaped, s1v1.EscapedValue);
				Assert.AreEqual(s1var2valueFormatted, s1v2.Value);
				Assert.AreEqual(s2var1valueEscaped, s2v1.EscapedValue);
				Assert.AreEqual(s2var2valueEscaped, s2v2.EscapedValue);

				data = config.GetLines(); // re-write config
			}
		}
	}
}
