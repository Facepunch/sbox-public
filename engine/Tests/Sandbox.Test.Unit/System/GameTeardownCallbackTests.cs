using Sandbox.Engine;

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

		try
		{
			using ( new GlobalContext.GlobalContextScope( menu ) )
			{
				surface.OnNeedsRepaint( 0, 0, 0 );
				Assert.AreSame( game, observed, "Game texture callback ran outside its own context" );

				game.CancellationTokenSource.Cancel();
				surface.OnNeedsRepaint( 0, 0, 0 );
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
