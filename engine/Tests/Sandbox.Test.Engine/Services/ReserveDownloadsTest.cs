using System;

namespace Services;

/// <summary>
/// Sizing up a load's downloads only fills the loading bar: a package lookup that never answers mustn't hold what waits on
/// it (a join installing the server's packages).
/// </summary>
[TestClass]
public class ReserveDownloadsTest
{
	[TestMethod]
	public async Task AnUnansweredLookupDoesntHoldTheLoad()
	{
		var fetch = LoadingScreen.FetchForSizing;
		var wait = LoadingScreen.ReserveWait;
		var visible = LoadingScreen.IsVisible;
		var never = new TaskCompletionSource<Package>();
		try
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.ReserveWait = TimeSpan.FromMilliseconds( 200 );
			LoadingScreen.FetchForSizing = ident => ident == "test.unanswered" ? never.Task : Task.FromResult<Package>( null );

			var reserve = LoadingScreen.ReserveDownloads( ["test.unanswered", "test.answered"] );
			var finished = await Task.WhenAny( reserve, Task.Delay( TimeSpan.FromSeconds( 10 ) ) );

			Assert.AreSame( reserve, finished, "ReserveDownloads waited on a lookup that never answers" );
			await reserve;
			Assert.IsFalse( never.Task.IsCompleted );
		}
		finally
		{
			never.TrySetResult( null );
			LoadingScreen.FetchForSizing = fetch;
			LoadingScreen.ReserveWait = wait;
			LoadingScreen.IsVisible = visible;
		}
	}
}
