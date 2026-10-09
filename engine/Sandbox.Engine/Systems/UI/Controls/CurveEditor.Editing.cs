namespace Sandbox.UI;

public partial class CurveEditor
{
	/// <summary>
	/// Select a boundary without changing its data or the view.
	/// </summary>
	public void SelectCurve( int index )
	{
		if ( index < 0 || index >= _curves.Length )
			return;
		_canvas.Finish();
		ActiveCurve = index;
		SelectKeys( [] );
	}

	public void SelectKey( int index ) => SelectKeys( index >= 0 && index < ActiveCurveValue.Length ? [new( ActiveCurve, index )] : [] );

	/// <summary>
	/// Select keys across either boundary. The last valid key becomes the primary numeric-edit target.
	/// </summary>
	public void SelectKeys( IEnumerable<CurveKey> keys )
	{
		var valid = keys.Where( x => x.CurveIndex >= 0 && x.CurveIndex < _curves.Length && x.KeyIndex >= 0 && x.KeyIndex < _curves[x.CurveIndex].Length ).Distinct().ToArray();
		_selection.Clear();
		_selection.UnionWith( valid );
		SelectedIndex = -1;
		if ( valid.Length > 0 )
		{
			ActiveCurve = valid[^1].CurveIndex;
			SelectedIndex = valid[^1].KeyIndex;
		}
		Sync();
	}

	public void SelectAll() => SelectKeys( AllKeys() );
	IEnumerable<CurveKey> AllKeys()
	{
		for ( int c = 0; c < _curves.Length; c++ )
			for ( int k = 0; k < _curves[c].Length; k++ )
				yield return new( c, k );
	}

	// Display coordinates use curve A's normalized space. Each boundary keeps its own units.
	Vector2 ToDisplay( Curve curve, Vector2 point )
	{
		var timeRange = _curves[0].TimeRange;
		var valueRange = _curves[0].ValueRange;
		return new( (curve.TimeRange.x + point.x * (curve.TimeRange.y - curve.TimeRange.x) - timeRange.x) / (timeRange.y - timeRange.x),
			(curve.ValueRange.x + point.y * (curve.ValueRange.y - curve.ValueRange.x) - valueRange.x) / (valueRange.y - valueRange.x) );
	}
	Vector2 FromDisplay( Curve curve, Vector2 point )
	{
		var timeRange = _curves[0].TimeRange;
		var valueRange = _curves[0].ValueRange;
		return new( (timeRange.x + point.x * (timeRange.y - timeRange.x) - curve.TimeRange.x) / (curve.TimeRange.y - curve.TimeRange.x),
			(valueRange.x + point.y * (valueRange.y - valueRange.x) - curve.ValueRange.x) / (curve.ValueRange.y - curve.ValueRange.x) );
	}
	Vector2 KeyPoint( CurveKey id, Curve[] curves = null )
	{
		var curve = (curves ?? _curves)[id.CurveIndex];
		var key = curve.Frames[id.KeyIndex];
		return ToDisplay( curve, new( key.Time, key.Value ) );
	}

	/// <summary>
	/// Add a normalized key to the active curve. Near-duplicate times select the existing key.
	/// </summary>
	public void AddKey( float time, float value ) => AddKey( new Curve.Frame( time, value ) );
	void AddKey( Curve.Frame key, Curve? source = null )
	{
		if ( !float.IsFinite( key.Time ) || !float.IsFinite( key.Value ) )
			return;
		var curve = source ?? ActiveCurveValue;
		key.Time = Math.Clamp( key.Time, 0, 1 );
		var keys = curve.Frames.ToList();
		int existing = keys.FindIndex( x => MathF.Abs( x.Time - key.Time ) < KeySpacing );
		if ( existing >= 0 )
		{
			SelectKey( existing );
			return;
		}
		keys.Add( key );
		keys.Sort();
		ChangeCurve( curve.WithFrames( keys ), keys.FindIndex( x => x.Time == key.Time ) );
	}

