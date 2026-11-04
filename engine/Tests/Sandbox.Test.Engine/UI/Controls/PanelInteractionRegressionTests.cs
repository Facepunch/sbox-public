using System;
using Sandbox.UI;
using NativeEngine;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class PanelInteractionRegressionTests
{
	UISurface surface;
	bool renderText;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		renderText = UiTesting.DisableTextRendering();
		surface = new UISurface { Size = new( 1200, 900 ), DpiScale = 1, MouseInside = true };
	}

	[TestCleanup]
	public void Cleanup()
	{
		surface.Dispose();
		TextBlock.ui_rendertext = renderText;
	}

	void Frame()
	{
		for ( int i = 0; i < 4; i++ ) UiTesting.Frame( surface );
	}

	void Move( Vector2 point )
	{
		surface.MouseMoved( point );
		Frame();
	}

	void Press( ButtonCode button, bool down )
	{
		surface.Input.AddMouseButton( button, down, default );
		Frame();
	}

	CurveEditor AddEditor()
	{
		var editor = surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 700;
		editor.Value = new Curve( new Curve.Frame( 0.33333334f, 0.37123456f ) ) { TimeRange = new( -7, 13 ), ValueRange = new( -12, 23 ) };
		Frame();
		return editor;
	}

	[TestMethod]
	public void ClickingAKeyDoesNotSnapRewriteOrCreateUndo()
	{
		var editor = AddEditor();
		editor.SnapToGrid = true;
		var before = editor.Value;
		int changes = 0;
		editor.ValueChanged = _ => changes++;
		var key = editor.Descendants.OfType<CanvasPanel.Handle>().Single( x => x.HasClass( "curve-key" ) );
		Move( key.Box.Rect.Center );
		Press( ButtonCode.MouseLeft, true );
		Press( ButtonCode.MouseLeft, false );
		Assert.AreEqual( 0, changes );
		Assert.IsFalse( editor.CanUndo );
		Assert.AreEqual( before.Frames[0], editor.Value.Frames[0] );
		Assert.AreEqual( 0, editor.SelectedIndex );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChangingSelectionOrEndingEditDuringDragDoesNotUseAStaleSnapshot( bool finishEdit )
	{
		var editor = AddEditor();
		var key = editor.Descendants.OfType<CanvasPanel.Handle>().Single( x => x.HasClass( "curve-key" ) );
		var point = key.Box.Rect.Center;
		Move( point ); Press( ButtonCode.MouseLeft, true );
		Assert.IsTrue( key.IsDragging );
		if ( finishEdit ) editor.EndEdit();
		else editor.SelectKey( -1 );
		Move( point + new Vector2( 20, 10 ) );
		Assert.IsFalse( key.IsDragging );
		Press( ButtonCode.MouseLeft, false );
		Assert.IsFalse( editor.CanUndo );
	}

	[TestMethod]
	public void RemovingAKeyDuringDragDoesNotRestoreTheDeletedKey()
	{
		var editor = AddEditor();
		editor.Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.5f, 0.5f ), new Curve.Frame( 1, 1 ) );
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		Move( canvas.Box.Rect.Position + canvas.CanvasToScreen( new( 0.5f, 0.5f ) ) );
		Press( ButtonCode.MouseLeft, true );
		editor.RemoveSelected(); Frame();
		Move( canvas.Box.Rect.Position + canvas.CanvasToScreen( new( 0.6f, 0.6f ) ) );
		Press( ButtonCode.MouseLeft, false );
		Assert.AreEqual( 2, editor.Value.Length );
		Assert.IsTrue( editor.CanUndo );
		editor.Undo();
		Assert.AreEqual( 3, editor.Value.Length );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ClosingPopupCommitsLiveFieldEditAndIdleEscapeBubbles( bool range )
	{
		var target = new CurveControlTests.Target();
		CurveControl control = range ? new CurveRangeControl() : new CurveControl();
		control.Parent = surface.Root;
		control.Style.Width = 500;
		control.Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( range ? nameof( target.Range ) : nameof( target.Response ) );
		Frame(); control.OpenEditor(); Frame();
		var editor = surface.Root.Descendants.OfType<CurveEditor>().Single();
		var field = editor.Descendants.OfType<NumberEntry>().Single( x => x.HasClass( "axis-y-max" ) );
		field.Focus(); Frame();
		field.OnTextEdited( "10" );
		Assert.AreEqual( 10f, (range ? target.Range.A : target.Response).ValueRange.y );
		editor.Ancestors.OfType<Popup>().Single().Delete( true ); Frame();
		Assert.AreEqual( 10f, (range ? target.Range.A : target.Response).ValueRange.y );
		control.OpenEditor(); Frame();
		editor = surface.Root.Descendants.OfType<CurveEditor>().Single();
		var canvas = editor.Descendants.OfType<GraphPanel>().Single();
		var escape = new ButtonEvent( "escape", true, 0, default );
		var popup = editor.Ancestors.OfType<Popup>().Single();
		canvas.OnButtonTyped( escape );
		Assert.IsTrue( popup.IsDeleting );
	}

	[TestMethod]
	public void LegacyCurveOpensWithoutWritingRepairsToTheProperty()
	{
		var original = new Curve( new Curve.Frame( -0.2f, 0.3f, float.NaN, float.PositiveInfinity ),
			new Curve.Frame( 0.5f, 0.2f ), new Curve.Frame( 0.5f, 0.6f ), new Curve.Frame( 1.2f, 0.7f ) )
		{ TimeRange = new( 4, 2 ), ValueRange = new( 5, 5 ) };
		var target = new CurveControlTests.Target { Response = original, Range = new( original, original ) };
		foreach ( bool range in new[] { false, true } )
		{
			CurveControl control = range ? new CurveRangeControl() : new CurveControl();
			control.Parent = surface.Root;
			control.Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( range ? nameof( target.Range ) : nameof( target.Response ) );
			Frame(); control.OpenEditor(); Frame();
			var editor = surface.Root.Descendants.OfType<CurveEditor>().Single();
			Assert.AreEqual( 4, editor.Value.Length );
			Assert.AreEqual( -0.2f, editor.Value.Frames[0].Time );
			Assert.IsTrue( editor.NavigationBounds.Value.Left < 2 );
			control.Delete( true ); Frame();
			Assert.AreEqual( original, target.Response );
			Assert.AreEqual( original, target.Range.A );
		}
	}

	[TestMethod]
	public void PanStartedOverFocusedChildSurvivesQueuedBlur()
	{
		var canvas = surface.Root.AddChild<CanvasPanel>();
		canvas.AcceptsFocus = true;
		canvas.Style.Set( "width: 600px; height: 300px;" );
		var child = canvas.AddChild<Button>();
		child.Style.Set( "width: 100px; height: 40px;" );
		Frame(); canvas.Focus(); Frame();
		Move( child.Box.Rect.Center );
		Press( ButtonCode.MouseMiddle, true );
		Assert.IsTrue( canvas.IsPanning );
		var before = canvas.ViewMin;
		Move( child.Box.Rect.Center + new Vector2( 20, 15 ) );
		Assert.AreNotEqual( before, canvas.ViewMin );
		Press( ButtonCode.MouseMiddle, false );
		Assert.IsFalse( canvas.IsPanning );
	}

	[TestMethod]
	public void IdleTabDoesNotConsumeEscape()
	{
		var tab = surface.Root.AddChild<Tab>();
		var escape = new ButtonEvent( "escape", true, 0, default );
		tab.OnButtonTyped( escape );
		Assert.IsFalse( escape.StopPropagation );
	}

	[TestMethod]
	public void UrlIconsKeepTheirSourceAndVisibility()
	{
		const string url = "https://example.invalid/icon.png";
		var button = surface.Root.AddChild<Button>();
		button.Icon = url;
		Assert.AreEqual( url, button.Icon );
		Assert.IsTrue( button.HasClass( "has-icon" ) );
		Assert.AreEqual( DisplayMode.Flex, button.Descendants.OfType<IconPanel>().First().Style.Display );
		var tab = surface.Root.AddChild<Tab>();
		tab.Icon = url;
		Assert.AreEqual( url, tab.Icon );
		Assert.IsTrue( tab.HasIcon );
		Assert.AreEqual( DisplayMode.Flex, tab.Descendants.OfType<IconPanel>().First().Style.Display );
	}

	[TestMethod]
	public void GraphCanTickBeforeItsFirstLayout()
	{
		var graph = surface.Root.AddChild<GraphPanel>();
		graph.Tick();
		graph.HorizontalAxis.Tick();
	}

	[TestMethod]
	public void FullNavigationBoundsTolerateRoundingAtNonzeroOrigins()
	{
		var canvas = surface.Root.AddChild<CanvasPanel>();
		canvas.PreserveAspectRatio = false;
		for ( int i = 1; i < 1000; i++ )
		{
			var bounds = new Rect( i * 0.0137f, -i * 0.0391f, 0.7123f, 0.4567f );
			canvas.NavigationBounds = bounds;
			canvas.SetView( new( -100, -100 ), new( 100, 100 ) );
			Assert.IsTrue( canvas.ViewMin.x >= bounds.Left && canvas.ViewMax.x <= bounds.Right );
			Assert.IsTrue( canvas.ViewMin.y >= bounds.Top && canvas.ViewMax.y <= bounds.Bottom );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CurvePreviewDrawsOnePathAndSkipsClippedRows( bool range )
	{
		CurveControl control = range ? new CurveRangeControl() : new CurveControl();
		control.Parent = surface.Root;
		control.Property = Game.TypeLibrary.GetSerializedObject( new CurveControlTests.Target() ).GetProperty( range ? "Range" : "Response" );
		control.Style.Width = 500;
		Frame();
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		using var painter = context.Begin( new Rect( 0, 0, 600, 400 ) );
		var checkpoint = context.Batcher.GetCheckpoint();
		control.OnDraw( painter );
		Assert.AreEqual( range ? 3 : 1, painter.InstanceCount );
		for ( int i = 0; i < 100; i++ )
		{
			context.Batcher.Rewind( checkpoint );
			control.OnDraw( painter );
		}
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
		{
			context.Batcher.Rewind( checkpoint );
			control.OnDraw( painter );
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Console.WriteLine( $"Curve preview (range={range}): {allocated / 100} bytes per draw." );
		Assert.IsTrue( allocated < 100 * 4096, $"Preview allocated {allocated / 100} bytes per draw." );
		int instances = painter.InstanceCount;
		painter.Clip( new Rect( 0, 100, 500, 100 ) );
		control.OnDraw( painter );
		Assert.AreEqual( instances, painter.InstanceCount );
	}

	[TestMethod]
	public void CachedLineReusesGeometryAndRespectsTransformsAndClips()
	{
		var line = new Painter.CachedLine();
		var points = Enumerable.Range( 0, 65 ).Select( i => new Vector2( i, MathF.Sin( i / 10f ) ) ).ToArray();
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		using var painter = context.Begin( new Rect( 0, 0, 600, 400 ) );
		for ( int i = 0; i < 100; i++ ) line.Draw( painter, points, Color.Red );
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ ) line.Draw( painter, points, Color.Blue );
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 100 * 1024, $"Cached line allocated {allocated / 100} bytes per draw." );
		Assert.AreEqual( 200, painter.InstanceCount );
		painter.Clip( new Rect( 0, 0, 100, 100 ) );
		Assert.IsTrue( painter.IsRectVisible( new Rect( 0, 0, 20, 20 ) ) );
		painter.Translate( new Vector2( 200, 0 ) );
		Assert.IsFalse( painter.IsRectVisible( new Rect( 0, 0, 20, 20 ) ) );
	}

	sealed class Draggable : Panel
	{
		public override bool WantsDrag => true;
	}

	[TestMethod]
	public void StartingAnotherButtonsDragPreservesHeldActiveAncestors()
	{
		var parent = surface.Root.AddChild<Panel>();
		parent.Style.Set( "width: 600px; height: 300px; pointer-events: all;" );
		var child = parent.AddChild<Draggable>();
		child.Style.Set( "width: 100px; height: 40px; pointer-events: all;" );
		Frame();
		Move( parent.Box.Rect.Center ); Press( ButtonCode.MouseLeft, true );
		Move( child.Box.Rect.Center ); Press( ButtonCode.MouseRight, true );
		Move( child.Box.Rect.Center + new Vector2( 30, 20 ) );
		Assert.IsTrue( surface.Input.MouseStates.Single( x => x.MouseButton == ButtonCode.MouseRight ).Dragged );
		Assert.IsTrue( parent.HasActive );
		Press( ButtonCode.MouseRight, false ); Press( ButtonCode.MouseLeft, false );
	}
}
