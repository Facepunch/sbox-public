using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// An enum property. Two options are shown side by side as a segmented control, anything more as a <see cref="UIDropdown"/>.
/// </summary>
public class UIEnumControl : BaseControl
{
	UIDropdown _dropdown;
	readonly List<(Panel panel, object value)> _segments = new();

	public override bool SupportsMultiEdit => true;

	public override void Rebuild()
	{
		DeleteChildren( true );
		_segments.Clear();
		_dropdown = null;

		if ( Property is null || !Property.PropertyType.IsEnum ) return;

		var options = TypeLibrary.GetEnumDescription( Property.PropertyType );
		if ( options is null ) return;

		var list = options.Select( o => new UIDropdown.Item( o.Title, o.ObjectValue ) ).ToList();

		SetClass( "segmented", list.Count == 2 );

		if ( list.Count == 2 )
		{
			foreach ( var item in list )
			{
				var segment = Add.Panel( "segment" );
				segment.Add.Label( item.Label, "label" );

				var captured = item;
				segment.AddEventListener( "onclick", () => Property.SetValue( captured.Value ) );

				_segments.Add( (segment, item.Value) );
			}

			return;
		}

		_dropdown = AddChild( new UIDropdown() );
		_dropdown.Items = list;
		_dropdown.Value = Property.GetValue<object>();
		_dropdown.ValueChanged = value => Property.SetValue( value );
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		var current = Property.GetValue<object>();

		if ( _dropdown.IsValid() )
		{
			_dropdown.Value = current;
			return;
		}

		foreach ( var (panel, value) in _segments )
			panel.SetClass( "active", Equals( value, current ) );
	}
}
