using NativeEngine;
using Sandbox.Engine;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// An OS window whose entire contents are panel UI. It owns the window, its swap chain and the UI
/// inside it. Its input comes straight from SDL and never touches the engine's input system -
/// there's no widget toolkit involved anywhere. A popup is one that hangs off another, the way a
/// menu does - see <see cref="Popup"/>.
/// <para>
/// Editor only. A game has one window and draws its UI into that. The window itself is the engine's
/// internal <c>SurfaceWindow</c>; this is the public face of it for tools, and lives in Sandbox.Tools so
/// game code can't open windows.
/// </para>
/// </summary>
public partial class PanelWindow : IDisposable
{
	/// <summary>
	/// The engine window behind this one.
	/// </summary>
	internal SurfaceWindow Core { get; }

	/// <summary>
	/// The window an engine window is shown through. Windows opened here have one from the start;
	/// those the engine opened itself - popups, tooltips - get a plain one the first time they're asked for.
	/// </summary>
	internal static PanelWindow FromCore( SurfaceWindow core )
	{
		if ( core is null ) return null;
		if ( core.Facade is PanelWindow window ) return window;

		// The engine window owns its facade, which lives exactly as long as it does - handing it over here is what
		// tells callers they aren't the ones to dispose it
		window = new PanelWindow( core );
		core.Facade = window;
		return window;
	}

	/// <summary>
	/// Wrap a window the engine opened.
	/// </summary>
	PanelWindow( SurfaceWindow core )
	{
		Core = core;
	}

	/// <summary>
	/// True when this wraps a window the engine opened itself, rather than one opened here - the hooks
	/// below then belong to that window, not to us. False while our own window is still being made: the
	/// engine asks about it from inside its constructor, before <see cref="Core"/> is set.
	/// </summary>
	bool WrapsEngineWindow => Core is { } core && core is not Hooks;

	static readonly List<PanelWindow> _all = new();
	static int _allVersion = -1;

	/// <summary>
	/// Every window that's currently open.
	/// </summary>
	public static IReadOnlyList<PanelWindow> All
	{
		get
		{
			if ( _allVersion != SurfaceWindow.AllVersion )
			{
				_all.Clear();
				foreach ( var window in SurfaceWindow.All ) _all.Add( FromCore( window ) );
				_allVersion = SurfaceWindow.AllVersion;
			}

			return _all;
		}
	}

	/// <summary>
	/// The window the OS is giving keyboard input to, if it's one of ours.
	/// </summary>
	public static PanelWindow Focused => FromCore( SurfaceWindow.Focused );

	/// <summary>
	/// The window a panel is being shown in, if it's one of ours.
	/// </summary>
	public static PanelWindow FromPanel( Panel panel ) => FromCore( SurfaceWindow.FromPanel( panel ) );

	/// <summary>
	/// Close every window.
	/// </summary>
	internal static void DisposeAll() => SurfaceWindow.DisposeAll();

	private protected SdlWindow Window => Core.Window;

	/// <summary>
	/// The OS window. Zero until it exists - a popup makes its own at the first frame boundary.
	/// </summary>
	internal IntPtr Handle => Core.Handle;

	/// <summary>
	/// A popup - a transient window like a menu, dismissed by a click anywhere else.
	/// </summary>
	public virtual bool IsPopup => WrapsEngineWindow && Core.IsPopup;

	/// <summary>
	/// A window that never takes the keyboard or the mouse.
	/// </summary>
	public virtual bool IgnoresInput => WrapsEngineWindow && Core.IgnoresInput;

	/// <summary>
	/// The window this one hangs off, if it's a popup or a floating tool window.
	/// </summary>
	internal virtual PanelWindow ParentWindow => WrapsEngineWindow ? FromCore( Core.ParentWindow as SurfaceWindow ) : null;

	/// <summary>
	/// The UI running in this window. Engine machinery - tool code wants <see cref="Root"/>.
	/// </summary>
	internal UISurface Surface => Core.Surface;

	/// <summary>
	/// The panel everything in this window hangs off.
	/// </summary>
	public RootPanel Root => Core.Root;

