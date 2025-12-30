using System;
using Sandbox.Utility;

namespace SystemTest;

[TestClass]
public class CommandLineTest
{
	[TestMethod]
	public void TestCommandLineParser()
	{
		CommandLine.CommandLineString = @"+game facepunch.walker garry.scenemap +something ""hello there im a string"" test -value 35455 +somethingelse ""a -switch inside a string""";
		CommandLine.Parse();

		Assert.AreEqual( "facepunch.walker garry.scenemap", CommandLine.GetSwitch( "game", string.Empty ) );
		Assert.AreEqual( @"""a -switch inside a string""", CommandLine.GetSwitch( "somethingelse", string.Empty ) );
		Assert.AreEqual( "35455", CommandLine.GetSwitch( "value", string.Empty ) );
		Assert.AreEqual( "35455", CommandLine.GetSwitch( "-value", string.Empty ) );
		Assert.AreEqual( @"""hello there im a string"" test", CommandLine.GetSwitch( "something", string.Empty ) );

		CommandLine.CommandLineString = @"D:\Facepunch\sbox\game\sbox-dev.dll -project D:\Facepunch\sbox\game\addons\sbox-deathmatch\sbdm.sbproj";
		CommandLine.Parse();

		Assert.AreEqual( @"D:\Facepunch\sbox\game\addons\sbox-deathmatch\sbdm.sbproj", CommandLine.GetSwitch( "project", string.Empty ) );
	}
}
