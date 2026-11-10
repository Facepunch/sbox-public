using Sandbox;
using Sandbox.Utility;
using System;

namespace Editor
{
	public partial class Window : Widget
	{
		public static List<Window> All = new();

		internal Native.QMainWindow _mainWindow;
		internal Native.CFramelessMainWindow _nativeWindow;

		public string Title
		{
			get => WindowTitle;
			set
			{
				WindowTitle = value;
				if ( MenuWidget is TitleBar tb ) tb.Title = value;
			}
		}

		Widget _canvas;

		public Widget Canvas
		{
			get => _canvas;
			set
			{
				_canvas = value;
				_mainWindow.setCentralWidget( _canvas?._widget ?? default );
			}
		}

		public virtual MenuBar MenuBar
		{
			get
			{
				if ( MenuWidget is MenuBar mb ) return mb;
				if ( MenuWidget is TitleBar tb ) return tb.MenuBar;

				return null;
			}

			set
			{
				_mainWindow.setMenuBar( value?._menubar ?? default );
			}
		}

		Widget _menu;
		public Widget MenuWidget
		{
			get => _menu;
			set
			{
				if ( _menu == value ) return;

				// destroy old one?

				if ( _menu is MenuBar mb )
				{
					MenuBar = mb;
				}

				_menu = value;
				_mainWindow.setMenuWidget( _menu?._widget ?? default );
			}
		}

		StatusBar _status;

		public StatusBar StatusBar
		{
			get
			{
				// If native changes a status bar after managed init, this can happen e.g Hammer
				if ( _status != null && _status._statusbar.IsNull )
				{
					_status = new StatusBar( _mainWindow.statusBar() );
				}

				return _status;
			}
			set
			{
				if ( _status == value ) return;

				// destroy old one?

				_status = value;
				_mainWindow.setStatusBar( _status?._statusbar ?? default );
			}
		}

		/// <summary>
		/// Initialises the window at the centre of the screen (or main editor window if one is present) by default.
		/// </summary>
		public bool StartCentered { get; set; } = true;

		public Window( Widget parent = null ) : base( false )
		{
			InteropSystem.Alloc( this );
			_nativeWindow = Native.CManagedMainWindow.Create( parent?._widget ?? default, this );

			Init();
		}

		internal Window( Native.CFramelessMainWindow ptr ) : base( false )
		{
			InteropSystem.Alloc( this );
			_nativeWindow = ptr;

			Init();
		}

		private void Init()
		{
			NativeInit( _nativeWindow );

			var isMainWindow = this is EditorMainWindow;
			var titleBar = new TitleBar( this, isMainWindow );
			titleBar.SetTitleBarWidgets( _nativeWindow );
			MenuWidget = titleBar;

			DeleteOnClose = true;
			_mainWindow.setAnimated( false );

			SetWindowIcon( "logo_rounded.png" );
		}

		protected override void OnResize()
		{
			base.OnResize();

			// Force redraw the titlebar so that we can change the maximize button
			if ( MenuWidget is TitleBar tb )
			{
				tb.Update();
			}
		}

		private const WindowFlags DialogWindowFlags = WindowFlags.Customized | WindowFlags.Dialog;

		public bool IsDialog
		{
			set
			{
				if ( IsDialog ) return;

				if ( value ) _mainWindow.setWindowFlags( DialogWindowFlags );
				else _mainWindow.setWindowFlags( _mainWindow.windowFlags() & ~(DialogWindowFlags) );
			}

			get => _mainWindow.windowFlags().Contains( DialogWindowFlags );
		}

		public bool CloseButtonVisible
		{
			set
			{
				if ( value ) _mainWindow.setWindowFlags( WindowFlags.Customized | WindowFlags.CloseButton );
				else _mainWindow.setWindowFlags( _mainWindow.windowFlags() & ~WindowFlags.CloseButton );
			}

			get => _mainWindow.windowFlags().Contains( WindowFlags.CloseButton );
		}

		internal static uint InitFramelessWindow( Native.CFramelessMainWindow ptr )
		{
			var win = new Window( ptr );
			return InteropSystem.GetAddress( win, true );
		}

		public override void SetWindowIcon( string name )
		{
			base.SetWindowIcon( name );
			if ( MenuWidget is TitleBar tb ) tb.IconPixmap = Pixmap.FromFile( name );
		}

		public override void SetWindowIcon( Pixmap icon )
		{
			base.SetWindowIcon( icon );
			if ( MenuWidget is TitleBar tb ) tb.IconPixmap = icon;
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_mainWindow = ptr;

			if ( _mainWindow == default )
				throw new System.Exception( "_mainWindow was null!" );

			base.NativeInit( ptr );

			All.Add( this );

			MenuWidget = new MenuBar( _mainWindow.menuBar() );
			_status = new StatusBar( _mainWindow.statusBar() );

			MenuWidget.Visible = false;

		}

		public override void Show()
		{
			string geometryCookie = EditorCookie.GetString( $"Window.{StateCookie}.Geometry", null );
			if ( StartCentered && geometryCookie is null )
			{
				// no saved geometry, so fallback to centre
				Center();
			}

			base.Show();
		}

		internal override void NativeShutdown()
		{
			base.NativeShutdown();

			_mainWindow = default;
			_nativeWindow = default;
			All.Remove( this );
		}

		public override void Close()
		{
			SaveToStateCookie();
			base.Close();
		}

		/// <summary>
		/// TODO this was a test, get rid of it
		/// </summary>
		public void Clear()
		{
			if ( !_mainWindow.IsValid )
				return;

			if ( MenuBar is MenuBar mb )
			{
				mb.Clear();
			}

			Canvas?.Destroy();
			Canvas = null;
		}

		protected override void OnClosed()
		{
			SaveToStateCookie();
			base.OnClosed();
		}

		public void AddToolBar( ToolBar bar, ToolbarPosition position = ToolbarPosition.Top )
		{
			_mainWindow.addToolBar( position, bar._toolbar );
		}

		public void RemoveToolBar( ToolBar bar )
		{
			_mainWindow.removeToolBar( bar._toolbar );
		}

		public string SaveState( int version = 0 ) => _mainWindow.saveState( version );
		public void RestoreState( string state ) => _mainWindow.restoreState( state );

		protected override void OnBlur( FocusChangeReason reason )
		{
			base.OnBlur( reason );

			SaveToStateCookie();
		}

		/// <summary>
		/// Position the window at the centre of the screen, or main editor window if one is present.
		/// </summary>
		public void Center() => CenterWindow();
	}
}
