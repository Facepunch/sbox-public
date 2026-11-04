namespace Sandbox.UI;

/// <summary>
/// A compact curve preview. Click to edit in a popup, like other property controls.
/// </summary>
[CustomEditor( typeof( Curve ) )]
[StyleSheet.Inline( "curvecontrol", ".curvecontrol { width: 100%; height: 32px; min-width: 0; flex-shrink: 0; pointer-events: all; cursor: pointer; } .curve-editor-popup { width: 760px; flex-direction: column; }" )]
public class CurveControl : BaseControl
{
	CurveRange _applied;
	CurveRange? _previewValue;
	readonly Painter.CachedLine _lowerLine = new(), _upperLine = new();
	readonly Painter.CachedPolygon _areaFill = new();
	Rect _previewRect;
	readonly Vector2[] _lower = new Vector2[65], _upper = new Vector2[65], _area = new Vector2[130];
	EditorPopup _popup;
	CurveEditor _editor;
	protected virtual bool IsRange => false;

	public CurveControl()
	{
		AddClass( "curvecontrol" );
		AcceptsFocus = true;
		Tooltip = "Edit curve";
		_applied = new( Curve.Ease, Curve.Ease );
	}

	CurveRange ReadValue()
	{
		if ( Property is null )
			return _applied;
		var range = IsRange ? Property.GetValue<CurveRange>() : new CurveRange( Property.GetValue<Curve>(), Property.GetValue<Curve>() );
		return new( CurveEditor.WithValidRanges( range.A ), CurveEditor.WithValidRanges( range.B ) );
	}

	void UpdateNavigationBounds()
	{
		var first = IsRange ? _editor.RangeValue.A : _editor.Value;
		var second = IsRange ? _editor.RangeValue.B : first;
		var min = new Vector2( Math.Min( first.TimeRange.x, second.TimeRange.x ), Math.Min( first.ValueRange.x, second.ValueRange.x ) );
		var max = new Vector2( Math.Max( first.TimeRange.y, second.TimeRange.y ), Math.Max( first.ValueRange.y, second.ValueRange.y ) );
		foreach ( var curve in new[] { first, second } )
		{
			foreach ( var key in curve.Frames )
			{
				float time = curve.TimeRange.x + key.Time * (curve.TimeRange.y - curve.TimeRange.x);
				min.x = Math.Min( min.x, time );
				max.x = Math.Max( max.x, time );
			}
		}
		_editor.NavigationBounds = new Rect( min, max - min );
	}

	void OnCurveChanged( Curve value )
	{
		_applied = new CurveRange( value, value );
		UpdateNavigationBounds();
		Property?.SetValue( value );
	}

	void OnRangeChanged( CurveRange value )
	{
		_applied = value;
		UpdateNavigationBounds();
		Property?.SetValue( value );
	}

	public override void Rebuild()
	{
		_applied = ReadValue();
		if ( _editor is not { IsValid: true, IsDeleting: false } )
			return;
		if ( IsRange )
			_editor.RangeValue = _applied;
		else
			_editor.Value = _applied.A;
		UpdateNavigationBounds();
	}

	public override void Tick()
	{
		base.Tick();
		if ( Property is not null && !ReadValue().Equals( _applied ) )
			Rebuild();
	}

	void UpdatePreview( Rect rect )
	{
		if ( _previewValue is { } cached && cached.Equals( _applied ) && _previewRect == rect )
			return;
		_previewValue = _applied;
		_previewRect = rect;
		var first = _applied.A;
		var second = _applied.B;
		float minTime = Math.Min( first.TimeRange.x, second.TimeRange.x );
		float maxTime = Math.Max( first.TimeRange.y, second.TimeRange.y );
		float minValue = Math.Min( first.ValueRange.x, second.ValueRange.x );
		float maxValue = Math.Max( first.ValueRange.y, second.ValueRange.y );
		for ( int i = 0; i < _lower.Length; i++ )
		{
			float fraction = i / 64f;
			float time = minTime + (maxTime - minTime) * fraction;
			float lower = first.Evaluate( time );
			float upper = IsRange ? second.Evaluate( time ) : lower;
			_lower[i] = new( fraction, float.IsFinite( lower ) ? lower : minValue );
			_upper[i] = new( fraction, float.IsFinite( upper ) ? upper : minValue );
			minValue = Math.Min( minValue, Math.Min( _lower[i].y, _upper[i].y ) );
			maxValue = Math.Max( maxValue, Math.Max( _lower[i].y, _upper[i].y ) );
		}
		Vector2 Map( Vector2 point ) => rect.Position + new Vector2( point.x * rect.Width,
			(1 - (point.y - minValue) / Math.Max( 0.000001f, maxValue - minValue )) * rect.Height );
		for ( int i = 0; i < _lower.Length; i++ )
		{
			_area[i] = _lower[i] = Map( _lower[i] );
			_area[^(i + 1)] = _upper[i] = Map( _upper[i] );
		}
	}

	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		var rect = new Rect( new Vector2( 6, 5 ) * ScaleToScreen, Box.Rect.Size - new Vector2( 12, 10 ) * ScaleToScreen );
		if ( rect.Width <= 0 || rect.Height <= 0 || !painter.IsRectVisible( rect ) )
			return;
		UpdatePreview( rect );
		var color = ComputedStyle?.FontColor ?? Color.White;
		using var scope = painter.Scope();
		painter.Clip( rect );
		if ( IsRange )
			_areaFill.Draw( painter, _area, color.WithAlpha( 0.15f ) );
		_lowerLine.Draw( painter, _lower, color );
		if ( IsRange )
			_upperLine.Draw( painter, _upper, color );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		e.StopPropagation();
		OpenEditor();
	}
	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button is "enter" or "space" )
		{
			OpenEditor();
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}

	/// <summary>
	/// Open the full editor for this property. Edits update the property and preview immediately.
	/// </summary>
	public void OpenEditor()
	{
		if ( _popup is { IsValid: true, IsDeleting: false } )
			return;
		Rebuild();
		var editor = new CurveEditor();
		try
		{
			if ( IsRange ) editor.RangeValue = _applied;
			else editor.Value = _applied.A;
		}
		catch
		{
			editor.Delete( true );
			throw;
		}
		_popup = new EditorPopup();
		_popup.AddClass( "curve-editor-popup" );
		var tools = _popup.AddChild<Toolbar>();
		tools.AddChild<Label>().Text = IsRange ? "Curve range" : "Curve";
		tools.AddSpacer();
		tools.AddButton( "", "close", () => _popup?.Delete() ).Tooltip = "Close";
		_editor = _popup.AddChild( editor );
		_editor.AcceptsFocus = true;
		if ( IsRange )
		{
			_editor.RangeValueChanged = OnRangeChanged;
		}
		else
		{
			_editor.ValueChanged = OnCurveChanged;
		}
		UpdateNavigationBounds();
		var bounds = _editor.NavigationBounds.Value;
		_editor.SetViewBounds( bounds.Position, bounds.Position + bounds.Size );
		_popup.CloseWhenParentIsHidden = true;
		_popup.SetPositioning( this, Popup.PositionMode.BelowLeft, 4 );
		_editor.Focus();
	}

	public override void OnDeleted()
	{
		_popup?.Delete( true );
		base.OnDeleted();
	}

	sealed class EditorPopup : Popup
	{
		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button == "escape" )
			{
				Delete();
				e.StopPropagation = true;
				return;
			}
			base.OnButtonTyped( e );
		}
	}
}