	/// <summary>
	/// Where the cursor is, in this window's pixels.
	/// </summary>
	public Vector2 MousePosition => Core.MousePosition;

	/// <summary>
	/// The user asked to close the window. Return false to keep it open - an unsaved changes
	/// prompt, say. Otherwise it's disposed, or hidden when <see cref="HideOnClose"/> is set.
	/// </summary>
	public Func<bool> OnCloseRequested { get => Core.OnCloseRequested; set => Core.OnCloseRequested = value; }

	/// <summary>
	/// When the user closes the window, hide it instead of disposing it, so it can be shown again
	/// with everything still in it. Off by default: closing disposes.
	/// </summary>
	public bool HideOnClose { get => Core.HideOnClose; set => Core.HideOnClose = value; }

	/// <summary>
	/// The window moved, whether the user dragged it or code set <see cref="Position"/>.
	/// </summary>
	public Action OnMoved { get => Core.OnMoved; set => Core.OnMoved = value; }

	/// <summary>
	/// The window is on a different display than it was. Its scale may have changed with it.
	/// </summary>
	public Action OnDisplayChanged { get => Core.OnDisplayChanged; set => Core.OnDisplayChanged = value; }

	/// <summary>
	/// The window's client size changed, whether the user dragged an edge or code set <see cref="Size"/>.
	/// </summary>
	public Action OnResized { get => Core.OnResized; set => Core.OnResized = value; }

	/// <summary>
	/// The window was minimized to the taskbar.
	/// </summary>
	public Action OnMinimized { get => Core.OnMinimized; set => Core.OnMinimized = value; }

	/// <summary>
	/// The window was maximized.
	/// </summary>
	public Action OnMaximized { get => Core.OnMaximized; set => Core.OnMaximized = value; }

	/// <summary>
	/// The window came back from being minimized or maximized.
	/// </summary>
	public Action OnRestored { get => Core.OnRestored; set => Core.OnRestored = value; }

	/// <summary>
	/// The window became the active one - it has the keyboard, its title bar lit up.
	/// </summary>
	public Action OnActivated { get => Core.OnActivated; set => Core.OnActivated = value; }

	/// <summary>
	/// The window stopped being the active one: the user went to another window, ours or
	/// someone else's.
	/// </summary>
	public Action OnDeactivated { get => Core.OnDeactivated; set => Core.OnDeactivated = value; }

	/// <summary>
	/// What the window clears to before the UI is drawn.
	/// </summary>
	public Color BackgroundColor { get => Core.BackgroundColor; set => Core.BackgroundColor = value; }

	/// <summary>
	/// The window's title bar text.
	/// </summary>
	public string Title { get => Core.Title; set => Core.Title = value; }

	/// <summary>
	/// Size of the window's client area, in the units the UI inside it is authored in. The window
	/// on the desktop is this much again bigger on a display that scales.
	/// </summary>
	public Vector2 Size { get => Core.Size; set => Core.Size = value; }

	/// <summary>
	/// Size of the window's client area in real pixels - what the swap chain is sized to, and what
	/// the surface lays its panels out in. Zero until there's an OS window to measure.
	/// </summary>
	internal Vector2 PixelSize => Core.PixelSize;

	private protected float DisplayScale => Core.DisplayScale;

	/// <summary>
	/// Surface pixels to window coordinates, for handing the OS a position or a size.
	/// </summary>
	internal Vector2 PixelsToWindow( Vector2 pixels ) => Core.PixelsToWindow( pixels );

	/// <summary>
	/// Position of the window on the desktop, in the OS's own coordinates - desktop pixels on
	/// Windows. Deliberately not the units <see cref="Size"/> is in: a desktop spanning displays
	/// that scale differently has no single UI unit to measure it in.
	/// </summary>
	public Vector2 Position { get => Core.Position; set => Core.Position = value; }

	/// <summary>
	/// The smallest the user can resize the window to. Zero means no limit.
	/// </summary>
	public Vector2 MinSize { get => Core.MinSize; set => Core.MinSize = value; }

