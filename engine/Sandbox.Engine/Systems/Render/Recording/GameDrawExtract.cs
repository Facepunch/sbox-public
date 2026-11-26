using NativeEngine;
using System.Threading;
using ObjectFlags = Sandbox.Rendering.SceneObjectFlags;

namespace Sandbox.Rendering;

/// <summary>
/// A game's own custom objects (<see cref="SceneCustomObject.RenderSceneObject"/> overrides and <see cref="SceneCustomObject.RenderOverride"/>s)
/// drawn on the render thread without running game code there (docs/reference/borealis-render-thread.md): in the extract,
/// on the main thread, each one a drawn camera sees runs its drawing once per layer kind it draws in (opaque or
/// translucent, the depth prepass, shadows, the overlay), with <see cref="Graphics"/> set up as that camera's view and
/// recording (<see cref="Graphics.RecordingScope"/>, <see cref="RecordingRenderBackend"/>) instead of drawing. Where the
/// frame draws the object, on the render thread, the recording for the view's camera and the layer's kind is replayed
/// (<see cref="Draws"/>).
/// </summary>
/// <remarks>
/// <para>
/// Which kinds an object draws in for a camera (its slots) comes from its flags and from the frames that drew it: a frame
/// that asks for a recording that isn't there (a view the extract doesn't know, a kind it didn't predict) replays the same
/// kind's recording for another camera if there is one, else draws nothing, and the next extract records it. A custom
/// object new to the extract has its first frame drawn on the main thread, which runs its drawing as before and teaches
/// the extract its slots. Shadow kinds are recorded without a frustum test (shadow views see what the camera doesn't),
/// and so are views that aren't a drawn camera's.
/// </para>
/// <para>
/// The extract runs after the main thread has joined the render thread (<c>EngineLoop.OnClientOutput</c>), so the render
/// world is the main thread's while it runs (lighting reads its probe volumes), and a recording replayed in one frame is
/// recorded again only after that frame is drawn.
/// </para>
/// </remarks>
internal static class GameDrawExtract
{
	/// <summary>Record game custom objects in the extract; off (<c>BOREALIS_CUSTOMEXTRACT=0</c>), frames with one draw on the main thread.</summary>
	internal static bool Enabled { get; set; }

	static GameDrawExtract()
	{
		Enabled = System.Environment.GetEnvironmentVariable( "BOREALIS_CUSTOMEXTRACT" ) != "0";
	}

	/// <summary>What a recording's view answers a game's drawing with: what its block was when the slot last drew.</summary>
	internal struct ViewFacts
	{
		public bool Known;
		public RenderViewport Viewport;
		public bool PostProcessEnabled;
		public int ToolsVisMode;
		public int CubeFace;
		public bool IsCapture;
		public Texture DepthTexture;
		public Texture ColorTexture;
		public ImageFormat ColorFormat;
		public MultisampleAmount Msaa;
		public Borealis.RenderWorld LightingWorld;
		public ulong LightingSubWorlds;
	}

	/// <summary>A custom object's drawing for one camera (its attributes, the root view's host) and one layer kind.</summary>
	internal sealed class Slot
	{
		public RenderAttributes Camera;
		public SceneLayerType Layer;
		public bool Overlay;
		public readonly DrawRecording Recording = new();
		public long RecordedFrame = -1;
		public ViewFacts Facts;

		/// <summary>Whether a frame asked for it (not only predicted from the flags): recorded even when its world isn't the camera's.</summary>
		public bool Asked;

		/// <summary>The extract whose frame last asked for it.</summary>
		public long LastAsked = -1;

		/// <summary>
		/// Whether a frame asked for it lately: then it's recorded though the object is outside the camera's frustum (a shadow
		/// cast into view, a reflection), for longer for shadows, whose views come and go with the lights.
		/// </summary>
		public bool AskedLately( long now ) => LastAsked >= now - (Layer == SceneLayerType.Shadow ? 30 : 2);
	}

	/// <summary>A custom object's slots, kept on it (<see cref="SceneCustomObject.DrawSlots"/>).</summary>
	internal sealed class Slots
	{
		public readonly List<Slot> List = new();

