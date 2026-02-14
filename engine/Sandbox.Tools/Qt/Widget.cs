using Sandbox.UI;
using System;

namespace Editor
{
	[Expose]
	public partial class Widget : QObject
	{
		internal Native.QWidget _widget;

		/// <summary>
		/// This is here for parent widgets to call into, that want to skip the CreateWidget shit.
		/// </summary>
		internal Widget( bool _ )
		{

		}


		public Widget() : this( null, false )
		{

		}

		internal Widget( IntPtr widget )
		{
			NativeInit( widget );
		}

		/// <summary>
		/// The default widget constructor
		/// </summary>
		/// <param name="parent">The parent to attach this to. This can be null while you're sorting stuff out, before you add it to a layout or something - but generally a null parent is something a window has.</param>
		/// <param name="isDarkWindow">If true we'll run a function on startup to force this to be a darkmode window. Basically pass true if this is going to be a window and we'll all be friends.</param>
		public Widget( Widget parent, bool isDarkWindow = false )
		{
			Sandbox.InteropSystem.Alloc( this );
			var widget = CWidget.CreateWidget( parent?._widget ?? default, this, isDarkWindow );

			NativeInit( widget );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			ThreadSafe.AssertIsMainThread();

			if ( _widget.IsValid )
			{
				Log.Warning( "NativeInit possible called multiple times" );
			}

			_widget = ptr;

			if ( _widget == default )
				throw new System.Exception( "_widget was null!" );

			base.NativeInit( ptr );

			var t = GetType();
			while ( t != typeof( Widget ) )
			{
				_widget.AddClassName( t.Name );
				t = t.BaseType;
			}

			EditorShortcuts.Register( this );
		}

		internal override void NativeShutdown()
		{
			ThreadSafe.AssertIsMainThread();
			_widget = default;

			base.NativeShutdown();

			EditorShortcuts.Unregister( this );
			styleWatch?.Dispose();
		}

		/// <summary>
		/// Makes the widget not interactable. This is also usually be reflected visually by the widget.
		/// The widget will not process any keyboard or mouse inputs. Applies retroactively to all children.
		/// </summary>
		public bool Enabled
		{
			get => IsValid ? _widget.isEnabled() : false;
			set
			{
				if ( !IsValid ) return;
				_widget.setEnabled( value );
			}
		}

		bool _readonly;
		/// <summary>
		/// Makes the widget read only. I.e. You can copy text of a text entry, but can't edit it.
		/// Applies retroactively to all children.
		/// </summary>
		public virtual bool ReadOnly
		{
			get => Parent != null && Parent.ReadOnly ? true : _readonly;
			set => _readonly = value;
		}

		/// <summary>
		/// Parent widget. If non null, position of this widget will be relative to the parent widget. Certain events will also propagate to the parent widget if unhandled.
		/// </summary>
		public Widget Parent
		{
			get => QObject.FindOrCreate( _widget.parentWidget() ) as Widget;
			set
			{
				if ( value == this ) throw new Exception( "Trying to parent to self" );

				_widget.setParent( value?._widget ?? default );
			}
		}

		/// <summary>
		/// Find the closest ancestor widget of type
		/// </summary>
		public T GetAncestor<T>() where T : Widget
		{
			if ( this is T t ) return t;
			return Parent?.GetAncestor<T>();
		}

		/// <summary>
		/// Get all descendants of type T
		/// </summary>
		public IEnumerable<T> GetDescendants<T>() where T : Widget
		{
			if ( this is T t ) yield return t;

			foreach ( var child in Children )
			{
				foreach ( var descendant in child.GetDescendants<T>() )
				{
					yield return descendant;
				}
			}
		}

		/// <summary>
		/// Returns whether or not the specified Widget is a descendent of this Widget.
		/// </summary>
		public bool IsDescendantOf( Widget parent )
		{
			if ( Parent == parent ) return true;
			if ( Parent == null ) return false;
			return Parent.IsDescendantOf( parent );
		}

		/// <summary>
		/// Returns whether or not the specified Widget is an ancestor of this Widget.
		/// </summary>
		public bool IsAncestorOf( Widget child )
		{
			if ( child.Parent == this ) return true;
			if ( child.Parent == null ) return false;
			return IsAncestorOf( child.Parent );
		}