	/// <summary>
	/// Split the curve at a normalized time while retaining its shape, including stepped and linear segments.
	/// </summary>
	public void InsertKey( float time )
	{
		if ( !float.IsFinite( time ) )
			return;
		time = Math.Clamp( time, 0, 1 );
		var curve = ActiveCurveValue;
		var key = new Curve.Frame( time, curve.EvaluateDelta( time ) );
		if ( curve.Length > 0 && time < curve.Frames[0].Time )
			key.Mode = Curve.HandleMode.Linear;
		if ( curve.Length > 0 && time > curve.Frames[^1].Time )
		{
			// The old last key previously extrapolated a constant, regardless of its outgoing tangent.
			var keys = curve.Frames.ToArray();
			if ( keys[^1].Mode is Curve.HandleMode.Mirrored or Curve.HandleMode.Split )
			{
				keys[^1].Out = 0;
				keys[^1].Mode = Curve.HandleMode.Split;
			}
			curve = curve.WithFrames( keys );
		}
		for ( int i = 0; i < curve.Length - 1; i++ )
		{
			var start = curve.Frames[i];
			var end = curve.Frames[i + 1];
			if ( time <= start.Time || time >= end.Time )
				continue;
			if ( start.Mode is Curve.HandleMode.Linear or Curve.HandleMode.Stepped )
			{
				key.Mode = start.Mode;
			}
			else
			{
				float timeSpan = end.Time - start.Time;
				float fraction = (time - start.Time) / timeSpan;
				float outgoing = start.Mode == Curve.HandleMode.Flat ? 0 : start.Out;
				float incoming = end.Mode == Curve.HandleMode.Flat ? 0 : -end.In;
				float valueDelta = end.Value - start.Value;
				float slope = (3 * fraction * fraction * ((incoming + outgoing) * timeSpan - 2 * valueDelta)
					+ 2 * fraction * ((-incoming - 2 * outgoing) * timeSpan + 3 * valueDelta) + outgoing * timeSpan) / timeSpan;
				key.Mode = Curve.HandleMode.Split;
				key.In = -slope;
				key.Out = slope;
			}
			break;
		}
		AddKey( key, curve );
	}

	/// <summary>
	/// Move one key, clamping its time before either neighbour.
	/// </summary>
	public void MoveKey( int index, float time, float value )
	{
		if ( index < 0 || index >= ActiveCurveValue.Length || !float.IsFinite( time ) || !float.IsFinite( value ) )
			return;
		var keys = ActiveCurveValue.Frames.ToArray();
		// Existing curves can have tighter spacing than we allow when adding or dragging keys.
		// A value-only edit must not push those keys apart.
		float min = index == 0 ? Math.Min( 0, keys[index].Time ) : Math.Min( keys[index].Time, keys[index - 1].Time + KeySpacing );
		float max = index == keys.Length - 1 ? Math.Max( 1, keys[index].Time ) : Math.Max( keys[index].Time, keys[index + 1].Time - KeySpacing );
		keys[index].Time = min <= max ? Math.Clamp( time, min, max ) : keys[index].Time;
		keys[index].Value = value;
		ChangeCurve( ActiveCurveValue.WithFrames( keys ), index );
	}

	/// <summary>
	/// Translate the selection by a delta in curve A's normalized display space, keeping group spacing intact.
	/// </summary>
	public void MoveSelection( Vector2 delta ) => MoveSelection( _curves, _selection, delta );
	bool IsCurrentDrag( EditorState edit ) => edit is not null && ReferenceEquals( edit, _currentEdit )
		&& edit.Active == ActiveCurve && edit.Primary == SelectedIndex && SelectedIndex >= 0
		&& _selection.SetEquals( edit.Selection ) && edit.Curves.Length == _curves.Length
		&& !edit.Curves.Where( ( curve, i ) => curve.Length != _curves[i].Length ).Any();

	void MoveSelectionFromStart( Vector2 delta )
	{
		if ( _currentEdit is not null )
			MoveSelection( _currentEdit.Curves, _currentEdit.Selection.ToHashSet(), delta );
	}
	void MoveSelection( Curve[] source, HashSet<CurveKey> selection, Vector2 delta )
	{
		if ( selection.Count == 0 || !float.IsFinite( delta.x ) || !float.IsFinite( delta.y ) )
			return;
		float minDelta = float.NegativeInfinity, maxDelta = float.PositiveInfinity;
		foreach ( var id in selection )
		{
			var curve = source[id.CurveIndex];
			var keys = curve.Frames;
			var key = keys[id.KeyIndex];
			float min = Math.Min( 0, key.Time ), max = Math.Max( 1, key.Time );
			if ( id.KeyIndex > 0 && !selection.Contains( id with
			{
				KeyIndex = id.KeyIndex - 1
			} ) )
				min = Math.Min( key.Time, keys[id.KeyIndex - 1].Time + KeySpacing );
			if ( id.KeyIndex < keys.Length - 1 && !selection.Contains( id with
			{
				KeyIndex = id.KeyIndex + 1
			} ) )
				max = Math.Max( key.Time, keys[id.KeyIndex + 1].Time - KeySpacing );
			float scale = (curve.TimeRange.y - curve.TimeRange.x) / (_curves[0].TimeRange.y - _curves[0].TimeRange.x);
			minDelta = Math.Max( minDelta, (min - key.Time) * scale );
			maxDelta = Math.Min( maxDelta, (max - key.Time) * scale );
		}
		delta.x = minDelta <= maxDelta ? Math.Clamp( delta.x, minDelta, maxDelta ) : 0;
		var result = (Curve[])source.Clone();
		foreach ( var group in selection.GroupBy( x => x.CurveIndex ) )
		{
			var curve = source[group.Key];
			var keys = curve.Frames.ToArray();
			foreach ( var id in group )
			{
				var p = FromDisplay( curve, KeyPoint( id, source ) + delta );
				keys[id.KeyIndex].Time = delta.x == 0 ? keys[id.KeyIndex].Time : p.x;
				keys[id.KeyIndex].Value = delta.y == 0 ? keys[id.KeyIndex].Value : p.y;
			}
			result[group.Key] = curve.WithFrames( keys );
		}
		Change( result );
	}

