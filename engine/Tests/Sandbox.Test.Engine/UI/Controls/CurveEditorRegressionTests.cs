using Sandbox.UI;
using System.Collections.Generic;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class CurveEditorRegressionTests
{
	UISurface _surface;
	CookieContainer _previousCookies;
	CookieContainer _cookies;
	bool _previousTextRendering;

	public class Target
	{
		public Curve Curve { get; set; } = Curve.Ease;
		public CurveRange Range { get; set; } = new( Curve.Ease, Curve.Linear );
	}

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		_previousTextRendering = UiTesting.DisableTextRendering();
		_previousCookies = Game.Cookies;
		_cookies = new CookieContainer( "curve-regressions", true, new MemoryFileSystem() );
		Game.Cookies = _cookies;
		_surface = new UISurface { Size = new Vector2( 1200, 900 ), DpiScale = 1, MouseInside = true };
	}

	[TestCleanup]
	public void Cleanup()
	{
		_surface.Dispose();
		_cookies.Dispose();
		Game.Cookies = _previousCookies;
		TextBlock.ui_rendertext = _previousTextRendering;
	}

	void Frame()
	{
		for ( int i = 0; i < 4; i++ )
			UiTesting.Frame( _surface );
	}

	CurveEditor Open( Target target, bool range = false )
	{
		CurveControl control = range ? new CurveRangeControl() : new CurveControl();
		control.Parent = _surface.Root;
		control.Style.Width = 600;
		control.Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( range ? nameof( Target.Range ) : nameof( Target.Curve ) );
		Frame();
		control.OpenEditor();
		Frame();
		return _surface.Root.Descendants.OfType<CurveEditor>().Single();
	}

	[TestMethod]
	public void MovingAKeyAgainstItsNeighbourCanBeReopened()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.5f, 0.5f ), new Curve.Frame( 1, 1 ) );
		editor.MoveKey( 0, 0.5f, 0.25f );
		var reopened = _surface.Root.AddChild<CurveEditor>();
		reopened.Value = editor.Value;

		CollectionAssert.AreEqual( editor.Value.Frames.ToArray(), reopened.Value.Frames.ToArray() );
	}

	[TestMethod]
	public void CloselySpacedAndDuplicateImportedKeysArePreserved()
	{
		var curve = new Curve( new Curve.Frame( 0.5f, 0 ), new Curve.Frame( 0.50001f, 1 ) );
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Value = curve;
		CollectionAssert.AreEqual( curve.Frames.ToArray(), editor.Value.Frames.ToArray() );
		editor.MoveKey( 0, 0.5f, 0.25f );
		Assert.AreEqual( 0.5f, editor.Value.Frames[0].Time );
		editor.SelectKey( 0 );
		editor.MoveSelection( new Vector2( 0, 0.25f ) );
		Assert.AreEqual( 0.5f, editor.Value.Frames[0].Time );
		Assert.AreEqual( 0.5f, editor.Value.Frames[0].Value );
		editor.Value = new Curve( new Curve.Frame( 0.5f, 0 ), new Curve.Frame( 0.5f, 1 ) );
		Assert.AreEqual( 2, editor.Value.Length );
		Assert.AreEqual( editor.Value.Frames[0].Time, editor.Value.Frames[1].Time );
	}

	[TestMethod]
	public void FlatPresetMatchesThePropertyAndKeepsUndoAfterTick()
	{
		var target = new Target();
		var editor = Open( target );
		var flat = editor.Descendants.OfType<Button>().Single( button => button.Tooltip == "Flat" );
		flat.OnButtonTyped( new ButtonEvent( "enter", true, 0, default ) );
		Frame();

		Assert.AreEqual( 1, target.Curve.Length );
		Assert.AreEqual( 0f, target.Curve.Evaluate( 0.5f ) );
		Assert.AreEqual( target.Curve.Evaluate( 0.5f ), editor.Value.Evaluate( 0.5f ) );
		Assert.IsTrue( editor.CanUndo );
		editor.Undo();
		Frame();
		CollectionAssert.AreEqual( Curve.Ease.Frames.ToArray(), target.Curve.Frames.ToArray() );
	}

	[TestMethod]
	public void EmptyCurvePreviewDoesNotInventAValue()
	{
		var target = new Target { Curve = new Curve() };
		var editor = Open( target );
		Frame();
		Assert.AreEqual( target.Curve.Evaluate( 0.5f ), editor.Value.Evaluate( 0.5f ) );
		Assert.IsFalse( editor.CanUndo );
	}

	[TestMethod]
	public void RangePopupBoundsIncludeBothCurvesAndFollowEditsAndUndo()
	{
		var upper = Curve.Linear;
		upper.TimeRange = new Vector2( -2, 3 );
		upper.ValueRange = new Vector2( 10, 20 );
		var editor = Open( new Target { Range = new CurveRange( Curve.Linear, upper ) }, range: true );
		editor.FitView();
		Assert.AreEqual( -2f, editor.ViewMin.x );
		Assert.IsTrue( editor.ViewMin.y <= 0 );
		Assert.AreEqual( 3f, editor.ViewMax.x );
		Assert.IsTrue( editor.ViewMax.y >= 20 );

		editor.EditAxisRange( "2", horizontal: false, maximum: true );
		Assert.AreEqual( 21f, editor.NavigationBounds.Value.Bottom );
		editor.Undo();
		Assert.AreEqual( 20f, editor.NavigationBounds.Value.Bottom );
		editor.FitView();
		Assert.IsTrue( editor.ViewMax.y >= 20 );
	}

	[TestMethod]
	public void OpenPresetListsRefreshAndOldIdsCannotTargetAnotherPreset()
	{
		var first = _surface.Root.AddChild<CurveEditor>();
		var second = _surface.Root.AddChild<CurveEditor>();
		first.Value = Curve.Linear;
		first.SavePreset();
		first.Value = Curve.Ease;
		first.SavePreset();
		var saved = first.PresetStore.Presets;
		Assert.AreEqual( 2, second.Descendants.Count( panel => panel.HasClass( "saved-curve-preset" ) ) );

		first.DeletePreset( 0 );
		Assert.AreEqual( 1, second.Descendants.Count( panel => panel.HasClass( "saved-curve-preset" ) ) );
		second.PresetStore.Replace( saved[1].Id, Curve.EaseOut );
		Assert.AreEqual( Curve.EaseOut.Evaluate( 0.5f ), first.SavedPresets[0].Evaluate( 0.5f ) );

		// A callback from a dismissed menu may still hold the removed identity.
		second.PresetStore.Replace( saved[0].Id, Curve.EaseIn );
		second.PresetStore.Remove( saved[0].Id );
		Assert.AreEqual( 1, first.SavedPresets.Count );
		Assert.AreEqual( saved[1].Id, first.PresetStore.Presets[0].Id );
		first.Delete( true );
		second.SavePreset();
		Assert.AreEqual( 2, second.SavedPresets.Count );
	}

	[TestMethod]
	public void ExistingSavedCurvesArePreservedWhenAddingPresetIds()
	{
		_cookies.Set( "curveeditor.presets", new List<Curve> { Curve.EaseIn } );
		var editor = _surface.Root.AddChild<CurveEditor>();
		Assert.AreEqual( 1, editor.SavedPresets.Count );
		Assert.AreEqual( Curve.EaseIn.Evaluate( 0.5f ), editor.SavedPresets[0].Evaluate( 0.5f ) );
	}

	[TestMethod]
	public void ShiftClickingAnotherCurveKeepsThePrimaryKeyOnItsOwnCurve()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 600;
		editor.Channels = [new( "A", Color.Red, Curve.Ease ), new( "B", Color.Blue, (Curve)0.8f )];
		editor.SelectKey( 1 );
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		_surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( new Vector2( 0.25f, 0.8f ) ) );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, KeyboardModifiers.Shift );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, KeyboardModifiers.Shift );
		Frame();

		Assert.AreEqual( 0, editor.ActiveCurve );
		Assert.AreEqual( 1, editor.SelectedIndex );
		CollectionAssert.AreEquivalent( new[] { new CurveEditor.CurveKey( 0, 1 ) }, editor.SelectedKeys.ToArray() );
	}

	[TestMethod]
	public void LosingFocusCancelsCurveBoxSelection()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 600;
		editor.Value = Curve.Ease;
		editor.SelectKey( 1 );
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		_surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( new Vector2( 0.25f, 0.8f ) ) );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default );
		Frame();
		Assert.IsTrue( canvas.IsBoxSelecting );
		Assert.AreEqual( 0, editor.SelectedKeys.Count );
		editor.Descendants.OfType<NumberEntry>().First().Focus();
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default );
		Frame();

		Assert.IsFalse( canvas.IsBoxSelecting );
		Assert.AreEqual( 1, editor.SelectedIndex );
		Assert.AreEqual( 1, editor.SelectedKeys.Count );
	}

	[TestMethod]
	public void ReleasingAnotherMouseButtonDoesNotEndCanvasPanning()
	{
		var canvas = _surface.Root.AddChild<CanvasPanel>();
		canvas.Style.Width = 600;
		canvas.Style.Height = 400;
		Frame();
		_surface.MouseMoved( canvas.Box.Rect.Center );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseMiddle, true, default );
		Frame();
		Assert.IsTrue( canvas.IsPanning );
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default );
		Frame();
		Assert.IsTrue( canvas.IsPanning );
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseMiddle, false, default );
		Frame();
		Assert.IsFalse( canvas.IsPanning );

		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseMiddle, true, default );
		Frame();
		Assert.IsTrue( canvas.IsPanning );
		_surface.Input.CancelPointerInteraction();
		Frame();
		Assert.IsFalse( canvas.IsPanning );
	}

	[TestMethod]
	public void RightClickInsertTargetsTheCurveUnderTheMouse()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 600;
		editor.Channels = [new( "A", Color.Red, (Curve)0.2f ), new( "B", Color.Blue, (Curve)0.8f )];
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		var position = canvas.Box.Rect.Position + canvas.CanvasToScreen( new Vector2( 0.25f, 0.8f ) );
		_surface.MouseMoved( position );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseRight, true, default );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseRight, false, default );
		Frame();

		Assert.AreEqual( 1, editor.ActiveCurve );
		var insert = _surface.Root.Descendants.OfType<Menu>().Single( menu => menu.Text == "Insert key on curve" );
		insert.Clicked();
		Assert.AreEqual( 1, editor.Channels[0].Value.Length );
		Assert.AreEqual( 2, editor.Channels[1].Value.Length );
		editor.Undo();
		Assert.AreEqual( 1, editor.Channels[1].Value.Length );
	}
	[TestMethod]
	public void KeysArePanelsWithFixedSizeAndClippedPicking()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 700;
		editor.Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.5f, 0.5f ), new Curve.Frame( 1, 1 ) );
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		var handles = canvas.Descendants.OfType<CanvasPanel.Handle>().Where( x => x.HasClass( "curve-key" ) ).ToArray();
		Assert.AreEqual( 3, handles.Length );
		var middle = handles.Single( x => x.Position == new Vector2( 0.5f ) );
		var size = middle.Box.Rect.Size;
		Panel hit = null;
		Vector2 Screen( Vector2 point ) => canvas.Box.Rect.Position + canvas.CanvasToScreen( point );
		PanelInput.CheckHover( _surface.Root, Screen( middle.Position ), ref hit );
		Assert.AreSame( middle, hit );
		canvas.ZoomAt( canvas.CanvasToScreen( middle.Position ), 0.5f );
		Frame();
		Assert.AreEqual( size, middle.Box.Rect.Size );
		hit = null;
		PanelInput.CheckHover( _surface.Root, Screen( middle.Position ), ref hit );
		Assert.AreSame( middle, hit );
		hit = null;
		PanelInput.CheckHover( _surface.Root, Screen( Vector2.Zero ), ref hit );
		Assert.IsFalse( hit is CanvasPanel.Handle, "Off-plot keys must not intercept input in the axis gutter." );
	}

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void DraggedKeysStayInTheVisiblePlotAndCanBeGrabbedAgain( bool zoomed, bool group )
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 700;
		var original = new Curve( new Curve.Frame( 0.4f, 0.4f ), new Curve.Frame( 0.6f, 0.6f ) )
		{
			ValueRange = new( -10, 30 )
		};
		editor.Value = original;
		editor.SnapToGrid = true;
		editor.SnapIncrement = new( 0.3f, 0.3f );
		if ( zoomed )
			editor.SetViewBounds( new( 0.2f, -2 ), new( 0.8f, 22 ) );
		if ( group )
			editor.SelectAll();
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		void Move( Vector2 point )
		{
			_surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( point ) );
			Frame();
		}
		void Press( bool down )
		{
			_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, down, default );
			Frame();
		}
		Move( new( 0.4f, 0.4f ) );
		Press( true );
		Move( new( 0.4f, 2 ) );
		Press( false );
		float top = zoomed ? 0.8f : 1;
		Assert.AreEqual( top, editor.Value.Frames[group ? 1 : 0].Value, 0.0001f );
		if ( group )
			Assert.AreEqual( 0.2f, editor.Value.Frames[1].Value - editor.Value.Frames[0].Value, 0.0001f );
		var key = editor.Value.Frames[group ? 1 : 0];
		Move( new( key.Time, key.Value ) );
		Press( true );
		Move( new( key.Time, -2 ) );
		Press( false );
		Assert.AreEqual( zoomed ? 0.2f : 0, editor.Value.Frames[0].Value, 0.0001f );
		editor.Undo();
		editor.Undo();
		CollectionAssert.AreEqual( original.Frames.ToArray(), editor.Value.Frames.ToArray() );
	}

	[TestMethod]
	public void CurveNavigationKeepsTimeBoundsButCanRevealOutOfRangeValues()
	{
		var editor = _surface.Root.AddChild<CurveEditor>();
		editor.Style.Width = 700;
		var original = new Curve( new Curve.Frame( 0, -2 ), new Curve.Frame( 1, 3 ) );
		editor.Value = original;
		editor.NavigationBounds = new Rect( 0, 0, 1, 1 );
		Frame();
		var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
		canvas.ZoomAt( canvas.CanvasToScreen( new( 0.5f ) ), 8 );
		Assert.AreEqual( 0f, editor.ViewMin.x, 0.0001f );
		Assert.AreEqual( 1f, editor.ViewMax.x, 0.0001f );
		Assert.IsTrue( editor.ViewMin.y < -2 && editor.ViewMax.y > 3 );
		float before = editor.ViewMin.y;
		canvas.Pan( new( 1000, 300 ) );
		Assert.IsTrue( editor.ViewMin.y > before );
		canvas.Pan( new( -1000, -600 ) );
		Assert.IsTrue( editor.ViewMin.y < before );
		Frame();
		Assert.AreEqual( 0f, editor.ViewMin.x, 0.0001f );
		Assert.AreEqual( 1f, editor.ViewMax.x, 0.0001f );
		editor.FitView();
		Assert.IsTrue( editor.ViewMin.y < -2 && editor.ViewMax.y > 3 );
		Assert.AreEqual( original.ValueRange, editor.Value.ValueRange );
		CollectionAssert.AreEqual( original.Frames.ToArray(), editor.Value.Frames.ToArray() );
	}

}
