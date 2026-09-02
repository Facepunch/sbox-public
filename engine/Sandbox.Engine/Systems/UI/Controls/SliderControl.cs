using Microsoft.AspNetCore.Components;
using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A float control drawn as a draggable slider, optionally with a text entry.
/// </summary>
[StyleSheet.Inline( "slidercontrol", Styles )]
[CustomEditor( typeof( float ), WithAllAttributes = [typeof( RangeAttribute )] )]
public class SliderControl : BaseControl
{
	const string Styles = """

		.slidercontrol
		{
		    flex-direction: row;
		    min-width: 50px;
		    position: relative;
		    flex-shrink: 0;
		    flex-direction: row;
		    cursor: pointer;
		    gap: 8px;
		    flex-grow: 1;
		    align-items: center;
		    pointer-events: all;

		    > .inner
		    {
		        flex-direction: column;
		        flex-shrink: 1;
		        flex-grow: 1;
		        min-height: 32px;
		        justify-content: center;

		        > .values
		        {
		            width: 100%;
		            pointer-events: none;
		            font-size: 14px;
		            color: #aaa;

		            > .left
		            {
		                flex-grow: 1;
		            }
		        }

		        > .track
		        {
		            position: relative;
		            background-color: #888;
		            height: 7px;
		            margin: 8px;
		            align-items: center;
		            border-radius: 4px;

		            > .track-active
		            {
		                background-color: #fff;
		                position: absolute;
		                height: 100%;
		                left: 0px;
		                border-radius: 4px;
		            }

		            > .thumb
		            {
		                position: relative;
		                background-color: #fff;
		                border-radius: 100px;
		                width: 16px;
		                height: 16px;
		                transform: translateX( -50% );
		            }
		        }
		    }

		    > .entry
		    {
		        flex-shrink: 0;
		        flex-grow: 0;
		        width: 50px;

		        > numberentry
		        {
		            background-color: transparent;

		            > .content-label
		            {
		                padding: 0 4px;
		            }
		        }
		    }
		}


		.slidercontrol .value-tooltip
		{
		    position: absolute;
		    bottom: 150%;
		    left: -8px;
		    z-index: 1000;
		    flex-direction: column;

		    > .label
		    {
		        background-color: black;
		        padding: 8px 12px;
		        border-radius: 8px;
		    }

		    >.tail
		    {
		        bottom: -0px;
		        background-color: black;
		        width: 10px;
		        height: 10px;
		        transform: rotateZ(45 deg) translateX( 4px );
		        position: absolute;
		    }
		}
		""";

	public override bool SupportsMultiEdit => true;

	[Parameter] public Action<float> OnValueChanged { get; set; }

	float _min = 0;
	float _max = 100;

	/// <summary>
	/// The right side of the slider.
	/// </summary>
	[Parameter]
	public float Max
	{
		get => _max;
		set { _max = value; UpdateVisuals(); }
	}

	/// <summary>
	/// The left side of the slider.
	/// </summary>
	[Parameter]
	public float Min
	{
		get => _min;
		set { _min = value; UpdateVisuals(); }
	}

	/// <summary>
	/// If set to 1, value will be rounded to 1's
	/// If set to 10, value will be rounded to 10's
	/// If set to 0.1, value will be rounded to 0.1's
	/// </summary>
	[Parameter] public float Step { get; set; } = 0.001f;

	bool _showRange;

	/// <summary>
	/// Show the range values above the slider
	/// </summary>
	[Parameter]
	public bool ShowRange
	{
		get => _showRange;
		set { _showRange = value; valuesPanel.Style.Display = value ? DisplayMode.Flex : DisplayMode.None; }
	}

	/// <summary>
	/// When changing the value show the tooltip
	/// </summary>
	[Parameter] public bool ShowValueTooltip { get; set; } = true;

	bool _showTextEntry;

	/// <summary>
	/// Show a text entry next to the slider
	/// </summary>
	[Parameter]
	public bool ShowTextEntry
	{
		get => _showTextEntry;
		set { _showTextEntry = value; entryPanel.Style.Display = value ? DisplayMode.Flex : DisplayMode.None; }
	}

	string _numberFormat = "0.###";

	/// <summary>
	/// How to display numbers in this control
	/// </summary>
	[Parameter]
	public string NumberFormat
	{
		get => _numberFormat;
		set { _numberFormat = value; TextEntryPanel.NumberFormat = value; UpdateVisuals(); }
	}

	float _value;

	[Parameter]
	public float Value
	{
		get => Property?.As.Float ?? _value;

		set
		{
			if ( Property is not null )
			{
				Property.As.Float = value;
				UpdateVisuals();
				return;
			}

			if ( _value == value )
				return;

			_value = value;
			UpdateVisuals();
		}
	}

