using Microsoft.AspNetCore.Components;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// One entry in a menu, and the menu it opens. A menu with no options is a plain command;
/// give it options and it becomes a submenu. A root menu with no parent is a context menu, or a
/// heading in a <see cref="MenuBar"/>.
/// </summary>
[Library( "menu" )]
[StyleSheet.Inline( "menu", Styles )]
public partial class Menu : Panel
{
	readonly IconPanel _icon;
	readonly Label _label;
	readonly Label _shortcutLabel;

	// Everything the list shows, in order - options and any other panels put in the menu
	readonly List<Panel> _rows = new();
	readonly List<Menu> _options = new();

	// Set once the row's own parts exist, so children added after that are rows of the menu
	readonly bool _built;

	public Menu()
	{
		AddClass( "menu" );

		var gutter = Add.Panel( "gutter part" );
		gutter.Add.Icon( "check", "check" );
		_icon = gutter.Add.Icon( null, "icon" );
		_label = Add.Label( null, "text part" );
		_shortcutLabel = Add.Label( null, "shortcut part" );
		Add.Icon( "chevron_right", "chevron part" );

		_built = true;
	}

	public Menu( string text, string icon = null ) : this()
	{
		Text = text;
		Icon = icon;
	}

	/// <summary>
	/// The text shown on the row.
	/// </summary>
	[Parameter]
	public string Text
	{
		get;
		set
		{
			field = value;
			_label.Text = value;
		}
	}

	/// <summary>
	/// Material icon shown in the gutter, before the text.
	/// </summary>
	[Parameter]
	public string Icon
	{
		get;
		set
		{
			field = value;
			_icon.Text = value;
			SetClass( "has-icon", !string.IsNullOrEmpty( value ) );
		}
	}

	/// <summary>
	/// A disabled row is dimmed and does nothing when clicked.
	/// </summary>
	[Parameter]
	public bool Enabled
	{
		get;
		set
		{
			field = value;
			SetClass( "disabled", !value );
		}
	} = true;

	/// <summary>
	/// Draws a line between rows instead of a row.
	/// </summary>
	[Parameter]
	public bool IsSeparator
	{
		get;
		set
		{
			field = value;
			SetClass( "separator", value );
		}
	}

	/// <summary>
	/// Clicking toggles <see cref="Checked"/> instead of running <see cref="Clicked"/>.
	/// </summary>
	[Parameter]
	public bool Checkable
	{
		get;
		set
		{
			field = value;
			SetClass( "checkable", value );
		}
	}

	/// <summary>
	/// Shows a check mark in the gutter.
	/// </summary>
	[Parameter]
	public bool Checked
	{
		get;
		set
		{
			field = value;
			SetClass( "checked", value );
		}
	}

	/// <summary>
	/// Shortcut text drawn on the right, like "Ctrl+S". Display only - nothing dispatches it yet.
	/// </summary>
	[Parameter]
	public string Shortcut
	{
		get;
		set
		{
			field = value;
			_shortcutLabel.Text = value;
			SetClass( "has-shortcut", !string.IsNullOrEmpty( value ) );
		}
	}

	/// <summary>
	/// Whether the menu stays up after this row is activated. Checkable rows do, so several can
	/// be flipped in one go; commands close the menu. Set it to say otherwise.
	/// </summary>
	[Parameter]
	public bool StaysOpen
	{
		get => _staysOpen ?? Checkable;
		set => _staysOpen = value;
	}

	bool? _staysOpen;

	/// <summary>
	/// Runs when the row is activated.
	/// </summary>
	[Parameter]
	public Action Clicked { get; set; }

	/// <summary>
	/// Runs with the new state when a checkable row is toggled. Setting it makes the row checkable.
	/// </summary>
	[Parameter]
	public Action<bool> Toggled
	{
		get;
		set
		{
			field = value;
			if ( value is not null ) Checkable = true;
		}
	}

	/// <summary>
	/// Fires every time this menu's options are about to be shown. Rebuild or enable things here.
	/// </summary>
	public event Action<Menu> AboutToShow;

