using Sandbox.Engine.Settings;
using Sandbox.Rendering;
using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// The native tools' scene views (toolscene: ModelDoc, the material editor's preview, the animgraph editor's preview, asset
/// previews and native thumbnails) drawn on Borealis.Gpu with <c>-borealisgpu</c>. A native view hands its frame over
/// (<c>CToolScene::RenderManaged</c>, <see cref="Request"/>): its worlds, camera and size, and the native window to present
/// in or the pixels to read back into. Native made those worlds and their objects, and every one has a managed twin, so
/// the views draw them as any other world. Native changes them behind managed code's back, so their objects report
/// their changes before each frame (<see cref="Poll"/>). docs/managed/borealis-gpu-path.md, the editor.
/// </summary>
internal static class ToolSceneViews
{
	/// <summary>
	/// <c>ToolSceneManagedRender_t</c> (src/public/toolscene/toolscene.h), as native lays it out.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct Request
	{
		public const int Version = 1;
		public const int MaxWorlds = 16;

		public int RequestVersion;
		public int WorldCount;
		public fixed int Worlds[MaxWorlds];
		public IntPtr Window;
		public int Width;
		public int Height;
		public fixed float Origin[3];
		public fixed float Angles[3];
		public float FovX;
		public float ZNear;
		public float ZFar;
		public float OrthoWidth;
		public float OrthoHeight;
		public fixed float Background[4];
		public byte* Pixels;
		public int PixelFormat;
		public int Handled;
	}

	/// <summary>
	/// <c>ToolSceneManagedPixels_t</c>.
	/// </summary>
	internal enum PixelFormat
	{
		None = 0,
		Rgb888 = 1,
		Rgba8888 = 2,
		Bgra8888 = 3,
	}

	/// <summary>
	/// A live view: the native window it presents in, and the camera that draws its worlds.
	/// </summary>
	sealed class View
	{
		public BorealisGpuWindow Window;
		public SceneCamera Camera;
	}

	static readonly Dictionary<IntPtr, View> views = new();

	// Read-backs share one camera
	static SceneCamera bitmapCamera;

	static ulong frame;

	/// <summary>
	/// Draw a native view's frame (<c>RenderToolScene</c>). Sets <see cref="Request.Handled"/> when it drew it, or skipped it
	/// on purpose; native draws it when it's left clear.
	/// </summary>
	internal static unsafe void Render( IntPtr requestPointer )
	{
		if ( requestPointer == IntPtr.Zero ) return;
		ref var request = ref *(Request*)requestPointer;
		if ( request.RequestVersion != Request.Version ) return;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend gpu ) return;
		if ( request.Width < 1 || request.Height < 1 || request.WorldCount < 1 ) return;