		/// <summary>Whether a frame drawn on the main thread has run its drawing, so its slots are known.</summary>
		public bool Learned;

		/// <summary>The extract whose frame drew it on the main thread to learn its slots, or 0.</summary>
		public long LearnFrame;
	}

	// The extracts so far: a slot's recording replays only in the frame of the extract that recorded it
	static long frame;

	// Whether this frame's game custom objects are all recorded, and why not
	static bool covers;
	static string notCovered;

	// The objects with slots, and the game-drawn objects gathered this extract
	static readonly List<SceneCustomObject> tracked = new();
	static readonly List<SceneCustomObject> gathered = new();

	// The drawn cameras' keys this frame: a slot for another key is a view the extract doesn't know
	static readonly List<RenderAttributes> drawnKeys = new();

	static RecordingRenderBackend backend;
	static RecordingFrameView view;

	// What the frame being drawn is drawing a custom object for: the camera's key, and whether it's the overlay
	[ThreadStatic] static RenderAttributes drawingFor;
	[ThreadStatic] static bool drawingOverlay;

	// Whether the frame drawn on the main thread now teaches slots (the window's inline frames)
	static bool learning;

	static long recorded, replays, misses, fallbacks, syncPoints;
	static readonly HashSet<string> syncSites = new();

	static readonly Sandbox.Diagnostics.PerformanceStats.Timings ExtractTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis custom object extract" );

	/// <summary>Drawings recorded in extracts so far.</summary>
	internal static long Recorded => Interlocked.Read( ref recorded );

	/// <summary>Recordings replayed where a frame drew a custom object.</summary>
	internal static long Replays => Interlocked.Read( ref replays );

	/// <summary>Draws of a custom object on the render thread with no recording to replay: nothing drawn, recorded next extract.</summary>
	internal static long Misses => Interlocked.Read( ref misses );

	/// <summary>Draws that replayed the same kind's recording for another camera (a view the extract doesn't know).</summary>
	internal static long Fallbacks => Interlocked.Read( ref fallbacks );

	/// <summary>Synchronous readbacks a game's drawing made while recorded: each waits for the GPU on the main thread.</summary>
	internal static long SyncPoints => Interlocked.Read( ref syncPoints );

	/// <summary>Whether a drawing is being recorded on this thread.</summary>
	internal static bool IsRecording => Graphics.Recording is not null;

	/// <summary>
	/// Whether this frame's game custom objects need no main thread: all recorded by its extract (or none). Null when they
	/// do, with why (<see cref="RenderSync.GameDrawsInFrame"/>).
	/// </summary>
	internal static string NotCovered => covers ? null : notCovered ?? "a custom object drawn by game code";

	/// <summary>A new frame: nothing it draws is covered until its extract records it.</summary>
	internal static void BeginFrame()
	{
		covers = false;
		notCovered = null;
	}

	/// <summary>The window's frame draws on the main thread from now (<paramref name="value"/>) or no longer: it teaches slots.</summary>
	internal static void Learning( bool value ) => learning = value && Enabled;

	/// <summary>A synchronous readback in a recorded drawing: counted, each call site logged once.</summary>
	internal static void CountSyncPoint( string what )
	{
		Interlocked.Increment( ref syncPoints );
		var site = new System.Diagnostics.StackTrace( 2, false ).ToString();
		lock ( syncSites )
		{
			if ( !syncSites.Add( site ) ) return;
		}

		Log.Info( $"A custom object's drawing read back ({what}) while recorded in the extract: a sync point, at {site}" );
	}

