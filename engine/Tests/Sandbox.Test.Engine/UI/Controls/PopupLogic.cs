using Sandbox.UI;
using System.Collections.Generic;
using System.Reflection;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Modifies UI System Global (static popup registry)
public class PopupLogicTest
{
	/// <summary>
	/// Minimal concrete popup - BasePopup is abstract and supplies all of the behavior under test.
	/// </summary>
	sealed class ProbePopup : BasePopup
	{
	}

	/// <summary>
	/// The private static popup registry inside BasePopup, fetched via reflection so the tests can
	/// assert real registration state rather than inferring it.
	/// </summary>
	static List<BasePopup> Registry => (List<BasePopup>)typeof( BasePopup )
		.GetField( "AllPopups", BindingFlags.NonPublic | BindingFlags.Static )
		.GetValue( null );

	/// <summary>
	/// The popup registry is static engine state, so delete and drop anything left behind by other
	/// tests to make membership assertions only see popups created by the current test.
	/// </summary>
	[TestInitialize]
	public void ResetPopupRegistry()
	{
		foreach ( var popup in Registry.ToArray() )
		{
			popup.Delete( true );
		}

		Registry.Clear();
	}

	/// <summary>
	/// Constructing a popup registers it in the static popup registry.
	/// </summary>
	[TestMethod]
	public void ConstructingPopupRegistersIt()
	{
		var popup = new ProbePopup();

		Assert.AreEqual( 1, Registry.Count );
		Assert.IsTrue( Registry.Contains( popup ) );
	}

	/// <summary>
	/// CloseAll deletes an open popup and unregisters it - the delete is the deferred (outro)
	/// variant, so the popup is flagged IsDeleting rather than destroyed on the spot.
	/// </summary>
	[TestMethod]
	public void CloseAllDeletesOpenPopups()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };

		BasePopup.CloseAll();

		Assert.IsFalse( Registry.Contains( popup ) );
		Assert.IsTrue( popup.IsDeleting );
	}

	/// <summary>
	/// A StayOpen popup with a valid parent survives CloseAll while its non-StayOpen sibling is
	/// closed and unregistered.
	/// </summary>
	[TestMethod]
	public void CloseAllRespectsStayOpenWithValidParent()
	{
		var root = new RootPanel();
		var pinned = new ProbePopup { Parent = root, StayOpen = true };
		var transient = new ProbePopup { Parent = root };

		BasePopup.CloseAll();

		Assert.IsTrue( Registry.Contains( pinned ) );
		Assert.IsFalse( pinned.IsDeleting );

		Assert.IsFalse( Registry.Contains( transient ) );
		Assert.IsTrue( transient.IsDeleting );
	}

	/// <summary>
	/// StayOpen is only honored when the popup still has a valid parent - an orphaned StayOpen
	/// popup is deleted by CloseAll like any other.
	/// </summary>
	[TestMethod]
	public void CloseAllDeletesOrphanedStayOpenPopups()
	{
		var orphan = new ProbePopup { StayOpen = true };

		BasePopup.CloseAll();

		Assert.IsFalse( Registry.Contains( orphan ) );
		Assert.IsTrue( orphan.IsDeleting );
	}

	/// <summary>
	/// Passing a panel to CloseAll spares the popup found in that panel's ancestry, so a popup is
	/// not closed by clicks/interactions inside its own hierarchy.
	/// </summary>
	[TestMethod]
	public void CloseAllSparesTheExceptedPopupHierarchy()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };
		var inner = new Panel { Parent = popup };

		BasePopup.CloseAll( inner );

		Assert.IsTrue( Registry.Contains( popup ) );
		Assert.IsFalse( popup.IsDeleting );
	}

	/// <summary>
	/// Deleting a popup directly (without going through CloseAll) also removes it from the
	/// registry via OnDeleted.
	/// </summary>
	[TestMethod]
	public void ImmediateDeleteUnregistersPopup()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };

		Assert.IsTrue( Registry.Contains( popup ) );

		popup.Delete( true );

		Assert.IsFalse( Registry.Contains( popup ) );
	}
}