	Panel entryPanel;
	Panel valuesPanel;
	Panel trackActivePanel;
	Panel tooltipPanel;
	Label tooltipLabel;
	Label minLabel;
	Label maxLabel;

	Panel TrackPanel { get; set; }
	Panel ThumbPanel { get; set; }
	TextEntry TextEntryPanel { get; set; }

	public SliderControl()
	{
		AddClass( "slidercontrol" );

		entryPanel = Add.Panel( "entry" );
		entryPanel.Style.Display = DisplayMode.None;

		var numberEntry = entryPanel.AddChild<NumberEntry>();
		numberEntry.NumberFormat = NumberFormat;
		numberEntry.OnTextEdited = OnTextEntryEdited;
		TextEntryPanel = numberEntry;

		var inner = Add.Panel( "inner" );

		valuesPanel = inner.Add.Panel( "values" );
		valuesPanel.Style.Display = DisplayMode.None;
		minLabel = valuesPanel.Add.Label( "", "left" );
		maxLabel = valuesPanel.Add.Label( "", "right" );

		TrackPanel = inner.Add.Panel( "track" );
		trackActivePanel = TrackPanel.Add.Panel( "track-active" );
		ThumbPanel = TrackPanel.Add.Panel( "thumb" );

		UpdateVisuals();
	}

	public SliderControl( float min, float max, float step = 1.0f ) : this()
	{
		Min = min;
		Max = max;
		Step = step;
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		ShowTextEntry = true;

		if ( Property.TryGetAttribute<RangeAttribute>( out var rangeAttribute ) )
		{
			Min = rangeAttribute.Min;
			Max = rangeAttribute.Max;
		}

		if ( Property.TryGetAttribute<StepAttribute>( out var stepAttribute ) )
		{
			Step = stepAttribute.Step;
		}
	}

	float renderedValue = float.NaN;

	public override void Tick()
	{
		base.Tick();

		// the bound property can change underneath us
		if ( Value != renderedValue )
		{
			UpdateVisuals();
		}

		UpdateTooltip();
	}

	void UpdateVisuals()
	{
		// can be called from a property setter during construction
		if ( ThumbPanel is null ) return;

		var value = Value;
		renderedValue = value;

		var position = MathX.LerpInverse( value, Min, Max, true ) * 100.0f;
		trackActivePanel.Style.Width = Length.Percent( position );
		ThumbPanel.Style.Left = Length.Percent( position );

		minLabel.Text = Min.ToString( NumberFormat );
		maxLabel.Text = Max.ToString( NumberFormat );

		if ( !TextEntryPanel.HasFocus )
		{
			TextEntryPanel.Text = value.ToString( NumberFormat );
		}
	}

	/// <summary>
	/// The tooltip only exists while we're being dragged.
	/// </summary>
	void UpdateTooltip()
	{
		var show = HasActive && ShowValueTooltip;

		if ( show && tooltipPanel is null )
		{
			tooltipPanel = ThumbPanel.Add.Panel( "value-tooltip" );
			tooltipLabel = tooltipPanel.Add.Label( "" );
			tooltipPanel.Add.Panel( "tail" );
		}
		else if ( !show && tooltipPanel is not null )
		{
			tooltipPanel.Delete( true );
			tooltipPanel = null;
			tooltipLabel = null;
		}

		if ( tooltipLabel is not null )
		{
			tooltipLabel.Text = Value.ToString( NumberFormat );
		}
	}

	void OnTextEntryEdited( string text )
	{
		Value = text.ToFloat( Value );
		OnValueChanged?.Invoke( Value );
	}

	/// <summary>
	/// Convert a screen position to a value. The value is clamped, but not snapped.
	/// </summary>
	public virtual float ScreenPosToValue( Vector2 pos )
	{
		var normalized = MathX.LerpInverse( pos.x, TrackPanel.Box.Left, TrackPanel.Box.Right, true );
		var scaled = MathX.LerpTo( Min, Max, normalized, true );
		return Step > 0 ? scaled.SnapToGrid( Step ) : scaled;
	}

	/// <summary>
	/// If we move the mouse while we're being pressed then set the value
	/// </summary>
	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !HasActive || e.MouseButton == MouseButtons.Middle ) return;

		Value = ScreenPosToValue( ScreenMousePosition );
		OnValueChanged?.Invoke( Value );
		e.StopPropagation();
	}

	/// <summary>
	/// On mouse press jump to that position
	/// </summary>
	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Value = ScreenPosToValue( ScreenMousePosition );
		OnValueChanged?.Invoke( Value );
		e.StopPropagation();

		TextEntryPanel?.Blur();
	}

	protected override void OnMiddleClick( MousePanelEvent e )
	{
		base.OnMiddleClick( e );
		e.StopPropagation();
	}
}
