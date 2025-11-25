using System;

namespace Sandbox.UI;

/// <summary>
/// A button that toggles a console variable between two given values.
/// </summary>
[Library( "ConvarToggleButton" )]
public class ConvarToggleButton : Button
{
	/// <summary>
	/// The console variable to modify using this button.
	/// </summary>
	public string ConVar { get; set; }

	/// <summary>
	/// The "On" value for the convar, when the button is pressed in.
	/// </summary>
	public string ValueOn { get; set; }

	/// <summary>
	/// The "Off" value for the convar.
	/// </summary>
	public string ValueOff { get; set; }

	public ConvarToggleButton()
	{
	}

	public ConvarToggleButton( Panel parent, string label, string convar, string onvalue, string offvalue, string icon = null )
	{
		this.Parent = parent;
		this.Icon = icon;
		this.Text = label;

		ConVar = convar;
		ValueOn = onvalue;
		ValueOff = offvalue;
	}

	public override void Tick()
	{
		base.Tick();

		if ( ConVar == null ) return;

		var val = ConsoleSystem.GetValue( ConVar );
		if ( val == null ) return;

		SetClass( "active", String.Equals( val, ValueOn, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Toggle the value of the <see cref="ConVar">ConVar</see> between <see cref="ValueOn">ValueOn</see> and <see cref="ValueOff">ValueOff</see>.<br/>
	/// If the convar's value is not either one of those, it will be set to <see cref="ValueOn">ValueOn</see>.
	/// </summary>
	public void Toggle()
	{
		if ( ConVar == null ) return;

		var val = ConsoleSystem.GetValue( ConVar );
		var status = String.Equals( val, ValueOn, StringComparison.OrdinalIgnoreCase );

		ConsoleSystem.Run( ConVar, status ? ValueOff : ValueOn );
	}

	public override void SetProperty( string name, string value )
	{
		base.SetProperty( name, value );

		if ( name == "on" ) ValueOn = value;
		if ( name == "off" ) ValueOff = value;
		if ( name == "convar" ) ConVar = value;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		Toggle();
	}
}