		public Margin ContentMargins
		{
			get => new Margin( _widget.contentsMargins().Rect );
			set => _widget.setContentsMargins( (int)value.Left, (int)value.Top, (int)value.Right, (int)value.Bottom );
		}

		public Rect ContentRect
		{
			get => _widget.contentsRect().Rect;
		}

		/// <summary>
		/// Size of this widget.
		/// </summary>
		public Vector2 Size
		{
			get => (Vector2)_widget.size();
			set => _widget.resize( value );
		}

		/// <summary>
		/// This panel's rect at 0,0
		/// </summary>
		[Hide]
		public Rect LocalRect => new Rect( 0, Size );

		/// <summary>
		/// This panel's rect in screen coordinates
		/// </summary>
		[Hide]
		public Rect ScreenRect => new Rect( ScreenPosition, Size );

		/// <summary>
		/// Utility to interact with a widget's width - use Size where possible
		/// </summary>
		[Hide]
		public float Width
		{
			get => Size.x;
			set => Size = Size.WithX( value );
		}

		/// <summary>
		/// Utility to interact with a widget's width - use Size where possible
		/// </summary>
		[Hide]
		public float Height
		{
			get => Size.y;
			set => Size = Size.WithY( value );
		}

		/// <summary>
		/// Sets <see cref="MinimumWidth"/> and <see cref="MinimumHeight"/> simultaneously.
		/// </summary>
		public Vector2 MinimumSize
		{
			get => new Vector2( MinimumWidth, MinimumHeight );
			set
			{
				MinimumWidth = value.x;
				MinimumHeight = value.y;
			}
		}

		/// <summary>
		/// This widgets width should never be smaller than the given value.
		/// </summary>
		public float MinimumWidth { get; set; }

		/// <summary>
		/// This widgets height should never be smaller than the given value.
		/// </summary>
		public float MinimumHeight { get; set; }

		Vector2 _fixedSize;

		/// <summary>
		/// Sets the fixed height for this widget
		/// </summary>
		public float FixedHeight
		{
			get => _fixedSize.x;
			set
			{
				_fixedSize.x = value;
				_widget.setFixedHeight( (int)value );
			}
		}

		/// <summary>
		/// Sets the fixed width for this widget
		/// </summary>
		public float FixedWidth
		{
			get => _fixedSize.y;
			set
			{
				_fixedSize.y = value;
				_widget.setFixedWidth( (int)value );
			}
		}

		public Vector2 FixedSize
		{
			get => _fixedSize;

			set
			{
				_fixedSize = value;
				_widget.setFixedSize( (int)value.x, (int)value.y );
			}
		}

		/// <summary>
		/// This widgets width should never be larger than the given value.
		/// </summary>
		public float MaximumWidth
		{
			get => MaximumSize.x;
			set => MaximumSize = MaximumSize.WithX( value );
		}

		/// <summary>
		/// This widgets height should never be larger than the given value.
		/// </summary>
		public float MaximumHeight
		{
			get => MaximumSize.y;
			set => MaximumSize = MaximumSize.WithY( value );
		}


		const float FloatClamp = 1000000;

		/// <summary>
		/// Sets <see cref="MaximumWidth"/> and <see cref="MaximumHeight"/> simultaneously.
		/// </summary>
		public Vector2 MaximumSize
		{
			get
			{
				var value = (Vector2)_widget.maximumSize();

				if ( value.x > FloatClamp ) value = value.WithX( FloatClamp );
				if ( value.y > FloatClamp ) value = value.WithY( FloatClamp );

				return value;
			}
			set
			{
				if ( value.x > FloatClamp ) value = value.WithX( FloatClamp );
				if ( value.y > FloatClamp ) value = value.WithY( FloatClamp );

				_widget.setMaximumSize( value );
			}
		}

		/// <summary>
		/// Position of this widget, relative to its parent if it has one.
		/// </summary>
		public Vector2 Position
		{
			get => (Vector2)_widget.pos();
			set
			{
				if ( Position == value ) return;

				_widget.move( value );
			}
		}

