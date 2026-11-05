using Sandbox.Engine;
using System;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class GlobalContextResetTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CancellationCallbacksCannotReachTheNewTokenOrAbortReset( bool callbackThrows )
	{
		var context = new GlobalContext();
		var oldEvents = context.EventSystem;
		bool? sawLiveToken = null;

		context.CancellationTokenSource.Token.Register( () =>
		{
			sawLiveToken = !context.CancellationTokenSource.IsCancellationRequested;
			if ( callbackThrows ) throw new InvalidOperationException( "Expected reset regression exception" );
		} );

		try
		{
			context.Reset();

			Assert.AreEqual( false, sawLiveToken, "A cancellation callback could reach the context's new token" );
			Assert.IsFalse( context.CancellationTokenSource.IsCancellationRequested );
			Assert.AreNotSame( oldEvents, context.EventSystem, "Reset stopped before replacing the event system" );
		}
		finally
		{
			context.Shutdown();
		}
	}
}
