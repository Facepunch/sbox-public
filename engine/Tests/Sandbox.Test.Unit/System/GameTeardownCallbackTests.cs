using Sandbox.Engine;
using System;
using System.Reflection;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class GameTeardownCallbackTests
{
	[TestMethod]
	public void LimitedWebSurfaceStopsCallingGameAfterSessionEnds()
	{
		var game = new GlobalContext();
		var menu = new GlobalContext();
		var calls = 0;
		GlobalContext observed = null;

		WebSurface surface;
		using ( new GlobalContext.GlobalContextScope( game ) ) surface = new WebSurface( true );
		surface.OnTexture = ( _, _ ) => { calls++; observed = GlobalContext.Current; };

		var repaint = typeof( WebSurface ).GetMethod( "OnNeedsRepaint", BindingFlags.Instance | BindingFlags.NonPublic );
		var args = new[] { Activator.CreateInstance( repaint.GetParameters()[0].ParameterType ) };

		try
		{
			using ( new GlobalContext.GlobalContextScope( menu ) )
			{
				repaint.Invoke( surface, args );
				Assert.AreSame( game, observed, "Game texture callback ran outside its own context" );

				game.CancellationTokenSource.Cancel();
				repaint.Invoke( surface, args );
			}

			Assert.AreEqual( 1, calls, "Game texture callback ran after its session ended" );
		}
		finally
		{
			surface.Dispose();
			game.CancellationTokenSource.Dispose();
			menu.CancellationTokenSource.Dispose();
		}
	}
}
