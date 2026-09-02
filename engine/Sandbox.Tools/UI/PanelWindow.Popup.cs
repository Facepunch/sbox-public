using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.UI;
using System;

namespace Editor;

public sealed partial class PanelWindow
{
	bool _isPopup;
	PanelWindow _parent;
	Vector2 _pendingPosition;
	Vector2 _pendingWindowSize;

	bool IPanelWindow.IsPopup => _isPopup;

	/// <summary>
	/// Open a popup - a borderless window that sits above its parent and can hang outside it, the
	/// way an OS menu does. The position is in the parent's client pixels, which is what a panel's
	/// <c>Box.Rect</c> is already in.
	/// <para>
	/// There is no size to pass. The popup is born hidden at the parent's size, shrinks to whatever
	/// is put in it, and only then appears - so what it ends up as is the size of its contents.
	/// </para>
	/// </summary>
	public static PanelWindow Popup( PanelWindow parent, Vector2 position )
	{
		ArgumentNullException.ThrowIfNull( parent );

		// SDL popup windows position themselves relative to their parent, in window coordinates
		return new PanelWindow( parent, parent.PixelsToWindow( position ) );
	}

	PanelWindow( PanelWindow parent, Vector2 localPosition )
	{
		ThreadSafe.AssertIsMainThread();

		_isPopup = true;
		_shown = false;
		SizeToContents = true;
		Borderless = true;

		_parent = parent;
		_pendingPosition = localPosition;

		// Start as big as the parent and let FitToContents take it down. Starting small would make
		// the contents lay out against a width they're about to lose, and it's that first layout
		// FitToContents measures.
		_pendingWindowSize = parent.PixelsToWindow( parent.PixelSize );

		Surface = new UISurface { DpiScale = parent.Surface.DpiScale, Size = parent.PixelSize };
		Surface.OnCursorChanged = x => _cursor = x;

		// The OS rounds and clips this window like its own menus - the styles square off
		// what would double-round inside that clip
		Surface.Root.AddClass( "os-popup" );

		// The root starts as big as the parent, and a stretched child would report the whole of
		// that back as its size and never shrink. Set here rather than in a stylesheet because
		// the root is above whatever sheet the contents bring with them.
		Surface.Root.Style.AlignItems = Align.FlexStart;

		_all.Add( this );
		PanelWindows.Register( this );
	}

	/// <summary>
	/// Make the OS window. Popups wait until the frame boundary for this - building a swap chain
	/// while another window is mid-render leaves it in a state that never presents.
	/// </summary>
	void CreateNativeWindow()
	{
		_window = PanelWindowNative.CreatePopup( _parent._window, (int)_pendingPosition.x, (int)_pendingPosition.y,
			(int)MathF.Ceiling( _pendingWindowSize.x ), (int)MathF.Ceiling( _pendingWindowSize.y ) );
		if ( _window == IntPtr.Zero )
			throw new Exception( "Couldn't create the popup" );

		// A popup can open on a display that scales differently to the window that spawned it
		Surface.DpiScale = PanelWindowNative.GetContentsScale( _window );

		_swapChain = PanelWindowNative.CreateSwapChain( _window, (int)RenderSettings.Instance.AntiAliasQuality.ToEngine(), false );
		_swapChainSize = PixelSize;

		_world = new SceneWorld();

		_camera = new SceneCamera( "PanelWindow Popup" )
		{
			World = _world,

			// Opaque - the compositor rounds the window's corners itself. Windows doesn't
			// composite swapchain alpha, so drawing our own round corners isn't an option.
			BackgroundColor = Color.Black,
			ClearFlags = ClearFlags.All,
			EnablePostProcessing = false,
			ZNear = 1,
			ZFar = 1000,
		};
	}
}
