using Sandbox.Engine;
using Sandbox.UI;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class InspectablePanelsTests
{
	static readonly List<GlobalContext> Observed = new();

	sealed class CollidingRoot : RootPanel
	{
		public static int NextHash;
		readonly int hash = NextHash;

		public override int GetHashCode() => hash;
		public override bool Equals( object obj ) { Observed.Add( GlobalContext.Current ); return ReferenceEquals( this, obj ); }
	}

	[TestMethod]
	public void MenuRootRegistrationDoesNotCallGameEquals()
	{
		ThreadSafe.MarkMainThread();
		Observed.Clear();

		var menuRoot = new RootPanel();
		Sandbox.Internal.IPanel.InspectablePanels.Remove( menuRoot );

		CollidingRoot.NextHash = RuntimeHelpers.GetHashCode( menuRoot );
		var gameRoot = new CollidingRoot();

		using ( GlobalContext.MenuScope() )
		{
			Sandbox.Internal.IPanel.InspectablePanels.Add( menuRoot );
			Sandbox.Internal.IPanel.InspectablePanels.Remove( menuRoot );
		}

		Assert.IsFalse( Observed.Contains( GlobalContext.Menu ), "Game Equals ran in the menu context" );

		gameRoot.Delete( true );
		menuRoot.Delete( true );
	}
}
