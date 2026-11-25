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
/// their changes before each frame (<see cref="Poll"/>). docs/reference/borealis-gpu-path.md, the editor.
/// </summary>
internal static partial class ToolSceneViews
{
	/// <summary>
	/// <c>ToolSceneManagedRender_t</c> (src/public/toolscene/toolscene.h), as native lays it out.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct Request
	{
		public const int Version = 7;
		public const int MaxWorlds = 16;
		public const int MaxSkyboxWorlds = 4;

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

		/// <summary>
		/// A tool overlay object's geometry, batch by batch (<see cref="Geometry"/>): object, batch, out geometry, vertices or
		/// null, room for vertices. Returns the batch's vertex count, 0 past its last batch.
		/// </summary>
		public delegate* unmanaged< IntPtr, int, Geometry*, ToolVertex*, int, int > GetGeometry;

		/// <summary>An object's text (<see cref="Text"/>): object, out text. Returns 1 for a text object, 0 otherwise.</summary>
		public delegate* unmanaged< IntPtr, Text*, int > GetText;

		/// <summary>The managed handle of the view's world in window pixels (its screen-space renderables), or 0.</summary>
		public int ScreenSpaceWorld;

		/// <summary>The camera's debug mode (<see cref="SceneCameraDebugMode"/>): full bright for an unlit map, lit by default.</summary>
		public int ToolsVisMode;

		/// <summary>How the view draws (<see cref="Flags"/>).</summary>
		public Flags DrawFlags;
		public int Padding;

		/// <summary>The 3D skybox's worlds (managed handles), none for no skybox.</summary>
		public int SkyboxWorldCount;
		public fixed int SkyboxWorlds[MaxSkyboxWorlds];

		/// <summary>Where the skybox is seen from for this view (native's <c>Compute3DSkyboxFrustum</c>), and its scale.</summary>
		public Vector3 SkyboxOrigin;
		public Angles SkyboxAngles;
		public float SkyboxScale;

		/// <summary>Gradient fog, when set: <c>GradientFogParams</c>, <c>GradientFogParams2</c>, <c>GradientFogParams3</c>.</summary>
		public int GradientFog;
		public fixed float GradientFogParams[12];

		/// <summary>
		/// Cubemap fog, when set: start, end, LOD bias, falloff exponent, height width, height start, height exponent; its
		/// entity's origin and angles; its texture's resource name (UTF-8).
		/// </summary>
		public int CubemapFog;
		public fixed float CubemapFogParams[7];
		public Vector3 CubemapFogOrigin;
		public Angles CubemapFogAngles;
		public int Padding2;
		public IntPtr CubemapFogTexture;

		/// <summary>Volumetric fog, when set: anisotropy, scattering, draw distance, fade in start and end, indirect strength.</summary>
		public int VolumetricFog;
		public fixed float VolumetricFogParams[6];
		public int Padding3;

		/// <summary>The view's tool HUD (<see cref="HudItem"/>): HUD, out items, room for items, width, height. Returns the count.</summary>
		public delegate* unmanaged< IntPtr, HudItem*, int, int, int, int > GetHud;
		public IntPtr Hud;

		/// <summary>
		/// <see cref="Flags.Raytraced"/>: the CPU path tracer's image (RGB floats, linear, row by row), its size and version (it
		/// moves on every write), the scale it's drawn with (the view's tonemap scalar) and its status text (UTF-8).
		/// </summary>
		public float* RaytracedImage;
		public int RaytracedWidth;
		public int RaytracedHeight;
		public int RaytracedVersion;
		public float RaytracedScale;
		public IntPtr StatusText;

		/// <summary>
		/// The scene's screen layer (a texture or bitmap asset's preview, <see cref="ToolScreenLayer"/>): its texture's resource
		/// name (UTF-8), or its pixels (BGRA8, their size and a version that moves when they change); the channel it shows
		/// and where it draws, in view pixels from the top left.
		/// </summary>
		public IntPtr OverlayTexture;
		public byte* OverlayPixels;
		public int OverlayWidth;
		public int OverlayHeight;
		public int OverlayVersion;
		public int OverlayChannel;
		public Vector4 OverlayRect;
	}

	/// <summary><c>ToolSceneManagedFlags_t</c>.</summary>
	[Flags]
	internal enum Flags
	{
		/// <summary>Lines only (Hammer's wireframe draw mode).</summary>
		Wireframe = 1,

		/// <summary>Outline the objects flagged for Hammer's selection stencil layers.</summary>
		SelectionOutline = 2,

		/// <summary>And outline where the selection's depth steps (Hammer's "use depth for selection outline").</summary>
		SelectionDepthOutline = 4,

		/// <summary>Grey out what isn't flagged for Hammer's enabled stencil layer (an instance is being edited).</summary>
		ActiveInstance = 8,

