using Native;
using System;

namespace Editor
{
	public partial class Menu : Widget
	{
		internal Native.QMenu _menu;

		public event Action AboutToShow;
		public event Action AboutToHide;

		public string Title
		{
			get => _menu.title();
			set => _menu.setTitle( value );
		}

		string _icon;

		public string Icon
		{
			get => _icon;
			set
			{
				if ( _icon == value ) return;

				_icon = value;
				_menu.setIcon( _icon );
			}
		}

		/// <summary>
		/// <para>
		/// This property holds whether tooltips of menu actions should be visible.
		/// </para>
		/// <para>
		/// This property specifies whether action menu entries show their tooltip.
		/// </para>
		/// <para>
		/// By default, this property is <c>false</c>.
		/// </para>
		/// </summary>
		public bool ToolTipsVisible
		{
			get => _menu.toolTipsVisible();
			set => _menu.setToolTipsVisible( value );
		}

		private QAction _parentAction;

		internal QAction GetParentAction() => _parentAction;

		public override string ToolTip
		{
			get => _parentAction.IsValid ? _parentAction.toolTip() : base.ToolTip;
			set
			{
				if ( _parentAction.IsValid ) _parentAction.setToolTip( value );
				else base.ToolTip = value;
			}
		}

		public Menu ParentMenu { get; private set; }
		public Menu RootMenu => ParentMenu?.RootMenu ?? this;

		internal Menu( Native.QWidget widget ) : base( false )
		{
			NativeInit( widget );
		}

		public Menu( Widget parent = null ) : base( false )
		{
			var ptr = Native.QMenu.Create( parent?._widget ?? default );
			NativeInit( ptr );
			DeleteOnClose = true;
		}

		public Menu( string title, Widget parent = null ) : this( parent )
		{
			Title = title;
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_menu = ptr;

			WidgetUtil.OnMenu_AboutToShow( ptr, Callback( OnAboutToShow ) );
			WidgetUtil.OnMenu_AboutToHide( ptr, Callback( OnAboutToHide ) );

			base.NativeInit( ptr );
		}

		internal override void NativeShutdown()
		{
			base.NativeShutdown();

			_menu = default;
			_parentAction = default;

			Options.Clear();
		}

		protected virtual void OnAboutToShow()
		{
			foreach ( var o in Options )
			{
				o.AboutToShow();
			}

			AboutToShow?.InvokeWithWarning();
		}

		protected virtual void OnAboutToHide()
		{
			AboutToHide?.InvokeWithWarning();
		}

		public virtual Option AddOption( string name, string icon = null, Action action = null, string shortcut = null )
		{
			if ( string.IsNullOrWhiteSpace( icon ) ) icon = null;

			var o = new Option( this, name, icon, action );

			if ( shortcut != null ) o.ShortcutName = shortcut;

			return AddOption( o );
		}

		public virtual Option AddOptionWithImage( string name, Pixmap icon, Action action = null, string shortcut = null )
		{
			var o = new Option( this, name, icon, action );

			if ( shortcut != null ) o.ShortcutName = shortcut;

			return AddOption( o );
		}

		/// <summary>
		/// Like AddOption, except will automatically create the menu path from the array of names
		/// </summary>
		public Option AddOption( string[] path, string icon = null, Action action = null, string shortcut = null )
		{
			return AddOption( path.Select( x => new PathElement( x, icon ) ).ToArray(), action, shortcut );
		}

		/// <summary>
		/// Like AddOption, except will automatically create the menu path from the array of names
		/// </summary>
		public Option AddOption( ReadOnlySpan<PathElement> path, Action action = null, string shortcut = null )
		{
			if ( path.Length == 1 )
			{
				return AddOption( path[0].Name, path[0].Icon, action, shortcut );
			}

			var o = FindOrCreateMenu( path[0].Name );
			if ( string.IsNullOrEmpty( o.Icon ) )
			{
				o.Icon = path[0].Icon;
			}

			return o.AddOption( path[1..], action, shortcut );
		}

