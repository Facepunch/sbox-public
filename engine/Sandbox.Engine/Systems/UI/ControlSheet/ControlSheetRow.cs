using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// Used by ControlSheet to display a single row of a property. This is created from a SerializedProperty
/// and contains a label and a control for editing the property. Controls are created using BaseControl.CreateFor.
/// </summary>
[StyleSheet.Inline( "controlsheetrow", Styles )]
public class ControlSheetRow : Panel
{
	const string Styles = """

		ControlSheetRow
		{
			flex-direction: row;
			flex-shrink: 0;
			max-height: 32px;
			border-radius: 4px;

			&.hidden
			{
				display: none;
			}
		}

		ControlSheetRow > .left
		{
			width: 150px;
			flex-shrink: 0;
			flex-grow: 0;
			padding: 6px;
		}

		ControlSheetRow > .right
		{
			flex-grow: 1;
		}

		ControlSheetRow > .left > .title
		{
			width: 150px;
			flex-shrink: 0;
			flex-grow: 0;
			font-size: 13px;
			white-space: nowrap;
			overflow: hidden;
		}

		ControlSheetRow > .right > textentry,
		ControlSheetRow > .right numberentry
		{
			background-color: #000a;
			flex-grow: 1;
			padding: 4px 8px;
			border-radius: 2px;
		}
		""";

	[Parameter]
	public SerializedProperty Property { get; set; }

	Panel _left;
	Label _title;
	Panel _right;

	InspectorVisibilityAttribute[] _visibilityAttributes;

	public ControlSheetRow()
	{
		_left = AddChild<Panel>( "left" );
		_title = _left.AddChild<Label>( "title" );

		_right = AddChild<Panel>( "right" );
	}

	internal void Initialize( SerializedProperty prop )
	{
		Property = prop;

		_visibilityAttributes = Property.GetAttributes<InspectorVisibilityAttribute>()?.ToArray();
	}

	protected override void OnParametersSet()
	{
		if ( Property is null )
			return;

		_title.Text = Property?.DisplayName;

		_right.DeleteChildren();

		var c = BaseControl.CreateFor( Property );
		if ( c is null ) return;
		_right.AddChild( c );
	}

	public override void Tick()
	{
		base.Tick();

		if ( _visibilityAttributes?.Length == 0 ) return;
		if ( Property.Parent is null ) return;

		bool hidden = _visibilityAttributes.All( x => x.TestCondition( Property.Parent ) );
		SetClass( "hidden", hidden );
	}
}
