using Microsoft.AspNetCore.Components;
using Sandbox.UI.Construct;
using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// Edits a Curve, CurveRange or named collection of curves. Keys retain the normalized coordinates used by Curve.Frame;
/// numeric fields and axes display actual time and value units. View navigation never edits data.
/// </summary>
[Library( "curveeditor" )]
[StyleSheet.Inline( "curveeditor", """
	.curveeditor { flex-direction: column; min-width: 0; flex-grow: 1; pointer-events: all; }
	.curveeditor > .curve-body { flex-direction: row; height: 380px; flex-shrink: 0; min-width: 0; }
	.curveeditor .curve-body > .curve-canvas { flex-grow: 1; min-width: 0; overflow: hidden; pointer-events: all; }
	.curveeditor .curve-inspector { display: none; width: 184px; flex-shrink: 0; flex-direction: column; align-items: stretch; }
	.curveeditor.show-inspector .curve-inspector { display: flex; }
	.curveeditor .curve-key-field { flex-direction: column; align-items: stretch; }
	.curveeditor .curve-presets { flex-direction: row; flex-shrink: 0; flex-wrap: wrap; max-height: 148px; overflow: scroll; }
	.curveeditor .curve-preset { width: 54px; height: 30px; flex-shrink: 0; }
	""" )]
public partial class CurveEditor : Panel
{
	/// <summary>
	/// A key's identity within this editor: indexes refer to the displayed curves.
	/// </summary>
	public readonly record struct CurveKey( int CurveIndex, int KeyIndex );

	const float KeySpacing = 0.0001f;
	Curve[] _curves = [Curve.Ease];
	Curve ActiveCurveValue => _curves[ActiveCurve];
	readonly HashSet<CurveKey> _selection = new();
	readonly CurveCanvas _canvas;
	readonly CurveKeyInspector _inspector;
	readonly CurveEditorToolbar _tools;
	readonly Stack<EditorState> _undo = new(), _redo = new();
	EditorState _currentEdit;

	record EditorState( Curve[] Curves, CurveKey[] Selection, int Active, int Primary );

	/// <summary>
	/// The active curve. Assigning switches to single-curve mode and clears undo history without a change event.
	/// </summary>
	[Parameter]
	public Curve Value
	{
		get => ActiveCurveValue;
		set => Assign( [Normalize( value )] );
	}

	/// <summary>
	/// The two range boundaries. Assigning enables filled range editing; curves can have different ranges and key times.
	/// </summary>
	[Parameter]
	public CurveRange RangeValue
	{
		get => new( _curves[0], _curves.Length == 2 ? _curves[1] : _curves[0] );
		set => Assign( [Normalize( value.A ), Normalize( value.B )], range: true );
	}
	public bool IsRange { get; private set; }
	public int ActiveCurve { get; private set; }
	public int SelectedIndex { get; private set; } = -1;
	public IReadOnlyCollection<CurveKey> SelectedKeys => _selection.ToArray();
	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;

	/// <summary>
	/// Snap dragged keys to SnapIncrement in actual time/value units. Ctrl temporarily enables snapping.
	/// </summary>
	[Parameter]
	public bool SnapToGrid { get; set; }

	/// <summary>
	/// Time and value snap increments in actual units, independent of zoom and grid labels. Non-positive values disable an axis.
	/// </summary>
	[Parameter] public Vector2 SnapIncrement { get; set; } = new( 0.01f, 0.01f );

	/// <summary>
	/// Raised for user edits to the active curve, including undo and cancellation.
	/// </summary>
	[Parameter]
	public Action<Curve> ValueChanged { get; set; }

	/// <summary>
	/// Raised once for each change to either boundary in range mode.
	/// </summary>
	[Parameter]
	public Action<CurveRange> RangeValueChanged { get; set; }
	public event Action EditStarted;
	public event Action EditFinished;

	public CurveEditor()
	{
		AddClass( "curveeditor" );
		_tools = AddChild( new CurveEditorToolbar( this ) );

		var body = Add.Panel( "curve-body" );
		_canvas = body.AddChild( new CurveCanvas( this ) );
		_inspector = body.AddChild( new CurveKeyInspector( this ) );
		AddChild( new CurvePresets( this ) );
		Sync();
	}

	static bool Same( Curve a, Curve b ) => a.TimeRange == b.TimeRange && a.ValueRange == b.ValueRange
		&& a.Length == b.Length && (a.Length == 0 || a.Frames.SequenceEqual( b.Frames ));
	static bool Same( Curve[] a, Curve[] b ) => a.Length == b.Length && a.Zip( b ).All( x => Same( x.First, x.Second ) );

	static Curve Normalize( Curve curve )
	{
		curve = WithValidRanges( curve );
		var frames = curve.Length == 0 ? Array.Empty<Curve.Frame>() : curve.Frames.OrderBy( x => x.Time ).ToArray();
		for ( int i = 0; i < frames.Length; i++ )
		{
			if ( !float.IsFinite( frames[i].Time ) || !float.IsFinite( frames[i].Value ) )
				throw new ArgumentException( "Curve keys must have finite time and value coordinates.", nameof( curve ) );
			// Legacy curves may contain unused infinite tangents. Repair the editing copy only.
			if ( !float.IsFinite( frames[i].In ) ) frames[i].In = 0;
			if ( !float.IsFinite( frames[i].Out ) ) frames[i].Out = 0;
		}
		return curve.WithFrames( frames );
	}