		public virtual Option AddOption( Option option )
		{
			option.SetParent( this );
			_menu.insertAction( default, option._action );
			Options.Add( option );
			return option;
		}

		/// <summary>
		/// Add a widget as an action to the menu.<br/>
		/// Some widgets such as <see cref="Widget"/> and <see cref="LineEdit"/> require <see cref="Widget.OnMouseReleased"/>
		/// to set <see cref="MouseEvent.Accepted"/> to <see langword="true"/> to prevent the menu from closing.
		/// </summary>
		public T AddWidget<T>( T widget ) where T : Widget
		{
			var widgetAction = Native.CQNoDeleteWidgetAction.Create( _object );
			widgetAction.setDefaultWidget( widget._widget );
			_menu.insertAction( default, widgetAction );
			_widgets.Add( (widget, widgetAction) );
			return widget;
		}

		/// <summary>
		/// Insert a widget at a specific position in the menu
		/// </summary>
		internal T InsertWidgetAt<T>( T widget, int position ) where T : Widget
		{
			var widgetAction = Native.CQNoDeleteWidgetAction.Create( _object );
			widgetAction.setDefaultWidget( widget._widget );

			if ( position == 0 )
			{
				// Insert at the very beginning - before the first item
				var firstAction = Options.FirstOrDefault()?._action ?? Menus.FirstOrDefault()?.GetParentAction();
				if ( firstAction.HasValue && !firstAction.Value.IsNull )
				{
					_menu.insertAction( firstAction.Value, widgetAction );
				}
				else
				{
					_menu.addAction( widgetAction );
				}
			}
			else
			{
				// Get all actions and insert at the specified position
				var actions = new List<QAction>();
				foreach ( var opt in Options )
				{
					actions.Add( opt._action );
				}
				foreach ( var menu in Menus )
				{
					var action = menu.GetParentAction();
					if ( action.IsValid )
						actions.Add( action );
				}

				if ( position < actions.Count && actions[position].IsValid )
				{
					_menu.insertAction( actions[position], widgetAction );
				}
				else
				{
					_menu.addAction( widgetAction );
				}
			}

			_widgets.Add( (widget, widgetAction) );
			return widget;
		}

		private class Heading : Widget
		{
			public Label Label { get; }

			public Heading( string title ) : base( null )
			{
				Layout = Layout.Row();
				Layout.Margin = 6;
				Layout.Spacing = 4;

				Label = Layout.Add( new Label( title ) { Color = Color.White } );
			}

			protected override void OnPaint()
			{
				base.OnPaint();

				Paint.ClearPen();
				Paint.SetBrush( Color.Lerp( Theme.ControlBackground, Theme.TextControl, 0.05f ) );
				Paint.DrawRect( LocalRect );
			}
		}

		public Label AddHeading( string title )
		{
			return AddWidget( new Heading( title ) ).Label;
		}

		public void GetPathTo( string path, List<Menu> list )
		{
			GetPathTo( GetSplitPath( path ), list );
		}

		public void GetPathTo( ReadOnlySpan<PathElement> path, List<Menu> list )
		{
			if ( path.Length <= 1 )
			{
				return;
			}

			var menu = FindOrCreateMenu( path[0].Name );
			if ( menu == null ) return;

			menu.Icon ??= path[0].Icon;

			list.Add( menu );

			menu.GetPathTo( path[1..], list );
		}

		public Menu FindOrCreateMenu( string name )
		{
			Menus.RemoveAll( x => !x.IsValid );

			var m = Menus.FirstOrDefault( x => x.Title.ToLower() == name.ToLower() );
			if ( m != null ) return m;

			return AddMenu( name );
		}

		protected List<Menu> Menus = [];
		protected List<Option> Options = [];

		private readonly List<(Widget Widget, QAction Action)> _widgets = [];

		public bool HasOptions => Options.Count > 0;
		public bool HasMenus => Menus.Count > 0;

		public int OptionCount => Options.Count;
		public int MenuCount => Menus.Count;

		public IReadOnlyList<Widget> Widgets => _widgets
			.Where( x => x.Widget.IsValid && x.Action.IsValid )
			.Select( x => x.Widget )
			.ToArray();

