using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class CurveEditorTests
{
	CurveEditor editor;
	[TestCleanup] public void Cleanup() => editor?.Delete( true );

	[TestInitialize] public void Setup() => ThreadSafe.MarkMainThread();

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void AxisEditsRescaleNavigationLimitsAndUndoKeepsKeysVisible( bool horizontal, bool maximum )
	{
		editor = new CurveEditor { Channels = [new( "X", Color.Red, Curve.Ease ), new( "Y", Color.Green, Curve.Ease )] };
		editor.NavigationBounds = new Rect( 0, 0, 1, 1 );
		editor.EditAxisRange( maximum ? "10" : "-10", horizontal, maximum );
		var min = horizontal ? editor.ViewMin.x : editor.ViewMin.y;
		var max = horizontal ? editor.ViewMax.x : editor.ViewMax.y;
		Assert.AreEqual( maximum ? 0 : -10, min, 0.001f );
		Assert.AreEqual( maximum ? 10 : 1, max, 0.001f );
		var limits = editor.NavigationBounds.Value;
		Assert.AreEqual( min, horizontal ? limits.Left : limits.Top, 0.001f );
		Assert.AreEqual( max, horizontal ? limits.Right : limits.Bottom, 0.001f );
		editor.Undo();
		Assert.AreEqual( Vector2.Zero, editor.ViewMin ); Assert.AreEqual( Vector2.One, editor.ViewMax );
		Assert.AreEqual( new Rect( 0, 0, 1, 1 ), editor.NavigationBounds.Value );
		editor.Redo();
		Assert.AreEqual( max, horizontal ? editor.ViewMax.x : editor.ViewMax.y, 0.001f );
	}

	[TestMethod]
	public void NamedChannelsShareSelectionAndUndoWithoutBecomingAFilledRange()
	{
		var original = new Curve( new Curve.Frame( 0.5f, 0.5f ) );
		editor = new CurveEditor { Channels = [new( "X", Color.Red, original ), new( "Y", Color.Green, original ), new( "Z", Color.Blue, original )] };
		int notifications = 0, rangeNotifications = 0;
		editor.ChannelsChanged = channels => { notifications++; Assert.AreEqual( "Z", channels[2].Name ); };
		editor.RangeValueChanged = _ => rangeNotifications++;
		Assert.IsFalse( editor.IsRange );
		editor.SelectAll(); Assert.AreEqual( 3, editor.SelectedKeys.Count );
		editor.MoveSelection( new( 0.1f, 0.2f ) );
		foreach ( var channel in editor.Channels )
		{
			Assert.AreEqual( 0.6f, channel.Value.Frames[0].Time, 0.0001f );
			Assert.AreEqual( 0.7f, channel.Value.Frames[0].Value, 0.0001f );
		}
		Assert.AreEqual( 1, notifications ); Assert.AreEqual( 0, rangeNotifications );
		editor.Undo();
		foreach ( var channel in editor.Channels ) Assert.AreEqual( 0.5f, channel.Value.Frames[0].Value );
		editor.Redo();
		editor.SelectCurve( 2 ); editor.ApplyPreset( Curve.Linear );
		Assert.AreEqual( 1, editor.Channels[0].Value.Length );
		Assert.AreEqual( 1, editor.Channels[1].Value.Length );
		Assert.AreEqual( 2, editor.Channels[2].Value.Length );
		Assert.AreEqual( Color.Blue, editor.Channels[2].Color );
	}

	[TestMethod]
	public void TwoIndependentChannelsAndRangeModeAreExplicitAndAssignmentsAreSilent()
	{
		editor = new CurveEditor();
		int changes = 0; editor.ChannelsChanged = _ => changes++;
		editor.Channels = [new( "R", Color.Red, Curve.Ease ), new( "G", Color.Green, Curve.Ease )];
		Assert.IsFalse( editor.IsRange );
		editor.RangeValue = new( Curve.Ease, Curve.Ease ); Assert.IsTrue( editor.IsRange );
		editor.Channels = [new( "R", Color.Red, Curve.Ease ), new( "G", Color.Green, Curve.Ease )];
		Assert.IsFalse( editor.IsRange );
		editor.Value = Curve.Linear; Assert.AreEqual( 1, editor.Channels.Count );
		Assert.AreEqual( 0, changes ); Assert.IsFalse( editor.CanUndo );
		Assert.ThrowsException<System.ArgumentException>( () => editor.Channels = [] );
	}

	[TestMethod]
	public void AdvancedButtonTogglesARightHandInspectorWithoutChangingData()
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 1000, 800 ), DpiScale = 1, MouseInside = true };
			editor = new CurveEditor { Parent = surface.Root }; editor.Style.Width = 800;
			void Frame() { for ( int i = 0; i < 4; i++ ) UiTesting.Frame( surface ); }
			Frame();
			var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
			var inspector = editor.Descendants.Single( p => p.HasClass( "curve-inspector" ) );
			var toggle = editor.Descendants.OfType<Button>().Single( p => p.Text == "Advanced" );
			var width = canvas.Box.Rect.Width;
			Assert.IsFalse( editor.ShowInspector ); Assert.IsFalse( inspector.IsVisible );
			void Toggle()
			{
				surface.MouseMoved( toggle.Box.Rect.Position + toggle.Box.Rect.Size * 0.5f ); Frame();
				surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default ); Frame();
				surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default ); Frame();
			}
			Toggle();
			Assert.IsTrue( editor.ShowInspector ); Assert.IsTrue( inspector.IsVisible );
			Assert.IsTrue( inspector.Box.Rect.Left >= canvas.Box.Rect.Right - 1 );
			Assert.IsTrue( canvas.Box.Rect.Width < width );
			Toggle(); Assert.IsFalse( editor.ShowInspector );
			Assert.AreEqual( width, canvas.Box.Rect.Width, 0.01f );
			Assert.IsFalse( editor.CanUndo );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}

	[TestMethod]
	public void AxisFieldsEditCurveRangesWhileNavigationOnlyChangesTheView()
	{
		var curve = Curve.Ease;
		curve.TimeRange = new( 2, 8 ); curve.ValueRange = new( -10, 10 );
		editor = new CurveEditor { Value = curve };
		int changes = 0;
		editor.ValueChanged = _ => changes++;
		editor.SetViewBounds( new( 0, -20 ), new( 10, 30 ) );
		Assert.AreEqual( new Vector2( 0, -20 ), editor.ViewMin );
		Assert.AreEqual( new Vector2( 10, 30 ), editor.ViewMax );
		Assert.AreEqual( 0, changes );
		var maximum = editor.Descendants.OfType<NumberEntry>().Single( x => x.HasClass( "axis-y-max" ) );
		maximum.OnTextEdited( "50" );
		Assert.AreEqual( new Vector2( -10, 50 ), editor.Value.ValueRange );
		Assert.AreEqual( 20f, editor.Value.Evaluate( 5 ), 0.001f );
		Assert.AreEqual( 70f, editor.ViewMax.y );
		CollectionAssert.AreEqual( curve.Frames.ToArray(), editor.Value.Frames.ToArray() );
		Assert.AreEqual( 1, changes );
		maximum.OnTextEdited( "-30" ); maximum.OnTextEdited( "NaN" );
		Assert.AreEqual( 1, changes );
		editor.Undo();
		Assert.AreEqual( curve.ValueRange, editor.Value.ValueRange );
		editor.Redo(); Assert.AreEqual( 50f, editor.Value.ValueRange.y );
	}

	[TestMethod]
	public void AxisRangeEditsApplyTheSameDeltaToBothCurvesAsOneUndoStep()
	{
		var a = Curve.Linear; a.TimeRange = new( 0, 5 );
		var b = Curve.Ease; b.TimeRange = new( 1, 4 );
		editor = new CurveEditor { RangeValue = new( a, b ) };
		int changes = 0; editor.RangeValueChanged = _ => changes++;
		editor.BeginEdit();
		editor.EditAxisRange( "8", true, true );
		editor.EditAxisRange( "10", true, true );
		editor.EndEdit();
		Assert.AreEqual( new Vector2( 0, 10 ), editor.RangeValue.A.TimeRange );
		Assert.AreEqual( new Vector2( 1, 9 ), editor.RangeValue.B.TimeRange );
		Assert.AreEqual( 2, changes );
		editor.Undo();
		Assert.AreEqual( a.TimeRange, editor.RangeValue.A.TimeRange );
		Assert.AreEqual( b.TimeRange, editor.RangeValue.B.TimeRange );
		Assert.IsFalse( editor.CanUndo );
	}

	[TestMethod]
	public void KeysStaySortedAndDistinctAndKeepRanges()
	{
		var curve = Curve.Linear;
		curve.TimeRange = new( 2, 8 ); curve.ValueRange = new( -10, 10 );
		editor = new CurveEditor { Value = curve };
		editor.AddKey( 0.5f, 0.75f );
		editor.AddKey( 0.5f, 1 );
		Assert.AreEqual( 3, editor.Value.Length );
		editor.MoveKey( 1, 2, 0.2f );
		Assert.IsTrue( editor.Value.Frames[1].Time < editor.Value.Frames[2].Time );
		Assert.AreEqual( new Vector2( 2, 8 ), editor.Value.TimeRange );
		Assert.AreEqual( new Vector2( -10, 10 ), editor.Value.ValueRange );
		editor.MoveKey( 1, float.NaN, 0 );
		Assert.AreEqual( 0.2f, editor.Value.Frames[1].Value );
	}

	[TestMethod]
	public void TangentsRespectMirroredAndSplitModes()
	{
		editor = new CurveEditor();
		editor.SetTangent( 0, false, 3 );
		Assert.AreEqual( -3f, editor.Value.Frames[0].In );
		Assert.AreEqual( 3f, editor.Value.Frames[0].Out );
		editor.SetKeyMode( 0, Curve.HandleMode.Split );
		editor.SetTangent( 0, true, 2 );
		Assert.AreEqual( 3f, editor.Value.Frames[0].Out );
		Assert.AreEqual( 2f, editor.Value.Frames[0].In );
		editor.SetKeyMode( 0, Curve.HandleMode.Flat );
		editor.SetTangent( 0, false, 10 );
		Assert.AreEqual( 3f, editor.Value.Frames[0].Out );
	}

	[TestMethod]
	public void WholeDragIsOneUndoStepAndCancellationRestoresOriginal()
	{
		editor = new CurveEditor();
		int started = 0, finished = 0;
		editor.EditStarted += () => started++;
		editor.EditFinished += () => finished++;
		editor.SelectKey( 0 );
		editor.BeginEdit();
		editor.MoveKey( 0, 0.1f, 0.1f );
		editor.MoveKey( 0, 0.2f, 0.2f );
		editor.EndEdit();
		Assert.AreEqual( 1, started ); Assert.AreEqual( 1, finished );
		editor.Undo();
		Assert.AreEqual( 0f, editor.Value.Frames[0].Time );
		Assert.IsFalse( editor.CanUndo );
		editor.Redo(); Assert.AreEqual( 0.2f, editor.Value.Frames[0].Time );
		editor.BeginEdit(); editor.MoveKey( 0, 0.3f, 0.8f ); editor.EndEdit( true );
		Assert.AreEqual( 0.2f, editor.Value.Frames[0].Time );
		editor.Undo(); Assert.AreEqual( 0f, editor.Value.Frames[0].Time );
	}

	[TestMethod]
	public void MouseDragChangesKeyAndTangentWithSingleUndoSteps()
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 600, 500 ), DpiScale = 1, MouseInside = true };
			editor = new CurveEditor { Parent = surface.Root, Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.5f, 0.5f ), new Curve.Frame( 1, 1 ) ) };
			editor.Style.Width = 600;
			void Frame() { for ( int i = 0; i < 3; i++ ) UiTesting.Frame( surface ); }
			void Move( Vector2 p ) { surface.MouseMoved( p ); Frame(); surface.MouseMoved( p ); Frame(); }
			void Button( bool down ) { surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, down, default ); Frame(); }
			Frame();
			var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
			var center = canvas.Box.Rect.Position + canvas.CanvasToScreen( new( 0.5f, 0.5f ) );
			Move( center ); Button( true );
			Move( center + new Vector2( 30, -15 ) );
			Move( center + new Vector2( 60, -30 ) ); Button( false );
			Assert.IsTrue( editor.Value.Frames[1].Time > 0.5f );
			Assert.IsTrue( editor.Value.Frames[1].Value > 0.5f );
			editor.Undo();
			Assert.AreEqual( 0.5f, editor.Value.Frames[1].Time );
			Assert.IsFalse( editor.CanUndo );
			Move( center + new Vector2( 48, 0 ) ); Button( true );
			Move( center + new Vector2( 48, -30 ) ); Button( false );
			Assert.IsTrue( editor.Value.Frames[1].Out > 0 );
			Assert.AreEqual( -editor.Value.Frames[1].Out, editor.Value.Frames[1].In );
			editor.Undo();
			Assert.AreEqual( 0f, editor.Value.Frames[1].Out );
			Assert.IsFalse( editor.CanUndo );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}

	[TestMethod]
	public void RangeSelectionMovesInActualUnitsAndUndoRestoresBothBoundaries()
	{
		var a = new Curve( new Curve.Frame( 0.5f, 0.5f ) ) { TimeRange = new( 0, 10 ), ValueRange = new( -5, 5 ) };
		var b = new Curve( new Curve.Frame( 0.5f, 0.5f ) ) { TimeRange = new( 2, 6 ), ValueRange = new( 0, 20 ) };
		editor = new CurveEditor { RangeValue = new( a, b ) };
		int changes = 0;
		editor.RangeValueChanged = _ => changes++;
		editor.SelectKeys( [new( 0, 0 ), new( 1, 0 )] );
		editor.MoveSelection( new( 0.1f, 0.2f ) );
		Assert.AreEqual( 0.6f, editor.RangeValue.A.Frames[0].Time, 0.00001f );
		Assert.AreEqual( 0.7f, editor.RangeValue.A.Frames[0].Value, 0.00001f );
		Assert.AreEqual( 0.75f, editor.RangeValue.B.Frames[0].Time, 0.00001f );
		Assert.AreEqual( 0.6f, editor.RangeValue.B.Frames[0].Value, 0.00001f );
		Assert.AreEqual( b.TimeRange, editor.RangeValue.B.TimeRange );
		Assert.AreEqual( 1, changes );
		editor.Undo();
		CollectionAssert.AreEqual( a.Frames.ToArray(), editor.RangeValue.A.Frames.ToArray() );
		CollectionAssert.AreEqual( b.Frames.ToArray(), editor.RangeValue.B.Frames.ToArray() );
		Assert.AreEqual( 2, editor.SelectedKeys.Count );
		Assert.AreEqual( 1, editor.ActiveCurve );
	}

	[TestMethod]
	public void GroupMovementClampsTogetherBeforeUnselectedNeighbours()
	{
		editor = new CurveEditor { Value = new Curve( new Curve.Frame( 0.1f, 0 ), new Curve.Frame( 0.3f, 0.2f ), new Curve.Frame( 0.6f, 0.8f ), new Curve.Frame( 0.9f, 1 ) ) };
		editor.SelectKeys( [new( 0, 1 ), new( 0, 2 )] );
		editor.MoveSelection( new( 1, 0.1f ) );
		Assert.IsTrue( editor.Value.Frames[2].Time < 0.9f );
		Assert.AreEqual( 0.3f, editor.Value.Frames[2].Time - editor.Value.Frames[1].Time, 0.00001f );
		Assert.AreEqual( 0.9f, editor.Value.Frames[3].Time );
		editor.Undo();
		Assert.AreEqual( 0.3f, editor.Value.Frames[1].Time );
		Assert.IsFalse( editor.CanUndo );
	}

	[TestMethod]
	public void InsertingKeysPreservesCubicLinearFlatAndSteppedEvaluation()
	{
		editor = new CurveEditor();
		foreach ( var mode in System.Enum.GetValues<Curve.HandleMode>() )
		{
			var original = new Curve( new Curve.Frame( 0, 0.2f, -2, 2 ) { Mode = mode }, new Curve.Frame( 1, 0.8f, -0.5f, 0.5f ) );
			editor.Value = original;
			editor.InsertKey( 0.37f );
			Assert.AreEqual( 3, editor.Value.Length );
			for ( int i = 0; i <= 100; i++ ) Assert.AreEqual( original.EvaluateDelta( i / 100f ), editor.Value.EvaluateDelta( i / 100f ), 0.00001f, mode.ToString() );
			editor.Undo();
			CollectionAssert.AreEqual( original.Frames.ToArray(), editor.Value.Frames.ToArray() );
		}
	}

	[TestMethod]
	public void InsertionBeforeAndAfterKeysPreservesConstantExtrapolation()
	{
		var original = new Curve( new Curve.Frame( 0.2f, 0.1f, -3, 3 ), new Curve.Frame( 0.8f, 0.9f, -2, 2 ) );
		editor = new CurveEditor { Value = original };
		editor.InsertKey( 0.1f ); editor.InsertKey( 0.9f );
		for ( int i = 0; i <= 100; i++ ) Assert.AreEqual( original.EvaluateDelta( i / 100f ), editor.Value.EvaluateDelta( i / 100f ), 0.00001f );
	}

	[TestMethod]
	public void SurfaceBoxSelectsBothBoundariesAndEscapeCancelsGroupDrag()
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 600, 600 ), DpiScale = 1, MouseInside = true };
			var a = new Curve( new Curve.Frame( 0.5f, 0.4f ) );
			var b = new Curve( new Curve.Frame( 0.5f, 0.6f ) );
			editor = new CurveEditor { Parent = surface.Root, RangeValue = new( a, b ) };
			editor.Style.Width = 600;
			void Frame() { for ( int i = 0; i < 3; i++ ) UiTesting.Frame( surface ); }
			void Move( Vector2 p ) { surface.MouseMoved( p ); Frame(); surface.MouseMoved( p ); Frame(); }
			void Button( bool down ) { surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, down, default ); Frame(); }
			Frame();
			var canvas = editor.Descendants.OfType<CanvasPanel>().Single();
			Assert.IsTrue( string.IsNullOrEmpty( canvas.Tooltip ) );
			Vector2 Point( float x, float y ) => canvas.Box.Rect.Position + canvas.CanvasToScreen( new( x, y ) );
			Move( Point( 0.4f, 0.7f ) ); Button( true ); Move( Point( 0.6f, 0.3f ) ); Button( false );
			Assert.AreEqual( 2, editor.SelectedKeys.Count );
			Move( Point( 0.5f, 0.4f ) ); Button( true ); Move( Point( 0.7f, 0.5f ) );
			Assert.AreEqual( 0.7f, editor.RangeValue.A.Frames[0].Time, 0.001f );
			Assert.AreEqual( 0.7f, editor.RangeValue.B.Frames[0].Time, 0.001f );
			canvas.OnButtonTyped( new ButtonEvent( "escape", true, 0, default ) ); Button( false );
			Assert.AreEqual( 0.5f, editor.RangeValue.A.Frames[0].Time );
			Assert.AreEqual( 0.5f, editor.RangeValue.B.Frames[0].Time );
			Assert.IsFalse( editor.CanUndo );
			Move( Point( 0.1f, 0.8f ) ); Button( true ); Move( Point( 0.2f, 0.9f ) );
			Assert.AreEqual( 0, editor.SelectedKeys.Count );
			canvas.OnButtonTyped( new ButtonEvent( "escape", true, 0, default ) ); Button( false );
			Assert.AreEqual( 2, editor.SelectedKeys.Count );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}

	[TestMethod]
	public void FilledRangeUsesActualUnitsAndRetainsSteppedDiscontinuities()
	{
		var a = new Curve( new Curve.Frame( 0, 0.2f ) { Mode = Curve.HandleMode.Stepped }, new Curve.Frame( 0.37f, 0.8f ), new Curve.Frame( 1, 0.8f ) );
		a.TimeRange = new( 0, 10 ); a.ValueRange = new( -10, 10 );
		var b = new Curve( new Curve.Frame( 0.5f, 0.5f ) ) { TimeRange = new( 2, 6 ), ValueRange = new( 0, 20 ) };
		var area = CurveEditor.BuildRangeArea( new( a, b ), 0, 1, 32 );
		var first = area.Take( area.Length / 2 ).ToArray();
		Assert.IsTrue( first.Any( p => p.x == 0.37f && System.MathF.Abs( p.y - 0.8f ) < 0.00001f ) );
		Assert.IsTrue( first.Any( p => p.x < 0.37f && p.x > 0.3699f && System.MathF.Abs( p.y - 0.2f ) < 0.00001f ) );
		Assert.IsTrue( area.Skip( area.Length / 2 ).All( p => System.MathF.Abs( p.y - 1 ) < 0.00001f ) );
		Assert.IsTrue( area.All( p => float.IsFinite( p.x ) && float.IsFinite( p.y ) ) );
	}

	[TestMethod]
	public void SmoothSelectedKeepsMonotoneSegmentsWithinTheirKeyValues()
	{
		editor = new CurveEditor { Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.05f, 0.6f ), new Curve.Frame( 0.8f, 0.7f ), new Curve.Frame( 1, 1 ) ) };
		editor.SelectAll(); editor.SmoothSelected();
		float previous = 0;
		for ( int i = 0; i <= 1000; i++ )
		{
			float value = editor.Value.EvaluateDelta( i / 1000f );
			Assert.IsTrue( value >= previous - 0.000001f && value <= 1.000001f ); previous = value;
		}
		Assert.AreEqual( 4, editor.SelectedKeys.Count );
		editor.Undo(); Assert.IsFalse( editor.CanUndo );
	}

	[TestMethod]
	public void SnappingUsesFineActualUnitIncrements()
	{
		editor = new CurveEditor();
		Assert.AreEqual( 0.14f, editor.SnapPoint( new( 0.137f, 0.263f ) ).x, 0.00001f );
		var curve = Curve.Linear;
		curve.TimeRange = new( 2, 8 ); curve.ValueRange = new( -10, 90 );
		editor.Value = curve; editor.SnapIncrement = new( 0.01f, 1 );
		var point = editor.SnapPoint( new( 0.137f, 0.263f ) );
		Assert.AreEqual( 2.82f, 2 + point.x * 6, 0.00001f );
		Assert.AreEqual( 16f, -10 + point.y * 100, 0.00001f );
	}

	[TestMethod]
	public void DeleteKeepsOneKeyAndAssignmentClearsHistoryWithoutNotification()
	{
		editor = new CurveEditor();
		editor.SelectKey( 0 ); editor.RemoveSelected(); editor.RemoveSelected();
		Assert.AreEqual( 1, editor.Value.Length );
		editor.Undo(); Assert.AreEqual( 2, editor.Value.Length );
		int changes = 0;
		editor.ValueChanged = _ => changes++;
		editor.Value = Curve.EaseIn;
		Assert.AreEqual( 0, changes ); Assert.IsFalse( editor.CanRedo ); Assert.IsFalse( editor.CanUndo );
	}
}
