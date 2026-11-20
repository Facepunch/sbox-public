using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// A number with a range
/// </summary>
public class UISlider : BaseControl
{
	const float ThumbWidth = 10f;
	const int MaxStepTicks = 24;
	const int DefaultDivisions = 10;
	static readonly float[] NiceMultiples = { 1f, 2f, 5f };

	readonly Panel _entryHolder;
	readonly NumberEntry _entry;
	readonly Panel _track;
	readonly Panel _thumb;
	readonly List<Panel> _ticks = new();

	float _min = 0f;
	float _max = 1f;
	float _step;
	float _rendered = float.NaN;

	public override bool SupportsMultiEdit => true;

	public UISlider()
	{
		_track = Add.Panel( "track" );
		_track.Add.Panel( "line" );
		_thumb = _track.Add.Panel( "thumb" );

		_entryHolder = Add.Panel( "entry" );
		_entry = _entryHolder.AddChild<NumberEntry>();
		_entry.NumberFormat = "0.##";
		_entry.OnTextEdited = OnTextEdited;
	}

	/// <summary>
	/// The left end of the bar. Set from the property's [Range] when there is one.
	/// </summary>
	[Parameter]
	public float Min
	{
		get => _min;
		set { _min = value; Refresh(); }
	}

	/// <summary>
	/// The right end of the bar. Set from the property's [Range] when there is one.
	/// </summary>
	[Parameter]
	public float Max
	{
		get => _max;
		set { _max = value; Refresh(); }
	}

	/// <summary>
	/// Values snap to this, and it's marked under the bar - coarsened to a round multiple when there
	/// would be too many marks. 0 for none, which marks tenths of the range instead.
	/// </summary>
	[Parameter]
	public float Step
	{
		get => _step;
		set { _step = value; Refresh(); }
	}

	/// <summary>
	/// Called with the new value when the user changes it.
	/// </summary>
	[Parameter] public Action<float> OnValueChanged { get; set; }

	float _value;

	bool _applyOnRelease;
	bool _pending;

	/// <summary>
	/// The number the slider shows. With a property it reads and writes that, without one it
	/// keeps the number itself - so the slider can be used anywhere
	/// </summary>
	[Parameter]
	public float Value
	{
		get => Property is null || _pending ? _value : Convert.ToSingle( Property.GetValue<object>( 0f ) );
		set
		{
			value = value.Clamp( _min, _max );
			if ( _step > 0f ) value = value.SnapToGrid( _step ).Clamp( _min, _max );

			if ( Property is null || IsHolding )
			{
				_value = value;
				_pending = Property is not null;
			}
			else
			{
				WriteProperty( value );
			}

			UpdateVisuals();
		}
	}

	bool IsHolding => _applyOnRelease && (_dragging && HasActive || _entry.HasFocus);

	void WriteProperty( float value )
	{
		var type = Property.PropertyType;
		if ( type == typeof( int ) ) Property.SetValue( (int)MathF.Round( value ) );
		else if ( type == typeof( double ) ) Property.SetValue( (double)value );
		else Property.SetValue( value );
	}

	void ApplyPending()
	{
		if ( !_pending ) return;

		_pending = false;
		WriteProperty( _value );
	}

	void Refresh()
	{
		// the setters also run from the constructor's object initializer, before the panels exist
		if ( _thumb is null ) return;

		RebuildTicks();
		UpdateVisuals();
	}

	/// <summary>
	/// Sets the value from the user's input, so <see cref="OnValueChanged"/> hears of it.
	/// </summary>
	void SetFromUser( float value )
	{
		Value = value;
		OnValueChanged?.Invoke( Value );
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		if ( Property.TryGetAttribute<RangeAttribute>( out var range ) )
		{
			_min = range.Min; // fields, the refresh comes once at the end
			_max = range.Max;
		}

		_step = Property.TryGetAttribute<StepAttribute>( out var step ) ? step.Step : 0f;
		_applyOnRelease = Property.HasAttribute<ApplyOnReleaseAttribute>();

		_entry.WholeNumbers = Property.PropertyType == typeof( int ) || Property.PropertyType == typeof( long );

		UpdateTrackGradient();
		RebuildTicks();
		UpdateVisuals();
	}

