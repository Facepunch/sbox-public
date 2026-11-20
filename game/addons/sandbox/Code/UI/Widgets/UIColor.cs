using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// Property for a color swatch and hex value, clicking on it open a color picker
/// </summary>
public class UIColor : BaseControl
{
	readonly Panel _swatch;
	readonly Panel _fill;
	readonly HexEntry _hex;
	Color _applied;

	public override bool SupportsMultiEdit => true;

	public UIColor()
	{
		_swatch = Add.Panel( "swatch" );
		_fill = _swatch.Add.Panel( "fill" );
		_swatch.AddEventListener( "onmousedown", OpenPicker );

		_hex = AddChild( new HexEntry() );
		_hex.ColorEntered = color =>
		{
			_applied = color;
			Property?.SetValue( color );
			ShowSwatch( color );
		};
		_hex.Blurred = () => ShowText( _applied );
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		_applied = Property.GetValue<Color>();
		ShowSwatch( _applied );
		ShowText( _applied );
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		var current = Property.GetValue<Color>();
		if ( current == _applied ) return;

		_applied = current;
		ShowSwatch( current );
		if ( !_hex.HasFocus ) ShowText( current );
	}

	void ShowSwatch( Color color )
	{
		_fill.Style.BackgroundColor = color;
	}

	void ShowText( Color color )
	{
		_hex.Text = color.Hex.ToUpperInvariant();
	}

	void OpenPicker()
	{
		_hex.Blur();
		_ = new UIColorPopup( _swatch, Property );
	}

	class HexEntry : TextEntry
	{
		public Action<Color> ColorEntered { get; set; }
		public Action Blurred { get; set; }

		public override void OnValueChanged()
		{
			base.OnValueChanged();

			if ( Color.TryParse( Text, out var color ) )
			{
				SetClass( "invalid", false );
				ColorEntered?.Invoke( color );
			}
			else
			{
				SetClass( "invalid", true );
			}
		}

		protected override void OnBlur( PanelEvent e )
		{
			base.OnBlur( e );

			SetClass( "invalid", false );
			Blurred?.Invoke();
		}
	}
}
