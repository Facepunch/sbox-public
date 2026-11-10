using Sandbox.Engine;

namespace SystemTests;

[TestClass]
public class ErrorReportTest
{
	/// <summary>
	/// In unit-test builds Sentry is disabled, so Initialize and Flush are
	/// guard-clause smoke checks. The one observable side effect headless is
	/// that ReportException always increments Application.ExceptionCount.
	/// </summary>
	[TestMethod]
	public void BasicReport()
	{
		ErrorReporter.Initialize();

		var countBefore = Sandbox.Application.ExceptionCount;

		try
		{
			throw new System.Exception( "Unit Test Exception" );
		}
		catch ( System.Exception e )
		{
			ErrorReporter.ReportException( e );
		}

		Assert.AreEqual( countBefore + 1, Sandbox.Application.ExceptionCount );

		ErrorReporter.Flush();
	}
}