	public void SetKeyMode( int index, Curve.HandleMode mode )
	{
		if ( index < 0 || index >= ActiveCurveValue.Length || !Enum.IsDefined( mode ) )
			return;
		SelectKey( index );
		SetSelectedMode( mode );
	}

	/// <summary>
	/// Set interpolation on all selected keys, across both boundaries, as a single edit.
	/// </summary>
	public void SetSelectedMode( Curve.HandleMode mode )
	{
		if ( !Enum.IsDefined( mode ) )
			return;
		EditSelected( ( keys, index ) =>
		{
			keys[index].Mode = mode;
			if ( mode == Curve.HandleMode.Mirrored )
				keys[index].In = -keys[index].Out;
		} );
	}
	void EditSelected( Action<Curve.Frame[], int> edit )
	{
		var curves = (Curve[])_curves.Clone();
		foreach ( var group in _selection.GroupBy( x => x.CurveIndex ) )
		{
			var keys = curves[group.Key].Frames.ToArray();
			foreach ( var id in group )
				edit( keys, id.KeyIndex );
			curves[group.Key] = curves[group.Key].WithFrames( keys );
		}
		Change( curves );
	}

	/// <summary>
	/// Calculate monotone, locally clamped slopes. This is a one-shot operation stored as ordinary mirrored tangents.
	/// </summary>
	public void SmoothSelected() => EditSelected( ( keys, index ) =>
	{
		float Slope( int a, int b ) => (keys[b].Value - keys[a].Value) / (keys[b].Time - keys[a].Time);
		float slope = 0;
		if ( keys.Length > 1 )
		{
			if ( index == 0 )
			{
				slope = Slope( 0, 1 );
			}
			else if ( index == keys.Length - 1 )
			{
				slope = Slope( index - 1, index );
			}
			else
			{
				float left = Slope( index - 1, index ), right = Slope( index, index + 1 );
				if ( left * right > 0 )
				{
					float a = keys[index].Time - keys[index - 1].Time, b = keys[index + 1].Time - keys[index].Time;
					slope = 3 * (a + b) / ((2 * b + a) / left + (b + 2 * a) / right);
				}
			}
		}
		keys[index].Mode = Curve.HandleMode.Mirrored;
		keys[index].In = -slope;
		keys[index].Out = slope;
	} );

	public void SetTangent( int index, bool incoming, float slope )
	{
		if ( index < 0 || index >= ActiveCurveValue.Length || !float.IsFinite( slope ) )
			return;
		var keys = ActiveCurveValue.Frames.ToArray();
		if ( keys[index].Mode is not (Curve.HandleMode.Mirrored or Curve.HandleMode.Split) )
			return;
		if ( incoming )
			keys[index].In = slope;
		else
			keys[index].Out = slope;
		if ( keys[index].Mode == Curve.HandleMode.Mirrored )
		{
			if ( incoming )
				keys[index].Out = -slope;
			else
				keys[index].In = -slope;
		}
		var curves = (Curve[])_curves.Clone();
		curves[ActiveCurve] = ActiveCurveValue.WithFrames( keys );
		Change( curves );
	}

	/// <summary>
	/// Delete selected keys while keeping at least one key per populated boundary.
	/// </summary>
	public void RemoveSelected()
	{
		var curves = (Curve[])_curves.Clone();
		foreach ( var group in _selection.GroupBy( x => x.CurveIndex ) )
		{
			var keys = curves[group.Key].Frames.ToList();
			foreach ( var id in group.OrderByDescending( x => x.KeyIndex ) )
				if ( keys.Count > 1 )
					keys.RemoveAt( id.KeyIndex );
			curves[group.Key] = curves[group.Key].WithFrames( keys );
		}
		Change( curves, [] );
	}

	/// <summary>
	/// Replace the active curve's keys, optionally applying the preset's axis ranges too.
	/// </summary>
	public void ApplyPreset( Curve preset, bool includeRanges = false ) => ChangeCurve( includeRanges ? Normalize( preset ) : ActiveCurveValue.WithFrames( Normalize( preset ).Frames ), -1 );
}
