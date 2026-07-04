using Editor;
using System.Collections.Generic;

namespace EditorTests;

[TestClass]
public class HistoryListTest
{
	/// <summary>
	/// A fresh list has nothing to show and nowhere to go.
	/// </summary>
	[TestMethod]
	public void EmptyListDefaults()
	{
		var history = new HistoryList<string>();

		Assert.IsNull( history.Current );
		Assert.IsFalse( history.CanGoBack );
		Assert.IsFalse( history.CanGoForward );
		Assert.IsFalse( history.Navigate( -1 ) );
		Assert.IsFalse( history.Navigate( 1 ) );
	}

	/// <summary>
	/// Adding items should make the newest one current, with history behind it.
	/// </summary>
	[TestMethod]
	public void AddMakesItemCurrent()
	{
		var history = new HistoryList<string>();

		history.Add( "first" );
		history.Add( "second" );

		Assert.AreEqual( "second", history.Current );
		Assert.IsTrue( history.CanGoBack );
		Assert.IsFalse( history.CanGoForward );
	}

	/// <summary>
	/// Adding the item we're already on shouldn't create a duplicate history entry.
	/// </summary>
	[TestMethod]
	public void AddingCurrentAgainIsIgnored()
	{
		var history = new HistoryList<string>();

		history.Add( "first" );
		history.Add( "first" );

		Assert.AreEqual( "first", history.Current );
		Assert.IsFalse( history.CanGoBack );
	}

	/// <summary>
	/// Navigation should move through the history in both directions and report each
	/// stop through <see cref="HistoryList{T}.OnNavigate"/>.
	/// </summary>
	[TestMethod]
	public void NavigateBackAndForward()
	{
		var history = new HistoryList<string>();
		var visited = new List<string>();
		history.OnNavigate = x => visited.Add( x );

		history.Add( "a" );
		history.Add( "b" );
		history.Add( "c" );

		Assert.IsTrue( history.Navigate( -1 ) );
		Assert.AreEqual( "b", history.Current );

		Assert.IsTrue( history.Navigate( -1 ) );
		Assert.AreEqual( "a", history.Current );
		Assert.IsFalse( history.CanGoBack );
		Assert.IsTrue( history.CanGoForward );

		Assert.IsTrue( history.Navigate( 2 ) );
		Assert.AreEqual( "c", history.Current );

		CollectionAssert.AreEqual( new[] { "b", "a", "c" }, visited );
	}

	/// <summary>
	/// Navigating past either end should clamp to the boundary, and report no change
	/// when already there.
	/// </summary>
	[TestMethod]
	public void NavigateClampsToEnds()
	{
		var history = new HistoryList<string>();

		history.Add( "a" );
		history.Add( "b" );
		history.Add( "c" );

		Assert.IsTrue( history.Navigate( -100 ) );
		Assert.AreEqual( "a", history.Current );
		Assert.IsFalse( history.Navigate( -1 ) );

		Assert.IsTrue( history.Navigate( 100 ) );
		Assert.AreEqual( "c", history.Current );
		Assert.IsFalse( history.Navigate( 1 ) );
	}

	/// <summary>
	/// Navigating by zero is never a change, and failed navigation shouldn't raise
	/// <see cref="HistoryList{T}.OnNavigate"/>.
	/// </summary>
	[TestMethod]
	public void FailedNavigationDoesNotNotify()
	{
		var history = new HistoryList<string>();
		var notified = 0;
		history.OnNavigate = _ => notified++;

		history.Add( "a" );

		Assert.IsFalse( history.Navigate( 0 ) );
		Assert.IsFalse( history.Navigate( -1 ) );
		Assert.IsFalse( history.Navigate( 1 ) );
		Assert.AreEqual( 0, notified );
	}

	/// <summary>
	/// Adding after navigating backwards should discard the forward entries, like a
	/// browser history does.
	/// </summary>
	[TestMethod]
	public void AddAfterGoingBackDiscardsForwardHistory()
	{
		var history = new HistoryList<string>();

		history.Add( "a" );
		history.Add( "b" );
		history.Add( "c" );

		Assert.IsTrue( history.Navigate( -2 ) );
		Assert.AreEqual( "a", history.Current );

		history.Add( "d" );

		Assert.AreEqual( "d", history.Current );
		Assert.IsFalse( history.CanGoForward );

		Assert.IsTrue( history.Navigate( -1 ) );
		Assert.AreEqual( "a", history.Current );
		Assert.IsFalse( history.CanGoBack );
	}

	/// <summary>
	/// Exceeding <see cref="HistoryList{T}.MaxItems"/> should trim the oldest entries
	/// while keeping the current position pointing at the same item.
	/// </summary>
	[TestMethod]
	public void MaxItemsTrimsOldestEntries()
	{
		var history = new HistoryList<string>
		{
			MaxItems = 3
		};

		history.Add( "a" );
		history.Add( "b" );
		history.Add( "c" );
		history.Add( "d" );
		history.Add( "e" );

		Assert.AreEqual( 3, history.list.Count );
		Assert.AreEqual( "e", history.Current );

		Assert.IsTrue( history.Navigate( -2 ) );
		Assert.AreEqual( "c", history.Current );
		Assert.IsFalse( history.CanGoBack );
	}

	/// <summary>
	/// Clearing should empty the list and reset navigation.
	/// </summary>
	[TestMethod]
	public void ClearResetsEverything()
	{
		var history = new HistoryList<string>();

		history.Add( "a" );
		history.Add( "b" );

		history.Clear();

		Assert.IsNull( history.Current );
		Assert.IsFalse( history.CanGoBack );
		Assert.IsFalse( history.CanGoForward );
	}
}