		/// <summary>
		/// Get all options in this menu and all submenus recursively
		/// </summary>
		internal List<(Option Option, string FullPath)> GetAllOptionsRecursive( string pathPrefix = "" )
		{
			var result = new List<(Option, string)>();

			foreach ( var option in Options )
			{
				var fullPath = string.IsNullOrEmpty( pathPrefix ) ? option.Text : $"{pathPrefix} > {option.Text}";
				result.Add( (option, fullPath) );
			}

			foreach ( var menu in Menus )
			{
				var newPath = string.IsNullOrEmpty( pathPrefix ) ? menu.Title : $"{pathPrefix} > {menu.Title}";
				result.AddRange( menu.GetAllOptionsRecursive( newPath ) );
			}

			return result;
		}

		/// <summary>
		/// Remove this menu from its parent safely
		/// </summary>
		internal void RemoveFromParent()
		{
			if ( ParentMenu != null && _parentAction.IsValid )
			{
				ParentMenu._menu.removeAction( _parentAction );
			}
		}

		public Menu AddMenu( string name, string icon = null )
		{
			var menu = new Menu( name, this ) { ParentMenu = this };

			if ( icon != null ) menu.Icon = icon;
			return AddMenu( menu );
		}

		public Menu AddMenu( Menu menu )
		{
			menu._parentAction = _menu.addMenu( menu._menu );

			Menus.Add( menu );

			menu.ParentMenu = this;
			menu.DeleteOnClose = false;

			return menu;
		}

		public Option GetOption( string name )
		{
			return Options.FirstOrDefault( x => x.Text == name );
		}

		public void RemoveOption( string name )
		{
			var o = GetOption( name );
			if ( o == null ) return;
			RemoveOption( o );
		}

		public void RemoveOption( Option option )
		{
			Options.Remove( option );
			_menu.removeAction( option._action );
		}

		public void RemoveWidget( Widget widget )
		{
			var match = _widgets.FirstOrDefault( x => x.Widget == widget );

			if ( match is ({ IsValid: true }, { IsValid: true } ) )
			{
				_widgets.Remove( match );
				_menu.removeAction( match.Action );
			}
		}

		/// <summary>
		/// Remove all options
		/// </summary>
		public void RemoveOptions()
		{
			foreach ( var option in Options.Where( x => x.IsValid ) )
			{
				_menu.removeAction( option._action );
			}

			Options.Clear();
		}

		/// <summary>
		/// Remove all menus
		/// </summary>
		public void RemoveMenus()
		{
			foreach ( var menu in Menus.Where( x => x.IsValid ) )
			{
				menu.Destroy();
			}

			Menus.Clear();
		}

		/// <summary>
		/// Remove all widgets
		/// </summary>
		public void RemoveWidgets()
		{
			foreach ( var (widget, action) in _widgets.Where( x => x.Action.IsValid ) )
			{
				action.deleteLater();
			}

			_widgets.Clear();
		}

		public Option AddSeparator()
		{
			return new Option( _menu.addSeparator() );
		}

		public void OpenAt( Vector2 position, bool modal = true )
		{
			if ( modal )
			{
				_menu.exec( position );
			}
			else
			{
				OnAboutToShow();
				Position = position;
				Visible = true;
			}

			AdjustSize();
			ConstrainToScreen();
		}

		/// <summary>
		/// Open this menu at the mouse cursor position
		/// </summary>
		public void OpenAtCursor( bool modal = false )
		{
			OpenAt( Application.CursorPosition, modal );
		}

		public void Clear()
		{
			if ( _menu.IsNull )
				return;

			_menu.clear();

			Menus?.Clear();
			Options?.Clear();
			_widgets?.Clear();
		}

		Option lastActive;

		public Option SelectedOption
		{
			get
			{
				var a = _menu.activeAction();
				if ( a.IsNull ) return null;

				if ( lastActive != null && lastActive._action == a )
					return lastActive;

				lastActive = new Option( a );
				return lastActive;
			}
		}
	}
}
