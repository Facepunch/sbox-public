using Sandbox.Diagnostics;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class NativeLoggingTest
{
	[TestMethod]
	public void PartialLinesResumeAfterTheNewline()
	{
		Logging.GetLogger( "NativeLoggingTest" );
		var previousConfig = NLog.LogManager.Configuration;
		using var target = new NLog.Targets.MemoryTarget { Layout = "${message}" };
		var config = new NLog.Config.LoggingConfiguration();
		config.AddRuleForAllLevels( target );
		NLog.LogManager.Configuration = config;
		try
		{
			Logging.PrintNative( 0, "NativeLoggingTest", "first\nsec" );
			Logging.PrintNative( 0, "NativeLoggingTest", "ond" );
			Assert.AreEqual( 1, target.Logs.Count );
			Logging.PrintNative( 0, "NativeLoggingTest", " line\r\nthird\npar" );
			Logging.PrintNative( 0, "NativeLoggingTest", "tial\n" );
			Logging.PrintNative( 0, "NativeLoggingTest", "last\n" );
			CollectionAssert.AreEqual( new[] { "first", "second line\r\nthird", "partial", "last" }, target.Logs.ToArray() );
		}
		finally
		{
			NLog.LogManager.Configuration = previousConfig;
		}
	}
}
