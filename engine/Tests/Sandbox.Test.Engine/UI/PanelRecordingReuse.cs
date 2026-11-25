using Sandbox.Engine;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UITests;

/// <summary>
/// A root keeps its last recording while nothing it draws has changed, and records again when anything does.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PanelRecordingReuseTest
{
	bool _renderText;
	bool _wasMainThread;

	[TestInitialize]
	public void Begin()
	{
		_renderText = UiTesting.DisableTextRendering();
		// Recording is main thread work
		_wasMainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();
	}

	[TestCleanup]
	public void Cleanup()
	{
		typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, _wasMainThread );
		TextBlock.ui_rendertext = _renderText;
		PanelRecording.Enabled = true;
		PanelRecording.RetainEnabled = true;
		GlobalContext.Current.UISystem.Clear();
	}

	static RootPanel Root() => new() { PanelBounds = new Rect( 0, 0, 800, 600 ) };

	static Panel Child( Panel parent, string style )
	{
		var panel = parent.AddChild<Panel>();
		panel.Style.Set( style );
		return panel;
	}

	static UICssBoxBatched.BoxInstance[] Frame( RootPanel root )
	{
		root.Layout();
		root.BuildCommandList();
		return root.PanelCommandList.FindResource<Painter.Context>().Batcher.Instances.ToArray();
	}

	/// <summary>
	/// Records <paramref name="root"/> once more and checks whether it replayed, then that what it holds is what a fresh
	/// recording makes.
	/// </summary>
	static void AssertFrame( RootPanel root, bool replayed )
	{
		var before = root.RecordingReplays;
		Frame( root );
		Assert.AreEqual( replayed ? before + 1 : before, root.RecordingReplays, replayed ? "expected a replay" : "expected a recording" );
		AssertSameAsFresh( root );
	}

	/// <summary>
	/// What <paramref name="root"/> holds draws what recording it afresh does: the same instances, naming rows of the
	/// same value. Row indices may differ, since replayed rows are added in another order.
	/// </summary>
	static void AssertSameAsFresh( RootPanel root )
	{
		var kept = Resolved( root );
		PanelRecording.Enabled = false;
		Frame( root );
		PanelRecording.Enabled = true;
		CollectionAssert.AreEqual( Resolved( root ), kept, "the kept recording differs from a fresh one" );
	}

	static string Bytes<T>( in T value ) where T : unmanaged => Convert.ToHexString( MemoryMarshal.AsBytes( new ReadOnlySpan<T>( in value ) ) );

	static string Chain( PainterBatcher batcher, int index )
	{
		var text = "";
		for ( ; index >= 0; index = batcher.Scissors[index].Next )
		{
			var row = batcher.Scissors[index];
			row.Next = 0;
			text += Bytes( row ) + ";";
		}
		return text;
	}

	/// <summary>
	/// Each instance with the rows it names written out in place of their indices.
	/// </summary>
	static string[] Resolved( RootPanel root ) => Resolved( root.PanelCommandList.FindResource<Painter.Context>().Batcher );

	static string[] Resolved( PainterBatcher batcher )
	{
		var result = new List<string>();
		foreach ( var instance in batcher.Instances )
		{
			var box = instance;
			var text = $"{Chain( batcher, box.ScissorIndex )}|{Chain( batcher, box.InverseScissorIndex )}|";
			text += box.TransformIndex >= 0 ? Bytes( batcher.Transforms[box.TransformIndex] ) : "-";
			text += "|" + (box.ShapeIndex >= 0 ? Bytes( batcher.Shapes[box.ShapeIndex] ) : "-");
			if ( (box.Flags & (1u << 14)) != 0 ) text += "|text " + Bytes( batcher.TextRows[box.TextureIndex] );
			else if ( box.TextureIndex < 0 ) text += "|gradient " + Bytes( batcher.Gradients[-box.TextureIndex - 1] );
			else text += "|texture " + box.TextureIndex;
			box.ScissorIndex = box.InverseScissorIndex = box.TransformIndex = box.ShapeIndex = box.TextureIndex = 0;
			result.Add( Bytes( box ) + "|" + text );
		}
		return result.ToArray();
	}

	[TestMethod]
	public void UnchangedRootReplays()
	{
		var root = Root();
		Child( root, "width: 100px; height: 50px; background-color: red;" );
		Child( root, "width: 100px; height: 50px; background-color: blue; border-radius: 4px;" );
		Frame( root );

		AssertFrame( root, replayed: true );
		AssertFrame( root, replayed: true );
	}

	[TestMethod]
	public void StyleChangeRecordsAgain()
	{
		var root = Root();
		var child = Child( root, "width: 100px; height: 50px; background-color: red;" );
		Frame( root );
		Frame( root );

		child.Style.BackgroundColor = Color.Green;
		AssertFrame( root, replayed: false );
		Assert.AreEqual( Color.Green, Frame( root ).Single().Color );
		AssertFrame( root, replayed: true );
	}

	[TestMethod]
	public void ClassChangeRecordsAgain()
	{
		var root = Root();
		root.StyleSheet.Parse( ".a { width: 10px; height: 10px; background-color: red; } .a.on { background-color: white; }" );
		var child = root.AddChild<Panel>( "a" );
		Frame( root );
		Frame( root );

		child.SetClass( "on", true );
		AssertFrame( root, replayed: false );
		Assert.AreEqual( Color.White, Frame( root ).Single().Color );
	}

	[TestMethod]
	public void AddingAndRemovingChildrenRecordsAgain()
	{
		var root = Root();
		var parent = Child( root, "width: 200px; height: 200px;" );
		Child( parent, "width: 10px; height: 10px; background-color: red;" );
		Frame( root );
		Frame( root );

		var added = Child( parent, "width: 10px; height: 10px; background-color: blue;" );
		AssertFrame( root, replayed: false );
		Assert.AreEqual( 2, Frame( root ).Length );

		added.Delete( true );
		AssertFrame( root, replayed: false );
		Assert.AreEqual( 1, Frame( root ).Length );
	}

	[TestMethod]
	public void VisibilityAndMovesRecordAgain()
	{
		var root = Root();
		var parent = Child( root, "width: 200px; height: 200px; padding-left: 0px;" );
		var child = Child( parent, "width: 10px; height: 10px; background-color: red;" );
		Frame( root );
		Frame( root );

		// Moved by its parent, not its own style
		parent.Style.PaddingLeft = 20;
		AssertFrame( root, replayed: false );
		Assert.AreEqual( 20, Frame( root ).Single().Rect.x );

		child.Style.Display = DisplayMode.None;
		AssertFrame( root, replayed: false );
		Assert.AreEqual( 0, Frame( root ).Length );
	}

	[TestMethod]
	public void ScrollingRecordsAgain()
	{
		var root = Root();
		var scroller = Child( root, "width: 100px; height: 100px; overflow: scroll; flex-direction: column;" );
		for ( int i = 0; i < 10; i++ ) Child( scroller, "width: 100px; height: 40px; flex-shrink: 0; background-color: red;" );
		Frame( root );
		Frame( root );

		scroller.ScrollOffset = new Vector2( 0, 30 );
		scroller.SetNeedsFinalLayout();
		AssertFrame( root, replayed: false );
		AssertFrame( root, replayed: true );
	}

	[TestMethod]
	public void LabelTextChangeRecordsAgain()
	{
		var root = Root();
		var label = root.AddChild<Label>();
		label.Style.Set( "background-color: red;" );
		label.Text = "one";
		Frame( root );
		Frame( root );
		AssertFrame( root, replayed: true );

		label.Text = "two two";
		AssertFrame( root, replayed: false );
	}

	[TestMethod]
	public void UnfocusedTextEntryReplays()
	{
		var root = Root();
		var entry = root.AddChild<TextEntry>();
		entry.Style.Set( "width: 200px; height: 30px; background-color: white;" );
		entry.Text = "search";
		Frame( root );
		Frame( root );
		AssertFrame( root, replayed: true );

		entry.Text = "searching";
		AssertFrame( root, replayed: false );
		AssertFrame( root, replayed: true );
	}

	sealed class CustomDrawn : Panel
	{
		public override void OnDraw( Painter painter )
		{
			painter.Fill = Color.Red;
			painter.Rect( new Rect( 0, 0, 10, 10 ) );
		}
	}

	[TestMethod]
	public void CustomDrawingRecordsEveryFrame()
	{
		var root = Root();
		var custom = root.AddChild<CustomDrawn>();
		custom.Style.Set( "width: 10px; height: 10px;" );
		Frame( root );
		Frame( root );
		AssertFrame( root, replayed: false );
	}

	[TestMethod]
	public void OpacityAndBoundsRecordAgain()
	{
		var root = Root();
		Child( root, "width: 100px; height: 50px; background-color: red;" );
		Frame( root );
		Frame( root );
		var replays = root.RecordingReplays;

		root.BuildCommandList( 0.5f );
		Assert.AreEqual( replays, root.RecordingReplays );

		root.PanelBounds = new Rect( 0, 0, 640, 480 );
		AssertFrame( root, replayed: false );
	}

	/// <summary>
	/// A card of the sort a HUD or menu is made of: clipped, rounded, shadowed, bordered, a transform, nested children.
	/// Gradients make textures, which this tier has no device for.
	/// </summary>
	static Panel Card( Panel parent, string color )
	{
		var card = Child( parent, $"width: 200px; height: 120px; overflow: hidden; border-radius: 8px; background-color: {color}; box-shadow: 2px 2px 6px black; transform: rotate( 5deg );" );
		Child( card, "width: 50px; height: 20px; background-color: gray; border: 2px solid white;" );
		var row = Child( card, "width: 180px; height: 40px; flex-direction: row;" );
		Child( row, "width: 20px; height: 20px; background-color: green; border-radius: 50%;" );
		Child( row, "width: 20px; height: 20px; background-color: yellow; box-shadow: inset 1px 1px 2px black;" );
		return card;
	}

	[TestMethod]
	public void UnchangedSubtreesReplayInsideARecording()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		Card( root, "red" );
		Card( root, "blue" );
		Frame( root );
		var replays = root.SubtreeReplays;

		Frame( root );
		Assert.IsTrue( root.SubtreeReplays >= replays + 2, "both cards should replay" );
		AssertSameAsFresh( root );

		// The fresh recording in between counts as last frame, so they replay from it as well
		Frame( root );
		AssertSameAsFresh( root );
	}

	[TestMethod]
	public void ChangedSubtreeRecordsWhileItsSiblingReplays()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var a = Card( root, "red" );
		Card( root, "blue" );
		Frame( root );
		Frame( root );

		a.Children.Last().Children.First().Style.BackgroundColor = Color.White;
		var replays = root.SubtreeReplays;
		Frame( root );
		Assert.IsTrue( root.SubtreeReplays > replays, "the unchanged card should replay" );
		AssertSameAsFresh( root );
		Assert.IsTrue( Frame( root ).Any( x => x.Color == Color.White ) );
	}

	[TestMethod]
	public void SubtreesRecordAgainUnderANewOpacityOrClip()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var holder = Child( root, "width: 400px; height: 300px; overflow: hidden; opacity: 1;" );
		Card( holder, "red" );
		Frame( root );
		Frame( root );

		holder.Style.Opacity = 0.5f;
		Frame( root );
		AssertSameAsFresh( root );

		holder.Style.Width = 100;
		Frame( root );
		AssertSameAsFresh( root );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void ReplayingDoesNotAllocate()
	{
		var kept = Root();
		Card( kept, "red" );
		kept.Layout();
		Allocations.AssertSettles( "Keeping a root's recording", () => kept.BuildCommandList() );

		// A custom drawing panel records the root every frame, so the cards replay inside it
		var recorded = Root();
		recorded.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		Card( recorded, "red" );
		Card( recorded, "blue" );
		recorded.Layout();
		var replays = recorded.SubtreeReplays;
		Allocations.AssertSettles( "Replaying subtrees inside a recording", () => recorded.BuildCommandList() );
		Assert.IsTrue( recorded.SubtreeReplays > replays );
	}

	[TestMethod]
	public void DisabledNeverReplays()
	{
		var root = Root();
		Child( root, "width: 100px; height: 50px; background-color: red;" );
		PanelRecording.Enabled = false;
		Frame( root );
		Frame( root );
		Frame( root );
		Assert.AreEqual( 0, root.RecordingReplays );
	}

	/// <summary>
	/// A spawn menu's worth of drawing: a panel of many small cells, enough to be kept in a list of its own once it stops changing.
	/// </summary>
	static Panel Menu( Panel parent )
	{
		var menu = Child( parent, "width: 700px; height: 500px; flex-wrap: wrap; overflow: hidden; background-color: #222;" );
		for ( int i = 0; i < 160; i++ )
			Child( menu, "width: 40px; height: 20px; margin: 2px; background-color: #446; border-radius: 3px;" );
		return menu;
	}

	static PainterBatcher Kept( Panel panel ) => panel.SubtreeRecording?.Kept is { Active: true } kept ? kept.Context.Batcher : null;

	/// <summary>
	/// What <paramref name="root"/> draws with <paramref name="menu"/>, its last child, kept in a list of its own, is what a
	/// fresh recording of the whole root into one list draws.
	/// </summary>
	static void AssertKeptSameAsFresh( RootPanel root, Panel menu )
	{
		var kept = Resolved( root ).Concat( Resolved( Kept( menu ) ) ).ToArray();
		PanelRecording.Enabled = false;
		Frame( root );
		PanelRecording.Enabled = true;
		CollectionAssert.AreEqual( Resolved( root ), kept, "the root and its kept subtree differ from a fresh recording" );
	}

	/// <summary>
	/// Records <paramref name="root"/> until <paramref name="menu"/> is kept in a list of its own, then once more.
	/// </summary>
	static void Settle( RootPanel root, Panel menu )
	{
		for ( int i = 0; i < PanelRecording.RetainAfter + 2 && Kept( menu ) is null; i++ ) Frame( root );
		Assert.IsNotNull( Kept( menu ), "the menu should be kept in a list of its own" );
		Frame( root );
	}

	[TestMethod]
	public void LargeUnchangedSubtreeIsKeptInItsOwnList()
	{
		// The custom drawing panel records the root every frame, like a HUD whose text changes
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var menu = Menu( root );
		Settle( root, menu );

		var inserts = root.RetainedInserts;
		var recordings = root.RetainedRecordings;
		var generation = Kept( menu ).Generation;
		var rootInstances = Frame( root ).Length;
		Assert.AreEqual( inserts + 1, root.RetainedInserts, "the kept list should be inserted as it was" );
		Assert.AreEqual( recordings, root.RetainedRecordings );
		Assert.AreEqual( generation, Kept( menu ).Generation, "an inserted list's tables aren't written again" );
		Assert.IsTrue( rootInstances < 10, "the root records only what's outside the kept subtree" );
		AssertKeptSameAsFresh( root, menu );
	}

	[TestMethod]
	public void ChangeInsideAKeptSubtreeRecordsItsList()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var menu = Menu( root );
		Settle( root, menu );

		var recordings = root.RetainedRecordings;
		menu.Children.ElementAt( 7 ).Style.BackgroundColor = Color.White;
		Frame( root );
		Assert.AreEqual( recordings + 1, root.RetainedRecordings, "the kept list should record again" );
		Assert.IsTrue( Kept( menu ).Instances.Any( x => x.Color == Color.White ) );
		AssertKeptSameAsFresh( root, menu );
	}

	[TestMethod]
	public void KeptSubtreeRecordsAgainUnderANewOpacity()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var holder = Child( root, "width: 800px; height: 600px; opacity: 1;" );
		var menu = Menu( holder );
		Settle( root, holder );

		var recordings = root.RetainedRecordings;
		holder.Style.Opacity = 0.5f;
		Frame( root );
		Assert.AreEqual( recordings + 1, root.RetainedRecordings );
		AssertKeptSameAsFresh( root, holder );
	}

	[TestMethod]
	public void SubtreeOutOfItsKeptListReplaysOnlyFromItsParent()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var menu = Menu( root );
		Settle( root, menu );

		// Its last recording is in the kept list's tables, which the root's can't replay from
		PanelRecording.RetainEnabled = false;
		var replays = root.SubtreeReplays;
		Frame( root );
		AssertSameAsFresh( root );
		Frame( root );
		Assert.IsTrue( root.SubtreeReplays > replays, "it replays from the root's own tables again" );
		AssertSameAsFresh( root );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void InsertingAKeptSubtreeDoesNotAllocate()
	{
		var root = Root();
		root.AddChild<CustomDrawn>().Style.Set( "width: 10px; height: 10px;" );
		var menu = Menu( root );
		root.Layout();
		Settle( root, menu );
		var inserts = root.RetainedInserts;
		Allocations.AssertSettles( "Inserting a kept subtree", () => root.BuildCommandList() );
		Assert.IsTrue( root.RetainedInserts > inserts );
	}
}
