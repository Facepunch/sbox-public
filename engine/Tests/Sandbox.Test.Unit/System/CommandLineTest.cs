using Sandbox.Utility;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class CommandLineTest
{
	/// <summary>
	/// The parser should split switches on +/- prefixes, keep quoted strings
	/// intact, and tolerate switch-name lookups with or without the prefix.
	/// CommandLine is process-global state, so we save and restore it around
	/// the test and don't run in parallel with anything else.
	/// </summary>
	[TestMethod]
	public void TestCommandLineParser()
	{
		var previousCommandLine = CommandLine.CommandLineString;

		try
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
		finally
		{
			CommandLine.CommandLineString = previousCommandLine;
			CommandLine.Parse();
		}
	}

	/// <summary>
	/// Parsing a launch command line from Steam (e.g. the website's Play button while running)
	/// returns its own switches and leaves the process command line alone.
	/// </summary>
	[TestMethod]
	public void ParseStringIsSeparate()
	{
		var before = CommandLine.Full;

		var switches = CommandLine.Parse( "-rungame facepunch.sandbox -webreferrer youtube.com" );
		Assert.AreEqual( 2, switches.Count );
		Assert.AreEqual( "facepunch.sandbox", switches["rungame"] );
		Assert.AreEqual( "youtube.com", switches["WebReferrer"] );

		switches = CommandLine.Parse( "-rungame facepunch.sandbox" );
		Assert.AreEqual( 1, switches.Count );
		Assert.AreEqual( "facepunch.sandbox", switches["rungame"] );
		Assert.IsFalse( switches.ContainsKey( "webreferrer" ) );

		Assert.AreEqual( 0, CommandLine.Parse( "" ).Count );
		Assert.AreEqual( 0, CommandLine.Parse( null ).Count );

		Assert.AreEqual( before, CommandLine.Full );
		Assert.IsFalse( CommandLine.HasSwitch( "rungame" ) );
	}
}