		/// <summary>Draw <see cref="Request.RaytracedImage"/> instead of the worlds (Hammer's baked lighting preview).</summary>
		Raytraced = 16,

		/// <summary>Draw the worlds' buffer objects as models of their draws (<see cref="BufferObjectModels"/>).</summary>
		BufferObjectModels = 32,
	}

	/// <summary>
	/// <c>ToolSceneText_t</c> (src/public/toolrenderutils/toolmatsysutils.h): a tool text object's text and where it's laid out.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct Text
	{
		public IntPtr Utf8;
		public Vector3 World;
		public Vector2 Mins;
		public Vector2 Maxs;
		public float MaxDistance;
		public Color32 Color;
		public int InWorld;
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
		public ToolSelection Selection;
		public ToolHud Hud;
		public ToolEnvironment Environment = new();
		public ToolRaytraced Raytraced = new();
		public ToolScreenLayer ScreenLayer = new();
	}

	static readonly Dictionary<IntPtr, View> views = new();

	// Read-backs share one camera
	static SceneCamera bitmapCamera;
	static ToolLights bitmapLights;
	static ToolOverlays bitmapOverlays;
	static ToolSelection bitmapSelection;
	static ToolHud bitmapHud;
	static ToolEnvironment bitmapEnvironment;
	static ToolRaytraced bitmapRaytraced;
	static ToolScreenLayer bitmapScreenLayer;

	/// <summary>
	/// The tool HUD read-backs draw: none by default, as native's (its multisampled read-back is resolved before its
	/// screen-space view draws, and a read-back's temporary view has a HUD of its own, empty). For checking a live view's HUD.
	/// </summary>
	internal static HudSource ReadBackHud { get; set; }

	/// <summary>
	/// Whether read-backs draw the HUD their request carries (a tool scene's own <c>CToolHud</c>): off by default, as
	/// <see cref="ReadBackHud"/>. For checks.
	/// </summary>
	internal static bool ReadBackRequestHud { get; set; }

	static ulong frame;

	/// <summary>
	/// Draw a native view's frame (<c>RenderToolScene</c>). Sets <see cref="Request.Handled"/> when it drew it, or skipped it
	/// on purpose; native draws it when it's left clear.
	/// </summary>
	internal static unsafe void Render( IntPtr requestPointer ) => Render( requestPointer, null );

	/// <summary>
	/// Draw a native view's frame with <paramref name="managedWorlds"/> as well: worlds managed code made and keeps up to date
	/// itself (Hammer's map scene and gizmos), drawn as they are, not polled or searched for native overlays.
	/// </summary>
	internal static unsafe void Render( IntPtr requestPointer, IReadOnlyList<SceneWorld> managedWorlds, HudSource hud = null )
	{
		if ( requestPointer == IntPtr.Zero ) return;
		ref var request = ref *(Request*)requestPointer;
		if ( request.RequestVersion != Request.Version ) return;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend gpu ) return;
		if ( request.Width < 1 || request.Height < 1 ) return;
		if ( request.WorldCount < 1 && (request.DrawFlags & Flags.Raytraced) == 0 ) return;