		/// <summary>
		/// Whether this widget is visible or not, in the tree. This will return false if a parent is hidden. You 
		/// might want to set Hidden if you're looking to check local visible status on a widget.
		/// </summary>
		public bool Visible
		{
			get => IsValid && _widget.isVisible();
			set
			{
				if ( !IsValid ) return;

				_widget.setVisible( value );
			}
		}

		/// <summary>
		/// Whether this widget is hidden. This differs from Visible because this will return the state for
		/// this particular widget, where as Visible returns false if a parent is hidden etc.
		/// </summary>
		public bool Hidden
		{
			get => IsValid && _widget.isHidden();
			set
			{
				if ( !IsValid ) return;
				_widget.setHidden( value );
			}
		}

		/// <summary>
		/// Name of the widget, usually for debugging purposes.
		/// </summary>
		public string Name
		{
			get => _widget.objectName();
			set => _widget.setObjectName( value );
		}

		public bool TranslucentBackground
		{
			get => HasFlag( Flag.WA_TranslucentBackground );
			set => SetFlag( Flag.WA_TranslucentBackground, value );
		}

		public bool NoSystemBackground
		{
			get => HasFlag( Flag.WA_NoSystemBackground );
			set => SetFlag( Flag.WA_NoSystemBackground, value );
		}

		public bool TransparentForMouseEvents
		{
			get => HasFlag( Flag.WA_TransparentForMouseEvents );
			set => SetFlag( Flag.WA_TransparentForMouseEvents, value );
		}

		public bool ShowWithoutActivating
		{
			get => HasFlag( Flag.WA_ShowWithoutActivating );
			set => SetFlag( Flag.WA_ShowWithoutActivating, value );
		}

		public bool MouseTracking
		{
			get => _widget.hasMouseTracking();
			set => _widget.setMouseTracking( value );
		}

		/// <summary>
		/// Accept drag and dropping shit on us
		/// </summary>
		public bool AcceptDrops
		{
			get => _widget.acceptDrops();
			set => _widget.setAcceptDrops( value );
		}

		public bool IsFramelessWindow
		{
			get => HasWindowFlag( WindowFlags.Window | WindowFlags.FramelessWindowHint );
			set
			{
				if ( value )
				{
					_widget.setWindowFlags( WindowFlags.Window | WindowFlags.FramelessWindowHint );
				}
				else
				{
					_widget.setWindowFlags( WindowFlags.Widget );
				}
			}
		}

		public bool IsTooltip
		{
			get => HasWindowFlag( WindowFlags.ToolTip );
			set
			{
				if ( value )
				{
					_widget.setWindowFlags( WindowFlags.ToolTip | WindowFlags.FramelessWindowHint | WindowFlags.NoDropShadowWindowHint );
				}
				else
				{
					_widget.setWindowFlags( WindowFlags.Widget );
				}
			}
		}

		public bool IsPopup
		{
			get => HasWindowFlag( WindowFlags.Popup );
			set
			{
				if ( value )
				{
					_widget.setWindowFlags( WindowFlags.Popup | WindowFlags.FramelessWindowHint | WindowFlags.NoDropShadowWindowHint );
				}
				else
				{
					_widget.setWindowFlags( WindowFlags.Widget );
				}
			}
		}

		public bool IsWindow
		{
			get => HasWindowFlag( WindowFlags.Window );
			set => SetWindowFlag( WindowFlags.Window, value );
		}

		public bool HasMaximizeButton
		{
			set
			{
				SetWindowFlag( WindowFlags.Customized, value );
				SetWindowFlag( WindowFlags.MaximizeButton, value );
			}

			get => !HasWindowFlag( WindowFlags.MaximizeButton );
		}

		/// <summary>
		/// Delete this widget when close is pressed
		/// </summary>
		public bool DeleteOnClose
		{
			get => HasFlag( Flag.DeleteOnClose );
			set => SetFlag( Flag.DeleteOnClose, value );
		}


		/// <summary>
		/// The scale this widget is using (multiplying Size by this value gives the actual native size)
		/// </summary>
		public float DpiScale => _widget.devicePixelRatioF();

		public void Focus( bool activateWindow = true )
		{
			if ( activateWindow && !_widget.isActiveWindow() )
				_widget.activateWindow();

			_widget.setFocus();
		}

