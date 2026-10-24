using Sandbox.Diagnostics;
using Sandbox.Engine;
using System;

namespace ConsoleTests;

[TestClass]
public class MenuLoggerTest
{
	/// <summary>
	/// Menu loggers are menu code, so they run in the menu's context whoever logged the message. A game package's
	/// static constructors log with the game's TypeLibrary locked - the console overlay creating a panel for that
	/// message must not touch the game's TypeLibrary, or the game load fails.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void MenuLoggerRunsInMenuContext()
	{
		const string message = "!MenuLogger!";

		GlobalContext seenContext = null;
		Exception listenerException = null;

		void Listener( LogEvent e )
		{
			if ( !e.Message.Contains( message ) )
				return;

			seenContext = GlobalContext.Current;

			try { _ = Game.TypeLibrary; }
			catch ( Exception ex ) { listenerException = ex; }
		}

		// Listeners only run immediately on the main thread - use our own so marking it doesn't leak into other tests
		var thread = new System.Threading.Thread( () =>
		{
			ThreadSafe.MarkMainThread();

			using ( GlobalContext.GameScope() )
			using ( GlobalContext.Current.DisableTypelibraryScope( "!Testing!" ) )
			{
				new Logger( "Test" ).Warning( $"{message}" );
			}
		} );

		MenuUtility.AddLogger( Listener );

		try
		{
			thread.Start();
			thread.Join();
		}
		finally
		{
			MenuUtility.RemoveLogger( Listener );
		}

		Assert.AreSame( GlobalContext.Menu, seenContext, "Menu logger ran in the menu context" );
		Assert.IsNull( listenerException, "Menu logger didn't hit the game's locked TypeLibrary" );
	}
}
