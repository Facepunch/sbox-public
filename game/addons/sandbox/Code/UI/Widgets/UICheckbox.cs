using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

public class UICheckbox : BaseControl
{
	readonly IconPanel _tick;
	bool _value;

	public override bool SupportsMultiEdit => true;

	[Parameter] public Action<bool> OnValueChanged { get; set; }

	public UICheckbox()
	{
		_tick = Add.Icon( "check", "tick" );
	}

	[Parameter]
	public bool Value
	{
		get => Property?.As.Bool ?? _value;
		set
		{
			if ( Property is not null )
			{
				Property.As.Bool = value;
				return;
			}

			_value = value;
		}
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "checked", Value );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Value = !Value;
		OnValueChanged?.Invoke( Value );
		e.StopPropagation();
	}
}