		/// <summary>
		/// Clear keyboard focus from this widget.
		/// </summary>
		public void Blur()
		{
			_widget.clearFocus();
		}

		/// <summary>
		/// Whether this widget has keyboard focus.
		/// </summary>
		public bool IsFocused
		{
			get => _widget.hasFocus();
		}

		public bool IsActiveWindow
		{
			get => _widget.isActiveWindow();
		}

		/// <summary>
		/// Sets the focus mode for this widget. This determines both how it will get focus and whether it will receive keyboard input.
		/// </summary>
		public FocusMode FocusMode
		{
			get => _widget.focusPolicy();
			set => _widget.setFocusPolicy( value );
		}

		/// <summary>
		/// Enables or disables the context menu on this widget.
		/// </summary>
		public bool ContextMenuEnabled
		{
			get => _widget.contextMenuEnabled();
			set => _widget.allowContextMenu( value );
		}

		internal enum Flag
		{
			WA_Disabled = 0,
			WA_UnderMouse = 1,
			WA_MouseTracking = 2,
			WA_ContentsPropagated = 3, // ## deprecated
			WA_OpaquePaintEvent = 4,

			WA_StaticContents = 5,
			WA_LaidOut = 7,
			WA_PaintOnScreen = 8,
			WA_NoSystemBackground = 9,
			WA_UpdatesDisabled = 10,
			WA_Mapped = 11,

			WA_InputMethodEnabled = 14,
			WA_WState_Visible = 15,
			WA_WState_Hidden = 16,

			WA_ForceDisabled = 32,
			WA_KeyCompression = 33,
			WA_PendingMoveEvent = 34,
			WA_PendingResizeEvent = 35,
			WA_SetPalette = 36,
			WA_SetFont = 37,
			WA_SetCursor = 38,
			WA_NoChildEventsFromChildren = 39,
			WA_WindowModified = 41,
			WA_Resized = 42,
			WA_Moved = 43,
			WA_PendingUpdate = 44,
			WA_InvalidSize = 45,

			WA_CustomWhatsThis = 47,
			WA_LayoutOnEntireRect = 48,
			WA_OutsideWSRange = 49,
			WA_GrabbedShortcut = 50,
			WA_TransparentForMouseEvents = 51,
			WA_PaintUnclipped = 52,
			WA_SetWindowIcon = 53,
			WA_NoMouseReplay = 54,
			DeleteOnClose = 55,
			WA_RightToLeft = 56,
			WA_SetLayoutDirection = 57,
			WA_NoChildEventsForParent = 58,
			WA_ForceUpdatesDisabled = 59,

			WA_WState_Created = 60,
			WA_WState_CompressKeys = 61,
			WA_WState_InPaintEvent = 62,
			WA_WState_Reparented = 63,
			WA_WState_ConfigPending = 64,
			WA_WState_Polished = 66,
			WA_WState_DND = 67, // ## deprecated
			WA_WState_OwnSizePolicy = 68,
			WA_WState_ExplicitShowHide = 69,

			WA_ShowModal = 70, // ## deprecated
			WA_MouseNoMask = 71,
			WA_GroupLeader = 72, // ## deprecated
			WA_NoMousePropagation = 73, // ## for now, might go away.
			WA_Hover = 74,
			WA_InputMethodTransparent = 75, // Don't reset IM when user clicks on this (for virtual keyboards on embedded)
			WA_QuitOnClose = 76,

			WA_KeyboardFocusChange = 77,

			WA_AcceptDrops = 78,
			WA_DropSiteRegistered = 79, // internal
			WA_ForceAcceptDrops = WA_DropSiteRegistered, // ## deprecated

			WA_WindowPropagation = 80,

			WA_NoX11EventCompression = 81,
			WA_TintedBackground = 82,
			WA_X11OpenGLOverlay = 83,
			WA_AlwaysShowToolTips = 84,
			WA_MacOpaqueSizeGrip = 85,
			WA_SetStyle = 86,

			WA_SetLocale = 87,
			WA_MacShowFocusRect = 88,

			WA_MacNormalSize = 89,  // Mac only
			WA_MacSmallSize = 90,   // Mac only
			WA_MacMiniSize = 91,    // Mac only