		try
		{
			if ( request.Window != IntPtr.Zero ) RenderLive( gpu, ref request, managedWorlds, hud );
			else RenderPixels( ref request, managedWorlds, ReadBackHud );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Tool scene view on Borealis.Gpu: {e.Message}" );
		}
	}

	// A live view is being drawn: from its window's update to its present
	static bool drawingLive;

	/// <summary>Live views asked for while another was being drawn, and left undrawn (<see cref="RenderLive"/>), for checks.</summary>
	internal static int NestedSkipped { get; private set; }

	static void RenderLive( BorealisGpuRenderBackend gpu, ref Request request, IReadOnlyList<SceneWorld> managedWorlds, HudSource hud )
	{
		// A view asked for inside another frame's block waits for its own frame: drawing it would nest blocks
		if ( Graphics.IsActive )
		{
			request.Handled = 1;
			return;
		}

		// Or asked for while another view is being drawn, outside its blocks: a window message answered while the main thread
		// waits in that frame (the window's update joining the frame thread, the scene's prepare joining its workers) draws a
		// view, Hammer's map view drawing in its WM_PAINT. Drawing it would acquire its window's image again before the outer
		// frame presented it ("the last acquired image wasn't presented"), or start a frame inside the outer one. It's left
		// undrawn and not handled: native asks again
		if ( drawingLive )
		{
			NestedSkipped++;
			return;
		}

		drawingLive = true;
		try
		{
			DrawLive( gpu, ref request, managedWorlds, hud );
		}
		finally
		{
			drawingLive = false;
		}
	}

	static void DrawLive( BorealisGpuRenderBackend gpu, ref Request request, IReadOnlyList<SceneWorld> managedWorlds, HudSource hud )
	{
		if ( !views.TryGetValue( request.Window, out var view ) )
		{
			var window = BorealisGpuWindow.FromNativeWindow( gpu, request.Window, new Vector2( request.Width, request.Height ), vsync: false );
			if ( window is null ) return;

			window.Samples = Graphics.RenderMultiSampleToNum( NativeEngine.RenderDeviceManager.GetBestMultisampleType( RenderSettings.Instance.AntiAliasQuality.ToEngine() ) );
			view = new View { Window = window, Camera = NewCamera( "Tool scene view" ) };
			views[request.Window] = view;
		}

		var size = new Vector2( request.Width, request.Height );
		if ( view.Overlays is null )
		{
			view.Overlays = new ToolOverlays( view.Lights.World );
			view.Camera.OnRenderOverlay = view.Overlays.DrawTextAction( view.Camera );
		}

		if ( view.Selection is null )
		{
			view.Selection = new ToolSelection();
			view.Hud = new ToolHud();
			view.Camera.OnRenderStageHook = StageHook( view.Selection, view.Hud, view.Raytraced, view.ScreenLayer );
		}

		Setup( view.Camera, view.Lights, view.Overlays, view.Selection, view.Environment, view.Raytraced, ref request, managedWorlds );
		view.ScreenLayer.Update( ref request );
		view.Hud.Gather( hud, ref request, true );
		request.Handled = 1;

		if ( !view.Window.Update( size ) ) return;
		if ( !view.Window.BeginFrame( view.Camera.BackgroundColor ) ) return;

		try
		{
			view.Camera.AddToRenderList( default, size );
		}
		finally
		{
			// Presented even when drawing threw: an image left acquired stops the window for good
			view.Window.EndFrame( NoOverlay, screenshots: false );
			view.Window.PresentFrame();
		}
	}

	static readonly Action NoOverlay = () => { };

	/// <summary>
	/// A view camera's stages: the raytraced preview's image and the screen layer, the selection's (<see cref="ToolSelection.OnStage"/>), then the
	/// preview's status and the HUD over everything.
	/// </summary>
	static Action<Stage, SceneCamera> StageHook( ToolSelection selection, ToolHud hud, ToolRaytraced raytraced, ToolScreenLayer screenLayer ) => ( stage, camera ) =>
	{
		if ( stage == Stage.AfterPostProcess )
		{
			raytraced.DrawImage();
			screenLayer.Draw();
		}

		selection.OnStage( stage, camera );
		if ( stage == Stage.AfterUI )
		{
			raytraced.DrawStatus();
			hud.Draw();
		}
	};

	static unsafe void RenderPixels( ref Request request, IReadOnlyList<SceneWorld> managedWorlds, HudSource hud )
	{
		var format = (PixelFormat)request.PixelFormat;
		if ( request.Pixels == null || format == PixelFormat.None ) return;

		bitmapCamera ??= NewCamera( "Tool scene read-back" );
		bitmapLights ??= new ToolLights();
		if ( bitmapOverlays is null )
		{
			bitmapOverlays = new ToolOverlays( bitmapLights.World );
			bitmapCamera.OnRenderOverlay = bitmapOverlays.DrawTextAction( bitmapCamera );
		}

		bitmapRaytraced ??= new ToolRaytraced();
		bitmapScreenLayer ??= new ToolScreenLayer();
		if ( bitmapSelection is null )
		{
			bitmapSelection = new ToolSelection();
			bitmapHud = new ToolHud();
			bitmapCamera.OnRenderStageHook = StageHook( bitmapSelection, bitmapHud, bitmapRaytraced, bitmapScreenLayer );
		}

		bitmapEnvironment ??= new ToolEnvironment();
		Setup( bitmapCamera, bitmapLights, bitmapOverlays, bitmapSelection, bitmapEnvironment, bitmapRaytraced, ref request, managedWorlds );
		bitmapScreenLayer.Update( ref request );

		// A read-back's view is a new one each time, its HUD's images numbered afresh
		bitmapHud.Clear();
		bitmapHud.Gather( hud, ref request, ReadBackRequestHud );

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
	static unsafe void Setup( SceneCamera camera, ToolLights lights, ToolOverlays overlays, ToolSelection selection, ToolEnvironment environment, ToolRaytraced raytraced, ref Request request, IReadOnlyList<SceneWorld> managedWorlds )
	{
		// The raytraced preview draws its image in place of the worlds: only the screen-space tool layers go over it
		raytraced.Update( ref request );
		if ( raytraced.Active ) managedWorlds = null;

		var count = raytraced.Active ? 0 : Math.Min( request.WorldCount, Request.MaxWorlds );
		camera.Worlds.Clear();
		camera.World = null;
		camera.World = lights.World;
		lights.BeginFrame();
		overlays.BeginFrame();
		selection.BeginFrame( request.DrawFlags );
		for ( int i = 0; i < count; i++ )
		{
			var world = Sandbox.HandleIndex.Get<SceneWorld>( request.Worlds[i] );
			if ( !world.IsValid() ) continue;

			Poll( world );
			if ( (request.DrawFlags & Flags.BufferObjectModels) != 0 ) BufferObjectModels.Give( world );
			lights.Mirror( world );
			selection.Gather( world );
			if ( request.GetGeometry != null ) overlays.Gather( world, request.GetGeometry );
			if ( request.GetText != null ) overlays.GatherText( world, request.GetText );
			camera.Worlds.Add( world );
		}

		if ( managedWorlds is not null )
		{
			foreach ( var world in managedWorlds )
			{
				if ( !world.IsValid() ) continue;

				lights.Mirror( world );
				camera.Worlds.Add( world );
			}
		}
		lights.EndFrame();

		// The screen-space world: its lines, triangles and text are in window pixels
		if ( request.ScreenSpaceWorld != 0 && Sandbox.HandleIndex.Get<SceneWorld>( request.ScreenSpaceWorld ) is { } screenSpace && screenSpace.IsValid() )
		{
			Poll( screenSpace );
			overlays.BeginScreenSpace();
			if ( request.GetGeometry != null ) overlays.Gather( screenSpace, request.GetGeometry, selection );
			if ( request.GetText != null ) overlays.GatherText( screenSpace, request.GetText );
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
		var mode = (SceneCameraDebugMode)request.ToolsVisMode;
		if ( camera.DebugMode != mode ) camera.DebugMode = mode;
		var wireframe = (request.DrawFlags & Flags.Wireframe) != 0;
		if ( camera.WireframeMode != wireframe ) camera.WireframeMode = wireframe;
		environment.Apply( camera, lights.World, ref request );
		overlays.PlaceScreenSpace( camera, new Vector2( request.Width, request.Height ) );
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
		var change = SceneObjectChange.Transform | SceneObjectChange.Bones | SceneObjectChange.Visibility | SceneObjectChange.Tint
			| SceneObjectChange.Settings | SceneObjectChange.Bounds;
		if ( frame % ModelPollFrames == 0 ) change |= SceneObjectChange.Model | SceneObjectChange.Material | SceneObjectChange.Flags;

		// Written by this view as it draws, not at the sync point, where native may have destroyed objects since
		world.WrittenByItsViews = true;
		var listening = world.Render is not null;
		lock ( world.InternalSceneObjects )
		{
			foreach ( var sceneObject in world.InternalSceneObjects )
			{
				// Visibility and tint as native has them now, not as they were first read
				sceneObject.ForgetNativeState();
				if ( listening ) sceneObject.NotifyChanged( change );
			}
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
		view.Selection?.Dispose();
		view.Hud?.Dispose();
		view.Environment.Dispose();
		view.Raytraced.Dispose();
		view.ScreenLayer.Dispose();
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
		bitmapSelection?.Dispose();
		bitmapSelection = null;
		bitmapHud?.Dispose();
		bitmapHud = null;
		bitmapEnvironment?.Dispose();
		bitmapEnvironment = null;
		bitmapRaytraced?.Dispose();
		bitmapRaytraced = null;
		bitmapScreenLayer?.Dispose();
		bitmapScreenLayer = null;
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
		readonly SceneCustomObject lit, unlit, noDepth;

		enum Pass { Lit, Unlit, NoDepth }

		struct Batch
		{
			public Material Material;
			public int Start;
			public int Count;
			public Graphics.PrimitiveType Primitive;
			public float LineThickness;
			public Pass Pass;
			public Combos Combos;
			public float PatternType;
		}

		/// <summary>The tool material combos native draws a vertex list with (<c>s_ToolMaterialAttributes</c>).</summary>
		[Flags]
		enum Combos : byte
		{
			ThickLines = 1,
			DepthBias = 2,
			NoCulling = 4,
			Shaded = 8,
			SnapToScreen = 16,
		}

		static readonly StringToken ThickLinesCombo = "D_THICK_LINES";
		static readonly StringToken DepthBiasCombo = "D_DEPTH_BIAS";
		static readonly StringToken NoCullingCombo = "D_NO_CULLING";
		static readonly StringToken ShadedCombo = "D_SHADED";
		static readonly StringToken SnapCombo = "D_SNAP_TO_SCREEN_PIXELS";
		static readonly StringToken PatternTypeName = "PatternType";
		static readonly StringToken LineThicknessName = "g_LineThickness";
		static readonly StringToken PointSizeName = "UniformPointSize";
		static readonly StringToken TangentFrameName = "UncompressedTangentFrame";

		static Combos ReadCombos( IntPtr nativeAttributes, out float patternType )
		{
			patternType = 0;
			if ( nativeAttributes == IntPtr.Zero ) return 0;

			NativeEngine.CRenderAttributes native = nativeAttributes;
			Combos combos = 0;
			if ( native.GetComboValue( ThickLinesCombo, 0 ) != 0 ) combos |= Combos.ThickLines;
			if ( native.GetComboValue( DepthBiasCombo, 0 ) != 0 ) combos |= Combos.DepthBias;
			if ( native.GetComboValue( NoCullingCombo, 0 ) != 0 ) combos |= Combos.NoCulling;
			if ( native.GetComboValue( ShadedCombo, 0 ) != 0 ) combos |= Combos.Shaded;
			if ( native.GetComboValue( SnapCombo, 0 ) != 0 ) combos |= Combos.SnapToScreen;
			patternType = native.GetFloatValue( PatternTypeName, 0 );
			return combos;
		}

		readonly List<Batch> batches = new();
		ToolVertex[] vertices = new ToolVertex[4096];
		int vertexCount;
		readonly Dictionary<IntPtr, Material> materials = new();
		readonly RenderAttributes attributes = new();
		readonly RenderAttributes noDepthAttributes = new();

		public ToolOverlays( SceneWorld world )
		{
			lit = new SceneCustomObject( world ) { RenderOverride = _ => Draw( Pass.Lit ) };
			lit.Flags.IsOpaque = true;
			lit.Flags.IsTranslucent = false;
			lit.Flags.CastShadows = false;
			unlit = new SceneCustomObject( world ) { RenderOverride = _ => Draw( Pass.Unlit ) };
			unlit.Flags.IsOpaque = false;
			unlit.Flags.IsTranslucent = true;
			unlit.Flags.CastShadows = false;

			// The no-Z tool layers, over everything, as gizmos that ignore depth draw
			noDepth = new SceneCustomObject( world ) { RenderOverride = _ => Draw( Pass.NoDepth ) };
			noDepth.Flags.CastShadows = false;
			noDepth.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
			noDepthAttributes.SetCombo( "D_NO_ZTEST", 1 );
		}

		public void BeginFrame()
		{
			batches.Clear();
			vertexCount = 0;
			texts.Clear();
			screenSpaceBatch = -1;
		}

		// Where the screen-space world's batches start, -1 for none: they're gathered last, and drawn without depth
		int screenSpaceBatch = -1;

		/// <summary>What's gathered from here on is the screen-space world's, in window pixels with y up from the bottom.</summary>
		public void BeginScreenSpace() => screenSpaceBatch = batches.Count;

		/// <summary>
		/// Put the screen-space world's vertices in front of the camera where they cover the pixels native's screen-space view
		/// draws them at (<c>CToolCamera::GetScreenSpaceFrustum</c>: x right, y up, from the bottom left), so the view's
		/// camera draws them over the frame. A projection keeps straight lines straight, so each vertex goes alone.
		/// </summary>
		public void PlaceScreenSpace( SceneCamera camera, Vector2 size )
		{
			if ( screenSpaceBatch < 0 || screenSpaceBatch >= batches.Count ) return;

			var screen = new Vector3( size.x, size.y, 0 );
			var distance = camera.ZNear * 4;
			var first = batches[screenSpaceBatch].Start;
			for ( int i = first; i < vertexCount; i++ )
			{
				ref var vertex = ref vertices[i];
				var ray = camera.GetRay( new Vector2( vertex.Position.x, size.y - vertex.Position.y ), screen );
				vertex.Position = ray.Position + ray.Forward * distance;
			}

			var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( batches );
			for ( int i = screenSpaceBatch; i < span.Length; i++ )
				span[i].Pass = Pass.NoDepth;
		}

		// ToolRenderingLayer_t: up to TRANSLUCENT drawn before post processing, lit with the scene; from UNLIT_OPAQUE_NOZ on
		// (the no-Z, overlay and screen-space layers) without depth
		const int LastLitToolLayer = 3;
		const int FirstNoDepthToolLayer = 8;

		static Pass PassOf( int toolLayer ) => toolLayer >= FirstNoDepthToolLayer ? Pass.NoDepth : toolLayer <= LastLitToolLayer ? Pass.Lit : Pass.Unlit;

		/// <summary>
		/// Copy out the overlay geometry of <paramref name="world"/>'s objects. Hammer's selection overlay rectangles go to
		/// <paramref name="selection"/> instead (<see cref="ToolSelection.TakeOverlay"/>).
		/// </summary>
		public void Gather( SceneWorld world, delegate* unmanaged< IntPtr, int, Geometry*, ToolVertex*, int, int > getGeometry, ToolSelection selection = null )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( !Drawn( sceneObject ) ) continue;

					for ( int batch = 0; ; batch++ )
					{
						Geometry geometry;
						var count = getGeometry( sceneObject.native, batch, &geometry, null, 0 );
						if ( count <= 0 ) break;
						if ( geometry.MaterialName == IntPtr.Zero ) continue;

						var material = MaterialFor( geometry.MaterialName );
						if ( material is null ) continue;

						if ( vertexCount + count > vertices.Length ) Array.Resize( ref vertices, Math.Max( vertices.Length * 2, vertexCount + count ) );
						fixed ( ToolVertex* destination = &vertices[vertexCount] )
						{
							// What it wrote, which can be fewer than it asked room for
							count = getGeometry( sceneObject.native, batch, &geometry, destination, count );
						}
						if ( count <= 0 ) continue;
						if ( selection is not null && selection.TakeOverlay( sceneObject, vertices.AsSpan( vertexCount, count ) ) ) continue;

						batches.Add( new Batch
						{
							Material = material,
							Start = vertexCount,
							Count = count,
							Primitive = geometry.VertsPerPrimitive switch { 1 => Graphics.PrimitiveType.Points, 2 => Graphics.PrimitiveType.Lines, _ => Graphics.PrimitiveType.Triangles },
							LineThickness = geometry.LineThickness,
							Pass = PassOf( geometry.ToolLayer ),
							Combos = ReadCombos( geometry.Attributes, out var patternType ),
							PatternType = patternType,
						} );
						vertexCount += count;
					}
				}
			}
		}

		struct TextItem
		{
			public string Text;
			public Vector3 World;
			public Vector2 Mins;
			public Vector2 Maxs;
			public float MaxDistance;
			public Color Color;
			public bool InWorld;
		}

		readonly List<TextItem> texts = new();

		// Each text object's last string, made again only when its bytes change
		readonly Dictionary<IntPtr, (ulong Hash, string Text)> strings = new();

		/// <summary>
		/// Copy out the text of <paramref name="world"/>'s text objects (joint, hitbox and attachment names; tool labels).
		/// </summary>
		public void GatherText( SceneWorld world, delegate* unmanaged< IntPtr, Text*, int > getText )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( !Drawn( sceneObject ) ) continue;

					Text text;
					if ( getText( sceneObject.native, &text ) == 0 || text.Utf8 == IntPtr.Zero ) continue;

					var value = StringFor( sceneObject.native, (byte*)text.Utf8 );
					if ( value is null ) continue;

					texts.Add( new TextItem
					{
						Text = value,
						World = text.World,
						Mins = text.Mins,
						Maxs = text.Maxs,
						MaxDistance = text.MaxDistance,
						Color = text.Color.ToColor(),
						InWorld = text.InWorld != 0,
					} );
				}
			}
		}

		string StringFor( IntPtr owner, byte* utf8 )
		{
			// FNV-1a over the bytes: no string is made while they stay the same
			ulong hash = 14695981039346656037UL;
			int length = 0;
			for ( ; length < 4096 && utf8[length] != 0; length++ )
				hash = (hash ^ utf8[length]) * 1099511628211UL;
			if ( length == 0 ) return null;

			if ( strings.TryGetValue( owner, out var known ) && known.Hash == hash ) return known.Text;

			if ( strings.Count > 1024 ) strings.Clear();
			var text = System.Text.Encoding.UTF8.GetString( utf8, length );
			strings[owner] = (hash, text);
			return text;
		}

		/// <summary>
		/// What draws the frame's text over a camera's frame, once post processing is done (<see cref="SceneCamera.OnRenderOverlay"/>).
		/// </summary>
		public Action DrawTextAction( SceneCamera camera ) => () => DrawText( camera );

		/// <summary>
		/// Draw the text gathered for the frame over it, in pixels: world-anchored text at its anchor's projection, as the
		/// scene system's debug text draws (<c>CDebugTextObjectDesc::DrawArray</c>).
		/// </summary>
		void DrawText( SceneCamera camera )
		{
			foreach ( var text in texts )
			{
				var origin = Vector2.Zero;
				if ( text.InWorld )
				{
					if ( !camera.ToScreen( text.World, out var screen ) ) continue;
					if ( text.MaxDistance > 0 && camera.Position.DistanceSquared( text.World ) > text.MaxDistance * text.MaxDistance ) continue;
					origin = new Vector2( MathF.Floor( screen.x ), MathF.Floor( screen.y ) );
				}

				var size = text.Maxs - text.Mins;
				if ( size.y <= 0 ) continue;

				// Its laid out glyphs' box, a line each; the glyphs are about four fifths of a line
				var lines = 1;
				foreach ( var c in text.Text )
					if ( c == '\n' ) lines++;
				var line = size.y / lines;
				Graphics.DrawText( new Rect( origin + text.Mins, size + new Vector2( line, 0 ) ), text.Text, text.Color, "Roboto", line * 0.8f, 600, TextFlag.LeftTop );
			}
		}

		/// <summary><c>SCENEOBJECTFLAG_IS_DISABLED</c>: hidden by its owner (a tool helper's <c>SetVisible( false )</c>).</summary>
		const ulong DisabledFlag = 1UL << 60;

		/// <summary>A native-drawn object native draws this frame.</summary>
		static bool Drawn( SceneObject sceneObject ) => sceneObject.GetType() == typeof( SceneObject ) && !sceneObject.native.IsNull
			&& sceneObject.RenderingEnabled && ((ulong)sceneObject.native.GetFlags() & DisabledFlag) == 0;

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

		void Draw( Pass pass )
		{
			if ( vertexCount == 0 ) return;

			fixed ( ToolVertex* all = vertices )
			{
				foreach ( var batch in batches )
				{
					if ( batch.Pass != pass ) continue;

					// Lines thicker than a pixel (D_THICK_LINES) widen in the vertex stage, four vertices a line; points are quads
					// of their size (a point cloud's), likewise
					var drawAttributes = pass == Pass.NoDepth ? noDepthAttributes : attributes;
					drawAttributes.Set( LineThicknessName, batch.LineThickness );
					drawAttributes.Set( TangentFrameName, true );
					drawAttributes.SetCombo( ThickLinesCombo, (batch.Combos & Combos.ThickLines) != 0 );
					drawAttributes.SetCombo( DepthBiasCombo, (batch.Combos & Combos.DepthBias) != 0 );
					drawAttributes.SetCombo( NoCullingCombo, (batch.Combos & Combos.NoCulling) != 0 );
					drawAttributes.SetCombo( ShadedCombo, (batch.Combos & Combos.Shaded) != 0 );
					drawAttributes.SetCombo( SnapCombo, (batch.Combos & Combos.SnapToScreen) != 0 );
					drawAttributes.Set( PatternTypeName, batch.PatternType );
					if ( batch.Primitive == Graphics.PrimitiveType.Points ) drawAttributes.Set( PointSizeName, batch.LineThickness );
					Graphics.DrawInternal( all + batch.Start, batch.Count, null, 0, batch.Material, drawAttributes, batch.Primitive );
				}
			}
		}

		public void Dispose()
		{
			lit.Delete();
			unlit.Delete();
			noDepth.Delete();
			materials.Clear();
		}
	}

	/// <summary>
	/// Managed stand-ins for the lights and skies native made in a view's worlds (a tool's lighting rig and environment).
	/// Native makes lights through <c>CreateLight</c>, so their managed twins are plain <see cref="SceneLight"/>s, which
	/// Borealis doesn't draw: it knows a light by its managed type, and a sun only from a main world. Each frame every native
	/// light gets a <see cref="SceneDirectionalLight"/>, <see cref="ScenePointLight"/> or <see cref="SceneSpotLight"/> in the
	/// view's own world, the camera's main world, with the native light's settings. Only the last directional light the
	/// worlds have gets one, the sun, as native's binner keeps the last and skips the others (ModelDoc's default rig: the
	/// preview map's <c>light_environment</c> lights, the rig's own "Sun" doesn't). Envmaps and probe volumes need none: the
	/// extra worlds' light the views that draw them.
	/// </summary>
	internal sealed class ToolLights : IDisposable
	{
		public SceneWorld World { get; } = new();

		readonly Dictionary<SceneLight, SceneLight> proxies = new();
		readonly HashSet<SceneLight> seen = new();
		readonly List<SceneLight> gone = new();

		SceneDirectionalLight sun;

		// The last directional light the worlds had this frame
		SceneLight sunSource;

		public void BeginFrame()
		{
			sunSource = null;
			seen.Clear();
			seenSkies.Clear();
		}

		/// <summary>
		/// Stand-ins for <paramref name="world"/>'s native lights, made or brought up to date.
		/// </summary>
		public void Mirror( SceneWorld world )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject is SceneSkyBox sky )
					{
						MirrorSky( sky );
						continue;
					}

					if ( sceneObject is not SceneLight light || light.GetType() != typeof( SceneLight ) || !light.lightNative.IsValid ) continue;

					MirrorLight( light, (ShadowLightType)light.lightNative.GetLightType() );
				}
			}
		}

		/// <summary>
		/// A stand-in for a native light of <paramref name="kind"/>, made or brought up to date; the sun's waits for
		/// <see cref="EndFrame"/>. Other kinds (envmaps, probes) have none.
		/// </summary>
		internal void MirrorLight( SceneLight light, ShadowLightType kind )
		{
			if ( kind is not (ShadowLightType.Point or ShadowLightType.Spot or ShadowLightType.Directional) ) return;

			// The sun is the last directional light the worlds have, as native's light binner keeps the last it meets
			// (CLightBinnerStandard::GatherVisibleLights) and skips the others
			if ( kind == ShadowLightType.Directional )
			{
				sunSource = light;
				return;
			}

			if ( !proxies.TryGetValue( light, out var proxy ) || !proxy.IsValid() )
			{
				proxy = Spare( kind ) ?? kind switch
				{
					ShadowLightType.Spot => new SceneSpotLight( World, light.Position, light.LightColor ),
					_ => new ScenePointLight( World, light.Position, light.Radius, light.LightColor ),
				};
				proxies[light] = proxy;
			}

			seen.Add( light );
			Copy( light, proxy );
		}

		/// <summary>
		/// A stand-in for a native sky (the tool's environment world's): Borealis draws only the main world's sky.
		/// </summary>
		void MirrorSky( SceneSkyBox sky )
		{
			seenSkies.Add( sky );
			if ( !skies.TryGetValue( sky, out var proxy ) || !proxy.IsValid() )
			{
				var material = sky.Material;
				if ( material is null ) return;

				proxy = new SceneSkyBox( World, material );
				skies[sky] = proxy;
			}

			var tint = sky.SkyTint;
			if ( proxy.SkyTint != tint ) proxy.SkyTint = tint;
			if ( proxy.RenderingEnabled != sky.RenderingEnabled ) proxy.RenderingEnabled = sky.RenderingEnabled;
		}

		// Stand-ins no native light has now, switched off
		readonly List<SceneLight> spares = new();

		SceneLight Spare( ShadowLightType kind )
		{
			for ( int i = spares.Count - 1; i >= 0; i-- )
			{
				var spare = spares[i];
				if ( !spare.IsValid() ) { spares.RemoveAt( i ); continue; }
				if ( (spare is SceneSpotLight) != (kind == ShadowLightType.Spot) ) continue;
				spares.RemoveAt( i );
				return spare;
			}

			return null;
		}

		readonly Dictionary<SceneSkyBox, SceneSkyBox> skies = new();
		readonly HashSet<SceneSkyBox> seenSkies = new();
		readonly List<SceneSkyBox> goneSkies = new();

		static void Copy( SceneLight from, SceneLight to )
		{
			// Native's direction is the rotation's forward, negated for a directional light (SetWorldDirection)
			var direction = from.WorldDirection;
			if ( to is SceneDirectionalLight ) direction = -direction;
			// A native light keeps its place in its light desc, not its scene object's transform (Hammer's lights, the worklight)
			var position = from.lightNative.IsValid ? from.lightNative.GetWorldPosition() : from.Position;
			var transform = new Transform( position, direction.LengthSquared < 1e-6f ? from.Rotation : Rotation.LookAt( direction ) );
			if ( to.Transform != transform ) to.Transform = transform;
			if ( to.LightColor != from.LightColor ) to.LightColor = from.LightColor;
			if ( to.Radius != from.Radius ) to.Radius = from.Radius;
			if ( to.ConstantAttenuation != from.ConstantAttenuation ) to.ConstantAttenuation = from.ConstantAttenuation;
			if ( to.LinearAttenuation != from.LinearAttenuation ) to.LinearAttenuation = from.LinearAttenuation;
			if ( to.QuadraticAttenuation != from.QuadraticAttenuation ) to.QuadraticAttenuation = from.QuadraticAttenuation;
			if ( to.ShadowsEnabled != from.ShadowsEnabled ) to.ShadowsEnabled = from.ShadowsEnabled;
			if ( to.RenderingEnabled != from.RenderingEnabled ) to.RenderingEnabled = from.RenderingEnabled;

			// Then the rest of how native lights it, as native has it now (native changes its lights: a light entity edited,
			// Hammer's worklight following the camera): falloff, range, attenuation, cone, shadows
			to.CopyNativeDescription( from );
		}

		/// <summary>
		/// Switch off the stand-ins of native lights no world had this frame.
		/// </summary>
		public void EndFrame()
		{
			if ( sunSource is not null )
			{
				if ( !sun.IsValid() ) sun = new SceneDirectionalLight( World, Rotation.Identity, sunSource.LightColor );
				Copy( sunSource, sun );
			}
			else if ( sun.IsValid() )
			{
				sun.Delete();
				sun = null;
			}

			foreach ( var (light, proxy) in proxies )
			{
				if ( !seen.Contains( light ) ) gone.Add( light );
			}

			// Kept switched off for the next native light of their kind rather than deleted: a read-back's temporary view makes
			// its lights afresh each time, and a light deleted mid-frame lights on to the frame's end, as native's does
			foreach ( var light in gone )
			{
				proxies.Remove( light, out var proxy );
				if ( !proxy.IsValid() ) continue;
				proxy.RenderingEnabled = false;
				spares.Add( proxy );
			}

			gone.Clear();

			foreach ( var (sky, _) in skies )
			{
				if ( !seenSkies.Contains( sky ) ) goneSkies.Add( sky );
			}

			foreach ( var sky in goneSkies )
			{
				skies.Remove( sky, out var proxy );
				proxy?.Delete();
			}

			goneSkies.Clear();
		}

		public void Dispose()
		{
			foreach ( var proxy in proxies.Values ) proxy.Delete();
			foreach ( var spare in spares ) spare.Delete();
			spares.Clear();
			proxies.Clear();
			foreach ( var proxy in skies.Values ) proxy.Delete();
			skies.Clear();
			if ( sun.IsValid() ) sun.Delete();
			sun = null;
			World.Delete();
		}
	}
}
