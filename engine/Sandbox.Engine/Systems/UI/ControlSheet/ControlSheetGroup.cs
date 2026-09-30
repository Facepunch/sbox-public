using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// A group for ControlSheet, consists of a title and a body containing properties.
/// </summary>
[StyleSheet.Inline( "controlsheetgroup", Styles )]
public class ControlSheetGroup : Panel
{
	const string Styles = """
		.controlgroup
		{
			border-radius: 8px;
			padding: 0.5rem;
			flex-direction: column;
			flex-shrink: 0;

			&.hidden
			{
				display: none;
			}
		}

		.controlgroup > .header
		{
			font-weight: 550;
			color: #fff;
			flex-shrink: 0;
			padding: 8px 0;
			text-shadow: 1px 1px 1px #0004;
		}

		.controlgroup > .body
		{
			flex-direction: column;
			flex-shrink: 0;
			gap: 2px;
			padding-left: 16px;

			&.hidden
			{
				display: none;
			}
		}
		""";

	public ControlSheetGroupHeader Header { get; set; }
	public Panel ToggleContainer { get; set; }
	public Panel Body { get; set; }
	public bool Closed { get; internal set; }


	InspectorVisibilityAttribute[] _visibility;

	public ControlSheetGroup()
	{
		AddClass( "controlgroup" );

		Header = AddChild<ControlSheetGroupHeader>( "header" );
		Body = AddChild<Panel>( "body" );
	}

	/// <summary>
	/// Set the control that is going to toggle this group open and closed.
	/// </summary>
	public void SetToggle( SerializedProperty toggleGroup )
	{
		Header.ToggleProperty = toggleGroup;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Header.ToggleProperty != null )
		{
			Body.SetClass( "hidden", Header.ToggleProperty.As.Bool == false );
		}

		if ( _visibility?.Length > 0 )
		{
			SetClass( "hidden", _visibility.All( x => x.TestCondition( Header.ToggleProperty?.Parent ) ) );
		}
	}

	/// <summary>
	/// Hide this group if these attributes say so.
	/// </summary>
	public void SetVisibility( InspectorVisibilityAttribute[] inspectorVisibilityAttributes )
	{
		_visibility = null;

		if ( inspectorVisibilityAttributes?.Length == 0 ) return;

		_visibility = inspectorVisibilityAttributes;
	}
}
