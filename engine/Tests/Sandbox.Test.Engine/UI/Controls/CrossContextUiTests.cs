using Sandbox.Engine;
using Sandbox.UI;
using System.Collections.Generic;
using System.Reflection;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class CrossContextUiTests
{
	sealed class DeleteProbePopup : BasePopup
	{
		public List<GlobalContext> DeleteContexts { get; } = new();

		public override void Delete( bool immediate = false )
		{
			DeleteContexts.Add( GlobalContext.Current );
			base.Delete( immediate );
		}
	}

	// IValid re-implemented explicitly, which the IValid.IsValid() extension would dispatch to
	sealed class ValidProbePopup : BasePopup, IValid
	{
		public List<GlobalContext> ValidContexts { get; } = new();

		bool IValid.IsValid
		{
			get
			{
				ValidContexts.Add( GlobalContext.Current );
				return true;
			}
		}
	}

	sealed class EqualsProbePopup : BasePopup
	{
		public int EqualsCalls;

		public override bool Equals( object obj )
		{
			EqualsCalls++;
			return base.Equals( obj );
		}

		public override int GetHashCode() => base.GetHashCode();
	}

	sealed class EqualsProbeWindow : FloatingWindow
	{
		public int EqualsCalls;

		public override bool Equals( object obj )
		{
			EqualsCalls++;
			return base.Equals( obj );
		}

		public override int GetHashCode() => base.GetHashCode();
	}

	static List<BasePopup> Popups => (List<BasePopup>)typeof( BasePopup )
		.GetField( "AllPopups", BindingFlags.NonPublic | BindingFlags.Static )
		.GetValue( null );

	static List<FloatingWindow> MinimizedWindows => (List<FloatingWindow>)typeof( FloatingWindow )
		.GetField( "minimizedWindows", BindingFlags.NonPublic | BindingFlags.Static )
		.GetValue( null );

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();

		foreach ( var popup in Popups.ToArray() )
			popup.Delete( true );

		Popups.Clear();
	}

	[TestMethod]
	public void EachSideOnlyClosesItsOwnPopups()
	{
		var game = GlobalContext.Current;
		var menuSystem = GlobalContext.Menu.UISystem;
		GlobalContext.Menu.UISystem = new UISystem();

		var gameRoot = new RootPanel();
		var menuRoot = new RootPanel( GlobalContext.Menu.UISystem );
		var gamePopup = new DeleteProbePopup { Parent = gameRoot };
		var menuPopup = new DeleteProbePopup { Parent = menuRoot };
		var validProbe = new ValidProbePopup { Parent = gameRoot };
		var deadPopup = new DeleteProbePopup();
		deadPopup.Delete( true );
		Popups.Add( deadPopup );

		try
		{
			using ( GlobalContext.MenuScope() )
			{
				BasePopup.CloseAll();
			}

			Assert.AreEqual( 0, gamePopup.DeleteContexts.Count, "The menu ran a game popup's code" );
			CollectionAssert.AreEqual( new[] { GlobalContext.Menu }, menuPopup.DeleteContexts );
			Assert.IsFalse( Popups.Contains( deadPopup ), "Dead popups weren't pruned" );
			Assert.IsFalse( validProbe.ValidContexts.Contains( GlobalContext.Menu ), "The menu ran a game popup's IValid" );

			var laterMenuPopup = new DeleteProbePopup { Parent = menuRoot };

			BasePopup.CloseAll();

			CollectionAssert.AreEqual( new[] { game }, gamePopup.DeleteContexts );
			Assert.AreEqual( 0, laterMenuPopup.DeleteContexts.Count, "The game closed a menu popup" );
		}
		finally
		{
			gameRoot.Delete( true );
			menuRoot.Delete( true );
			GlobalContext.Menu.UISystem = menuSystem;
		}
	}

	[TestMethod]
	public void PopupRemovalDoesNotCallOtherPopupsEquals()
	{
		var probe = new EqualsProbePopup();
		var popup = new DeleteProbePopup();

		try
		{
			popup.Delete( true );

			Assert.AreEqual( 0, probe.EqualsCalls );
		}
		finally
		{
			probe.Delete( true );
		}
	}

	[TestMethod]
	public void MinimizedWindowRemovalDoesNotCallOtherWindowsEquals()
	{
		var probe = new EqualsProbeWindow();
		var window = new FloatingWindow();
		MinimizedWindows.Add( probe );

		try
		{
			window.Delete( true );

			Assert.AreEqual( 0, probe.EqualsCalls );
		}
		finally
		{
			MinimizedWindows.RemoveAll( x => ReferenceEquals( x, probe ) );
			probe.Delete( true );
		}
	}
}
