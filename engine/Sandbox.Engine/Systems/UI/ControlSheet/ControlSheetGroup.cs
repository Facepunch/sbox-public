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
			flex-shrink: 0;
		}

		.controlgroup > .body
		{
			flex-direction: column;
			flex-shrink: 0;
			gap: 2px;

			&.hidden
			{
				display: none;
			}
		}

		// Like the editor's: an open group is set apart by space under it, not an indented bracket
		.controlgroup.titled > .body
		{
			margin: 4px 0px 12px 0px;
		}

		// A group inside another - a class or struct's own groups - steps in with a line down its
		// side, and a smaller title, so it reads as part of the group above
		.controlgroup .controlgroup.titled
		{
			margin-left: 6px;
			padding: 0px 0px 0px 10px;
			border-left: 1px solid #ffffff1a;
		}

		.controlgroup .controlgroup.titled > .header
		{
			font-size: 12px;
			min-height: 26px;
		}

		.controlgroup .controlgroup.titled > .body
		{
			margin-bottom: 6px;
		}
		""";

	public ControlSheetGroupHeader Header { get; set; }
	public Panel ToggleContainer { get; set; }
	public Panel Body { get; set; }
	public bool Closed { get; internal set; }

	// Fold state per group title for the session, so a rebuilt sheet keeps it
	static readonly Dictionary<string, bool> _folded = new();
	string _foldKey;


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

	/// <summary>
	/// Let the header fold this group. Starts folded if <paramref name="startFolded"/>,
	/// unless it was folded or opened before.
	/// </summary>
	internal void SetFoldable( string name, bool startFolded )
	{
		_foldKey = name;
		Closed = _folded.GetValueOrDefault( name, startFolded );
		Header.Foldable = true;
		Header.OnFold = Toggle;
	}

	/// <summary>
	/// Fold or unfold the group.
	/// </summary>
	public void Toggle()
	{
		Closed = !Closed;
		if ( _foldKey is not null ) _folded[_foldKey] = Closed;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Header.ToggleProperty != null )
		{
			Body.SetClass( "hidden", Header.ToggleProperty.As.Bool == false );
		}
		else if ( Header.Foldable )
		{
			Body.SetClass( "hidden", Closed );
			Header.Folded = Closed;
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