	/// <summary>
	/// The extract's part for game custom objects, on the main thread after the cameras are prepared (<paramref name="cameras"/>,
	/// in order) and the render thread has drawn the last frame: last frame's recordings are let go, then, when the frame may
	/// draw on the render thread (<paramref name="record"/>), each game custom object a camera draws is recorded for it, kind
	/// by kind. Afterwards <see cref="NotCovered"/> says whether the frame
	/// may draw on the render thread.
	/// </summary>
	internal static void Extract( List<CameraComponent> cameras, bool record )
	{
		sceneCameras.Clear();
		foreach ( var camera in cameras )
		{
			if ( camera.IsValid() && camera.SceneCamera is { } sceneCamera ) sceneCameras.Add( sceneCamera );
		}

		try
		{
			Extract( sceneCameras, record );
		}
		finally
		{
			sceneCameras.Clear();
		}
	}

	static readonly List<SceneCamera> sceneCameras = new();

	/// <summary><see cref="Extract(List{CameraComponent}, bool)"/> for the scene cameras the frame draws, in order.</summary>
	internal static void Extract( List<SceneCamera> cameras, bool record )
	{
		ThreadSafe.AssertIsMainThread();
		frame++;
		Retire();
		covers = false;
		notCovered = null;
		if ( !Enabled || !record ) return;

		SceneCustomObject.CopyGameDrawn( gathered );
		if ( gathered.Count == 0 )
		{
			covers = true;
			return;
		}

		using var timing = ExtractTiming.Scope();
		try
		{
			drawnKeys.Clear();
			foreach ( var camera in cameras ) drawnKeys.Add( camera.Attributes );

			// An object a camera draws for the first time draws on the main thread this frame, teaching its slots
			var learn = false;
			foreach ( var custom in gathered )
			{
				if ( custom.DrawSlots is null )
				{
					custom.DrawSlots = new Slots();
					tracked.Add( custom );
				}

				var slots = custom.DrawSlots;
				if ( slots.Learned ) continue;
				if ( slots.LearnFrame > 0 && slots.LearnFrame < frame )
				{
					slots.Learned = true;
					continue;
				}

				if ( !DrawnByAny( cameras, custom ) ) continue;
				slots.LearnFrame = frame;
				learn = true;
			}

			if ( learn )
			{
				notCovered = "a new custom object";
				return;
			}

			backend ??= new RecordingRenderBackend();
			view ??= new RecordingFrameView();

			// Each view's recording, its cost in a zone of its own
			foreach ( var camera in cameras )
			{
				using ( global::Borealis.Zones.CustomExtract.Start() )
					RecordFor( camera );
			}

			// Views the extract doesn't know (a reflection's, a nested camera's) that asked: recorded from the first camera
			if ( cameras.Count > 0 ) RecordUnknownViews( cameras[0] );
			covers = true;
		}
		finally
		{
			gathered.Clear();
		}
	}

	/// <summary>Let go of what the last extract recorded, and of the objects gone.</summary>
	static void Retire()
	{
		for ( int i = tracked.Count - 1; i >= 0; i-- )
		{
			var custom = tracked[i];
			var slots = custom.DrawSlots;
			var gone = !custom.IsValid() || slots is null;
			if ( slots is not null )
			{
				foreach ( var slot in slots.List )
				{
					if ( slot.RecordedFrame < 0 ) continue;
					slot.Recording.Clear();
					slot.RecordedFrame = -1;
				}
			}

			if ( !gone ) continue;
			custom.DrawSlots = null;
			tracked[i] = tracked[^1];
			tracked.RemoveAt( tracked.Count - 1 );
		}
	}

	static bool DrawnByAny( List<SceneCamera> cameras, SceneCustomObject custom )
	{
		foreach ( var camera in cameras )
		{
			if ( DrawnBy( camera, custom ) ) return true;
		}

		return false;
	}

	// Whether the camera draws the object's world
	static bool DrawnBy( SceneCamera camera, SceneObject custom )
	{
		var world = custom.World;
		if ( world is null ) return false;
		return camera.World == world || (camera.Worlds?.Contains( world ) ?? false);
	}

