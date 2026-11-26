using Sandbox.Engine;
using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.UI;

/// <summary>
/// A control for editing enum properties. Can either display a dropdown or a button group depending on the number of options.
/// </summary>
[CustomEditor( typeof( Enum ) )]
[StyleSheet.Inline( "enumcontrol", Styles )]
public partial class EnumControl : BaseControl
{
	const string Styles = """
		EnumControl
		{
			gap: 2px;
			flex-grow: 1;
		}

		EnumControl DropDown,
		EnumControl ButtonGroup
		{
			border-radius: 8px;
			background-color: #000a;
			flex-grow: 1;
		}

		EnumControl DropDown
		{
			flex-grow: 1;
			min-height: 32px;
		}

		EnumControl ButtonGroup
		{
			border-radius: 12px;
			overflow: hidden;
			min-height: 32px;

			Button
			{
				flex-grow: 1;
				justify-content: center;
				align-items: center;
				gap: 4px;
				color: #aaa;
				font-size: 1rem;
				cursor: pointer;

				.icon
				{
					color: #08f;
				}

				&:hover
				{
					color: #ddd;

					.icon
					{
						color: #3af;
					}
				}

				&:active
				{
					background-color: #04a;
					color: white;
					transform: translateX( 1px ) translateY( 1px );

					.icon
					{
						color: #fff;
					}
				}

				&.active
				{
					background-color: #08f;
					color: white;
					pointer-events: none;

					.icon
					{
						color: #fff;
					}
				}
			}
		}
		""";

	public override bool SupportsMultiEdit => true;

	ButtonGroup _buttons;
	DropDown _dropdown;

	/// <summary>
	/// Show buttons for exactly this many options. By default, enums with up to four options use buttons.
	/// </summary>
	[Parameter]
	public int? ButtonGroupOptionCount
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			Rebuild();
		}
	}

	public override void Rebuild()
	{
		DeleteChildren( true );
		_buttons = null;
		_dropdown = null;

		if ( Property?.PropertyType?.IsEnum != true ) return;

		var options = GlobalContext.Current.TypeLibrary.GetEnumDescription( Property.PropertyType );
		if ( options == null )
		{
			Log.Warning( $"Couldn't get enum description for {Property.PropertyType}" );
			return;
		}

		var count = options.Count();
		bool useButtonGroup = ButtonGroupOptionCount is int exact ? count == exact : count <= 4;

		if ( useButtonGroup )
		{
			CreateButtonGroup( options );
		}
		else
		{
			CreateDropdown( options );
		}
	}

	void CreateDropdown( EnumDescription options )
	{
		_dropdown = AddChild<DropDown>();

		foreach ( var o in options )
		{
			_dropdown.Options.Add( new Option( o.Title, o.Icon, o.ObjectValue ) );
		}

		_dropdown.Value = Property.GetValue<object>();
		_dropdown.ValueChanged = _ => Property.SetValue( _dropdown.Selected.Value );
	}

	void CreateButtonGroup( EnumDescription options )
	{
		_buttons = AddChild<ButtonGroup>();

		foreach ( var o in options )
		{
			var button = _buttons.AddChild<Button>();
			button.Text = o.Title;
			button.Icon = o.Icon;
			button.Value = o.ObjectValue;
		}

		SyncButtons( Property.GetValue<object>() );
		_buttons.ValueChanged = value =>
		{
			if ( !Equals( Property.GetValue<object>(), value ) )
				Property.SetValue( value );
		};
	}

	void SyncButtons( object value )
	{
		_buttons.SelectedButton = _buttons.ChildrenOfType<Button>().FirstOrDefault( button => Equals( button.Value, value ) );
		if ( !Equals( _buttons.Value, value ) )
			_buttons.Value = value;
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		var value = Property.GetValue<object>();
		if ( _buttons is not null && !Equals( _buttons.Value, value ) )
			SyncButtons( value );

		if ( _dropdown is not null && !Equals( _dropdown.Value, value ) )
		{
			_dropdown.Value = value;
			if ( _dropdown.Selected is null )
			{
				_dropdown.Text = null;
				_dropdown.Icon = null;
			}
		}
	}
}
