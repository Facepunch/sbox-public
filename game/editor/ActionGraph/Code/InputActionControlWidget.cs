using Editor.NodeEditor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.ActionGraphs;

[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( InputActionAttribute ) } )]
internal class InputActionControlWidget : ControlWidget
{
	public override bool IsControlButton => true;
	public override bool IsControlHovered => base.IsControlHovered || _menu.IsValid();

	Menu _menu;

	public InputActionControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 2;
	}

	protected override void PaintControl()
	{
		var value = SerializedProperty.GetValue<string>();

		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		var rect = LocalRect;

		rect = rect.Shrink( 8, 0 );

		var inputName = value?.ToLower() ?? "None";
		Paint.SetPen( color );
		Paint.DrawText( rect, string.IsNullOrWhiteSpace( inputName ) ? "None" : inputName, TextFlag.LeftCenter );

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );
	}

	public override void StartEditing()
	{
		if ( !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	void OpenMenu()
	{
		var actions = Sandbox.Input.ActionNames
			.ToArray();

		_menu = new ContextMenu( this );
		_menu.DeleteOnClose = true;

		_menu.AddLineEdit( "Filter", placeholder: "Filter Actions..",
			autoFocus: true,
			onChange: s => PopulateInputActionMenu( _menu, actions, s ) );

		_menu.AboutToShow += () => PopulateInputActionMenu( _menu, actions );

		_menu.OpenAtCursor( true );
		_menu.MinimumWidth = ScreenRect.Width;
	}

	private void PopulateInputActionMenu( Menu menu, IEnumerable<string> actions, string filter = null )
	{
		menu.RemoveMenus();
		menu.RemoveOptions();

		const int maxFiltered = 10;

		var useFilter = !string.IsNullOrEmpty( filter );
		var truncated = 0;

		_menu.AddOption( "Set to None", "clear_all", () => SetAction( "" ) );

		if ( useFilter )
		{
			var filtered = actions.Where( x => x.Contains( filter, StringComparison.OrdinalIgnoreCase ) ).ToArray();

			if ( filtered.Length > maxFiltered + 1 )
			{
				truncated = filtered.Length - maxFiltered;
				actions = filtered.Take( maxFiltered );
			}
			else
			{
				actions = filtered;
			}
		}

		actions = actions
			.OrderBy( x => Sandbox.Input.GetGroupName( x ) ?? "Other", StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x, StringComparer.OrdinalIgnoreCase );

		foreach ( var action in actions )
		{
			var group = Sandbox.Input.GetGroupName( action ) ?? "Other";
			var groupMenu = string.IsNullOrEmpty( filter ) ? menu.FindOrCreateMenu( group ) : menu;

			var option = groupMenu.AddOption( action?.ToLower(), "gamepad" );
			option.Triggered += () => SetAction( action );
		}

		if ( truncated > 0 )
		{
			menu.AddOption( $"...and {truncated} more" );
		}

		menu.AdjustSize();
		menu.Update();
	}

	private void SetAction( string action )
	{
		PropertyStartEdit();
		SerializedProperty.SetValue( action );
		PropertyFinishEdit();
	}
}
