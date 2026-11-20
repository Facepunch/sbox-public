using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A string property marked [FontName], picked from <see cref="GameFonts"/>. Each one is shown in its own font.
/// </summary>
public class UIFontControl : BaseControl
{
	UIDropdown _dropdown;

	public override bool SupportsMultiEdit => true;

	public override void Rebuild()
	{
		DeleteChildren( true );

		if ( Property is null ) return;

		_dropdown = AddChild( new UIDropdown() );
		_dropdown.Tokenize = false;
		_dropdown.Items = GameFonts.Families.Select( x => new UIDropdown.Item( x, x, x ) ).ToList();
		_dropdown.Value = Property.GetValue<string>();
		_dropdown.ValueChanged = value => Property.SetValue( value );
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null || !_dropdown.IsValid() ) return;

		_dropdown.Value = Property.GetValue<string>();
	}
}