	internal static Curve WithValidRanges( Curve curve )
	{
		static Vector2 ValidRange( Vector2 range )
		{
			if ( !float.IsFinite( range.x ) || !float.IsFinite( range.y ) || !float.IsFinite( range.y - range.x ) )
				return new( 0, 1 );
			if ( range.x > range.y )
				return new( range.y, range.x );
			if ( range.x == range.y )
				return new( 0, 1 );
			return range;
		}
		curve.TimeRange = ValidRange( curve.TimeRange );
		curve.ValueRange = ValidRange( curve.ValueRange );
		return curve;
	}

	void Assign( Curve[] curves, CurveChannel[] channels = null, bool range = false )
	{
		if ( Same( _curves, curves ) && IsRange == range && SameChannels( channels ) )
			return;
		_canvas?.Finish();
		EndEdit();
		_curves = curves;
		_channels = channels;
		IsRange = range;
		_selection.Clear();
		ActiveCurve = 0;
		SelectedIndex = -1;
		_undo.Clear();
		_redo.Clear();
		Sync();
	}

	EditorState Capture() => new( (Curve[])_curves.Clone(), _selection.ToArray(), ActiveCurve, SelectedIndex );
	void Install( EditorState state )
	{
		_curves = (Curve[])state.Curves.Clone();
		ActiveCurve = state.Active;
		SelectedIndex = state.Primary;
		_selection.Clear();
		_selection.UnionWith( state.Selection );
	}
	void Notify()
	{
		ValueChanged?.Invoke( ActiveCurveValue );
		if ( IsRange )
			RangeValueChanged?.Invoke( RangeValue );
		if ( _channels is not null )
			ChannelsChanged?.Invoke( Channels );
	}
	internal void BeginEdit()
	{
		if ( _currentEdit is not null )
			return;
		_currentEdit = Capture();
		EditStarted?.Invoke();
	}
	internal void EndEdit( bool cancel = false )
	{
		if ( _currentEdit is not { } before )
			return;
		_currentEdit = null;
		if ( !Same( before.Curves, _curves ) )
		{
			if ( cancel )
			{
				Install( before );
				Notify();
			}
			else
			{
				_undo.Push( before );
				_redo.Clear();
			}
		}
		Sync();
		EditFinished?.Invoke();
	}
	void Change( Curve[] curves, CurveKey[] selection = null )
	{
		if ( Same( _curves, curves ) )
		{
			if ( selection is not null )
				SelectKeys( selection );
			return;
		}
		bool single = _currentEdit is null;
		BeginEdit();
		_curves = curves;
		if ( selection is not null )
			SelectKeys( selection );
		Sync();
		Notify();
		if ( single )
			EndEdit();
	}
	void ChangeCurve( Curve curve, int selection )
	{
		var curves = (Curve[])_curves.Clone();
		curves[ActiveCurve] = curve;
		Change( curves, selection < 0 ? [] : [new( ActiveCurve, selection )] );
	}

	public void Undo() => Restore( _undo, _redo );
	public void Redo() => Restore( _redo, _undo );
	public override void OnButtonTyped( ButtonEvent e )
	{
		// Toolbar commands leave focus on their buttons. Keep editor undo available there;
		// text fields retain their own text-editing shortcuts.
		if ( !Descendants.OfType<TextEntry>().Any( x => x.HasFocus ) && e.HasCtrl && e.Button is "z" or "y" )
		{
			if ( e.Button == "y" || e.HasShift )
				Redo();
			else
				Undo();
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}
	void Restore( Stack<EditorState> from, Stack<EditorState> to )
	{
		_canvas.Finish();
		EndEdit();
		if ( !from.TryPop( out var item ) )
			return;
		EditStarted?.Invoke();
		to.Push( Capture() );
		Install( item );
		Sync();
		Notify();
		EditFinished?.Invoke();
	}
	internal Vector2 SnapPoint( Vector2 displayPoint )
	{
		var curve = _curves[0];
		var point = new Vector2( curve.TimeRange.x, curve.ValueRange.x ) + displayPoint * new Vector2( curve.TimeRange.y - curve.TimeRange.x, curve.ValueRange.y - curve.ValueRange.x );
		static float Snap( float value, float step ) => float.IsFinite( step ) && step > 0 ? MathF.Round( value / step ) * step : value;
		point = new( Snap( point.x, SnapIncrement.x ), Snap( point.y, SnapIncrement.y ) );
		return new( (point.x - curve.TimeRange.x) / (curve.TimeRange.y - curve.TimeRange.x), (point.y - curve.ValueRange.x) / (curve.ValueRange.y - curve.ValueRange.x) );
	}

	void EditNumber( string text, bool time )
	{
		if ( SelectedIndex < 0 || !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) || !float.IsFinite( value ) )
			return;
		var key = ActiveCurveValue.Frames[SelectedIndex];
		var range = time ? ActiveCurveValue.TimeRange : ActiveCurveValue.ValueRange;
		var old = range.x + (time ? key.Time : key.Value) * (range.y - range.x);
		var display = time ? _curves[0].TimeRange : _curves[0].ValueRange;
		float delta = (value - old) / (display.y - display.x);
		MoveSelection( time ? new Vector2( delta, 0 ) : new Vector2( 0, delta ) );
	}

	void Sync()
	{
		if ( _inspector is null || !_canvas.IsValid || !_tools.IsValid )
			return;
		ApplyNavigationBounds( _navigationBounds );
		_inspector.Sync();
		_tools.Sync();

	}
}