	/// <summary>
	/// The largest the user can resize the window to. Zero means no limit.
	/// </summary>
	public Vector2 MaxSize { get => Core.MaxSize; set => Core.MaxSize = value; }

	/// <summary>
	/// Whether the OS is allowed to maximize the window - the caption button, double clicking
	/// the title bar, Win+Up, snap. Windows drawing their own chrome check this for their
	/// maximize button too.
	/// </summary>
	public bool CanMaximize { get => Core.CanMaximize; set => Core.CanMaximize = value; }

	/// <summary>
	/// Whether the compositor draws its shadow around the window. A decorated window has one from
	/// its frame; a borderless window or a popup has to ask, and both do by default.
	/// </summary>
	public bool DropShadow { get => Core.DropShadow; set => Core.DropShadow = value; }

	/// <summary>
	/// Whether the compositor rounds the window's corners, the way it rounds the OS's own menus.
	/// Popups ask for it. Windows 10 and earlier stay square.
	/// </summary>
	public bool RoundedCorners { get => Core.RoundedCorners; set => Core.RoundedCorners = value; }

	/// <summary>
	/// Whether the native window exists and is not hidden. Minimized windows can still be visible.
	/// </summary>
	public bool IsVisible => Core.IsVisible;

	/// <summary>
	/// Is the window minimized to the taskbar?
	/// </summary>
	public bool IsMinimized => Core.IsMinimized;

	/// <summary>
	/// Keep the window above every window that isn't. Palettes, overlays, a video you want to
	/// keep watching while working in something else.
	/// </summary>
	public bool KeepOnTop { get => Core.KeepOnTop; set => Core.KeepOnTop = value; }

	/// <summary>
	/// How see-through the whole window is, contents included. 1 is opaque, 0 is invisible.
	/// </summary>
	public float Opacity { get => Core.Opacity; set => Core.Opacity = value; }

	/// <summary>
	/// Whether the window gets a button in the taskbar. On by default; a palette or a floating
	/// panel that belongs to another window usually turns it off.
	/// </summary>
	public bool ShowInTaskbar { get => Core.ShowInTaskbar; set => Core.ShowInTaskbar = value; }

	/// <summary>
	/// Whether the user can resize the window by its edges. Off makes the size whatever code says.
	/// </summary>
	public bool Resizable { get => Core.Resizable; set => Core.Resizable = value; }

	/// <summary>
	/// Fill the window's display. Borderless over the desktop unless <see cref="ExclusiveFullscreen"/>
	/// is set. Turning it off puts the window back where it was.
	/// </summary>
	public bool Fullscreen { get => Core.Fullscreen; set => Core.Fullscreen = value; }

	/// <summary>
	/// Whether <see cref="Fullscreen"/> takes the display over with a real display mode, the way
	/// a game does, instead of a borderless window over the desktop. Off by default: borderless
	/// switches instantly and plays well with other windows and displays.
	/// </summary>
	public bool ExclusiveFullscreen { get => Core.ExclusiveFullscreen; set => Core.ExclusiveFullscreen = value; }

	/// <summary>
	/// Is the window fullscreen right now, either way?
	/// </summary>
	public bool IsFullscreen => Core.IsFullscreen;

	/// <summary>
	/// The whole of the display the window is on, in the same desktop coordinates as <see cref="Position"/>.
	/// </summary>
	public Rect DisplayBounds => Core.DisplayBounds;

	/// <summary>
	/// The part of the window's display that isn't under the taskbar or a dock, in desktop coordinates.
	/// </summary>
	public Rect DisplayWorkArea => Core.DisplayWorkArea;

	/// <summary>
	/// Hold the window at this width to height ratio while the user resizes it - 16f / 9 keeps a
	/// video window the shape of its video. Null lets it be any shape.
	/// </summary>
	public float? AspectRatioLock { get => Core.AspectRatioLock; set => Core.AspectRatioLock = value; }

	/// <summary>
	/// The window this one belongs to. An owned window stays above its owner, minimizes with it,
	/// and is closed with it - a tool palette, a dialog. Null makes it a top level window again.
	/// </summary>
	public PanelWindow Owner
	{
		get => FromCore( Core.Owner );
		set => Core.Owner = value?.Core;
	}