	/// <summary>
	/// Fires when this menu's options are hidden, however that happened.
	/// </summary>
	public event Action<Menu> Closed;

	/// <summary>
	/// The menu this row is an option of. Null for a root menu.
	/// </summary>
	public Menu ParentMenu { get; private set; }

	/// <summary>
	/// The top of this menu tree.
	/// </summary>
	public Menu RootMenu => ParentMenu?.RootMenu ?? this;

	/// <summary>
	/// The option rows of this menu, in order.
	/// </summary>
	public IReadOnlyList<Menu> Options => _options;

	/// <summary>
	/// Everything the list shows, in order - the options and any other panels added.
	/// </summary>
	public IReadOnlyList<Panel> Rows => _rows;

	/// <summary>
	/// Does this row open a submenu?
	/// </summary>
	public bool HasOptions => _rows.Count > 0;

	/// <summary>
	/// Add a row. Returns it so you can keep configuring it or add options to it.
	/// </summary>
	public Menu AddOption( string text, string icon = null, Action action = null )
	{
		return AddOption( new Menu( text, icon ) { Clicked = action } );
	}

	/// <inheritdoc cref="AddOption(string, string, Action)"/>
	public Menu AddOption( string text, Action action )
	{
		return AddOption( text, null, action );
	}

	/// <summary>
	/// Add a checkable row.
	/// </summary>
	public Menu AddOption( string text, Action<bool> toggled )
	{
		return AddOption( new Menu( text ) { Toggled = toggled } );
	}

	/// <summary>
	/// Add a submenu. Same as <see cref="AddOption(string, string, Action)"/> without an action.
	/// </summary>
	public Menu AddMenu( string text, string icon = null )
	{
		return AddOption( new Menu( text, icon ) );
	}

	/// <summary>
	/// Add a line between rows.
	/// </summary>
	public Menu AddSeparator()
	{
		return AddOption( new Menu { IsSeparator = true } );
	}

	/// <summary>
	/// Add a row you built yourself.
	/// </summary>
	public Menu AddOption( Menu option )
	{
		option.ParentMenu?.Remove( option );
		option.ParentMenu = this;
		_options.Add( option );
		AddRow( option );
		return option;
	}

	/// <summary>
	/// Add any panel as a row - a text entry, a slider, a heading. It's shown in the list like
	/// an option but the menu leaves it alone.
	/// </summary>
	public T AddWidget<T>( T widget ) where T : Panel
	{
		AddRow( widget );
		return widget;
	}

	void AddRow( Panel row )
	{
		_rows.Add( row );
		row.AddClass( "menu-row" );
		if ( IsOpen ) row.Parent = _list;

		SetClass( "has-submenu", true );
	}

	/// <summary>
	/// Take a row out without deleting it.
	/// </summary>
	public void Remove( Panel row )
	{
		if ( !_rows.Remove( row ) ) return;

		if ( row is Menu option )
		{
			option.Close();
			option.ParentMenu = null;
			_options.Remove( option );
		}

		if ( row.Parent == _list ) row.Parent = null;
		row.RemoveClass( "menu-row" );

		SetClass( "has-submenu", HasOptions );
	}

	/// <summary>
	/// The first option with this text, or null.
	/// </summary>
	public Menu FindOption( string text )
	{
		return _options.FirstOrDefault( x => x.Text == text );
	}

	/// <summary>
	/// Delete every row.
	/// </summary>
	public void Clear()
	{
		foreach ( var row in _rows.ToArray() )
		{
			Remove( row );
			row.Delete( true );
		}
	}

	/// <summary>
	/// A panel nested inside a menu - from Razor, or AddChild - is a row of it, not part of the
	/// row itself. It stays hidden in the row until the menu opens and moves it into the list.
	/// </summary>
	protected override void OnChildAdded( Panel child )
	{
		base.OnChildAdded( child );

		if ( !_built ) return;

		if ( child is Menu option ) AddOption( option );
		else AddWidget( child );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		Close();
	}
}
