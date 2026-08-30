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
	const string Styles = """

		.switchcontrol
		{
		    flex-direction: row;
		    width: 100px;
		    min-height: 24px;
		    align-items: center;
		    cursor: pointer;

		    .switch-frame
		    {
		        flex-grow: 0;
		        flex-shrink: 1;
		        width: 48px;
		        height: 16px;
		        background-color: #fff1;
		        margin: 0px 5px;
		        align-items: center;
		        border-radius: 100px;
		        transition: all 0.4s linear;

		        .switch-inner
		        {
		            position: relative;
		            flex-grow: 0;
		            flex-shrink: 1;
		            background-color: #999;
		            width: 25px;
		            height: 25px;
		            border-radius: 100px;
		            left: 20%;
		            transform: translateX( -50% );
		            transition: all 0.3s ease-out;
		        }
		    }

		    &.active
		    {
		        .switch-frame
		        {
		            background-color: #fffa;
		        }

		        .switch-inner
		        {
		            left: 80%;
		            background-color: #fff;
		        }
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