	/// <summary>
	/// Block the <see cref="Owner"/> from taking input while this window is open, the way a
	/// dialog does. Does nothing without an owner. Cleared on close, so the owner isn't left dead.
	/// </summary>
	public bool Modal { get => Core.Modal; set => Core.Modal = value; }

	/// <summary>
	/// Whether the user can close the window: the caption button greys out and Alt+F4 does
	/// nothing. Code can still close it. Windows drawing their own chrome check this for their
	/// close button too.
	/// </summary>
	public bool CanClose { get => Core.CanClose; set => Core.CanClose = value; }

	/// <summary>
	/// Does this window have keyboard focus?
	/// </summary>
	public bool IsFocused => Core.IsFocused;

	/// <summary>
	/// Keep drawing at the display's frame rate even when nobody is looking at this window.
	/// Idle windows are paced right down - set this for one with something moving in it that
	/// has to keep moving, like a video or a live preview.
	/// </summary>
	public bool AlwaysFullFrameRate { get => Core.AlwaysFullFrameRate; set => Core.AlwaysFullFrameRate = value; }

	/// <summary>
	/// Is this window still open?
	/// </summary>
	public bool IsOpen => Core.IsOpen;

	/// <summary>
	/// True if we're drawing the title bar and borders ourselves. Popups always are.
	/// </summary>
	public bool Borderless => Core.Borderless;

	/// <summary>
	/// Does this window's present wait for the display? Changing it takes effect at the start of the
	/// window's next frame, when the swap chain is matched to the window.
	/// </summary>
	public bool VSync { get => Core.VSync; set => Core.VSync = value; }

	/// <summary>
	/// Is the window maximized?
	/// </summary>
	public bool IsMaximized => Core.IsMaximized;

	/// <summary>
	/// Open a window and start running UI in it. The size is in the units the UI inside it is
	/// authored in, the same as <see cref="Size"/> - on a display that scales, the window on the
	/// desktop comes out that much bigger, so what you asked for is what fits inside it.
	/// </summary>
	public PanelWindow( string title, Vector2 size ) : this( title, size, null, false )
	{
	}

	/// <summary>
	/// Open a window at a given desktop position, in the OS's own coordinates - see
	/// <see cref="Position"/>. Pass null to center it on the primary display.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2? position ) : this( title, size, position, false )
	{
	}

	/// <summary>
	/// Open a window. A borderless window has no OS title bar - draw your own, and mark the panels
	/// that should drag it with the <c>window-drag</c> class.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2? position, bool borderless ) : this( title, size, position, borderless, false )
	{
	}

	/// <summary>
	/// Open a window. With <paramref name="vsync"/> the window's present blocks for the display,
	/// which is what an app that has nothing else to do wants - the launcher paces itself on it.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2? position, bool borderless, bool vsync )
	{
		Core = new Hooks( this, title, size, position, borderless, vsync );
		Core.Facade = this;
	}

	/// <summary>
	/// The swap chain's sample count. None: panel UI antialiases in its shaders, and MSAA adds attachments and a resolve
	/// without improving it. A window drawing a scene into it may want some (SceneLab's <c>-swapchain-msaa</c>). Read while
	/// the base constructor runs, so an override can't use the derived class's fields.
	/// </summary>
	internal virtual RenderMultisampleType SwapChainMultisample => RenderMultisampleType.RENDER_MULTISAMPLE_NONE;

	/// <summary>
	/// The window is closing. Its surface and popups are still there.
	/// </summary>
	private protected virtual void OnClosing() { }

	/// <summary>
	/// Close the window and delete its panels.
	/// </summary>
	public void Dispose() => Core.Dispose();

	/// <summary>
	/// The user clicked the window's close button.
	/// </summary>
	public void RequestClose() => Core.RequestClose();

	/// <summary>
	/// Minimize the window.
	/// </summary>
	public void Minimize() => Core.Minimize();

