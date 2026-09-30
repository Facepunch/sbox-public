using Microsoft.AspNetCore.Components;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A horizontal or vertical strip of commands and controls. Children keep their natural size;
/// when space runs out the strip scrolls. Add ordinary panels directly to embed other controls.
/// </summary>
[Library( "toolbar" )]
[StyleSheet.Inline( "toolbar", Styles )]
public class Toolbar : Panel
{
	const string Styles = """
		.toolbar
		{
			flex-direction: row;
			align-items: center;
			flex-shrink: 0;
			min-width: 0;
			min-height: 0;
			overflow-x: scroll;
			overflow-y: hidden;
			pointer-events: all;
		}
		.toolbar > * { flex-shrink: 0; }
		.toolbar > .button
		{
			align-items: center;
			justify-content: center;
			white-space: nowrap;
		}
		.toolbar > .button.disabled { pointer-events: none; }
		.toolbar > .toolbar-spacer { flex-grow: 1; flex-shrink: 1; }
		.toolbar.vertical
		{
			flex-direction: column;
			align-items: stretch;
			overflow-x: hidden;
			overflow-y: scroll;
		}
		""";

	public Toolbar()
	{
		AddClass( "toolbar" );
	}

	/// <summary>
	/// Arrange commands top to bottom instead of left to right.
	/// </summary>
	[Parameter]
	public bool Vertical
	{
		get => HasClass( "vertical" );
		set => SetClass( "vertical", value );
	}

	/// <summary>
	/// Add a command. Use empty text for an icon-only button, and set its Tooltip.
	/// </summary>
	public Button AddButton( string text, string icon, Action clicked )
	{
		var button = AddChild( new Button( text, icon ) );
		button.AddEventListener( "onclick", () =>
		{
			if ( !button.Disabled ) clicked?.Invoke();
		} );
		return button;
	}

	/// <summary>
	/// Add a toggle. Its Active property holds the current checked state.
	/// </summary>
	public Button AddToggle( string text, string icon, bool value, Action<bool> changed )
	{
		Button button = null;
		button = AddButton( text, icon, () =>
		{
			button.Active = !button.Active;
			changed?.Invoke( button.Active );
		} );
		button.Active = value;
		return button;
	}

	/// <summary>
	/// Add a dropdown command. The toolbar owns the supplied menu and deletes it with the button.
	/// </summary>
	public Button AddMenu( string text, string icon, Menu menu )
	{
		ArgumentNullException.ThrowIfNull( menu );
		return AddChild( new MenuButton( text, icon, menu ) );
	}

	/// <summary>
	/// Add a divider perpendicular to the toolbar's orientation.
	/// </summary>
	public Panel AddSeparator() => Add.Panel( "toolbar-separator" );

	/// <summary>
	/// Push the following controls to the far end of the toolbar.
	/// </summary>
	public Panel AddSpacer() => Add.Panel( "toolbar-spacer" );

	/// <summary>
	/// Arrow keys move between buttons; embedded editors keep their own keyboard behaviour.
	/// </summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		var buttons = Children.OfType<Button>().Where( x => x.IsVisible && !x.Disabled && x.AcceptsFocus ).ToArray();
		var index = Array.FindIndex( buttons, x => x.HasFocus );
		var previous = Vertical ? "up" : "left";
		var next = Vertical ? "down" : "right";
		if ( index >= 0 && (e.Button == previous || e.Button == next || e.Button is "home" or "end") )
		{
			var target = e.Button switch
			{
				"home" => 0,
				"end" => buttons.Length - 1,
				_ => (index + (e.Button == next ? 1 : -1) + buttons.Length) % buttons.Length
			};
			buttons[target].Focus();
			buttons[target].ScrollAncestorsIntoView();
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}

	sealed class MenuButton : Button
	{
		readonly Menu _menu;

		public MenuButton( string text, string icon, Menu menu ) : base( text, icon )
		{
			_menu = menu;
			Add.Icon( "expand_more", "toolbar-chevron" );
		}

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );
			if ( Disabled ) return;
			if ( _menu.IsOpen ) _menu.Close();
			else _menu.Open( this, Parent is Toolbar { Vertical: true } ? Popup.PositionMode.RightTop : Popup.PositionMode.BelowLeft, 4 );
			e.StopPropagation();
		}

		public override void Tick()
		{
			Active = _menu.IsOpen;
			base.Tick();
		}

		public override void OnDeleted()
		{
			_menu.Delete( true );
			base.OnDeleted();
		}
	}
}