			WA_LayoutUsesWidgetRect = 92,
			WA_StyledBackground = 93, // internal

			WA_CanHostQMdiSubWindowTitleBar = 95, // Internal

			WA_MacAlwaysShowToolWindow = 96, // Mac only

			WA_StyleSheet = 97, // internal

			WA_ShowWithoutActivating = 98,

			WA_X11BypassTransientForHint = 99,

			WA_NativeWindow = 100,
			WA_DontCreateNativeAncestors = 101,

			WA_MacVariableSize = 102,    // Mac only

			WA_DontShowOnScreen = 103,

			// window types from http://standards.freedesktop.org/wm-spec/
			WA_X11NetWmWindowTypeDesktop = 104,
			WA_X11NetWmWindowTypeDock = 105,
			WA_X11NetWmWindowTypeToolBar = 106,
			WA_X11NetWmWindowTypeMenu = 107,
			WA_X11NetWmWindowTypeUtility = 108,
			WA_X11NetWmWindowTypeSplash = 109,
			WA_X11NetWmWindowTypeDialog = 110,
			WA_X11NetWmWindowTypeDropDownMenu = 111,
			WA_X11NetWmWindowTypePopupMenu = 112,
			WA_X11NetWmWindowTypeToolTip = 113,
			WA_X11NetWmWindowTypeNotification = 114,
			WA_X11NetWmWindowTypeCombo = 115,
			WA_X11NetWmWindowTypeDND = 116,

			WA_SetWindowModality = 118,
			WA_WState_WindowOpacitySet = 119, // internal
			WA_TranslucentBackground = 120,

			WA_AcceptTouchEvents = 121,
			WA_WState_AcceptedTouchBeginEvent = 122,
			WA_TouchPadAcceptSingleTouchEvents = 123,

			WA_X11DoNotAcceptFocus = 126,
			WA_MacNoShadow = 127,

			WA_AlwaysStackOnTop = 128,

			WA_TabletTracking = 129,

			WA_ContentsMarginsRespectsSafeArea = 130,

			WA_StyleSheetTarget = 131,

			// Add new attributes before this line
			WA_AttributeCount
		}

		internal void SetFlag( Flag f, bool b ) => _widget.setAttribute( f, b );

		internal bool HasFlag( Flag f ) => _widget.testAttribute( f );

		public WindowFlags WindowFlags
		{
			get => _widget.windowFlags();
			set => _widget.setWindowFlags( value );
		}

		internal void SetWindowFlag( WindowFlags f, bool b ) => _widget.setWindowFlags( b ? (_widget.windowFlags() & f) : (_widget.windowFlags() & ~f) );
		internal bool HasWindowFlag( WindowFlags f ) => _widget.windowFlags().Contains( f );

		/// <summary>
		/// Directly set CSS style sheet(s) for this widget. Same format as a .css file.
		/// </summary>
		/// <param name="sheet"></param>
		public void SetStyles( string sheet )
		{
			_widget.setStyleSheet( sheet );
		}

		Sandbox.FileWatch styleWatch;

		/// <summary>
		/// Set a file to load CSS for this widget from.
		/// </summary>
		public void SetStylesheetFile( string filename )
		{
			styleWatch?.Dispose();

			styleWatch = Editor.FileSystem.Mounted.Watch( filename );
			styleWatch.OnChanges += ( x ) => LoadStylesheetFile( filename );

			LoadStylesheetFile( filename );
		}

		internal void LoadStylesheetFile( string filename )
		{
			var txt = Editor.FileSystem.Mounted.ReadAllText( filename );
			SetStyles( txt );
		}

		/// <summary>
		/// Child widgets of this widget.
		/// </summary>
		public IEnumerable<Widget> Children
		{
			get
			{
				var childPointers = GetChildren();
				var children = new List<Widget>( childPointers.Length );

				foreach ( var p in childPointers )
				{
					var o = FindOrCreate( p );
					if ( o is Widget w && w.IsValid )
					{
						children.Add( w );
					}
				}

				return children;
			}
		}

		/// <summary>
		/// Destroys all child widgets of this widget.
		/// </summary>
		public void DestroyChildren()
		{
			foreach ( var child in Children )
			{
				child.Destroy();
			}
		}