	/// <summary>
	/// Fill the display.
	/// </summary>
	public void Maximize() => Core.Maximize();

	/// <summary>
	/// Maximize the window, or put it back if it already is.
	/// </summary>
	public void ToggleMaximized() => Core.ToggleMaximized();

	/// <summary>
	/// Take the window off screen without closing it. Everything in it stays; <see cref="Show"/>
	/// brings it back where it was.
	/// </summary>
	public void Hide() => Core.Hide();

	/// <summary>
	/// Put a hidden window back on screen.
	/// </summary>
	public void Show() => Core.Show();

	/// <summary>
	/// Put the window behind every other window. Focus stays where it is.
	/// </summary>
	public void SendToBack() => Core.SendToBack();

	/// <summary>
	/// Raise the window above the others without taking focus. <see cref="Focus"/> is the one
	/// that focuses it too.
	/// </summary>
	public void BringToFront() => Core.BringToFront();

	/// <summary>
	/// The icon in the title bar and on the taskbar button. The OS scales it, so 32 or 64 pixels
	/// square is plenty; it's copied, so the bitmap can go afterwards.
	/// </summary>
	public void SetIcon( Bitmap icon ) => Core.SetIcon( icon );

	/// <summary>
	/// Centre the window on its <see cref="Owner"/>, or on the usable part of its display when
	/// it has none. Either way it ends up on screen.
	/// </summary>
	public void MoveToCenter() => Core.MoveToCenter();

	/// <summary>
	/// Move the window the least distance that puts all of it on the usable part of its display.
	/// A window taller or wider than the display keeps its top left on screen.
	/// </summary>
	public void SnapToDisplay() => Core.SnapToDisplay();

	/// <summary>
	/// Flash the taskbar button to get the user's attention. Briefly by default; until focused
	/// keeps going until they click the window.
	/// </summary>
	public void FlashTaskbar( bool untilFocused = false ) => Core.FlashTaskbar( untilFocused );

	/// <summary>
	/// Stop a <see cref="FlashTaskbar"/> that's still going.
	/// </summary>
	public void StopFlashing() => Core.StopFlashing();

	/// <summary>
	/// A small icon over the corner of the window's taskbar button - a badge for a count, a
	/// status. 16 pixels square is what the taskbar shows. Null clears it. The description is
	/// what a screen reader says for it.
	/// </summary>
	public void SetOverlayIcon( Bitmap icon, string description = null ) => Core.SetOverlayIcon( icon, description );

	/// <summary>
	/// Bring the window to the front.
	/// </summary>
	public void Focus() => Core.Focus();

	//
	// Input
	//

	/// <summary>
	/// How far from the edge of a borderless window counts as a resize handle, in the same units
	/// the UI is authored in - it grows with the display scale so the grab stays the same size.
	/// </summary>
	public float ResizeBorder { get => Core.ResizeBorder; set => Core.ResizeBorder = value; }

	/// <summary>
	/// Ask the OS to pick a folder, starting at <paramref name="defaultPath"/>. Null when the
	/// user cancels or the dialog fails. Call on the main thread. Any pending dialog is joined,
	/// including requests from other windows or for a different dialog kind.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickFolder( string defaultPath = null ) => Core.PickFolder( defaultPath );

	/// <summary>
	/// Ask the OS for a file to open. The filter is name and extension list pairs, like
	/// "Scene files|scene;prefab|All files|*". Null on cancellation or error. Call on the main thread.
	/// Joins any pending dialog, including another window or dialog kind.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickOpenFile( string defaultPath = null, string filters = null ) => Core.PickOpenFile( defaultPath, filters );

	/// <summary>
	/// Ask the OS where to save a file. <paramref name="defaultPath"/> can end in a suggested
	/// file name. Filters as in <see cref="PickOpenFile"/>. Null on cancellation or error. Call on the main thread.
	/// Joins any pending dialog, including another window or dialog kind.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickSaveFile( string defaultPath = null, string filters = null ) => Core.PickSaveFile( defaultPath, filters );

	internal Sdl.HitTestResult HitTest( Vector2 position ) => Core.HitTest( position );

