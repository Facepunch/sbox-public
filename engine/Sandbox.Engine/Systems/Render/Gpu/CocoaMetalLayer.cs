using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// The <c>CAMetalLayer</c> Borealis.Gpu's swap chain presents to on macOS (<c>SurfaceSource.Metal</c>), added as a sublayer of
/// an <c>NSView</c> that something else owns: SDL's game window, or an editor widget's native view. The driver sizes the
/// layer's drawables to the swap chain, but not the layer itself, so <see cref="Fit"/> keeps it over the view. Main thread only.
/// </summary>
internal sealed class CocoaMetalLayer : IDisposable
{
	[StructLayout( LayoutKind.Sequential )]
	struct CGRect
	{
		public double X, Y, Width, Height;
	}

	const string ObjC = "/usr/lib/libobjc.A.dylib";

	[DllImport( ObjC )] static extern nint objc_getClass( string name );
	[DllImport( ObjC )] static extern nint sel_registerName( string name );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern nint Send( nint target, nint selector );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern nint Send( nint target, nint selector, nint arg );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern void Send( nint target, nint selector, byte arg );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern void Send( nint target, nint selector, double arg );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern void Send( nint target, nint selector, CGRect arg );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern CGRect SendRect( nint target, nint selector );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern double SendDouble( nint target, nint selector );

	readonly nint view;
	nint layer;

	/// <summary>The <c>CAMetalLayer*</c>.</summary>
	public nint Layer => layer;

	CocoaMetalLayer( nint view, nint layer )
	{
		this.view = view;
		this.layer = layer;
	}

	/// <summary>A layer over an <c>NSWindow</c>'s content view.</summary>
	public static CocoaMetalLayer ForWindow( nint nsWindow )
	{
		var content = Send( nsWindow, sel_registerName( "contentView" ) );
		if ( content == 0 ) throw new PlatformNotSupportedException( "The Cocoa window has no content view to present to" );
		return ForView( content );
	}

	/// <summary>A layer over an <c>NSView</c>, which is made layer-backed if it isn't.</summary>
	public static CocoaMetalLayer ForView( nint view )
	{
		// CAMetalLayer lives in QuartzCore, which nothing here links
		NativeLibrary.TryLoad( "/System/Library/Frameworks/QuartzCore.framework/QuartzCore", out _ );
		var metalClass = objc_getClass( "CAMetalLayer" );
		if ( metalClass == 0 ) throw new PlatformNotSupportedException( "CAMetalLayer isn't available" );

		var layer = Send( Send( metalClass, sel_registerName( "alloc" ) ), sel_registerName( "init" ) );
		if ( layer == 0 ) throw new PlatformNotSupportedException( "Couldn't make a CAMetalLayer" );

		Send( view, sel_registerName( "setWantsLayer:" ), (byte)1 );
		var hosting = Send( view, sel_registerName( "layer" ) );
		Send( hosting, sel_registerName( "addSublayer:" ), layer );

		var made = new CocoaMetalLayer( view, layer );
		made.Fit();
		return made;
	}

	/// <summary>Cover the view, at the screen's pixel density.</summary>
	public void Fit()
	{
		if ( layer == 0 ) return;

		var bounds = SendRect( view, sel_registerName( "bounds" ) );
		Send( layer, sel_registerName( "setFrame:" ), bounds );

		var window = Send( view, sel_registerName( "window" ) );
		if ( window != 0 ) Send( layer, sel_registerName( "setContentsScale:" ), SendDouble( window, sel_registerName( "backingScaleFactor" ) ) );
	}

	/// <summary>Take the layer off the view. The swap chain presenting to it is disposed first.</summary>
	public void Dispose()
	{
		if ( layer == 0 ) return;

		Send( layer, sel_registerName( "removeFromSuperlayer" ) );
		Send( layer, sel_registerName( "release" ) );
		layer = 0;
	}
}
