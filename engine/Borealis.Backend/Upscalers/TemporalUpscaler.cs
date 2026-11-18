using Borealis.Gpu;
using System.IO;
using System.Runtime.InteropServices;

namespace Borealis.Backend.Upscalers;

/// <summary>The temporal upscalers a player can choose.</summary>
public enum TemporalUpscalerKind
{
	/// <summary>AMD FidelityFX Super Resolution 3.</summary>
	Fsr3,

	/// <summary>NVIDIA DLSS: NVIDIA's GPUs only.</summary>
	Dlss,
}

/// <summary>
/// FSR3 or DLSS on a Borealis.Gpu device, from the engine's <c>borealis_upscalers</c> library (<c>src/borealis_upscalers</c>,
/// beside the engine's other native libraries): the same FidelityFX and NGX libraries and settings as Source 2's device uses,
/// recorded into the frame's own list. One is made per render and display size; HDR colour, reverse-Z depth.
/// </summary>
public sealed unsafe class TemporalUpscaler : IDisposable
{
	const string LibraryName = "borealis_upscalers";

	/// <summary>The images a frame's upscale reads and writes, as the library takes them.</summary>
	[StructLayout( LayoutKind.Sequential )]
	struct Image
	{
		public ulong Handle, View;
		public int Format;
		public uint Width, Height;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct DeviceInfo
	{
		public ulong Instance, PhysicalDevice, Device, GetInstanceProcAddr;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct DispatchInfo
	{
		public ulong CommandBuffer;
		public Image Color, Depth, MotionVectors, Reactive, Output;
		public float JitterX, JitterY;
		public uint RenderWidth, RenderHeight;
		public float Sharpness, FrameTimeDeltaMs, CameraNear, CameraFovVertical;
		public int Reset;
	}

	static readonly object loadLock = new();
	static nint library;
	static bool tried;
	static delegate* unmanaged< DeviceInfo*, delegate* unmanaged< int, byte*, void >, int > load;
	static delegate* unmanaged< uint, uint, uint, uint, int, int, nint > fsr3Create;
	static delegate* unmanaged< nint, DispatchInfo*, int > fsr3Dispatch;
	static delegate* unmanaged< nint, void > fsr3Destroy;
	static delegate* unmanaged< byte*, int, int > dlssInstanceExtensions;
	static delegate* unmanaged< ulong, ulong, ulong, byte*, int, int > dlssDeviceExtensions;
	static delegate* unmanaged< int, int, nint > dlssCreate;
	static delegate* unmanaged< nint, DispatchInfo*, int > dlssDispatch;
	static delegate* unmanaged< nint, void > dlssDestroy;

	// The device the library loaded Vulkan from: one at a time
	static Device loadedFor;

	/// <summary>Where the library's messages go: severity (0 information, 1 warning, 2 error) and text.</summary>
	public static Action<int, string> Message { get; set; }

	/// <summary>Where to look first for the library; the engine's native folder beside the application otherwise.</summary>
	public static string Directory { get; set; }

	/// <summary>Whether the library is there to call (Windows builds have it).</summary>
	public static bool Available => Load();

	static bool Load()
	{
		lock ( loadLock )
		{
			if ( tried ) return library != 0;
			tried = true;
			foreach ( var directory in Candidates() )
			{
				var path = Path.Combine( directory, EngineFolders.Library( LibraryName ) );
				if ( !File.Exists( path ) || !NativeLibrary.TryLoad( path, out var handle ) ) continue;

				nint Export( string name ) => NativeLibrary.GetExport( handle, name );
				try
				{
					load = (delegate* unmanaged< DeviceInfo*, delegate* unmanaged< int, byte*, void >, int >)Export( "BorealisUpscalersLoad" );
					fsr3Create = (delegate* unmanaged< uint, uint, uint, uint, int, int, nint >)Export( "BorealisFsr3Create" );
					fsr3Dispatch = (delegate* unmanaged< nint, DispatchInfo*, int >)Export( "BorealisFsr3Dispatch" );
					fsr3Destroy = (delegate* unmanaged< nint, void >)Export( "BorealisFsr3Destroy" );
					dlssInstanceExtensions = (delegate* unmanaged< byte*, int, int >)Export( "BorealisDlssInstanceExtensions" );
					dlssDeviceExtensions = (delegate* unmanaged< ulong, ulong, ulong, byte*, int, int >)Export( "BorealisDlssDeviceExtensions" );
					dlssCreate = (delegate* unmanaged< int, int, nint >)Export( "BorealisDlssCreate" );
					dlssDispatch = (delegate* unmanaged< nint, DispatchInfo*, int >)Export( "BorealisDlssDispatch" );
					dlssDestroy = (delegate* unmanaged< nint, void >)Export( "BorealisDlssDestroy" );
				}
				catch ( EntryPointNotFoundException )
				{
					NativeLibrary.Free( handle );
					continue;
				}

				library = handle;
				return true;
			}

			return false;
		}
	}

	static IEnumerable<string> Candidates()
	{
		if ( !string.IsNullOrEmpty( Directory ) ) yield return Directory;
		for ( var dir = new DirectoryInfo( AppContext.BaseDirectory ); dir is not null; dir = dir.Parent )
		{
			yield return Path.Combine( dir.FullName, EngineFolders.Native );
			yield return EngineFolders.NativeIn( Path.Combine( dir.FullName, "game" ) );
		}
	}

	[UnmanagedCallersOnly]
	static void OnMessage( int severity, byte* text )
	{
		try
		{
			Message?.Invoke( severity, Marshal.PtrToStringUTF8( (nint)text ) );
		}
		catch
		{
			// Nothing may escape into native code
		}
	}

	/// <summary>
	/// The instance extensions DLSS asks for, for <see cref="DeviceOptions.OptionalInstanceExtensions"/>: empty without the
	/// library.
	/// </summary>
	public static IReadOnlyList<string> DlssInstanceExtensions()
	{
		if ( !Load() ) return [];
		return Names( ( p, n ) => dlssInstanceExtensions( p, n ) );
	}

	/// <summary>
	/// The device extensions DLSS asks for on an adapter, for <see cref="DeviceOptions.OptionalDeviceExtensions"/>: none but on
	/// NVIDIA's.
	/// </summary>
	public static IReadOnlyList<string> DlssDeviceExtensions( nint instance, nint physicalDevice )
	{
		if ( !Load() ) return [];
		var loader = (ulong)Device.LoaderProcAddr;
		return Names( ( p, n ) => dlssDeviceExtensions( (ulong)instance, loader, (ulong)physicalDevice, p, n ) );
	}

	delegate int NameQuery( byte* names, int capacity );

	static IReadOnlyList<string> Names( NameQuery query )
	{
		var needed = query( null, 0 );
		if ( needed <= 1 ) return [];
		var buffer = new byte[needed];
		fixed ( byte* p = buffer ) query( p, needed );
		return System.Text.Encoding.UTF8.GetString( buffer, 0, needed - 1 ).Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	readonly Device gpu;
	nint context;

	/// <summary>Which upscaler it is.</summary>
	public TemporalUpscalerKind Kind { get; }

	/// <summary>The size frames are rendered at.</summary>
	public (int Width, int Height) RenderSize { get; }

	/// <summary>The size they're upscaled to.</summary>
	public (int Width, int Height) DisplaySize { get; }

	TemporalUpscaler( Device gpu, TemporalUpscalerKind kind, nint context, (int, int) render, (int, int) display )
	{
		this.gpu = gpu;
		this.context = context;
		Kind = kind;
		RenderSize = render;
		DisplaySize = display;
	}

	/// <summary>
	/// An upscaler of <paramref name="kind"/> on <paramref name="gpu"/>, from <paramref name="render"/> to
	/// <paramref name="display"/>. Null where it can't run: no library, DLSS on a GPU that isn't NVIDIA's, or the library
	/// refused (it says why through <see cref="Message"/>).
	/// </summary>
	public static TemporalUpscaler Create( Device gpu, TemporalUpscalerKind kind, (int Width, int Height) render, (int Width, int Height) display )
	{
		if ( render.Width < 1 || render.Height < 1 || display.Width < 1 || display.Height < 1 || !Load() ) return null;
		if ( kind == TemporalUpscalerKind.Dlss && gpu.Adapter.VendorId != 0x10DE ) return null;

		lock ( loadLock )
		{
			if ( loadedFor != gpu )
			{
				var native = gpu.Native;
				var info = new DeviceInfo { Instance = (ulong)native.Instance, PhysicalDevice = (ulong)native.PhysicalDevice, Device = (ulong)native.Device, GetInstanceProcAddr = (ulong)native.GetInstanceProcAddr };
				if ( load( &info, &OnMessage ) == 0 ) return null;
				loadedFor = gpu;
			}
		}

		var made = kind == TemporalUpscalerKind.Fsr3
			? fsr3Create( (uint)render.Width, (uint)render.Height, (uint)display.Width, (uint)display.Height, 1, 1 )
			: dlssCreate( 1, 1 );
		return made == 0 ? null : new TemporalUpscaler( gpu, kind, made, render, display );
	}

	/// <summary>
	/// The jitter sequence's length: FSR3's (<c>ffxFsr3UpscalerGetJitterPhaseCount</c>, 8 * (display / render)², truncated)
	/// or DLSS's as Source 2's device has it (8 * ⌈(display / render)²⌉).
	/// </summary>
	public int JitterPhaseCount
	{
		get
		{
			var ratio = (float)DisplaySize.Width / RenderSize.Width;
			return Math.Max( 1, Kind == TemporalUpscalerKind.Fsr3 ? (int)(8.0f * MathF.Pow( ratio, 2.0f )) : 8 * (int)MathF.Ceiling( ratio * ratio ) );
		}
	}

	/// <summary>The jitter of phase <paramref name="index"/>, in pixels within ±0.5: Halton (2, 3) from index 1, as both have it.</summary>
	public (float X, float Y) Jitter( int index )
	{
		var count = JitterPhaseCount;
		var i = index % count + 1;
		return (Halton( i, 2 ) - 0.5f, Halton( i, 3 ) - 0.5f);
	}

	static float Halton( int index, int radix )
	{
		float f = 1, result = 0;
		for ( var i = index; i > 0; i /= radix )
		{
			f /= radix;
			result += f * (i % radix);
		}

		return result;
	}

	/// <summary>One frame's upscale settings.</summary>
	public struct Frame
	{
		/// <summary>This frame's jitter, in pixels.</summary>
		public float JitterX, JitterY;

		/// <summary>FSR3's sharpening, 0 to 1: 0 is off. DLSS has none.</summary>
		public float Sharpness;

		/// <summary>Time since the last upscaled frame, in milliseconds.</summary>
		public float FrameTimeDeltaMs;

		/// <summary>The camera's near plane, in world units, and vertical field of view in radians.</summary>
		public float CameraNear, CameraFovVertical;

		/// <summary>Drop the history: a camera cut.</summary>
		public bool Reset;
	}

	/// <summary>
	/// Record the upscale of <paramref name="color"/> (with its <paramref name="depth"/>, <paramref name="motionVectors"/> and
	/// optional <paramref name="reactive"/> mask, all at the render size) into <paramref name="output"/> (the display size,
	/// a storage texture) in <paramref name="context"/>'s list. Returns whether the library recorded it. One a frame: FSR3
	/// keeps the image views it makes for a dispatch for its next three (FFX_MAX_QUEUED_FRAMES), so no more than that may be
	/// in flight.
	/// </summary>
	public bool Dispatch( GraphicsContext context, TextureHandle color, TextureHandle depth, TextureHandle motionVectors, TextureHandle reactive, TextureHandle output, in Frame frame )
	{
		if ( this.context == 0 ) return false;

		// All in General: the library moves its inputs to the layout it samples them in and back
		context.PrepareNative( color, ImageLayout.General, false );
		context.PrepareNative( depth, ImageLayout.General, false );
		context.PrepareNative( motionVectors, ImageLayout.General, false );
		if ( reactive.IsValid ) context.PrepareNative( reactive, ImageLayout.General, false );
		context.PrepareNative( output, ImageLayout.General, true );

		var info = new DispatchInfo
		{
			Color = ImageOf( color ),
			Depth = ImageOf( depth ),
			MotionVectors = ImageOf( motionVectors ),
			Reactive = reactive.IsValid ? ImageOf( reactive ) : default,
			Output = ImageOf( output ),
			JitterX = frame.JitterX,
			JitterY = frame.JitterY,
			RenderWidth = (uint)RenderSize.Width,
			RenderHeight = (uint)RenderSize.Height,
			Sharpness = Kind == TemporalUpscalerKind.Fsr3 ? frame.Sharpness : 0,
			FrameTimeDeltaMs = frame.FrameTimeDeltaMs,
			CameraNear = frame.CameraNear,
			CameraFovVertical = frame.CameraFovVertical,
			Reset = frame.Reset ? 1 : 0,
		};

		var list = context.List;
		info.CommandBuffer = (ulong)list.BeginNative();
		try
		{
			return Kind == TemporalUpscalerKind.Fsr3 ? fsr3Dispatch( this.context, &info ) == 0 : dlssDispatch( this.context, &info ) != 0;
		}
		finally
		{
			list.EndNative();
		}
	}

	Image ImageOf( TextureHandle texture )
	{
		var native = gpu.NativeOf( texture );
		return new Image { Handle = native.Image, View = native.View, Format = native.Format, Width = (uint)native.Width, Height = (uint)native.Height };
	}

	/// <summary>Free the upscaler's context and images. The GPU must be done with what it recorded.</summary>
	public void Dispose()
	{
		if ( context == 0 ) return;
		if ( Kind == TemporalUpscalerKind.Fsr3 ) fsr3Destroy( context );
		else dlssDestroy( context );
		context = 0;
	}
}