		try
		{
			if ( request.Window != IntPtr.Zero ) RenderLive( gpu, ref request );
			else RenderPixels( ref request );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Tool scene view on Borealis.Gpu: {e.Message}" );
		}
	}

	static void RenderLive( BorealisGpuRenderBackend gpu, ref Request request )
	{
		// A view asked for inside another frame's block waits for its own frame: drawing it would nest blocks
		if ( Graphics.IsActive )
		{
			request.Handled = 1;
			return;
		}

		if ( !views.TryGetValue( request.Window, out var view ) )
		{
			var window = BorealisGpuWindow.FromNativeWindow( gpu, request.Window, new Vector2( request.Width, request.Height ), vsync: false );
			if ( window is null ) return;

			window.Samples = Graphics.RenderMultiSampleToNum( NativeEngine.RenderDeviceManager.GetBestMultisampleType( RenderSettings.Instance.AntiAliasQuality.ToEngine() ) );
			view = new View { Window = window, Camera = NewCamera( "Tool scene view" ) };
			views[request.Window] = view;
		}

		var size = new Vector2( request.Width, request.Height );
		Setup( view.Camera, ref request );
		request.Handled = 1;

		if ( !view.Window.Update( size ) ) return;
		if ( !view.Window.BeginFrame( view.Camera.BackgroundColor ) ) return;

		try
		{
			view.Camera.AddToRenderList( default, size );
		}
		finally
		{
			view.Window.EndFrame( NoOverlay, screenshots: false );
		}

		view.Window.PresentFrame();
	}

	static readonly Action NoOverlay = () => { };

	static unsafe void RenderPixels( ref Request request )
	{
		var format = (PixelFormat)request.PixelFormat;
		if ( request.Pixels == null || format == PixelFormat.None ) return;

		bitmapCamera ??= NewCamera( "Tool scene read-back" );
		Setup( bitmapCamera, ref request );

		using var bitmap = new Bitmap( request.Width, request.Height );
		bitmapCamera.RenderToBitmap( bitmap );

		// RenderToBitmap writes RGBA8 bytes
		var count = request.Width * request.Height;
		var source = new ReadOnlySpan<byte>( bitmap.GetPointer(), count * 4 );
		var bytes = format == PixelFormat.Rgb888 ? 3 : 4;
		var destination = new Span<byte>( request.Pixels, count * bytes );
		Convert( source, destination, format );
		request.Handled = 1;
	}

	/// <summary>
	/// RGBA8 pixels into the format native asked for.
	/// </summary>
	internal static void Convert( ReadOnlySpan<byte> rgba, Span<byte> destination, PixelFormat format )
	{
		var count = rgba.Length / 4;
		switch ( format )
		{
			case PixelFormat.Rgba8888:
				rgba[..(count * 4)].CopyTo( destination );
				break;

			case PixelFormat.Bgra8888:
				for ( int i = 0; i < count; i++ )
				{
					destination[i * 4 + 0] = rgba[i * 4 + 2];
					destination[i * 4 + 1] = rgba[i * 4 + 1];
					destination[i * 4 + 2] = rgba[i * 4 + 0];
					destination[i * 4 + 3] = rgba[i * 4 + 3];
				}
				break;

			case PixelFormat.Rgb888:
				for ( int i = 0; i < count; i++ )
				{
					destination[i * 3 + 0] = rgba[i * 4 + 0];
					destination[i * 3 + 1] = rgba[i * 4 + 1];
					destination[i * 3 + 2] = rgba[i * 4 + 2];
				}
				break;
		}
	}

	static SceneCamera NewCamera( string name ) => new( name )
	{
		ToolsView = true,
		EnablePostProcessing = true,
		ClearFlags = ClearFlags.All,
	};

	/// <summary>
	/// Point a camera at a request's worlds, from its camera.
	/// </summary>
	static unsafe void Setup( SceneCamera camera, ref Request request )
	{
		var count = Math.Min( request.WorldCount, Request.MaxWorlds );
		SceneWorld main = null;
		camera.Worlds.Clear();
		for ( int i = 0; i < count; i++ )
		{
			var world = Sandbox.HandleIndex.Get<SceneWorld>( request.Worlds[i] );
			if ( !world.IsValid() ) continue;

			Poll( world );
			if ( main is null )
			{
				main = world;
				camera.World = null;
				camera.World = world;
			}
			else
			{
				camera.Worlds.Add( world );
			}
		}

		frame++;

		camera.Position = new Vector3( request.Origin[0], request.Origin[1], request.Origin[2] );
		camera.Rotation = Rotation.From( request.Angles[0], request.Angles[1], request.Angles[2] );
		camera.ZNear = request.ZNear;
		camera.ZFar = request.ZFar;
		camera.Ortho = request.FovX < 0;
		if ( camera.Ortho ) camera.OrthoHeight = request.OrthoHeight;
		else camera.FieldOfView = request.FovX;
		camera.BackgroundColor = new Color( request.Background[0], request.Background[1], request.Background[2], request.Background[3] );
	}

	/// <summary>Frames between re-reading a native-authored object's model and materials.</summary>
	const ulong ModelPollFrames = 30;

	/// <summary>
	/// Native moves, animates and edits a tool world's objects without telling managed code, so before a frame each
	/// object reports what can change cheaply (placement, pose, visibility, tint, light settings), and its model and
	/// materials every <see cref="ModelPollFrames"/> frames. Tool worlds hold tens of objects.
	/// </summary>
	static void Poll( SceneWorld world )
	{
		if ( world.ChangeListener is null ) return;

		var change = SceneObjectChange.Transform | SceneObjectChange.Bones | SceneObjectChange.Visibility | SceneObjectChange.Tint
			| SceneObjectChange.Settings | SceneObjectChange.Bounds;
		if ( frame % ModelPollFrames == 0 ) change |= SceneObjectChange.Model | SceneObjectChange.Material | SceneObjectChange.Flags;

		lock ( world.InternalSceneObjects )
		{
			foreach ( var sceneObject in world.InternalSceneObjects )
				sceneObject.NotifyChanged( change );
		}
	}

	/// <summary>
	/// A native view's window is going (<c>ReleaseToolSceneView</c>): its swap chain goes first.
	/// </summary>
	internal static void Release( IntPtr window )
	{
		if ( !views.Remove( window, out var view ) ) return;

		view.Window.Dispose();
		view.Camera.Dispose();
	}

	/// <summary>
	/// The editor is shutting down: every view's swap chain goes while its window is alive.
	/// </summary>
	internal static void ShutdownRendering()
	{
		foreach ( var window in views.Keys.ToArray() )
			Release( window );

		bitmapCamera?.Dispose();
		bitmapCamera = null;
	}
}