		internal override void OnDestroyingLater()
		{
			Visible = false;
			Parent = null;
		}


		CursorShape _cursor = CursorShape.None;

		/// <summary>
		/// Cursor override for this widget.
		/// </summary>
		public virtual CursorShape Cursor
		{
			get => _cursor;
			set
			{
				_cursor = value;
				_pixmapCursor = null;

				if ( Cursor == CursorShape.None )
				{
					_widget.unsetCursor();
					return;
				}

				_widget.setCursor( Cursor );
			}
		}

		private Pixmap _pixmapCursor;

		/// <summary>
		/// Custom cursor override for this widget.
		/// Will override <see cref="Cursor"/> with <see cref="CursorShape.CustomCursor"/>.
		/// </summary>
		public virtual Pixmap PixmapCursor
		{
			get => _pixmapCursor;
			set
			{
				if ( _pixmapCursor?.ptr == value.ptr )
					return;

				_pixmapCursor = value;

				if ( _pixmapCursor == null )
				{
					_cursor = CursorShape.None;
					_widget.unsetCursor();
					return;
				}

				_cursor = CursorShape.CustomCursor;
				_widget.setCursor( _pixmapCursor.ptr );
			}
		}

		/// <summary>
		/// Tell this widget that shit changed and it needs to redraw
		/// </summary>
		public virtual void Update()
		{
			if ( !IsValid ) return;
			_widget.update();
		}

		/// <summary>
		/// Repaint immediately
		/// </summary>
		internal void Repaint()
		{
			if ( !IsValid ) return;
			_widget.repaint();
		}


		/// <summary>
		/// Position of the widget relative to the monitor's top left corner.
		/// </summary>
		public Vector2 ScreenPosition => ToScreen( default );


		/// <summary>
		/// Transform coordinates relative to the panel's top left corner, to coordinates relative to monitors's top left corner.
		/// </summary>
		/// <param name="p">Position on the panel, relative it its top left corner.</param>
		/// <returns>The same position relative to the monitors top left corner.</returns>
		public Vector2 ToScreen( Vector2 p )
		{
			if ( !IsValid ) return default;
			return (Vector2)_widget.mapToGlobal( p );
		}

		/// <summary>
		/// Transform coordinates relative to the monitors's top left corner, to coordinates relative to panel's top left corner.
		/// </summary>
		/// <param name="p">Position relative to the monitors top left corner.</param>
		/// <returns>The same position on the panel, relative it its top left corner.</returns>
		public Vector2 FromScreen( Vector2 p ) => (Vector2)_widget.mapFromGlobal( p );

		public void PostKeyEvent( KeyCode key )
		{
			WidgetUtil.PostKeyEvent( _widget, (int)key );
		}

		Widget _focusProxy;
		public Widget FocusProxy
		{
			get => _focusProxy;
			set
			{
				_focusProxy = value;
				_widget.setFocusProxy( _focusProxy?._widget ?? default );
			}
		}

		public bool IsUnderMouse => _widget.underMouse();

		public void SetSizeMode( SizeMode horizontal, SizeMode vertical )
		{
			_widget.setSizePolicy( horizontal, vertical );
		}

		public SizeMode HorizontalSizeMode
		{
			get => _widget.GetHorizontalSizePolicy();
			set => _widget.SetHorizontalSizePolicy( value );
		}

		public SizeMode VerticalSizeMode
		{
			get => _widget.GetVerticalSizePolicy();
			set => _widget.SetVerticalSizePolicy( value );
		}

		/// <summary>
		/// Serialize position and size of this widget to a string, which can then be passed to <see cref="RestoreGeometry"/>.
		/// </summary>
		/// <returns></returns>
		public string SaveGeometry() => _widget.saveGeometry();

		/// <summary>
		/// Restore position and size previously stored via <see cref="SaveGeometry"/>.
		/// </summary>
		/// <param name="state"></param>
		public void RestoreGeometry( string state )
		{
			if ( string.IsNullOrWhiteSpace( state ) )
				return;

			_widget.restoreGeometry( state );
		}

