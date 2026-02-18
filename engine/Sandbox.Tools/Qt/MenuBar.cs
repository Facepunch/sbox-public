using System;

namespace Editor;

public partial class MenuBar : Widget
{
	internal Native.QMenuBar _menubar;

	internal MenuBar( Native.QMenuBar widget ) : base( false )
	{
		NativeInit( widget );
	}

	public MenuBar( Widget parent ) : base( false )
	{
		Sandbox.InteropSystem.Alloc( this );
		NativeInit( CMenuBar.Create( parent?._widget ?? default, this ) );
	}

	internal override void NativeInit( IntPtr ptr )
	{
		_menubar = ptr;

		base.NativeInit( ptr );
	}
	internal override void NativeShutdown()
	{
		base.NativeShutdown();

		_menubar = default;
	}

	public Option AddOption( string path, string icon = null, Action action = null, string shortcut = null )
	{
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		var menu = GetPathTo( path );
		return menu.Last().AddOption( parts.Last(), icon, action, shortcut );
	}

	public void AddOption( string path, Option option )
	{
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		var menu = GetPathTo( path );
		option.Text = parts.Last();
		menu.Last().AddOption( option );
	}

	public void RemovePath( string path )
	{
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var menus = GetPathTo( path );
		if ( menus.Count == 0 ) return;

		menus.Last().RemoveOption( parts.Last() );
		menus.Reverse();

		foreach ( var menu in menus )
		{
			if ( menu.HasOptions ) break;
			if ( menu.HasMenus ) break;

			menu.Destroy();
		}
	}

	public List<Menu> GetPathTo( string path )
	{
		var list = new List<Menu>();
		var parts = Menu.GetSplitPath( path );
		if ( parts.Length == 1 ) return null;

		var menu = FindOrCreateMenu( parts[0].Name );
		if ( menu == null ) return null;

		menu.Icon = parts[0].Icon;

		list.Add( menu );

		if ( parts.Length <= 2 ) return list;

		menu.GetPathTo( parts[1..], list );
		return list;
	}

	List<Menu> Menus = new();

	public Menu FindOrCreateMenu( string name )
	{
		Menus.RemoveAll( x => !x.IsValid );

		var m = Menus.FirstOrDefault( x => x.Title.ToLower() == name.ToLower() );
		if ( m != null ) return m;

		return AddMenu( name );
	}

	public Option AddSeparator()
	{
		return new Option( _menubar.addSeparator() );
	}

	public Menu AddMenu( string name )
	{
		var m = new Menu( name, this );
		m.DeleteOnClose = false;

		_menubar.addMenu( m._menu );

		Menus.Add( m );

		return m;
	}

	public Menu AddMenu( string icon, string name )
	{
		var m = new Menu( name, this );
		m.DeleteOnClose = false;

		_menubar.addMenu( m._menu );
		return m;
	}

	public void Clear()
	{
		_menubar.clear();
	}
}
