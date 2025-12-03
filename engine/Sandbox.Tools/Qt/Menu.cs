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

		private bool _toolTipsVisible;

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
			get => _toolTipsVisible;
			set => _menu.setToolTipsVisible( _toolTipsVisible = value );
		}

		private QAction _parentAction;

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

			// This is 99% the wanted behaviour, and 99% forgotten about
			// meaning we're ending up with a shit load of unused menus
			// hidden, just existing.
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

		List<Menu> Menus = new();
		List<Option> Options = new();

		private readonly List<(Widget Widget, QAction Action)> _widgets = new();

		public bool HasOptions => Options.Count > 0;
		public bool HasMenus => Menus.Count > 0;

		public int OptionCount => Options.Count;
		public int MenuCount => Menus.Count;

		public IReadOnlyList<Widget> Widgets => _widgets
			.Where( x => x.Widget.IsValid && x.Action.IsValid )
			.Select( x => x.Widget )
			.ToArray();

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

	/// <summary>
	/// Identical to Menu except DeleteOnClose defaults to true
	/// </summary>
	public class ContextMenu : Menu
	{
		public ContextMenu( Widget parent = null ) : base( parent )
		{
			DeleteOnClose = true;
		}
	}
}