		/// <summary>
		/// If set, this text will be displayed after a certain delay of hovering this widget with the mouse cursor.
		/// </summary>
		public virtual string ToolTip
		{
			get => _widget.toolTip();
			set => _widget.setToolTip( value );
		}

		/// <summary>
		/// If set, hovering over this widget will set the text of a <see cref="StatusBar"/> of the window the widget belongs to.
		/// </summary>
		public string StatusTip
		{
			get => _widget.statusTip();
			set => _widget.setStatusTip( value );
		}

		public event Action<Widget> OnChildValuesChanged;

		protected virtual void Signal( WidgetSignal signal )
		{
			if ( signal.Type == "valuechanged" )
			{
				ChildValuesChanged( signal.SourceWidget );
			}

			if ( !signal.Propagate )
				return;

			Parent?.Signal( signal );
		}

		public virtual void ChildValuesChanged( Widget source )
		{
			OnChildValuesChanged?.Invoke( source );
		}

		public void MakeSignal( string name )
		{
			Signal( new WidgetSignal { SourceWidget = this, Type = name, Propagate = true } );
		}

		/// <summary>
		/// When a value on this widget changed due to user input (ie, checking a box, editing a form)
		/// this is called, which sends a signal up the parent widgets.
		/// </summary>
		protected void SignalValuesChanged()
		{
			BindSystem?.Flush();
			MakeSignal( "valuechanged" );
		}

		/// <summary>
		/// Adjusts the size of the widget to fit its contents.
		/// </summary>
		public void AdjustSize()
		{
			_widget.adjustSize();

			if ( Width < MinimumWidth ) Width = MinimumWidth;
			if ( Height < MinimumHeight ) Height = MinimumHeight;
		}

		/// <summary>
		/// Returns the geometry of the screen this widget is currently on.
		/// </summary>
		public Rect ScreenGeometry => _widget.ScreenGeometry().Rect;

		/// <summary>
		/// Constrain this widget to the screen it's currently on.
		/// </summary>
		public void ConstrainToScreen()
		{
			AdjustSize();

			WidgetUtil.ConstrainToScreen( _widget );
		}

		/// <summary>
		/// Reposition this widget to ensure it is within the given rectangle.
		/// </summary>
		/// <param name="parentRect">Rectangle to constraint to, relative to the parent widget.</param>
		public void ConstrainTo( Rect parentRect )
		{
			AdjustSize();

			var p = Position;
			var s = Size;
			var pr = new Rect( p, s );

			if ( pr.Top < parentRect.Top ) p = p.WithY( parentRect.Top );
			if ( pr.Left < parentRect.Left ) p = p.WithX( parentRect.Left );
			if ( pr.Bottom > parentRect.Bottom ) p = p.WithY( parentRect.Bottom - s.y );
			if ( pr.Right > parentRect.Right ) p = p.WithX( parentRect.Right - s.x );

			Position = p;
		}

		public virtual void SetWindowIcon( string name )
		{
			_widget.setWindowIcon( name );
		}

		public virtual void SetWindowIcon( Pixmap icon )
		{
			_widget.setWindowIconFromPixmap( icon.ptr );
		}

		public virtual string WindowTitle
		{
			get => _widget.windowTitle();
			set => _widget.setWindowTitle( value );
		}

		/// <summary>
		/// Make this widget visible.
		/// </summary>
		public virtual void Show()
		{
			_widget.show();
		}

		/// <summary>
		/// Make this widget not visible.
		/// </summary>
		public virtual void Hide()
		{
			_widget.hide();
		}

		/// <summary>
		/// If a window - will close
		/// </summary>
		public virtual void Close()
		{
			if ( !IsValid ) return;
			_widget.close();
		}

		public void MakeMinimized()
		{
			_widget.showMinimized();
		}

		public bool IsMinimized => _widget.isMinimized();

		public void MakeMaximized()
		{
			_widget.showMaximized();
		}

		public bool IsMaximized => _widget.isMaximized();

		public void MakeWindowed()
		{
			_widget.showNormal();
		}

		/// <summary>
		/// Set this window to be modal. This means it will appear on top of everything and block input to everything else.
		/// </summary>
		public void SetModal( bool on, bool application = false )
		{
			if ( !on )
			{
				_widget.setWindowModality( WindowModality.None );
			}
			else
			{
				_widget.setWindowModality( application ? WindowModality.Application : WindowModality.Window );
			}
		}

