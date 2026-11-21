using System.Collections.Generic;
using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class VirtualSelectionTest
{
	[TestCleanup]
	public void Cleanup() => GlobalContext.Current.UISystem.Clear();

	static (RootPanel Root, VirtualList List, Dictionary<int, Label> Labels) CreateList( string style = "" )
	{
		ThreadSafe.MarkMainThread();
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var list = new VirtualList { Parent = root, AllowChildSelection = true, ItemHeight = 20 };
		list.Style.Set( $"width: 200px; height: 100px; flex-direction: column; {style}" );
		var labels = new Dictionary<int, Label>();
		list.OnCreateCell = ( cell, data ) =>
		{
			var label = new Label { Parent = cell, Text = $"Row {data}" };
			label.Style.Set( "width: 200px; height: 20px; font-size: 12px;" );
			labels[(int)data] = label;
		};
		list.Items = Enumerable.Range( 0, 100 ).Select( x => (object)x ).ToList();
		root.Layout();
		root.Layout();
		return (root, list, labels);
	}

	static string Rows( int first, int count ) => string.Join( "\n", Enumerable.Range( first, count ).Select( x => $"Row {x}" ) );

	static void Drag( RootPanel root, Label from, Label to, Vector2 end )
	{
		var selection = new Selection();
		selection.UpdateSelection( root, from, true, true, false, new Vector2( 0, from.Box.Rect.Center.y ) );
		selection.UpdateSelection( root, to, true, false, false, end );
	}

	[TestMethod]
	public void SelectionSurvivesScrollingAndCopiesInItemOrder()
	{
		var (root, list, labels) = CreateList();
		list.ScrollOffset = new Vector2( 0, 200 );
		root.Layout();
		root.Layout();

		var target = labels[12];
		var selection = new Selection();
		selection.UpdateSelection( root, target, true, true, false, new Vector2( 199, target.Box.Rect.Center.y ) );
		list.ScrollOffset = new Vector2( 0, 100 );
		root.Layout();
		root.Layout();
		Assert.IsTrue( target.IsValid );

		var end = new Vector2( 0, labels[5].Box.Rect.Center.y );
		selection.UpdateSelection( root, labels[5], true, false, false, end );
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
		list.RequestCopy();
		root.Layout();
		var copied = list.GetClipboardValue( false );
		Assert.AreEqual( Rows( 5, 8 ), copied );

		selection.UpdateSelection( root, labels[5], false, false, true, end );
		root.Layout();
		root.Layout();
		Assert.IsFalse( target.IsValid );

		list.ScrollOffset = new Vector2( 0, 0 );
		root.Layout();
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
		list.RequestCopy();
		root.Layout();
		Assert.AreEqual( copied, list.GetClipboardValue( false ) );

		selection.UpdateSelection( root, list, true, true, false, new Vector2( 0, 0 ) );
		root.Layout();
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void DraggingPastTheBottomStopsAtTheLastVisibleRow()
	{
		var (root, list, labels) = CreateList();
		Drag( root, labels[1], labels[1], new Vector2( 100, list.Box.Rect.Bottom + 400 ) );
		root.Layout();
		root.Layout();

		Assert.AreEqual( Rows( 1, 4 ), list.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void DraggingPastTheBottomIncludesRowsVisibleInPadding()
	{
		var (root, list, labels) = CreateList( "padding-top: 20px; padding-bottom: 20px;" );
		Drag( root, labels[1], labels[1], new Vector2( 100, list.Box.Rect.Bottom + 400 ) );
		root.Layout();

		Assert.AreEqual( Rows( 1, 3 ), list.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void ReleaseWithoutDragUpdateKeepsSelection()
	{
		var (root, list, labels) = CreateList();
		var selection = new Selection();
		selection.UpdateSelection( root, labels[1], true, true, false, new Vector2( 0, labels[1].Box.Rect.Center.y ) );
		selection.UpdateSelection( root, labels[3], false, false, true, new Vector2( 199, labels[3].Box.Rect.Center.y ) );
		root.Layout();

		list.ScrollOffset = new Vector2( 0, 200 );
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
		list.RequestCopy();
		root.Layout();
		Assert.AreEqual( Rows( 1, 3 ), list.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void CopyRequestedAfterTickWaitsForRows()
	{
		var (root, list, labels) = CreateList();
		Drag( root, labels[1], labels[3], new Vector2( 199, labels[3].Box.Rect.Center.y ) );
		root.Layout();
		list.ScrollOffset = new Vector2( 0, 200 );
		root.Layout();

		list.RequestCopy();
		root.PostLayout();
		root.Layout();
		Assert.AreEqual( Rows( 1, 3 ), list.GetClipboardValue( false ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnselectClearsSelection( bool fromRoot )
	{
		var (root, list, labels) = CreateList();
		Drag( root, labels[1], labels[3], new Vector2( 199, labels[3].Box.Rect.Center.y ) );
		root.Layout();
		Assert.AreEqual( Rows( 1, 3 ), list.GetClipboardValue( false ) );

		(fromRoot ? (Panel)root : list).UnselectAllInChildren();
		root.Layout();
		Assert.IsNull( list.GetClipboardValue( false ) );
	}
}
