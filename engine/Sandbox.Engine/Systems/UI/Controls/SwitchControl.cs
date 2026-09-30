using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A boolean control drawn as an on/off switch.
/// </summary>
[StyleSheet.Inline( "switchcontrol", Styles )]
[CustomEditor( typeof( bool ) )]
public class SwitchControl : BaseControl
{
	// A track with a knob that slides across it, and the label beside it. Colours come from
	// the stylesheet - these are the neutral defaults for when nothing themes it.
	const string Styles = """

		.switchcontrol
		{
		    flex-direction: row;
		    align-items: center;
		    flex-shrink: 0;
		    cursor: pointer;

		    .switch-frame
		    {
		        flex-shrink: 0;
		        width: 32px;
		        height: 18px;
		        padding: 2px;
		        flex-direction: row;
		        align-items: center;
		        border-radius: 9px;
		        background-color: #ffffff20;
		        transition: background-color 0.15s ease-out;

		        .switch-inner
		        {
		            width: 14px;
		            height: 14px;
		            border-radius: 7px;
		            background-color: #8a8f98;
		            transition: margin-left 0.15s ease-out, background-color 0.15s ease-out;
		        }
		    }

		    .switch-label
		    {
		        margin-left: 9px;
		        font-size: 12px;
		    }

		    &.active
		    {
		        .switch-frame { background-color: #3273eb; }
		        .switch-inner { margin-left: 14px; background-color: #ffffff; }
		    }
		}
		""";

	public override bool SupportsMultiEdit => true;

	/// <summary>
	/// Called when the switch is toggled.
	/// </summary>
	public Action<bool> OnValueChanged { get; set; }

	Label labelPanel;

	/// <summary>
	/// Optional text shown next to the switch.
	/// </summary>
	public string Label
	{
		get => labelPanel?.Text;
		set
		{
			if ( string.IsNullOrEmpty( value ) )
			{
				labelPanel?.Delete( true );
				labelPanel = null;
				return;
			}

			labelPanel ??= Add.Label( "", "switch-label" );
			labelPanel.Text = value;
		}
	}

	bool _value;

	public bool Value
	{
		get => Property?.As.Bool ?? _value;

		set
		{
			if ( Property is not null )
			{
				Property.As.Bool = value;
				UpdateState();
				return;
			}

			if ( _value == value )
				return;

			_value = value;
			UpdateState();
		}
	}

	public SwitchControl()
	{
		AddClass( "switchcontrol" );

		var frame = Add.Panel( "switch-frame" );
		frame.Add.Panel( "switch-inner" );

		UpdateState();
	}

	public override void Tick()
	{
		base.Tick();

		// the bound property can change underneath us
		UpdateState();
	}

	void UpdateState()
	{
		var value = Value;
		SetClass( "active", value );
		SetClass( "inactive", !value );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Value = !Value;
		OnValueChanged?.Invoke( Value );
		e.StopPropagation();
	}
}
