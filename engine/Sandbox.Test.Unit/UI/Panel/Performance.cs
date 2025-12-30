using Sandbox.Engine;
using Sandbox.UI;
using System.Diagnostics;
namespace UITest.Panels;

public partial class Performance
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	[TestMethod]
	public void CreationPerformanceNonLinear()
	{
		var root = new RootPanel();

		for ( int a = 0; a < 10; a++ ) // 10
		{
			var p = root.Add.Panel( "one" );

			for ( int b = 0; b < 10; b++ ) // 100
			{
				var q = p.Add.Panel( "two" );

				for ( int c = 0; c < 10; c++ ) // 1000
				{
					var r = q.Add.Panel( "two" );

					for ( int d = 0; d < 10; d++ ) // 10000
					{
						r.Add.Panel( "two" );
					}
				}
			}
		}
	}

	[TestMethod]
	public void CreationPerformanceLinear()
	{
		var root = new RootPanel();

		for ( int i = 0; i < 10000; i++ )
		{
			root.Add.Panel( "one" );
		}
	}

	[TestMethod]
	public void Simulate()
	{
		Assert.AreEqual( GlobalContext.Current.UISystem.RootPanels.Count, 0 );

		var root = new RootPanel();

		for ( int a = 0; a < 100; a++ ) // 10
		{
			var p = root.Add.Panel( "one" );

			for ( int b = 0; b < 100; b++ ) // 1000
			{
				var q = p.Add.Panel( "two" );
			}
		}

		GlobalContext.Current.UISystem.Simulate( true );

		var calls = 10;
		var sw = Stopwatch.StartNew();
		for ( int i = 0; i < calls; i++ )
		{
			GlobalContext.Current.UISystem.Simulate( true );
		}

		System.Console.WriteLine( $"Time: {sw.Elapsed.TotalMilliseconds}ms for {calls} calls" );
	}
}
