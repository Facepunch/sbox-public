using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// One axis of a colour as a strip with a handle on it. Runs left to right, or bottom to top when
/// it's vertical. Whoever owns it paints the gradient behind the handle.
/// </summary>
[StyleSheet.Inline( "colorstrip", Styles )]
internal class ColorStrip : ColorDragPanel
{
	const string Styles = ColorPickerStyles.Handle + """
		.colorstrip
		{
			position: relative;
			height: 14px;
			flex-grow: 1;
			flex-shrink: 0;
			border-radius: 7px;
			cursor: pointer;
			pointer-events: all;

			> .fill
			{
				position: absolute;
				left: 0;
				top: 0;
				right: 0;
				bottom: 0;
				border-radius: 7px;
			}

			&.vertical
			{
				width: 14px;
				height: auto;
				flex-grow: 0;
				align-self: stretch;
			}
		}
		""";

	readonly Panel _fill;
	readonly Panel _handle;
	float _value;

	/// <summary>
	/// Bottom to top rather than left to right.
	/// </summary>
	public bool Vertical { get; }

	/// <summary>
	/// Called as the handle is dragged.
	/// </summary>
	public Action<float> ValueChanged { get; set; }

	/// <summary>
	/// Where the handle is, 0 to 1.
	/// </summary>
	public float Value
	{
		get => _value;
		set
		{
			_value = Math.Clamp( value, 0.0f, 1.0f );

			if ( Vertical )
			{
				_handle.Style.Left = Length.Percent( 50 );
				_handle.Style.Top = Length.Percent( (1.0f - _value) * 100.0f );
			}
			else
			{
				_handle.Style.Left = Length.Percent( _value * 100.0f );
				_handle.Style.Top = Length.Percent( 50 );
			}
		}
	}

	/// <param name="vertical">Bottom to top rather than left to right.</param>
	/// <param name="transparent">The gradient has see-through parts, so a checkerboard goes behind it.
	/// Opaque strips leave it off - the fill's anti-aliased ends would let it show as a faint ring.</param>
	public ColorStrip( bool vertical = false, bool transparent = false )
	{
		Vertical = vertical;

		AddClass( "colorstrip" );
		SetClass( "vertical", vertical );

		if ( transparent )
		{
			Style.BackgroundImage = ColorPickerTextures.Checkerboard;
			Style.BackgroundSizeX = ColorPickerTextures.CheckerSize;
			Style.BackgroundSizeY = ColorPickerTextures.CheckerSize;
			Style.BackgroundRepeat = BackgroundRepeat.Repeat;
		}

		_fill = Add.Panel( "fill" );
		_handle = Add.Panel( "color-handle" );

		Value = 0;
	}

	/// <summary>
	/// Paint the strip as a run of colours, first at the start of the strip.
	/// </summary>
	public void SetGradient( params Color[] stops )
	{
		var direction = Vertical ? "to top" : "to right";
		_fill.Style.Set( "background-image", $"linear-gradient( {direction}, {string.Join( ", ", stops.Select( x => x.Rgba ) )} )" );
	}

	/// <summary>
	/// What the handle is filled with, normally the colour it's sitting on.
	/// </summary>
	public void SetHandleColor( Color color )
	{
		_handle.Style.BackgroundColor = color;
	}

	protected override void OnDrag( Vector2 fraction )
	{
		Value = Vertical ? 1.0f - fraction.y : fraction.x;
		ValueChanged?.Invoke( Value );
	}
}
