using Sandbox.Helpers;

namespace Editor;

/// <summary>
/// Undo/redo history for a single <see cref="CurveEditor"/>. Created with the editor and thrown away
/// with it, so opening the curve editor always starts with an empty history.
/// </summary>
public sealed class CurveEditorHistory
{
	/// <summary>
	/// Repeats of the same coalescing action (mouse wheel notches) within this window merge into the
	/// entry they already created, instead of filling the history with one entry per notch.
	/// </summary>
	static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds( 500 );

	readonly CurveEditor editor;
	readonly UndoSystem undo = new();

	int depth;
	string title;
	bool coalesce;
	State before;
	bool restoring;

	/// <summary>
	/// Called when an entry is added, undone or redone.
	/// </summary>
	public Action OnChanged;

	public bool CanUndo => undo.Back.Count > 0;
	public bool CanRedo => undo.Forward.Count > 0;

	internal CurveEditorHistory( CurveEditor editor )
	{
		this.editor = editor;
		undo.Initialize();
	}

	/// <summary>
	/// Start recording a change, and stop recording when the returned scope is disposed. For gestures
	/// that span several events, keep the scope in a field and dispose it when the gesture ends.
	/// Nested pushes fold into the outermost one and take its title, and a scope that turns out to
	/// change nothing is discarded. Pass <paramref name="coalesce"/> for repeating actions with no
	/// natural end, so consecutive repeats merge into one entry.
	/// </summary>
	public IDisposable Push( string title, bool coalesce = false )
	{
		if ( restoring )
			return null;

		if ( depth++ == 0 )
		{
			this.title = title;
			this.coalesce = coalesce;
			before = Capture();
		}

		return new Scope( this );
	}

	public bool Undo()
	{
		if ( IsMouseGestureActive ) return false;

		Close();

		if ( !undo.Undo() )
			return false;

		OnChanged?.Invoke();
		return true;
	}

	public bool Redo()
	{
		if ( IsMouseGestureActive ) return false;

		Close();

		if ( !undo.Redo() )
			return false;

		OnChanged?.Invoke();
		return true;
	}

	/// <summary>
	/// Don't undo or redo mid-drag. Restoring rebuilds the handles, and if one of them is being dragged,
	/// Qt never clears the start positions it recorded for that drag because the release has no handle
	/// to go to. The next drag would then move its handle from a missing start position, which throws
	/// it to the top left of the chart.
	/// </summary>
	static bool IsMouseGestureActive => Application.MouseButtons != 0;

	/// <summary>
	/// Commit any open scope. Stops a scope that was never disposed - because the widget holding it
	/// got destroyed mid-edit - from swallowing the rest of the session's history.
	/// </summary>
	void Close()
	{
		while ( depth > 0 )
		{
			Pop();
		}
	}

	void Pop()
	{
		if ( depth == 0 ) return;
		if ( --depth > 0 ) return;

		var after = Capture();
		if ( before.Matches( after ) ) return;

		var from = before;

		if ( coalesce && undo.Back.TryPeek( out var last ) && last.Name == title
			&& DateTime.UtcNow - last.Timestamp < CoalesceWindow )
		{
			last.Redo = () => Restore( after );
			last.Timestamp = DateTime.UtcNow;
		}
		else
		{
			undo.Insert( title, () => Restore( from ), () => Restore( after ) );
		}

		OnChanged?.Invoke();
	}

	State Capture()
	{
		var curves = editor.Curves;
		var states = new CurveState[curves.Count];

		for ( var i = 0; i < states.Length; i++ )
		{
			states[i] = new CurveState( curves[i] );
		}

		return new State
		{
			Curves = states,
			MoveRangeWhenPanning = editor.MoveRangeWhenPanning,
			MoveRangeWhenSettingMinMax = editor.MoveRangeWhenSettingMinMax
		};
	}

	void Restore( State state )
	{
		restoring = true;

		try
		{
			editor.MoveRangeWhenPanning = state.MoveRangeWhenPanning;
			editor.MoveRangeWhenSettingMinMax = state.MoveRangeWhenSettingMinMax;
			editor.UpdateOptionButtons();

			var curves = editor.Curves;

			for ( var i = 0; i < curves.Count && i < state.Curves.Length; i++ )
			{
				var curve = curves[i];

				// Assigning Value rebuilds the handles and refits the viewport, so put the viewport back after
				curve.Value = state.Curves[i].Value;
				curve.Viewport = state.Curves[i].Viewport;
				curve.UpdateHandlePositions();

				editor.UpdateBackgroundFromCurve( curve );
			}

			editor.UpdateRangePolygon();

			// Push the restored curves back out through the value binding
			BindSystem.Flush();

			editor.RefreshRangeWidgets();
		}
		finally
		{
			restoring = false;
		}
	}

	sealed class Scope : IDisposable
	{
		CurveEditorHistory history;

		public Scope( CurveEditorHistory history ) => this.history = history;

		public void Dispose()
		{
			var h = history;
			history = null;
			h?.Pop();
		}
	}

	readonly struct CurveState
	{
		public readonly Curve Value;
		public readonly Vector4 Viewport;

		public CurveState( GraphicsItems.EditableCurve curve )
		{
			Value = curve.Value;
			Viewport = curve.Viewport;
		}
	}

	sealed class State
	{
		public CurveState[] Curves;
		public bool MoveRangeWhenPanning;
		public bool MoveRangeWhenSettingMinMax;

		public bool Matches( State other )
		{
			if ( MoveRangeWhenPanning != other.MoveRangeWhenPanning ) return false;
			if ( MoveRangeWhenSettingMinMax != other.MoveRangeWhenSettingMinMax ) return false;
			if ( Curves.Length != other.Curves.Length ) return false;

			for ( var i = 0; i < Curves.Length; i++ )
			{
				if ( Curves[i].Viewport != other.Curves[i].Viewport ) return false;
				if ( !CurvesMatch( Curves[i].Value, other.Curves[i].Value ) ) return false;
			}

			return true;
		}

		/// <summary>
		/// Curve compares its frames by array reference, so compare them properly by hand.
		/// </summary>
		static bool CurvesMatch( in Curve a, in Curve b )
		{
			if ( a.TimeRange != b.TimeRange || a.ValueRange != b.ValueRange ) return false;
			if ( a.Frames == b.Frames ) return true;
			if ( a.Length != b.Length ) return false;

			for ( var i = 0; i < a.Length; i++ )
			{
				var x = a[i];
				var y = b[i];

				if ( x.Time != y.Time || x.Value != y.Value || x.In != y.In || x.Out != y.Out || x.Mode != y.Mode )
					return false;
			}

			return true;
		}
	}
}