	static void RecordFor( SceneCamera camera )
	{
		var key = camera.Attributes;
		SetCamera( view.Frustum, camera );
		var frustum = Graphics.FrustumOf( view.Frustum );

		foreach ( var custom in gathered )
		{
			var slots = custom.DrawSlots;
			var drawn = DrawnBy( camera, custom );
			if ( drawn ) Predict( slots, key, custom.CurrentFlags );

			// Infinite bounds (a custom object's until it sets its own) are always in view
			var bounds = custom.WorldBounds;
			var inView = bounds.Mins.x <= -float.MaxValue || frustum.IsInside( bounds, true );
			foreach ( var slot in slots.List )
			{
				if ( slot.Camera != key || slot.RecordedFrame == frame ) continue;
				if ( !drawn && !slot.Asked ) continue;
				if ( !inView && !slot.AskedLately( frame ) ) continue;
				Record( slot, custom, camera );
			}
		}
	}

	static void RecordUnknownViews( SceneCamera first )
	{
		SetCamera( view.Frustum, first );
		foreach ( var custom in gathered )
		{
			var slots = custom.DrawSlots.List;
			for ( int i = slots.Count - 1; i >= 0; i-- )
			{
				var slot = slots[i];
				if ( slot.RecordedFrame == frame || drawnKeys.Contains( slot.Camera ) ) continue;

				// A view no frame has asked for in a while (a camera gone) is forgotten
				if ( slot.LastAsked < frame - 60 )
				{
					slots.RemoveAt( i );
					continue;
				}

				Record( slot, custom, first );
			}
		}
	}

	// The slots the object's flags say it draws in for a camera, if it has none for it yet
	static void Predict( Slots slots, RenderAttributes key, ObjectFlags flags )
	{
		foreach ( var slot in slots.List )
		{
			if ( slot.Camera == key ) return;
		}

		if ( (flags & ObjectFlags.IsOpaque) != 0 ) slots.List.Add( new Slot { Camera = key, Layer = SceneLayerType.Opaque } );
		if ( (flags & ObjectFlags.IsTranslucent) != 0 ) slots.List.Add( new Slot { Camera = key, Layer = SceneLayerType.Translucent } );
		if ( (flags & ObjectFlags.CastShadows) == ObjectFlags.CastShadows ) slots.List.Add( new Slot { Camera = key, Layer = SceneLayerType.Shadow } );
	}

	static void SetCamera( CFrustum frustum, SceneCamera camera )
	{
		var size = camera.Size;
		var aspect = size.y > 0 ? size.x / size.y : 1;
		if ( camera.Ortho ) frustum.InitOrthoCamera( camera.Position, camera.Rotation.Angles(), camera.ZNear, camera.ZFar, camera.OrthoHeight * aspect, camera.OrthoHeight );
		else frustum.InitCamera( camera.Position, camera.Rotation.Angles(), camera.ZNear, camera.ZFar, camera.FieldOfView, aspect );
	}

	// What a slot's view says before it has drawn.
	static ViewFacts Defaults( SceneCamera camera ) => new()
	{
		Viewport = new RenderViewport( new Rect( 0, 0, camera.Size.x, camera.Size.y ), 0, 1 ),
		PostProcessEnabled = camera.EnablePostProcessing,
		CubeFace = -1,
		ColorFormat = ImageFormat.RGBA16161616F,
	};

	static void Record( Slot slot, SceneCustomObject custom, SceneCamera camera )
	{
		var recording = slot.Recording;
		var facts = slot.Facts.Known ? slot.Facts : Defaults( camera );
		view.Into = recording;
		view.Facts = facts;
		backend.Begin( recording, facts.Viewport );
		try
		{
			using var scope = Graphics.RecordingScope.Create( backend, view, recording, slot.Layer, facts.ColorFormat, facts.Msaa );
			custom.RecordInternal();
		}
		finally
		{
			backend.End();
			view.Into = null;
		}

		slot.RecordedFrame = frame;
		recorded++;
	}

	/// <summary>
	/// Around a custom object's drawing in a frame: the camera it's drawn for (the root view's host attributes) and whether
	/// it's the camera's overlay, which pick the recording a game custom object replays (<see cref="Draws"/>).
	/// </summary>
	internal static DrawingScope DrawingFor( RenderAttributes camera, bool overlay )
	{
		var scope = new DrawingScope( drawingFor, drawingOverlay );
		drawingFor = camera;
		drawingOverlay = overlay;
		return scope;
	}