		/// <summary>
		/// Returns true if this is a modal window. This means it will appear on top of everything and block input to everything else.
		/// </summary>
		public bool IsModal() => _widget.isModal();


		/// <summary>
		/// Calling this will set the WS_EX_NOACTIVATE flag on the window internally, which will stop
		/// it taking focus away from other windows.
		/// </summary>
		public void DisableWindowActivation()
		{
			WidgetUtil.SetWindowNoActivate( _widget );
		}


		public void SetEffectOpacity( float f )
		{
			_widget.SetEffectOpacity( f );
		}

		public float WindowOpacity
		{
			get => _widget.windowOpacity();
			set => _widget.setWindowOpacity( value );
		}

		int _contentHash = -1234;
		RealTimeSince timeSinceUpdate = 10;

		/// <summary>
		/// Call every frame/tick to redraw this Widget on content change
		/// </summary>
		public bool SetContentHash( int hash, float secondsDebounce = 0.1f )
		{
			if ( timeSinceUpdate < secondsDebounce )
				return false;

			timeSinceUpdate = 0;

			if ( _contentHash == hash ) return false;

			_contentHash = hash;
			Update();
			return true;
		}

		/// <summary>
		/// Call every frame/tick to redraw this Widget on content change
		/// </summary>
		public bool SetContentHash( Func<int> getHash, float secondsDebounce = 0.1f )
		{
			if ( timeSinceUpdate < secondsDebounce )
				return false;

			timeSinceUpdate = 0;

			var hash = getHash();

			if ( _contentHash == hash ) return false;

			_contentHash = hash;
			Update();
			return true;
		}

		/// <summary>
		/// If true, Update will call 
		/// </summary>
		public bool UpdatesEnabled
		{
			get => _widget.updatesEnabled();
			set => _widget.setUpdatesEnabled( value );
		}

		/// <summary>
		/// Align this widget to its parents edge, with an offset.
		/// </summary>
		public virtual void AlignToParent( TextFlag alignment, Vector2 offset = default )
		{
			AdjustSize();

			if ( Parent is null )
				return;

			var rect = Parent.LocalRect;
			var size = Size + offset * 2;
			var childRect = rect.Align( size, alignment );
			Position = childRect.Position + offset;
		}

		/// <summary>
		/// Tell everything that the geometry of this has changed
		/// </summary>
		public void UpdateGeometry()
		{
			_widget.updateGeometry();
		}

		/// <summary>
		/// Get the top level window widget
		/// </summary>
		public Widget GetWindow()
		{
			return QObject.FindOrCreate( _widget.window() ) as Widget;
		}

	}





	public class WidgetSignal
	{
		public string Type;
		public Widget SourceWidget;
		public bool Propagate;
	}

	public enum FocusMode
	{
		/// <summary>
		/// Do not accept focus.
		/// </summary>
		None = 0,

		/// <summary>
		/// Accept focus by tabbing.
		/// </summary>
		Tab = 0x1,

		/// <summary>
		/// Accept focus by being clicked on.
		/// </summary>
		Click = 0x2,

		/// <summary>
		/// Accept focus by clicking or tabbing.
		/// </summary>
		TabOrClick = Tab | Click | 0x8,

		/// <summary>
		/// Accept focus when using the mouse wheel too.
		/// </summary>
		TabOrClickOrWheel = TabOrClick | 0x4
	}

	internal enum WindowModality
	{
		None,
		Window,
		Application
	}

	/// <summary>
	/// Suspends updates in the widget for this using scope.
	/// </summary>
	public class SuspendUpdates : System.IDisposable
	{
		Widget Widget;
		bool oldState;

		public static SuspendUpdates For( Widget widget )
		{
			if ( widget is null )
				return null;

			if ( widget.Hidden )
				return null;

			return new SuspendUpdates( widget );
		}

		internal SuspendUpdates( Widget widget )
		{
			Widget = widget;

			oldState = Widget.Hidden;
			Widget.Hidden = true;
		}

		public void Dispose()
		{
			Widget.Hidden = oldState;
		}
	}

}