	static string Format( float value ) => value.ToString( "0.##" );

	public override void OnHotloaded()
	{
		base.OnHotloaded();
		Rebuild();
	}

	/// <summary>Honor the same gradient metadata used by the editor's numeric sliders.</summary>
	void UpdateTrackGradient()
	{
		var line = _track.Children.First( panel => panel.HasClass( "line" ) );
		var gradient = Property.TryGetAttribute<SliderGradientAttribute>( out var attribute )
			? attribute.CreateGradient() : new Gradient();
		bool hasGradient = gradient.Colors.Count > 0;
		SetClass( "gradient", hasGradient );
		if ( !hasGradient )
		{
			line.Style.Set( "background-image: none;" );
			return;
		}

		var stops = gradient.Colors.Select( frame =>
			$"{frame.Value.Hex} {(frame.Time * 100f).ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture )}%" );
		line.Style.Set( $"background-image: linear-gradient(to right, {string.Join( ", ", stops )});" );
	}

	void RebuildTicks()
	{
		foreach ( var tick in _ticks ) tick.Delete( true );
		_ticks.Clear();

		if ( _max <= _min ) return;

		var positions = new List<float> { 0f, 1f };
		var range = _max - _min;
		var interval = TickInterval( range );

		// marks sit on the same absolute grid values snap to, not offsets from Min
		var first = (long)MathF.Ceiling( _min / interval );
		var last = (long)MathF.Floor( _max / interval );
		var epsilon = interval * 0.001f;

		for ( var i = first; i <= last; i++ )
		{
			var value = i * interval;
			if ( value - _min < epsilon || _max - value < epsilon ) continue; // the ends are already marked

			positions.Add( (value - _min) / range );
		}

		foreach ( var position in positions )
		{
			var tick = _track.Add.Panel( "tick" );
			tick.Style.Left = Length.Percent( position * 100f );
			tick.Style.MarginLeft = -position; // the 1px tick stays inside the bar at the end
			_ticks.Add( tick );
		}
	}

	/// <summary>
	/// The step, or a tenth of the range without one, grown by 2/5/10-style multiples until it
	/// marks at most <see cref="MaxStepTicks"/> divisions.
	/// </summary>
	float TickInterval( float range )
	{
		var baseInterval = _step > 0f ? _step : range / DefaultDivisions;
		var interval = baseInterval;

		for ( var decade = 1f; range / interval > MaxStepTicks; decade *= 10f )
		{
			foreach ( var multiple in NiceMultiples )
			{
				interval = baseInterval * multiple * decade;
				if ( range / interval <= MaxStepTicks ) break;
			}
		}

		return interval;
	}

	void UpdateVisuals()
	{
		var value = Value;
		_rendered = value;

		var position = _max > _min ? MathX.LerpInverse( value, _min, _max, true ) : 0f;

		_thumb.Style.Left = Length.Percent( position * 100f );
		_thumb.Style.MarginLeft = -ThumbWidth * 0.5f;

		if ( !_entry.HasFocus )
			_entry.Text = Format( value );
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "dragging", _dragging && HasActive );

		if ( !IsHolding ) ApplyPending();

		if ( Value != _rendered )
			UpdateVisuals();
	}

	void OnTextEdited( string text )
	{
		SetFromUser( text.ToFloat( Value ) );
	}

	float ScreenPosToValue()
	{
		var normalized = MathX.LerpInverse( _track.MousePosition.x, 0f, _track.Box.Rect.Width, true );
		return MathX.LerpTo( _min, _max, normalized, true );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		// only the track drags, the box takes typing
		_dragging = IsOverTrack( e );
		if ( !_dragging ) return;

		SetFromUser( ScreenPosToValue() );
		_entry.Blur();
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !HasActive || e.MouseButton == MouseButtons.Middle ) return;
		if ( !_dragging ) return;

		SetFromUser( ScreenPosToValue() );
		e.StopPropagation();
	}

	bool _dragging;

	public override void OnDeleted()
	{
		base.OnDeleted();
		ApplyPending();
	}

	bool IsOverTrack( MousePanelEvent e ) => e.Target == _track || e.Target?.Parent == _track;
}