	//
	// Popups and tooltips
	//

	/// <summary>
	/// Open a popup - a borderless window that sits above this one and can hang outside it, the
	/// way an OS menu does. The position is in this window's client pixels, which is what a panel's
	/// <c>Box.Rect</c> is already in.
	/// <para>
	/// There is no size to pass. The popup is born hidden at the parent's size, shrinks to whatever
	/// is put in it, and only then appears - so what it ends up as is the size of its contents.
	/// </para>
	/// </summary>
	public static PanelWindow Popup( PanelWindow parent, Vector2 position, bool ignoresInput = false )
	{
		ArgumentNullException.ThrowIfNull( parent );

		return FromCore( SurfaceWindow.Popup( parent.Core, position, ignoresInput ) );
	}

	/// <summary>
	/// The tooltip window open over this one, if there is one.
	/// </summary>
	public PanelWindow TooltipWindow => FromCore( Core.TooltipWindow );

	//
	// Frames
	//

	/// <summary>
	/// Whether the OS window is on screen. A window is born hidden and appears once it has drawn
	/// its contents, so this is false for its first frame or two.
	/// </summary>
	public bool IsShown => Core.IsShown;

	/// <summary>
	/// Resize the window to fit the first thing on the surface. Popups use this so a menu is
	/// exactly as big as its contents, however far outside the parent window that ends up.
	/// </summary>
	public bool SizeToContents { get => Core.SizeToContents; set => Core.SizeToContents = value; }

	/// <summary>
	/// Simulate and draw. Called once a frame by the engine loop, and again from resize events
	/// while a drag has the main thread parked in a modal loop.
	/// </summary>
	internal bool Frame() => Core.Frame();

	/// <summary>
	/// Let frames run inside a frame that's already running - see IPanelWindow.
	/// </summary>
	public bool AllowNestedFrame { get => Core.AllowNestedFrame; set => Core.AllowNestedFrame = value; }

	/// <summary>
	/// Draw into the swap chain before the panels do - they're drawn over it. Turn
	/// <see cref="ClearsBackground"/> off and whatever this draws shows through the panels
	/// wherever they're transparent.
	/// </summary>
	private protected virtual void OnRenderBackground( SwapChainHandle_t swapChain, Vector2 size ) { }

	/// <summary>
	/// Whether the swap chain is cleared to <see cref="BackgroundColor"/> before the panels draw.
	/// Off for a window that fills it itself in <see cref="OnRenderBackground"/>.
	/// </summary>
	private protected bool ClearsBackground { get => Core.ClearsBackground; set => Core.ClearsBackground = value; }

	/// <summary>
	/// Whether the window follows its display's scale as it's dragged between displays.
	/// </summary>
	private protected virtual bool FollowsDisplayScale => true;

	/// <summary>
	/// The window has drawn its first frame and is about to be shown.
	/// </summary>
	private protected virtual void OnFirstShow() { }

	/// <summary>
	/// The engine window behind a window opened here. Carries the engine's hooks back up to this
	/// class, so a tool window overrides them the same way it always has.
	/// </summary>
	sealed class Hooks( PanelWindow owner, string title, Vector2 size, Vector2? position, bool borderless, bool vsync )
		: SurfaceWindow( title, size, position, borderless, vsync )
	{
		// Initialized before the base constructor runs, which reads SwapChainMultisample
		readonly PanelWindow _owner = owner;

		public override bool IsPopup => _owner.IsPopup;
		public override bool IgnoresInput => _owner.IgnoresInput;
		internal override IPanelWindow ParentWindow => _owner.ParentWindow?.Core;
		internal override RenderMultisampleType SwapChainMultisample => _owner.SwapChainMultisample;
		internal override bool FollowsDisplayScale => _owner.FollowsDisplayScale;
		internal override void OnClosing() => _owner.OnClosing();
		internal override void OnRenderBackground( SwapChainHandle_t swapChain, Vector2 size ) => _owner.OnRenderBackground( swapChain, size );
		internal override void OnFirstShow() => _owner.OnFirstShow();
	}
}
