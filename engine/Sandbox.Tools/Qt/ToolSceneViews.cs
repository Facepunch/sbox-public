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
		public const int Version = 2;
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

		/// <summary><c>GetToolSceneObjectGeometry</c>: a tool overlay object's geometry (<see cref="Geometry"/>).</summary>
		public delegate* unmanaged< IntPtr, Geometry*, ToolVertex*, int, int > GetGeometry;
	}

	/// <summary>
	/// <c>ToolSceneGeometry_t</c> (src/public/toolrenderutils/toolmatsysutils.h): a vertex list's draw.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct Geometry
	{
		public int VertsPerPrimitive;
		public int VertexCount;
		public int ToolLayer;
		public float LineThickness;
		public IntPtr MaterialName;
		public IntPtr Attributes;
	}

	/// <summary>
	/// <c>ColoredVertex_t</c>, the vertex every tool overlay draws with, in native's <c>g_ColoredVertexLayout</c>.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct ToolVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.TexCoord] public Vector3 TexCoord;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.Color] public Color32 Color;
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
		public ToolLights Lights = new();
		public ToolOverlays Overlays;
	}

	static readonly Dictionary<IntPtr, View> views = new();

	// Read-backs share one camera
	static SceneCamera bitmapCamera;
	static ToolLights bitmapLights;
	static ToolOverlays bitmapOverlays;

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
		view.Overlays ??= new ToolOverlays( view.Lights.World );
		Setup( view.Camera, view.Lights, view.Overlays, ref request );
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
		bitmapLights ??= new ToolLights();
		bitmapOverlays ??= new ToolOverlays( bitmapLights.World );
		Setup( bitmapCamera, bitmapLights, bitmapOverlays, ref request );

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
	/// Point a camera at a request's worlds, from its camera. Its main world is the view's light stand-ins
	/// (<see cref="ToolLights"/>); the native worlds are extra worlds of it.
	/// </summary>
	static unsafe void Setup( SceneCamera camera, ToolLights lights, ToolOverlays overlays, ref Request request )
	{
		var count = Math.Min( request.WorldCount, Request.MaxWorlds );
		camera.Worlds.Clear();
		camera.World = null;
		camera.World = lights.World;
		lights.BeginFrame();
		overlays.BeginFrame();
		for ( int i = 0; i < count; i++ )
		{
			var world = Sandbox.HandleIndex.Get<SceneWorld>( request.Worlds[i] );
			if ( !world.IsValid() ) continue;

			Poll( world );
			lights.Mirror( world );
			if ( request.GetGeometry != null ) overlays.Gather( world, request.GetGeometry );
			camera.Worlds.Add( world );
		}
		lights.EndFrame();

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
		view.Overlays?.Dispose();
		view.Lights.Dispose();
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
		bitmapOverlays?.Dispose();
		bitmapOverlays = null;
		bitmapLights?.Dispose();
		bitmapLights = null;
	}

	/// <summary>
	/// The tool overlays in a view's worlds (the lines, triangles and points <c>CToolRenderContext</c> drew: ground planes,
	/// grids, handles, gizmos), drawn by two custom objects in the view's own world: lit tool layers with the scene's opaque
	/// objects, unlit ones with its translucent ones. Native draws them with their own <c>DrawArray</c>, which Borealis
	/// doesn't run, so each frame their world-space vertices are copied out (<c>GetToolSceneObjectGeometry</c>) and drawn
	/// with their materials.
	/// </summary>
	internal sealed unsafe class ToolOverlays : IDisposable
	{
		readonly SceneCustomObject lit, unlit;

		struct Batch
		{
			public Material Material;
			public int Start;
			public int Count;
			public Graphics.PrimitiveType Primitive;
			public float LineThickness;
			public bool Lit;
		}

		readonly List<Batch> batches = new();
		ToolVertex[] vertices = new ToolVertex[4096];
		int vertexCount;
		readonly Dictionary<IntPtr, Material> materials = new();
		readonly RenderAttributes attributes = new();

		public ToolOverlays( SceneWorld world )
		{
			lit = new SceneCustomObject( world ) { RenderOverride = _ => Draw( true ) };
			lit.Flags.IsOpaque = true;
			lit.Flags.IsTranslucent = false;
			lit.Flags.CastShadows = false;
			unlit = new SceneCustomObject( world ) { RenderOverride = _ => Draw( false ) };
			unlit.Flags.IsOpaque = false;
			unlit.Flags.IsTranslucent = true;
			unlit.Flags.CastShadows = false;
		}

		public void BeginFrame()
		{
			batches.Clear();
			vertexCount = 0;
		}

		// Tool layers drawn before post processing, lit with the scene: ToolRenderingLayer_t up to TRANSLUCENT
		const int LastLitToolLayer = 3;

		/// <summary>
		/// Copy out the overlay geometry of <paramref name="world"/>'s objects.
		/// </summary>
		public void Gather( SceneWorld world, delegate* unmanaged< IntPtr, Geometry*, ToolVertex*, int, int > getGeometry )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject.GetType() != typeof( SceneObject ) || sceneObject.native.IsNull || !sceneObject.RenderingEnabled ) continue;

					Geometry geometry;
					var count = getGeometry( sceneObject.native, &geometry, null, 0 );
					if ( count <= 0 || geometry.MaterialName == IntPtr.Zero ) continue;

					var material = MaterialFor( geometry.MaterialName );
					if ( material is null ) continue;

					if ( vertexCount + count > vertices.Length ) Array.Resize( ref vertices, Math.Max( vertices.Length * 2, vertexCount + count ) );
					fixed ( ToolVertex* destination = &vertices[vertexCount] )
					{
						// What it wrote, which can be fewer than it asked room for
						count = getGeometry( sceneObject.native, &geometry, destination, count );
						if ( count <= 0 ) continue;
					}

					batches.Add( new Batch
					{
						Material = material,
						Start = vertexCount,
						Count = count,
						Primitive = geometry.VertsPerPrimitive switch { 1 => Graphics.PrimitiveType.Points, 2 => Graphics.PrimitiveType.Lines, _ => Graphics.PrimitiveType.Triangles },
						LineThickness = geometry.LineThickness,
						Lit = geometry.ToolLayer <= LastLitToolLayer,
					} );
					vertexCount += count;
				}
			}
		}

		Material MaterialFor( IntPtr name )
		{
			if ( materials.TryGetValue( name, out var material ) ) return material;

			// A material the tools made in code may not load from its name, and draws nothing
			var path = Marshal.PtrToStringUTF8( name );
			material = string.IsNullOrEmpty( path ) ? null : Material.Load( path );
			if ( material is not null && material.IsError ) material = null;
			materials[name] = material;
			return material;
		}

		void Draw( bool drawLit )
		{
			if ( vertexCount == 0 ) return;

			fixed ( ToolVertex* all = vertices )
			{
				foreach ( var batch in batches )
				{
					if ( batch.Lit != drawLit ) continue;

					attributes.Set( "g_LineThickness", batch.LineThickness );
					attributes.Set( "UncompressedTangentFrame", true );
					Graphics.DrawInternal( all + batch.Start, batch.Count, null, 0, batch.Material, attributes, batch.Primitive );
				}
			}
		}

		public void Dispose()
		{
			lit.Delete();
			unlit.Delete();
			materials.Clear();
		}
	}

	/// <summary>
	/// Managed stand-ins for the lights native made in a view's worlds (a tool's lighting rig). Native makes them through
	/// <c>CreateLight</c>, so their managed twins are plain <see cref="SceneLight"/>s, which Borealis doesn't draw: it
	/// knows a light by its managed type, and a sun only from a main world. Each frame every native light gets a
	/// <see cref="SceneDirectionalLight"/>, <see cref="ScenePointLight"/> or <see cref="SceneSpotLight"/> in the view's own
	/// world, the camera's main world, with the native light's settings.
	/// </summary>
	internal sealed class ToolLights : IDisposable
	{
		public SceneWorld World { get; } = new();

		readonly Dictionary<SceneLight, SceneLight> proxies = new();
		readonly HashSet<SceneLight> seen = new();
		readonly List<SceneLight> gone = new();

		public void BeginFrame() => seen.Clear();

		/// <summary>
		/// Stand-ins for <paramref name="world"/>'s native lights, made or brought up to date.
		/// </summary>
		public void Mirror( SceneWorld world )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject is not SceneLight light || light.GetType() != typeof( SceneLight ) || !light.lightNative.IsValid ) continue;

					var kind = (ShadowLightType)light.lightNative.GetLightType();
					if ( kind is not (ShadowLightType.Point or ShadowLightType.Spot or ShadowLightType.Directional) ) continue;

					if ( !proxies.TryGetValue( light, out var proxy ) || !proxy.IsValid() )
					{
						proxy = kind switch
						{
							ShadowLightType.Directional => new SceneDirectionalLight( World, Rotation.Identity, light.LightColor ),
							ShadowLightType.Spot => new SceneSpotLight( World, light.Position, light.LightColor ),
							_ => new ScenePointLight( World, light.Position, light.Radius, light.LightColor ),
						};
						proxies[light] = proxy;
					}

					seen.Add( light );
					Copy( light, proxy );
				}
			}
		}

		static void Copy( SceneLight from, SceneLight to )
		{
			// Native's direction is the rotation's forward, negated for a directional light (SetWorldDirection)
			var direction = from.WorldDirection;
			if ( to is SceneDirectionalLight ) direction = -direction;
			var transform = new Transform( from.Position, direction.LengthSquared < 1e-6f ? from.Rotation : Rotation.LookAt( direction ) );
			if ( to.Transform != transform ) to.Transform = transform;
			if ( to.LightColor != from.LightColor ) to.LightColor = from.LightColor;
			if ( to.Radius != from.Radius ) to.Radius = from.Radius;
			if ( to.ConstantAttenuation != from.ConstantAttenuation ) to.ConstantAttenuation = from.ConstantAttenuation;
			if ( to.LinearAttenuation != from.LinearAttenuation ) to.LinearAttenuation = from.LinearAttenuation;
			if ( to.QuadraticAttenuation != from.QuadraticAttenuation ) to.QuadraticAttenuation = from.QuadraticAttenuation;
			if ( to.ShadowsEnabled != from.ShadowsEnabled ) to.ShadowsEnabled = from.ShadowsEnabled;
			if ( to.RenderingEnabled != from.RenderingEnabled ) to.RenderingEnabled = from.RenderingEnabled;

			if ( to is SceneSpotLight spot )
			{
				var inner = from.lightNative.GetTheta();
				var outer = from.lightNative.GetPhi();
				if ( spot.ConeInner != inner ) spot.ConeInner = inner;
				if ( spot.ConeOuter != outer ) spot.ConeOuter = outer;
			}
		}

		/// <summary>
		/// Delete the stand-ins of native lights no world had this frame.
		/// </summary>
		public void EndFrame()
		{
			foreach ( var (light, proxy) in proxies )
			{
				if ( !seen.Contains( light ) ) gone.Add( light );
			}

			foreach ( var light in gone )
			{
				proxies.Remove( light, out var proxy );
				proxy?.Delete();
			}

			gone.Clear();
		}

		public void Dispose()
		{
			foreach ( var proxy in proxies.Values ) proxy.Delete();
			proxies.Clear();
			World.Delete();
		}
	}
}