	internal readonly ref struct DrawingScope( RenderAttributes camera, bool overlay )
	{
		public void Dispose()
		{
			drawingFor = camera;
			drawingOverlay = overlay;
		}
	}

	/// <summary>
	/// Draw a game custom object where a frame draws it, if it's done here: on the render thread its recording for this view
	/// and layer replays (or nothing draws, with none), and true; elsewhere false, and the caller runs its drawing (a frame
	/// on the main thread teaching its slots). Game code never runs on the render thread.
	/// </summary>
	internal static bool Draws( SceneCustomObject custom )
	{
		if ( !RenderSync.OnRenderThread )
		{
			if ( learning && drawingFor is not null ) Ask( custom, out _ );
			return false;
		}

		var exact = Ask( custom, out var other );
		var slot = exact is { RecordedFrame: var at } && at == frame ? exact : other;
		if ( slot is null )
		{
			Interlocked.Increment( ref misses );
			return true;
		}

		if ( slot != exact ) Interlocked.Increment( ref fallbacks );
		Interlocked.Increment( ref replays );
		slot.Recording.Replay();
		return true;
	}

	// The slot for this view and layer (made if there's none, and learned from the block it's drawn in), and another camera's
	// slot of the same kind recorded this frame
	static Slot Ask( SceneCustomObject custom, out Slot other )
	{
		other = null;
		var slots = custom.DrawSlots;
		if ( slots is null ) return null;

		var key = drawingFor;
		var layer = Graphics.LayerType;
		var overlay = drawingOverlay;
		Slot exact = null;
		foreach ( var slot in slots.List )
		{
			if ( slot.Layer != layer || slot.Overlay != overlay ) continue;
			if ( slot.Camera == key ) exact = slot;
			else if ( slot.RecordedFrame == frame ) other ??= slot;
		}

		if ( exact is null && key is not null )
		{
			exact = new Slot { Camera = key, Layer = layer, Overlay = overlay };
			slots.List.Add( exact );
		}

		if ( exact is not null )
		{
			exact.Asked = true;
			exact.LastAsked = frame;
			Learn( ref exact.Facts );
		}

		return exact;
	}

	// What the block the object is drawn in says, for its next recording's view
	static void Learn( ref ViewFacts facts )
	{
		facts.Known = true;
		facts.Viewport = Graphics.Backend.GetViewport();
		facts.ColorFormat = Graphics.IdealColorFormat;
		facts.Msaa = Graphics.IdealMsaaLevel;
		if ( Graphics.CurrentView is not BorealisGpuView gpu ) return;

		Graphics.IFrameView frameView = gpu;
		facts.PostProcessEnabled = gpu.PostProcessEnabled;
		facts.ToolsVisMode = gpu.ToolsVisMode;
		facts.CubeFace = gpu.CubeFace;
		facts.IsCapture = frameView.IsCapture;
		facts.DepthTexture = frameView.DepthTexture;
		facts.ColorTexture = frameView.ColorTexture;
		facts.LightingWorld = gpu.LightingWorld;
		facts.LightingSubWorlds = gpu.LightingSubWorlds;
	}

	/// <summary>Let go of every recording and slot, at shutdown or when the game goes.</summary>
	internal static void Shutdown()
	{
		Retire();
		foreach ( var custom in tracked ) custom.DrawSlots = null;
		tracked.Clear();
		backend = null;
		view?.Dispose();
		view = null;
	}

	[ConCmd( "r_custom_extract", Help = "How game custom objects were drawn on the render thread: recordings made in the extract, replays, misses, fallbacks and sync points" )]
	internal static void Print()
	{
		Log.Info( $"Custom object extract {(Enabled ? "on" : "off")}: {Recorded} recorded, {Replays} replayed, {Fallbacks} from another camera, {Misses} missed, {SyncPoints} sync points; {tracked.Count} objects tracked; last frame {(covers ? "covered" : NotCovered)}" );
	}
}
