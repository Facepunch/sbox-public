using Sandbox;

namespace ServicesTests;

[TestClass]
[DoNotParallelize]
public class ActivityLoadingTest
{
	[TestInitialize]
	public void Reset()
	{
		Api.Activity.CurrentLoad?.End( "superseded" );
		Api.Activity.TakeCompletedLoad( "any" );
		Api.Activity.LoadBegin( "reset", false ).End( "cancel" );
	}

	[TestMethod]
	public void LoadCarriesTheRequestThatStartedIt()
	{
		Api.Activity.GameRequested( new( "menu", "org.game", "home", "Trending", 3 ) );

		var load = Api.Activity.LoadBegin( "org.game#12", false );

		Assert.AreEqual( "home", load.Origin.Surface );
		Assert.AreEqual( 3, load.Origin.Position );
	}

	[TestMethod]
	public void GenericRequestDoesNotReplaceTheMenus()
	{
		Api.Activity.GameRequested( new( "menu", "org.game", "home" ) );
		Api.Activity.GameRequested( new( "console", "org.game" ), replace: false );

		Assert.AreEqual( "menu", Api.Activity.LoadBegin( "org.game", false ).Origin.Kind );
	}

	[TestMethod]
	public void RequestIsUsedOnce()
	{
		Api.Activity.GameRequested( new( "friend" ) );
		Api.Activity.LoadBegin( "org.game", true ).End( "cancel" );

		Assert.IsNull( Api.Activity.LoadBegin( "org.game", false ).Origin );
	}

	[TestMethod]
	public void SuccessfulLoadGoesOnTheNextHeartbeatForThatGameOnly()
	{
		Api.Activity.GameRequested( new( "menu", "org.game", "search" ) );
		var load = Api.Activity.LoadBegin( "org.game#12", false );
		load.Stage( "install" );
		load.Downloaded( 1000, 4, 1.5 );
		Api.Activity.LoadFinished();

		Assert.IsNull( Api.Activity.TakeCompletedLoad( "org.other" ).Load );

		var (data, origin) = Api.Activity.TakeCompletedLoad( "ORG.GAME" );
		Assert.AreEqual( "success", data["outcome"] );
		Assert.AreEqual( 1000L, data["bytes"] );
		Assert.AreEqual( "search", origin.Surface );

		Assert.IsNull( Api.Activity.TakeCompletedLoad( "org.game" ).Load );
	}

	[TestMethod]
	public void NewLoadSupersedesTheRunningOne()
	{
		var first = Api.Activity.LoadBegin( "org.first", false );
		var second = Api.Activity.LoadBegin( "org.second", false );

		Assert.IsTrue( first.Ended );
		Assert.AreSame( second, Api.Activity.CurrentLoad );
	}

	[TestMethod]
	public void EndedLoadIgnoresLaterStages()
	{
		var load = Api.Activity.LoadBegin( "org.game", false );
		Api.Activity.LoadAbandoned( "Map not found" );

		load.Stage( "scene" );
		Api.Activity.LoadFinished();

		Assert.IsNull( Api.Activity.CurrentLoad );
		Assert.IsNull( Api.Activity.TakeCompletedLoad( "org.game" ).Load );
	}
}
