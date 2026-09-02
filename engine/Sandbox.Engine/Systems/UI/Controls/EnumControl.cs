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

	public EnumControl()
	{

	}

	public override void Rebuild()
	{
		if ( Property == null ) return;
		if ( !Property.PropertyType.IsEnum ) return;

		var options = Sandbox.Internal.GlobalGameNamespace.TypeLibrary.GetEnumDescription( Property.PropertyType );
		if ( options == null )
		{
			Log.Warning( $"Couldn't get enum description for {Property.PropertyType}" );
			return;
		}

		bool useButtonGroup = options.Count() <= 4;

		// TODO - add ButtonGroupAttribute to override this?

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
		var dd = AddChild( new DropDown() );

		foreach ( var o in options )
		{
			dd.Options.Add( new Option( o.Title, o.Icon, o.ObjectValue ) );
		}

		// Show what the property is already set to, the same as the button group does
		dd.Value = Property.GetValue<object>();

		dd.ValueChanged = ( val ) => Property.SetValue( val );
	}

	void CreateButtonGroup( EnumDescription options )
	{
		var group = AddChild( new ButtonGroup() );
		group.Value = Property.GetValue<object>();

		foreach ( var o in options )
		{
			var button = group.AddChild( new Button() );
			button.Text = o.Title;
			button.Icon = o.Icon;
			button.Value = o.ObjectValue;
		}

		group.ValueChanged = ( val ) => Property.SetValue( group.Value );
	}
}
