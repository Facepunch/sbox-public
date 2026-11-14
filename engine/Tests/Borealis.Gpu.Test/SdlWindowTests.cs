using Borealis.Gpu;
using Borealis.Sdl;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// Borealis.Sdl's windows under Borealis.Gpu: a hidden window, a swap chain on SDL's own Vulkan surface, frames presented
/// through a resize. Inconclusive where there's no SDL3, no display or no Vulkan 1.3 GPU.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SdlWindowTests
{
	[TestMethod]
	public void SwapChainOnHiddenWindowPresentsAndResizes()
	{
		if ( !SdlLibrary.IsAvailable ) Assert.Inconclusive( "No SDL3 library" );
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );
		if ( !Sdl.TryInit( out var error ) ) Assert.Inconclusive( error );

		var errors = new List<string>();
		try
		{
			using var window = new SdlWindow( new SdlWindowOptions { Title = "Borealis.Sdl test", Width = 320, Height = 240, Hidden = true } );
			Assert.IsFalse( window.Visible );
			Assert.IsTrue( window.IsVulkan );
			AssertSize( (320, 240), window.PixelSize );
			Assert.AreSame( window, Sdl.FindWindow( window.Id ) );

			var extensions = Sdl.VulkanInstanceExtensions;
			CollectionAssert.Contains( extensions.ToArray(), "VK_KHR_surface" );

			using ( var device = CreateDevice( extensions, errors ) )
			{
				using ( var swapChain = device.CreateSwapChain( new SwapChainInfo { Surface = SurfaceSource.Custom( window.CreateVulkanSurface ), Width = 320, Height = 240 } ) )
				{
					AssertSize( window.PixelSize, (swapChain.Width, swapChain.Height) );
					Present( device, swapChain, new Vector4( 1, 0, 0, 1 ) );

					window.SetPixelSize( 400, 300 );
					Sdl.PumpEvents();
					AssertSize( (400, 300), window.PixelSize );
					Assert.IsTrue( window.Resized, "No pixel size event after resizing" );
					window.Resized = false;

					var (w, h) = window.PixelSize;
					swapChain.Resize( w, h );
					AssertSize( (400, 300), (swapChain.Width, swapChain.Height) );
					Present( device, swapChain, new Vector4( 0, 1, 0, 1 ) );
				}

				device.WaitIdle();
			}

			window.Dispose();
			Assert.IsNull( Sdl.FindWindow( window.Id ) );
		}
		finally
		{
			Sdl.Quit();
		}

		Assert.AreEqual( 0, errors.Count, "Validation errors:\n" + string.Join( "\n", errors.Take( 10 ) ) );
	}

	/// <summary>A presenting device with SDL's instance extensions and validation on; inconclusive without one.</summary>
	static Device CreateDevice( IReadOnlyList<string> extensions, List<string> errors )
	{
		try
		{
			return Device.Create( new DeviceOptions
			{
				ApplicationName = "Borealis.Sdl test",
				Validation = true,
				InstanceExtensions = extensions,
				Message = m =>
				{
					if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( $"{m.Id}: {m.Text}" );
				},
			} );
		}
		catch ( GpuException e )
		{
			Assert.Inconclusive( e.Message );
			return null;
		}
	}

	static void Present( Device device, SwapChain swapChain, Vector4 color )
	{
		device.BeginFrame();
		Assert.IsTrue( swapChain.Acquire( out var image ), "No image to draw into" );

		var list = device.BeginCommandList( QueueType.Graphics, "Clear window" );
		list.Barrier( TextureBarrier.Transition( image, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( image, color );
		list.Barrier( TextureBarrier.Transition( image, ImageLayout.CopyDestination, ImageLayout.Present, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.BottomOfPipe, MemoryAccess.None ) );
		device.Submit( list, swapChain: swapChain );
		swapChain.Present();
	}

	/// <summary>Sizes in pixels, allowing a pixel of rounding where a window unit is more than one pixel.</summary>
	static void AssertSize( (int Width, int Height) expected, (int Width, int Height) actual )
	{
		Assert.IsTrue( Math.Abs( expected.Width - actual.Width ) <= 1 && Math.Abs( expected.Height - actual.Height ) <= 1, $"Expected {expected}, got {actual}" );
	}

	[TestMethod]
	public unsafe void TranslatesRawEvents()
	{
		var raw = new byte[Sdl.RawEventSize];
		Assert.AreEqual( 128, raw.Length );

		fixed ( byte* p = raw )
		{
			// SDL_KeyboardEvent: Escape down with left shift, in window 7
			Write( p, 0, 0x300u );
			Write( p, 8, 1234ul );
			Write( p, 16, 7u );
			Write( p, 24, 41 );
			Write( p, 28, 27u );
			Write( p, 32, (ushort)0x0001 );
			p[36] = 1;
			var key = Sdl.Translate( (nint)p );
			Assert.AreEqual( SdlEventKind.KeyDown, key.Kind );
			Assert.AreEqual( SdlKey.Escape, key.Key );
			Assert.AreEqual( 27u, key.Keycode );
			Assert.AreEqual( 7u, key.WindowId );
			Assert.AreEqual( 1234ul, key.Timestamp );
			Assert.IsTrue( (key.Modifiers & SdlKeyModifiers.Shift) != 0 );
			Assert.IsFalse( key.Repeat );

			// SDL_WindowEvent: pixel size changed to 640x480
			raw.AsSpan().Clear();
			Write( p, 0, 0x207u );
			Write( p, 16, 3u );
			Write( p, 20, 640 );
			Write( p, 24, 480 );
			var size = Sdl.Translate( (nint)p );
			Assert.AreEqual( SdlEventKind.WindowPixelSizeChanged, size.Kind );
			Assert.AreEqual( (3u, 640, 480), (size.WindowId, size.Width, size.Height) );

			// SDL_MouseWheelEvent, flipped: reported as the user moved it
			raw.AsSpan().Clear();
			Write( p, 0, 0x403u );
			Write( p, 28, 2.0f );
			Write( p, 32, 1u );
			var wheel = Sdl.Translate( (nint)p );
			Assert.AreEqual( SdlEventKind.MouseWheel, wheel.Kind );
			Assert.AreEqual( -2.0f, wheel.DeltaY );

			// SDL_MouseButtonEvent: right button double click at (10, 20)
			raw.AsSpan().Clear();
			Write( p, 0, 0x401u );
			p[24] = 3;
			p[25] = 1;
			p[26] = 2;
			Write( p, 28, 10.0f );
			Write( p, 32, 20.0f );
			var button = Sdl.Translate( (nint)p );
			Assert.AreEqual( (SdlEventKind.MouseButtonDown, SdlMouseButton.Right, 2, 10.0f, 20.0f), (button.Kind, button.Button, button.Clicks, button.X, button.Y) );

			// SDL_TextInputEvent
			var text = Marshal.StringToCoTaskMemUTF8( "hé" );
			raw.AsSpan().Clear();
			Write( p, 0, 0x303u );
			Write( p, 24, text );
			Assert.AreEqual( "hé", Sdl.Translate( (nint)p ).Text );
			Marshal.FreeCoTaskMem( text );

			// Unmapped types keep SDL's number
			raw.AsSpan().Clear();
			Write( p, 0, 0x650u );
			var other = Sdl.Translate( (nint)p );
			Assert.AreEqual( (SdlEventKind.Other, 0x650u), (other.Kind, other.Type) );
		}
	}

	static unsafe void Write<T>( byte* p, int offset, T value ) where T : unmanaged => *(T*)(p + offset) = value;
}
