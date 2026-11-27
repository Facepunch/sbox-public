using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using Sandbox.Rendering;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GpuCommandList = Borealis.Gpu.CommandList;
using GpuDevice = Borealis.Gpu.Device;
using GpuFormat = Borealis.Gpu.Format;
using Num = System.Numerics;
using ProgramType = Borealis.Source2.Shaders.ShaderProgramType;
using RenderViewport = NativeEngine.RenderViewport;

namespace Sandbox;

/// <summary>
/// <see cref="Graphics"/> on a Borealis.Gpu device of its own: render blocks with no view (<see cref="Graphics.Scope.CreateOn"/>)
/// record into one of the device's command lists through Borealis.Backend's <see cref="GraphicsContext"/>, which tracks
/// layouts and opens rendering scopes itself, and draw with the engine's compiled shaders through
/// <see cref="ShaderDrawer"/>, bound by attribute name from managed attributes. What <c>Painter.Begin( Texture )</c>
/// needs runs here: CPU-vertex and instanced draws, clears, render targets, frame grabs, gaussian mips and compute; draws
/// from vertex and index buffers, direct or indirect, and models drawn with transforms, an instance count or indirect
/// arguments (BorealisGpuRenderBackend.Draws.cs); a mirrored scene object drawn again (<see cref="DrawObject"/>).
/// The rest throws <see cref="NotSupportedException"/> (docs/reference/borealis-gpu-path.md).
/// </summary>
/// <remarks>
/// Engine resources get a backing on this device the first time it uses them. Render targets are made empty with the
/// texture's size, format and mips; other textures come from their compiled files or the pixels they were made with;
/// buffers made while recording for this device start empty, and others start with what the device they were made for holds. A backing lives until its
/// resource is disposed, or until this backend is.
/// </remarks>
internal sealed partial class BorealisGpuRenderBackend : RenderBackend, IDisposable
{
	readonly GpuDevice gpu;
	readonly BackendDevice services;
	readonly ContentFiles files;
	readonly ShaderDrawer drawer;
	readonly GraphicsContext context;
	readonly bool ownsDevice;
	readonly IntPtr uploadKey;
	static int nextKey;

	readonly ConditionalWeakTable<Texture, Backing<TextureHandle>> textures = new();
	readonly ConditionalWeakTable<GpuBuffer, Backing<BufferHandle>> buffers = new();
	// Backings made here (not borrowed): destroyed when their texture is released, or collected without being released
	readonly HashSet<TextureHandle> textureOwners = new();

	// Owned backings whose texture or buffer was collected without being released (their finalizers), freed at the next frame
	readonly System.Collections.Concurrent.ConcurrentQueue<TextureHandle> collectedTextures = new();
	readonly System.Collections.Concurrent.ConcurrentQueue<BufferHandle> collectedBuffers = new();

	/// <summary>Owned backings freed after their texture or buffer was collected without being released, for soaks and tests.</summary>
	internal int CollectedBackingsFreed { get; private set; }
	readonly HashSet<BufferHandle> ownedBuffers = new();
	readonly Dictionary<SamplerState, (SamplerHandle Sampler, int Index)> samplers = new();
	readonly Dictionary<string, ShaderFile> shaders = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<uint, string> names = new();
	readonly ShaderAttributes lowered = new();

	GpuCommandList list;
	GpuCommandList lendNext;
	bool lent;
	readonly BufferHandle pipelineTextures;

	// SetRenderState's material, for the draw after it
	Material pendingMaterial;
	ShaderFile pendingShader;
	RenderAttributes pendingAttributes;

	sealed class Backing<T>( T handle )
	{
		public T Handle = handle;

		/// <summary>
		/// Set for a backing made here: where its handle goes when its engine resource is collected without being released (a
		/// GpuBuffer's or Texture's finalizer doesn't reach this backend), so the device memory goes with it.
		/// </summary>
		public System.Collections.Concurrent.ConcurrentQueue<T> Collected;

		~Backing() => Collected?.Enqueue( Handle );
	}

	/// <summary>Free owned backings whose engine textures and buffers were collected without being released.</summary>
	void FreeCollectedBackings()
	{
		while ( collectedBuffers.TryDequeue( out var buffer ) )
		{
			if ( !ownedBuffers.Remove( buffer ) ) continue;
			gpu.Destroy( buffer );
			CollectedBackingsFreed++;
		}

		if ( collectedTextures.IsEmpty ) return;
		lock ( textureOwners )
		{
			while ( collectedTextures.TryDequeue( out var texture ) )
			{
				if ( !textureOwners.Remove( texture ) ) continue;
				context.Forget( texture );
				gpu.Destroy( texture );
				CollectedBackingsFreed++;
			}
		}
	}

	/// <summary>
	/// A backend on a device of its own, introduced to the driver with Source 2's application and engine names so it gets
	/// the same driver profile (AMD's changes trilinear filtering otherwise). <paramref name="source2Names"/> false uses
	/// Borealis's, to show the difference. <paramref name="presentation"/> lets it present to windows; with
	/// <paramref name="frames"/> the first block of each engine frame starts the device's next frame, as the process's
	/// device does. <paramref name="markers"/> writes progress markers for the device-loss report at each list's ends
	/// (<see cref="DeviceOptions.ProgressMarkers"/>), and <paramref name="passMarkers"/> at each pass's too
	/// (<see cref="DeviceOptions.PassProgressMarkers"/>). <paramref name="pipelineCacheFolder"/> keeps the device's pipeline
	/// cache there (<see cref="DeviceOptions.PipelineCacheFolder"/>); with <paramref name="frames"/>, it's saved in the
	/// background every <see cref="PipelineCacheSaveFrames"/> frames when pipelines were made.
	/// </summary>
	public static BorealisGpuRenderBackend Create( bool validation = false, Action<DeviceMessage> message = null, bool source2Names = true, bool presentation = false, bool frames = false, bool markers = false, string pipelineCacheFolder = null, bool gpuAssisted = false, bool syncValidation = false, bool gpuAssistedDescriptors = false, bool passMarkers = false )
	{
		var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = source2Names ? "Sbox" : "Borealis",
			EngineName = source2Names ? "Source2" : "Borealis",
			Validation = validation || gpuAssisted || syncValidation || gpuAssistedDescriptors,
			// Bounds checks of buffer reads in the shaders, without the layer's in-shader descriptor lookups (they reset RDNA4)
			// unless asked for: they're what checks the layouts of the images bindless arrays read, which synchronization
			// validation skips
			GpuAssistedValidation = gpuAssisted || gpuAssistedDescriptors,
			SynchronizationValidation = syncValidation,
			GpuAssistedDescriptorChecks = gpuAssistedDescriptors,
			Presentation = presentation,
			ProgressMarkers = markers,
			PassProgressMarkers = passMarkers,
			// The process's device notes frames that stop (where the frame thread waited, how far the queues got)
			HangWatch = frames && Environment.GetEnvironmentVariable( "BOREALIS_HANGWATCH" ) != "0",
			PipelineCacheFolder = pipelineCacheFolder,
			Message = message,

			// The process's device takes what DLSS asks for, where the GPU has it (NVIDIA's), as Source 2's device does
			OptionalInstanceExtensions = frames ? Borealis.Backend.Upscalers.TemporalUpscaler.DlssInstanceExtensions() : null,
			OptionalDeviceExtensions = frames ? Borealis.Backend.Upscalers.TemporalUpscaler.DlssDeviceExtensions : null,
		} );

		gpu.Profiler = new TraceZones();
		return new BorealisGpuRenderBackend( gpu, ownsDevice: true ) { followsFrames = frames };
	}

	/// <summary>
	/// The device's own zones (pipeline and shader module creation) in the engine's profiler, while it records. On the render
	/// thread they're timed too: it should never make a pipeline or a shader module (they're made on workers and at loads),
	/// so each one it makes is counted (<see cref="RenderThreadCompiles"/>) and one over 20 ms is logged.
	/// </summary>
	sealed class TraceZones : IDeviceProfiler
	{
		// The render thread's open zones: when each began, or 0 for a zone that isn't a compile
		[ThreadStatic] static long[] started;
		[ThreadStatic] static int depth;

		public void BeginZone( nint name, uint color )
		{
			Sandbox.Utility.ProfilerZones.Begin( name, color );
			if ( !ThreadSafe.IsRenderThread ) return;

			started ??= new long[16];
			var compile = name == Borealis.Gpu.Device.PipelineZoneName || name == Borealis.Gpu.Device.ShaderModuleZoneName;
			if ( depth < started.Length ) started[depth] = compile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			depth++;
		}

		public void EndZone()
		{
			Sandbox.Utility.ProfilerZones.End();
			if ( !ThreadSafe.IsRenderThread || depth == 0 ) return;

			depth--;
			if ( depth >= started.Length || started[depth] == 0 ) return;
			var ms = System.Diagnostics.Stopwatch.GetElapsedTime( started[depth] ).TotalMilliseconds;
			System.Threading.Interlocked.Increment( ref renderThreadCompiles );
			RenderThreadCompileTiming.AddMillisecondsFromAnyThread( ms );
			if ( ms > SlowRenderThreadCompileMs ) Log.Warning( $"The render thread waited {ms:F1} ms making a pipeline or shader module, which it should never make: at {new System.Diagnostics.StackTrace( 2, false )}" );
		}
	}

	static long renderThreadCompiles;

	/// <summary>Pipelines and shader modules the render thread made (it should make none), for the tests and stats.</summary>
	internal static long RenderThreadCompiles => System.Threading.Interlocked.Read( ref renderThreadCompiles );

	const double SlowRenderThreadCompileMs = 20;
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings RenderThreadCompileTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis render thread compiles" );

	sealed class LoadingDraws
	{
		public int Unseen;
		public int Revealed;
	}

	static readonly ConditionalWeakTable<Scene, LoadingDraws> loadingDraws = new();

	/// <summary>The loading screen's warm-up render for this scene was submitted.</summary>
	internal static void MarkUnseenRendered( Scene scene )
	{
		if ( scene is not null && loadingDraws.TryGetValue( scene, out var draws ) )
			System.Threading.Interlocked.Increment( ref draws.Unseen );
	}

	/// <summary>The game's cameras drew this scene behind the loading screen.</summary>
	internal static void MarkRevealRendered( Scene scene )
	{
		if ( scene is not null && Revealing == scene && loadingDraws.TryGetValue( scene, out var draws ) )
			System.Threading.Interlocked.Increment( ref draws.Revealed );
	}

	/// <summary>
	/// Whether the game's scene is loading as the engine shows it: its loading tasks, the loading screen, or connecting.
	/// <c>Game.Render</c> draws the scene as usual otherwise. Not the game instance's own flag: nothing clears that when a
	/// game started without a scene load adds its map (a <c>MapInstance</c>), so its frames, on screen, kept loading budgets
	/// and native's envmap order, and its world was never counted as shown.
	/// </summary>
	internal static bool GameLoading( Scene scene ) => (scene?.IsLoading ?? false) || LoadingScreen.IsVisible || Networking.IsConnecting;

	/// <summary>The most frames it waits for an unseen render once the models are made (a scene with no camera has none).</summary>
	internal static int UnseenRenderWaitFrames { get; set; } = 8;

	const int ContentWaitLimitSeconds = 60;

	/// <summary>
	/// Once a scene's loading tasks are done: wait, behind the loading screen, until the game window's device has read and
	/// made the models and textures the scene's frames asked for (<see cref="BackendDevice.LoadingModels"/>,
	/// <see cref="BackendDevice.StreamingTextures"/>), and the shaders and pipelines its unseen renders asked for
	/// (<see cref="BackendDevice.CompilingShaders"/>), with a safety limit. Native's load makes its models before the first
	/// frame; without this a map drew its first seconds with models missing and textures at their tails, and after an update
	/// its first frame compiled the shaders the frame draws with.
	/// </summary>
	internal static async Task WaitForContent( Scene scene )
	{
		if ( (RenderBackend.Default as BorealisGpuRenderBackend)?.services is not { AsyncModels: true } services ) return;
		var draws = loadingDraws.GetOrCreateValue( scene );
		var deadline = TimeSpan.FromSeconds( ContentWaitLimitSeconds );
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		var made = false;
		var rendersAt = 0;
		ulong madeFrame = 0;
		while ( scene.IsValid )
		{
			if ( services.LoadingModels || services.CompilingShaders )
			{
				made = false;
			}
			else
			{
				if ( !made )
				{
					made = true;
					madeFrame = Application.FrameCount;
					rendersAt = System.Threading.Volatile.Read( ref draws.Unseen );
				}

				// Frames, not seconds: a loading frame can take half a second
				var rendered = System.Threading.Volatile.Read( ref draws.Unseen ) != rendersAt || Application.FrameCount - madeFrame >= (ulong)UnseenRenderWaitFrames;
				if ( rendered && !services.StreamingTextures ) break;
			}

			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ) >= deadline )
			{
				Log.Warning( $"Borealis content wait exceeded {ContentWaitLimitSeconds} seconds (LoadingModels: {services.LoadingModels}, CompilingShaders: {services.CompilingShaders}, StreamingTextures: {services.StreamingTextures})." );
				break;
			}

			await FrameStartWaiters.Next();
		}
	}

	/// <summary>
	/// Before a scene's load finishes in the game window on Borealis.Gpu, wait for the next frame's first tasks
	/// (<see cref="FrameStartWaiters.Next"/>), so it finishes before the game ticks in that frame. Its waits (for content, the
	/// navmesh) end in whichever frame's tasks run next, often the last ones, after the frame's render: the game's first tick
	/// and render then took a frame of their own after the loading screen (220-290 ms after a load on Flatgrass and
	/// Construct), where native's load finished in a frame's first tasks and had them in the frame it finished in.
	/// </summary>
	internal static Task BeforeLoadFinishes() => (RenderBackend.Default as BorealisGpuRenderBackend)?.services is { AsyncModels: true } ? FrameStartWaiters.Next() : Task.CompletedTask;

	/// <summary>
	/// Whether a game scene's load leaves its loading screen up until its first frames have drawn behind it
	/// (<see cref="RevealWhenDrawn(Scene)"/>): in the game window on Borealis.Gpu, with models made in the background.
	/// </summary>
	internal static bool HoldsReveal => RevealFrames > 0 && !Application.IsEditor && !Application.IsHeadless && !Application.IsUnitTest
		&& (RenderBackend.Default as BorealisGpuRenderBackend)?.services is { AsyncModels: true } && Sandbox.Engine.GameWindow.Current?.Borealis is not null;

	/// <summary>Frames the game ticks and draws behind the loading screen once its scene has loaded, at least (0: none, the screen goes with the load).</summary>
	internal static int RevealFrames { get; set; } = 2;

	/// <summary>The longest the loading screen stays up for them (<see cref="RevealWhenDrawn(Scene)"/>).</summary>
	internal static double RevealWaitSeconds { get; set; } = 3;

	/// <summary>
	/// Hide the loading screen once the game's first frames after its scene's load have drawn behind it: at least
	/// <see cref="RevealFrames"/> of them, the last two with no model being made and no shader or pipeline being compiled
	/// (<see cref="BackendDevice.LoadingModels"/>, <see cref="BackendDevice.CompilingShaders"/>), up to
	/// <see cref="RevealWaitSeconds"/>. The load ends with the player not spawned yet: the frames before then draw the scene
	/// from where its camera starts, without the player's view, its viewmodel and its HUD's objects, so the first frame shown
	/// made their pipelines, models and targets itself (seconds with an empty pipeline cache). Behind the screen the game
	/// ticks and draws as it will once the screen goes (<see cref="Revealing"/>: every camera, on the render thread), and what
	/// those draws ask for is made. The screen goes at a frame's start, so the game's tick and render in that frame are the
	/// first shown.
	/// Left alone when something else shows or hides the screen meanwhile.
	/// </summary>
	internal static Task RevealWhenDrawn( Scene scene ) => RevealWhenDrawn( scene, StillMaking );

	/// <summary>Whether the game window's device is still making models, shaders or pipelines its frames asked for.</summary>
	static bool StillMaking() => (RenderBackend.Default as BorealisGpuRenderBackend)?.services is { } services && (services.LoadingModels || services.CompilingShaders);

	/// <summary><see cref="RevealWhenDrawn(Scene)"/>, with what says the frames still make something given.</summary>
	internal static async Task RevealWhenDrawn( Scene scene, Func<bool> making )
	{
		var change = LoadingScreen.Changes;
		var draws = loadingDraws.GetOrCreateValue( scene );
		var last = System.Threading.Volatile.Read( ref draws.Revealed );
		var start = 0L;
		var drawn = 0;
		var ready = 0;
		revealing = scene;
		try
		{
			while ( drawn < RevealFrames || ready < 2 )
			{
				await FrameStartWaiters.Next();
				if ( LoadingScreen.Changes != change ) return;

				// Timed from the first frame after the load's: that one finishes the load (a second or two with an empty cache)
				if ( start == 0 ) start = System.Diagnostics.Stopwatch.GetTimestamp();
				if ( !scene.IsValid() || System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalSeconds >= RevealWaitSeconds ) break;
				var count = System.Threading.Volatile.Read( ref draws.Revealed );
				if ( count == last ) continue;

				drawn += count - last;
				last = count;
				ready = making() ? 0 : ready + 1;
			}

			LoadingScreen.IsVisible = false;
		}
		finally
		{
			if ( Revealing == scene ) revealing = null;
		}
	}

	/// <summary>
	/// The scene whose loaded frames draw behind the loading screen (<see cref="RevealWhenDrawn(Scene)"/>), or null. Its frames
	/// draw as the frames after the screen will (<c>Game.ExtractRender</c>: every camera, on the render thread), not as a
	/// loading frame does: the render thread's first frame of the game made its own targets and state, 60-110 ms on Construct
	/// on an RX 5700 XT, in the first frame shown.
	/// </summary>
	internal static Scene Revealing => revealing;
	static volatile Scene revealing;

	/// <summary>Validation errors the device has reported.</summary>
	public int ValidationErrors => gpu.ValidationErrors;

	bool followsFrames;
	ulong frame;

	/// <summary>
	/// Each engine frame is a device frame: starting one waits for the frame before last, then recycles what it used. Called
	/// before anything records in an engine frame: a block, an acquire, a scene render (which can come before either).
	/// </summary>
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings FrameStartTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu frame start" );

	internal void FollowFrame()
	{
		// Never inside a Borealis frame's recording (a block it lends): its lists would be left in the frame before. While the
		// render thread draws, a block lent is its frame's, not the main thread's, which joins it (it skipped the join before,
		// and drew a frame of its own over the render thread's)
		// A lost device is said and handled whatever was recording when it went
		BorealisGpuDeviceLost.Check( gpu );
		if ( !followsFrames ) return;
		if ( lendNext is not null && !Overlapping ) return;

		// Started ahead on the frame thread (HandOverFrame): joined, and finished here; that may be before this engine frame
		// ends, when something records in its end
		JoinFrameThread();
		if ( frame >= Application.FrameCount ) return;
		using var timing = FrameStartTiming.Scope();
		frame = Application.FrameCount;
		SubmitUploads();
		FreeModelGeometry();
		FreeCollectedBackings();
		LimitAnisotropy();
		BiasMips( Application.FrameCount );

		// Behind a loading screen nobody watches the frames: what a map loads is made and streamed with bigger budgets
		services.Loading = GameLoading( Sandbox.Engine.GlobalContext.Game?.ActiveScene );
		services.BeginFrame();
		TimeFrame();
		if ( frame % PipelineCacheSaveFrames == 0 ) SavePipelineCacheInBackground();
	}

	[ConVar( "r_borealis_forward_wave32", Help = "Use 32-lane subgroups for opaque Borealis surface shaders on supported AMD GPUs." )]
	internal static bool ForwardWave32
	{
		get => SurfaceShaders.ForwardWave32;
		set => SurfaceShaders.ForwardWave32 = value;
	}

	/// <summary>
	/// Native's <c>r_max_anisotropy</c> (its render device's, which Borealis.Gpu runs without): the texture quality
	/// profiles and the player's saved setting set it.
	/// </summary>
	[ConVar( "r_max_anisotropy", Saved = true, Min = 1, Max = 16, Help = "Clamp max anisotropic filtering to this maximum (1 disables anisotropic filtering)." )]
	internal static int MaxAnisotropy { get; set; } = 4;

	int anisotropyChanges = -1;

	/// <summary>
	/// Every sampler's anisotropy at most the player's <c>r_max_anisotropy</c> (native's default 4), as native clamps each
	/// anisotropic sampler to it; read again when a convar changed, before anything records in the frame.
	/// </summary>
	void LimitAnisotropy()
	{
		var changes = ConVarSystem.Changes;
		if ( changes == anisotropyChanges ) return;
		anisotropyChanges = changes;
		gpu.SetAnisotropyLimit( ConVarSystem.GetFloat( "r_max_anisotropy", 4, true ) );
	}

	float mipBias;
	ulong mipBiasFrame;

	/// <summary>
	/// A temporal upscaler running this frame asks for its texture mip bias (native's <c>SetTextureGlobalMipLODBias</c>):
	/// the samplers take it at the start of the next frame (<see cref="GpuDevice.SetGlobalMipBias"/>), as native's device
	/// rebuilds its samplers between frames.
	/// </summary>
	internal void RequestMipBias( float bias )
	{
		mipBias = bias;
		mipBiasFrame = Application.FrameCount;
	}

	/// <summary>
	/// The mip bias a temporal upscaler asked for within the last two frames, or none, as native drops its upscaler (and the
	/// bias) once no view has used it for two frames; before anything records in the frame.
	/// </summary>
	void BiasMips( ulong frameCount )
	{
		var bias = mipBiasFrame > 0 && frameCount - mipBiasFrame <= 2 ? mipBias : 0.0f;
		gpu.SetGlobalMipBias( bias );
	}

	// The GPU's busy time per device frame: a pass timer over every list (Borealis's and the engine's blocks), read back
	// once the GPU is past the frame. Someone else's timer (a lab's, a probe's) replaces it while it's set.
	Borealis.Backend.PassTimer busyTimer;
	List<Borealis.Backend.PassTimer.Timing> busyTimings = new(), publishedTimings = new();

	/// <summary>
	/// The lists and passes of the device frame <see cref="GpuBusyFrame"/>, in the order they began on the GPU: what the
	/// dev tools' GPU passes show (<see cref="Sandbox.Diagnostics.GpuProfilerStats"/>). Replaced as a later frame is read.
	/// </summary>
	internal IReadOnlyList<Borealis.Backend.PassTimer.Timing> GpuBusyTimings => publishedTimings;

	/// <summary>
	/// The GPU's busy time in a recent device frame, in milliseconds: every list's execution added up, the engine's blocks
	/// and Borealis's frames, without the time the GPU waited between them. Null without timestamps. As native's GPU frame time.
	/// </summary>
	public double? GpuBusyMs { get; private set; }

	/// <summary>Which device frame <see cref="GpuBusyMs"/> was (the engine frame it followed).</summary>
	public ulong GpuBusyFrame { get; private set; }

	/// <summary>
	/// Frames between reads of <see cref="GpuBusyMs"/> while nothing shows the passes; while the dev tools do
	/// (<see cref="Sandbox.Diagnostics.GpuProfilerStats.Enabled"/>), every frame.
	/// </summary>
	internal const int BusyReadFrames = 8;

	/// <summary>The first device frame the next read of <see cref="GpuBusyMs"/> may take (<see cref="NextBusyRead"/>).</summary>
	long busyReadFrom;

	/// <summary>
	/// The first frame a read after one of <paramref name="read"/> may take: the next while someone shows every frame's passes,
	/// else <see cref="BusyReadFrames"/> on. A read that finds its frame unfinished or its slot reused leaves this as it was,
	/// so the next frame tries again with a later frame rather than waiting out another interval.
	/// </summary>
	internal static long NextBusyRead( long read, bool everyFrame ) => read + (everyFrame ? 1 : BusyReadFrames);

	/// <summary>Held while <see cref="GpuBusyTimings"/> is swapped for a later frame's, and by whoever reads it.</summary>
	internal readonly System.Threading.Lock GpuBusyLock = new();

	void TimeFrame()
	{
		// The last frame's read has finished before its slot can be begun again
		FinishBusyRead();
		UpdateGpuBound();

		if ( busyTimer is null )
		{
			if ( services.PassTimer is not null || !gpu.Capabilities.Timestamps ) return;
			// -pass-stats: each pass also counts primitives and shader invocations; closes rendering at scope edges, so not for timing
			busyTimer = new Borealis.Backend.PassTimer( gpu, statistics: Sandbox.Utility.CommandLine.HasSwitch( "-pass-stats" ) );
		}

		services.PassTimer ??= busyTimer;
		if ( services.PassTimer != busyTimer ) return;

		busyTimer.BeginFrame( (long)frame );

		// The frame before last is done (starting this frame waited for it); older ones are read once, every frame while the
		// dev tools show the passes, every BusyReadFrames frames otherwise. The read goes to a worker: reading the queries back
		// takes 0.4 ms on some drivers. It reads a slot this frame doesn't write, and the next frame's start waits for it
		var done = (long)frame - (Borealis.Backend.PassTimer.Slots - 1);
		if ( done <= 0 || done < busyReadFrom ) return;
		busyReadFrame = done;
		busyReadCaller = Environment.CurrentManagedThreadId;
		busyReading = true;
		busyRead = JobDispatcher.Begin( 1, this, static ( backend, _ ) => backend.ReadBusy(), itemsPerBatch: 1 );
	}

	// The busy timer's read of a finished frame on a worker (TimeFrame), whether one was started, the frame it reads, and
	// whether it read it
	JobHandle busyRead;
	bool busyReading;
	long busyReadFrame;
	bool busyReadOk;
	int busyReadCaller;

	/// <summary>Busy timer reads that ran on a worker rather than the thread that started the frame, for the tests.</summary>
	internal long BusyReadsElsewhere { get; private set; }

	// On a worker: read the frame TimeFrame asked for and publish it
	void ReadBusy()
	{
		using var zone = global::Borealis.Zones.GpuTimingsRead.Start();
		if ( Environment.CurrentManagedThreadId != busyReadCaller ) BusyReadsElsewhere++;
		busyReadOk = busyTimer.TryRead( busyReadFrame, busyTimings );
		if ( !busyReadOk ) return;

		var busy = 0.0;
		foreach ( var timing in busyTimings )
		{
			if ( timing.Kind == Borealis.Backend.PassTimer.ScopeKind.List ) busy += timing.Ms;
		}

		lock ( GpuBusyLock )
		{
			GpuBusyMs = busy;
			GpuBusyFrame = (ulong)busyReadFrame;
			if ( counterTimeline is not null ) AddCounterTimeline( busyTimings );
			(busyTimings, publishedTimings) = (publishedTimings, busyTimings);
		}
	}

	/// <summary>
	/// Wait for the busy timer's read started last frame, if one was, and take when the next may be: before the timer begins
	/// another frame, and before it goes.
	/// </summary>
	void FinishBusyRead()
	{
		if ( !busyReading ) return;
		busyReading = false;
		var handle = busyRead;
		busyRead = default;
		handle.Join();

		// Every frame while the dev tools' GPU view or a GPU counter capture (r_gpucounters) reads them
		if ( busyReadOk ) busyReadFrom = NextBusyRead( busyReadFrame, Sandbox.Diagnostics.GpuProfilerStats.Enabled || counterTimeline is not null );
	}

	/// <summary>
	/// Whether the GPU is what limits recent frames, as async compute needs to pay: it overlaps the GPU's work, for two more
	/// submissions and a compute handoff on the CPU each frame (~0.03 ms in the editor on an RTX 3080). Bound once the GPU's
	/// busy time (<see cref="GpuBusyMs"/>: every list on either queue added up, about what it takes without the overlap)
	/// reaches 85% of the time between frames, or frame starts wait for the GPU for 10% of it (a GPU behind, vsync's pacing);
	/// no longer once those fall below 70% and 2%. True until a frame's busy time is read, and without timestamps.
	/// </summary>
	internal bool GpuBound { get; private set; } = true;

	long boundTicks, boundSlotWaits;
	double boundFrameMs, boundSlotWaitShare;
	ulong boundBusyFrame;

	void UpdateGpuBound()
	{
		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		var waits = gpu.FrameSlotWaitTicks;
		if ( boundTicks != 0 )
		{
			var ticks = now - boundTicks;
			var ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

			// A hitch (a load, a breakpoint) says nothing about steady frames
			if ( ms > 0 && ms < 100 )
			{
				var waitShare = (double)(waits - boundSlotWaits) / ticks;
				boundFrameMs = boundFrameMs == 0 ? ms : boundFrameMs + (ms - boundFrameMs) * 0.1;
				boundSlotWaitShare += (waitShare - boundSlotWaitShare) * 0.1;
			}
		}

		boundTicks = now;
		boundSlotWaits = waits;

		// Decided as each busy time is read (every BusyReadFrames frames), with hysteresis on both, so frames don't keep
		// switching between plans
		if ( GpuBusyMs is not { } busy || GpuBusyFrame == boundBusyFrame || boundFrameMs <= 0 ) return;
		boundBusyFrame = GpuBusyFrame;
		var busyShare = busy / boundFrameMs;
		GpuBound = GpuBound ? busyShare >= 0.7 || boundSlotWaitShare >= 0.02 : busyShare >= 0.85 || boundSlotWaitShare >= 0.1;
	}

	/// <summary>Frames between background saves of the pipeline cache.</summary>
	const ulong PipelineCacheSaveFrames = 900;

	int savingPipelineCache;

	/// <summary>Save the pipeline cache now, on a worker (a scene has loaded, as native flushes its own then).</summary>
	internal void FlushPipelineCache() => SavePipelineCacheInBackground();

	/// <summary>Save the device's pipeline cache on a worker if pipelines were made since the last save (and none is saving).</summary>
	void SavePipelineCacheInBackground()
	{
		if ( gpu.PipelineCachePath is null || System.Threading.Interlocked.Exchange( ref savingPipelineCache, 1 ) != 0 ) return;

		// A kept work item: this runs every PipelineCacheSaveFrames frames, mostly to find nothing new, and a task each time
		// allocated
		System.Threading.ThreadPool.UnsafeQueueUserWorkItem( pipelineCacheSaver ??= new PipelineCacheSaver( this ), preferLocal: false );
	}

	PipelineCacheSaver pipelineCacheSaver;

	sealed class PipelineCacheSaver( BorealisGpuRenderBackend backend ) : System.Threading.IThreadPoolWorkItem
	{
		public void Execute()
		{
			try
			{
				backend.gpu.SavePipelineCache();
				backend.services.SavePipelineManifest();
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Saving the pipeline cache failed: {e.Message}" );
			}
			finally
			{
				System.Threading.Volatile.Write( ref backend.savingPipelineCache, 0 );
			}
		}
	}

	/// <summary>
	/// Where the game's or the menu's mounted files have a compiled file on disk: a game package's models, materials,
	/// textures and shaders, which are named by their hash in the download cache.
	/// </summary>
	internal static string MountedFile( string path )
	{
		foreach ( var context in (Sandbox.Engine.GlobalContext[])[Sandbox.Engine.GlobalContext.Game, Sandbox.Engine.GlobalContext.Menu] )
		{
			if ( context?.FileMount is not { } mount ) continue;
			try
			{
				if ( mount.FileExists( path ) && mount.GetFullPath( path ) is { } full ) return full;
			}
			catch ( System.IO.IOException )
			{
				// A package mounting or unmounting while a worker looks: not found this time
			}
		}

		return null;
	}

	BorealisGpuRenderBackend( GpuDevice gpu, bool ownsDevice )
	{
		this.gpu = gpu;
		this.ownsDevice = ownsDevice;
		files = ContentFiles.FindGame();
		files.Resolve = MountedFile;
		files.ResolveSource = MountedSource;

		// Slang that game code compiled (Shader.CompileAsync), found by its name under compiled/
		System.IO.Directory.CreateDirectory( Sandbox.Engine.Shaders.GameShaders.SlangFolder );
		files.AddFallback( Sandbox.Engine.Shaders.GameShaders.SlangFolder );
		services = new BackendDevice( gpu, files );
		services.Shaders.Unloaded = message => Log.Warning( $"Borealis.Gpu: {message}" );
		services.Passes.Error = message => Log.Warning( $"Borealis.Gpu: {message}" );
		services.Passes.Info = message => Log.Info( message );
		services.Surfaces.Info = message => Log.Info( message );
		services.Surfaces.Error = message => Log.Warning( $"Borealis.Gpu: {message}" );
		services.Warn = message => Log.Warning( message );
		drawer = new ShaderDrawer( services );
		context = new GraphicsContext( gpu, drawer, null ) { Warn = message => Log.Warning( message ) };

		// The frame's pipeline textures (ambient occlusion's and reflections' results) by bindless index, 0 for none: what
		// Source 2's scene system keeps per frame (PipelineTextureSlot)
		pipelineTextures = gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage, Name = "Pipeline textures" } );
		gpu.WriteBuffer<uint>( pipelineTextures, [0, 0, 0, 0] );
		gpu.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, pipelineTextures );
		uploadKey = new IntPtr( -1 - System.Threading.Interlocked.Increment( ref nextKey ) );
		Register( this );
	}

	/// <summary>The device everything records on.</summary>
	public GpuDevice Device => gpu;

	/// <summary>The context blocks record through.</summary>
	public GraphicsContext Context => context;

	/// <summary>
	/// The renderer services draws bind through: the device's shaders, placeholders and material objects, shared with
	/// Borealis's scene on this device.
	/// </summary>
	public BackendDevice Services => services;

	/// <summary>Bind through material objects (the default); off binds by reflection every draw, for comparisons.</summary>
	public bool MaterialObjects
	{
		get => drawer.MaterialObjects;
		set => drawer.MaterialObjects = value;
	}

	/// <summary>Material objects made, sets written through them and held now, and draws that fell back to reflection.</summary>
	public (int Objects, int SetsWritten, int SetsHeld, int Fallbacks) MaterialObjectStats
	{
		get
		{
			var (objects, written, held) = drawer.MaterialObjectStats;
			return (objects, written, held, drawer.Fallbacks);
		}
	}

	//
	// Blocks and resources
	//

	public override RenderAttributes BeginBlock()
	{
		// Joined first: while the render thread draws, the block recording is its own
		FollowFrame();
		if ( list is not null ) throw new InvalidOperationException( "A Borealis.Gpu block is already recording." );

		TrackWorkerTextures();
		WriteWorkerUpdates();
		WriteSheets();
		// A block recording into the list of uploads (BeginUpdateWork) goes in it, not after it
		if ( !inUploads ) SubmitUploads();
		PollReadbacks();
		lent = lendNext is not null;
		list = lendNext ?? gpu.BeginCommandList( QueueType.Graphics, "Graphics block" );
		lendNext = null;
		try
		{
			presentTo = null;
			context.Begin( list );
			pendingShader = null;
			pendingAttributes = null;

			// Timed with Borealis's lists when a pass timer is on (diagnostics): the engine's own blocks, not lent ones
			if ( !lent && services.PassTimer is { } timer )
			{
				timer.ResetIn( list );
				blockTimer = timer.Begin( list, "Engine block", Borealis.Backend.PassTimer.ScopeKind.List );
			}
			else blockTimer = -1;

			// One set for every block, emptied as each starts: blocks don't overlap
			reusedBlockAttributes ??= RenderAttributes.CreateManaged();
			reusedBlockAttributes.Clear();
			return blockAttributes = reusedBlockAttributes;
		}
		catch
		{
			if ( !lent ) list.Discard();
			list = null;
			lent = false;
			throw;
		}
	}

	int blockTimer = -1;

	/// <summary>The pass timer the frame's lists are timed by, if any: engine scopes inside them time themselves with it.</summary>
	internal Borealis.Backend.PassTimer ScopeTimer => services.PassTimer;

	/// <summary>
	/// Time what the block being recorded records until <see cref="EndTimed"/> as a scope of the pass timer's own, named
	/// <paramref name="name"/> (a camera's stage, its output's copy, the window's overlay), so the engine's work after the scene
	/// shows by part with Borealis's passes. Nothing without a timer or a block.
	/// </summary>
	internal TimedScope BeginTimed( string name )
	{
		if ( list is null || services.PassTimer is not { } timer ) return default;
		return new TimedScope( list, timer.Begin( list, name, Borealis.Backend.PassTimer.ScopeKind.Own, count: false ) );
	}

	/// <summary>End a scope <see cref="BeginTimed"/> began, while its list is still the one recording.</summary>
	internal void EndTimed( TimedScope scope )
	{
		if ( scope.List is null || scope.Id < 0 || scope.List != list ) return;
		services.PassTimer?.End( list, scope.Id );
	}

	/// <summary>A scope <see cref="BeginTimed"/> began: its list and its pass timer scope.</summary>
	internal readonly record struct TimedScope( GpuCommandList List, int Id );

	/// <summary>
	/// After the last frame: drop what the last block and draw were given, which would hold their textures and material
	/// past their owners, and the sheet cache. Blocks remake the set as they start, and sheets are written again when asked for.
	/// </summary>
	internal void ReleaseFrameState()
	{
		pendingAttributes = null;
		pendingMaterial = null;
		pendingShader = null;
		blockAttributes = null;
		reusedBlockAttributes = null;
		drawer.ReleasePass();
		ReleaseModelAttributes();
		ReleaseSheets();
	}

	// The block's attributes, which a draw's own are layered over, as Source 2's contexts layer them
	RenderAttributes blockAttributes, reusedBlockAttributes;

	/// <summary>
	/// Record the next block into <paramref name="list"/>, which another recorder on the device is part way through (Borealis's
	/// frame, at a camera's stage), and leave it to that recorder to submit: the block's drawing lands at that point of it.
	/// </summary>
	internal void LendNextBlock( GpuCommandList list ) => lendNext = list;

	/// <summary>
	/// Draw with a lent frame's transform buffer as <c>g_TransformBuffer</c> (under the draws' own attributes) and its instance
	/// ids at <paramref name="offset"/> bytes (<see cref="ShaderDrawer.InstanceIds"/>), as a custom object inside Borealis's
	/// frame draws on Source 2. Default handles stop.
	/// </summary>
	internal void LendInstances( BufferHandle transforms, BufferHandle ids, long offset )
	{
		lentTransforms = transforms;
		drawer.InstanceIds = (ids, offset);
	}

	BufferHandle lentTransforms;

	/// <summary>The depth range draws map to (<see cref="ShaderDrawer.DepthRange"/>): a lent frame's, else 0 to 1.</summary>
	internal (float Min, float Max) DepthRange
	{
		get => drawer.DepthRange;
		set => drawer.DepthRange = value;
	}

	/// <summary>
	/// The shader mode draws take by name (<see cref="GraphicsContext.Mode"/>): <c>Depth</c> where a custom object draws into
	/// a shadow map or the depth prepass. Null draws each shader's first mode.
	/// </summary>
	internal string DrawMode
	{
		get => context.Mode;
		set => context.Mode = value;
	}

	/// <summary>The depth bias of draws into depth alone (<see cref="ShaderDrawer.DepthBias"/>): a shadow map's.</summary>
	internal (int Constant, float Slope) DepthBias
	{
		get => drawer.DepthBias;
		set => drawer.DepthBias = value;
	}

	/// <summary>Write a GPU timestamp in this block's list (<see cref="GraphicsContext.WriteTimestamp"/>).</summary>
	internal void WriteTimestamp( TimestampQueries queries, int index, PipelineStage stage, int reset = 0 )
	{
		_ = Recording;
		context.WriteTimestamp( queries, index, stage, reset );
	}


	/// <summary>
	/// Name <paramref name="texture"/> as the frame's pipeline texture in <paramref name="slot"/> (<c>PipelineTextureSlot</c>)
	/// for what draws after, in this block's list, as Source 2's scene system does for ambient occlusion's result.
	/// </summary>
	internal void SetPipelineTexture( int slot, Texture texture )
	{
		_ = Recording;
		Span<uint> index = [(uint)TextureIndex( texture )];
		if ( list.Queue == QueueType.Compute ) computeSlots |= 1 << slot;
		context.MemoryBarrier();
		context.UpdateBuffer( pipelineTextures, MemoryMarshal.AsBytes( index ), slot * 4 );
	}

	// Pipeline texture slots set on the async compute queue since the last clear (ambient occlusion, run early)
	int computeSlots;

	/// <summary>
	/// Name no pipeline textures for what draws after, in this block's list: what a frame starts with. Slots the async compute
	/// queue set for this frame (<see cref="SetPipelineTexture"/> in a compute block, before the frame's first stage) are left
	/// as they are: that queue may be writing them while this one clears.
	/// </summary>
	internal void ClearPipelineTextures()
	{
		_ = Recording;
		context.MemoryBarrier();
		if ( computeSlots == 0 ) context.UpdateBuffer( pipelineTextures, stackalloc byte[16], 0 );
		else
		{
			Span<byte> none = stackalloc byte[4];
			for ( int slot = 0; slot < 4; slot++ )
			{
				if ( (computeSlots & (1 << slot)) == 0 ) context.UpdateBuffer( pipelineTextures, none, slot * 4 );
			}
		}

		computeSlots = 0;
	}

	public override void EndBlock()
	{
		try
		{
			context.FinishReadable();
			if ( blockTimer >= 0 ) services.PassTimer?.End( list, blockTimer );
			blockTimer = -1;
			if ( inUploads )
			{
				// Read back once the uploads are submitted, with the work before and after it in their list
				uploadReadbacks.AddRange( blockReadbacks );
				blockReadbacks.Clear();
				uploads?.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
				written.Clear();
			}
			else
			{
				var finished = list;
				list = null;

				// Held for the next submission (the frame's present, or what waits for it), as Borealis's lists are, rather than a
				// vkQueueSubmit2 of its own: three of those cost ~0.04 ms a frame for a camera in a window
				var submitted = lent ? default : presentTo is null && services.BatchSubmits ? gpu.SubmitLater( finished ) : gpu.Submit( finished, default, presentTo );
				SubmittedReadbacks( blockReadbacks, submitted );
			}
		}
		finally
		{
			if ( !lent ) list?.Discard();
			inUploads = false;
			// A viewport's depth range set in a block of its own ends with it (a lent frame keeps its own)
			if ( !lent ) DepthRange = (0, 1);
			list = null;
			lent = false;
			presentTo = null;
			pendingAttributes = null;
			pendingMaterial = null;
			pendingLayout = null;
			blockAttributes = null;
			drawer.Material = null;
		}
	}

	// Whether the block recording records into the list of uploads (BeginUpdateWork)
	bool inUploads;

	/// <summary>Blocks the game asked for outside one, recorded with the uploads rather than submitted each, for the tests.</summary>
	internal int UpdateWorkRecorded { get; private set; }

	/// <summary>
	/// Work the game asks for outside a block (a dispatch, a copy, mips, painting into a texture) on the main thread: recorded
	/// into the list of uploads, after everything recorded there so far and before what's recorded next, in call order with
	/// the update's buffer writes, and submitted with them before the next block (the frame's first, a readback's), rather than
	/// in a block of its own submitted at once. A readback after it submits it first, as it submits uploads.
	/// </summary>
	internal override Graphics.Scope BeginUpdateWork()
	{
		FromMain();
		if ( list is not null || lendNext is not null || !ThreadSafe.IsMainThread ) return Graphics.Scope.CreateOn( this );

		var into = UploadList();
		into.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		written.Clear();
		lendNext = into;
		inUploads = true;
		UpdateWorkRecorded++;
		try
		{
			return Graphics.Scope.CreateOn( this );
		}
		catch
		{
			lendNext = null;
			inUploads = false;
			throw;
		}
	}

	SwapChain presentTo;
	readonly Dictionary<SwapChain, ulong> acquiredIn = new();

	/// <summary>
	/// Call before a swap chain acquires: a second acquire in one device frame (a window drawn twice in an engine frame,
	/// from a resize) starts the next device frame, since each frame slot has one acquire semaphore per swap chain.
	/// </summary>
	public void BeforeAcquire( SwapChain swapChain )
	{
		// The frame has to have started before the acquire, so the acquire's semaphore belongs to the frame whose submit
		// waits on it
		FollowFrame();
		if ( acquiredIn.TryGetValue( swapChain, out var last ) && last == gpu.FrameNumber )
		{
			SubmitUploads();
			services.BeginFrame();
		}

		acquiredIn[swapChain] = gpu.FrameNumber;
	}

	/// <summary>Forget a swap chain that's going away.</summary>
	public void Forget( SwapChain swapChain ) => acquiredIn.Remove( swapChain );

	/// <summary>
	/// How a game window presents with vsync off: <see cref="PresentMode.Immediate"/>, or what the environment variable
	/// <c>BOREALIS_NOVSYNC_MODE</c> asks for (<c>mailbox</c>, <c>latest</c> for <see cref="PresentMode.FifoLatestReady"/>, <c>fifo</c>),
	/// for comparing present modes in device-loss repros.
	/// </summary>
	internal static PresentMode NoVSyncMode { get; } = System.Environment.GetEnvironmentVariable( "BOREALIS_NOVSYNC_MODE" )?.ToLowerInvariant() switch
	{
		"mailbox" => PresentMode.Mailbox,
		"fifo" => PresentMode.Fifo,
		"latest" => PresentMode.FifoLatestReady,
		_ => PresentMode.Immediate,
	};

	/// <summary>
	/// A swap chain on a window, presenting with vsync or as soon as it can.
	/// </summary>
	public SwapChain CreateSwapChain( SurfaceSource surface, int width, int height, bool vsync ) => gpu.CreateSwapChain( new SwapChainInfo
	{
		Surface = surface,
		Width = width,
		Height = height,
		PresentMode = vsync ? PresentMode.Fifo : NoVSyncMode,
		Linear = true,
	} );

	/// <summary>
	/// Copy a texture into the image <paramref name="swapChain"/> acquired, last thing in the block, which then submits for
	/// that image; present it after the block ends.
	/// </summary>
	public void CopyToSwapChain( Texture source, SwapChain swapChain )
	{
		_ = Recording;
		context.CopyToPresent( TextureOf( source ), swapChain.Current );
		presentTo = swapChain;
	}

	/// <summary>
	/// Read a map's compiled content on this device, after the game's: the loose files of the package it's in and its VPK,
	/// with its 3D skybox's beside it, as the engine's search path has them once the map is mounted. Its world geometry,
	/// materials and textures then load here by path.
	/// </summary>
	/// <summary>The last map whose content was added (<see cref="AddMapContent"/>), not counting 3D skyboxes: <c>construct_25</c>.</summary>
	internal string LastMap { get; private set; }

	internal void AddMapContent( string vpk )
	{
		// A map's 3D skybox is a map of its own (<map>_sky), loaded after it: it isn't the map being played
		var mapName = System.IO.Path.GetFileNameWithoutExtension( vpk );
		if ( LastMap is null || !mapName.EndsWith( "_sky", StringComparison.OrdinalIgnoreCase ) ) LastMap = mapName;
		vpk = System.IO.Path.GetFullPath( vpk );
		var sky = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( vpk ), System.IO.Path.GetFileNameWithoutExtension( vpk ) + "_sky.vpk" );
		var added = mapPackages.Add( vpk );
		if ( added )
		{
			// <package>/maps/<map>.vpk
			if ( System.IO.Path.GetDirectoryName( System.IO.Path.GetDirectoryName( vpk ) ) is { } root && mapRoots.Add( root ) ) files.Add( root );
			AddPackage( vpk );

			if ( System.IO.File.Exists( sky ) && mapPackages.Add( sky ) ) AddPackage( sky );
		}

		// The map's lightmaps (and its 3D skybox's), read and decoded on a worker while the map loads, not on the frame that
		// first draws it. Every load asks: the lightmaps go when the last world drawing them does, so a map loaded again (a
		// reload, or a change back to it) reads them again, and nothing is read while they're still held.
		var map = System.IO.Path.GetFileNameWithoutExtension( vpk );
		services.ReadMapLightmapsAhead( $"maps/{map}" );
		if ( System.IO.File.Exists( sky ) ) services.ReadMapLightmapsAhead( $"maps/{map}_sky" );

		// The process's video memory as the map's world is made (both devices: Source 2 still loads the map's lightmaps)
		if ( added ) Log.Info( $"Borealis.Gpu memory at {System.IO.Path.GetFileName( vpk )}: {gpu.DescribeMemory()}{(services.DescribeStreaming() is { } streaming ? $"; {streaming}" : "")}" );
	}

	void AddPackage( string vpk )
	{
		var package = Borealis.Source2.Resources.VpkFile.Open( vpk );
		mapVpks.Add( package );
		files.AddPackage( package );
	}

	readonly List<Borealis.Source2.Resources.VpkFile> mapVpks = new();

	readonly HashSet<string> mapPackages = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> mapRoots = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Wait until everything submitted has finished.</summary>
	public void WaitIdle() => gpu.WaitIdle();

	public override IntPtr UploadContext => uploadKey;

	public override int TextureIndex( Texture texture ) => gpu.BindlessIndex( SampledTextureOf( texture ) );

	public override int SamplerIndex( SamplerState state )
	{
		// Asked for from more than one thread at once (UI painting among them)
		lock ( samplers )
		{
			if ( samplers.TryGetValue( state, out var made ) ) return made.Index;

			var info = state.Filter switch
			{
				FilterMode.Point => SamplerInfo.Point(),
				FilterMode.Bilinear => SamplerInfo.Linear() with { MipFilter = Filter.Nearest },
				FilterMode.Anisotropic => SamplerInfo.Linear( anisotropy: state.MaxAnisotropy ),
				_ => SamplerInfo.Linear(),
			};
			info.AddressU = Address( state.AddressModeU );
			info.AddressV = Address( state.AddressModeV );
			info.AddressW = Address( state.AddressModeW );
			info.MaxLod = 15;

			// Native's samplers from a SamplerState don't take the upscalers' global mip bias
			info.FixedMipBias = true;

			var sampler = gpu.CreateSampler( info );
			made = (sampler, gpu.SamplerIndex( sampler ));
			samplers[state] = made;
			return made.Index;
		}
	}

	static AddressMode Address( TextureAddressMode mode ) => mode switch
	{
		TextureAddressMode.Mirror or TextureAddressMode.MirrorOnce => AddressMode.Mirror,
		TextureAddressMode.Clamp => AddressMode.Clamp,
		TextureAddressMode.Border => AddressMode.Border,
		_ => AddressMode.Wrap,
	};

	public override void Release( object resource )
	{
		// While the render thread draws a frame, what it draws with stays until it's done: released at the main thread's next
		// join (ReleaseDeferred), as resources the game lets go of mid-frame are on the main thread
		if ( drawing && !ThreadSafe.IsRenderThread )
		{
			lock ( deferredReleases ) deferredReleases.Add( resource );
			return;
		}

		ReleaseNow( resource );
	}

	// Released while the render thread drew (Release), and a copy walked as they're released
	readonly List<object> deferredReleases = new(), releasing = new();

	/// <summary>Release what was let go of while the render thread drew, now that it's done.</summary>
	void ReleaseDeferred()
	{
		lock ( deferredReleases )
		{
			if ( deferredReleases.Count == 0 ) return;
			releasing.AddRange( deferredReleases );
			deferredReleases.Clear();
		}

		foreach ( var resource in releasing ) ReleaseNow( resource );
		releasing.Clear();
	}

	/// <summary>
	/// A texture that took another's place (<see cref="Texture.CopyFrom"/>) when the pixels the other was made with are gone
	/// (<see cref="Make"/> lets them go) gets a copy of the other's image. Behind a render thread's drawing, in order with the
	/// release of the copy <paramref name="to"/> had, which it would otherwise follow.
	/// </summary>
	public override void Adopt( Texture from, Texture to )
	{
		if ( drawing && !ThreadSafe.IsRenderThread )
		{
			lock ( deferredReleases ) deferredReleases.Add( (from, to) );
			return;
		}

		if ( !drawing && ThreadSafe.IsMainThread ) ReleaseDeferred();
		AdoptNow( from, to );
	}

	void AdoptNow( Texture from, Texture to )
	{
		TextureHandle source;
		lock ( textureOwners )
		{
			// A render target is drawn into again, and a borrowed backing stays its owner's
			if ( !textures.TryGetValue( from, out var backing ) || !textureOwners.Contains( backing.Handle ) || from.IsRenderTarget ) return;
			source = backing.Handle;
		}

		if ( textures.TryGetValue( to, out _ ) ) ReleaseNow( to );
		var info = gpu.InfoOf( source );
		var copy = gpu.CreateTexture( info );
		Track( copy, ImageLayout.Undefined );
		lock ( textureOwners )
		{
			textures.Add( to, new Backing<TextureHandle>( copy ) { Collected = collectedTextures } );
			textureOwners.Add( copy );
		}

		backingsVersion++;
		var layers = ArrayLayersOf( info );
		for ( int mip = 0; mip < info.Mips; mip++ )
		{
			for ( int layer = 0; layer < layers; layer++ ) CopyTexture( from, to, mip, layer, mip, layer );
		}
	}

	void ReleaseNow( object resource )
	{
		backingsVersion++;
		if ( resource is Texture released ) Released?.Invoke( this, released );

		switch ( resource )
		{
			case ValueTuple<Texture, Texture> adopted:
				AdoptNow( adopted.Item1, adopted.Item2 );
				break;

			case Texture texture when textures.TryGetValue( texture, out var backing ):
				lock ( textureOwners )
				{
					textures.Remove( texture );
					context.Forget( backing.Handle );
					backing.Collected = null;

					// A borrowed backing (a Borealis frame's target) stays its owner's
					if ( textureOwners.Remove( backing.Handle ) ) gpu.Destroy( backing.Handle );
				}

				break;

			case GpuBuffer buffer when buffers.TryGetValue( buffer, out var backing ):
				// Writes waiting for the next block may name it: submit them first, so it's freed after they've run
				SubmitUploads();
				ForgetCounter( buffer );
				buffers.Remove( buffer );
				backing.Collected = null;
				ownedBuffers.Remove( backing.Handle );
				gpu.Destroy( backing.Handle );
				break;

			case GpuBuffer buffer:
				ForgetCounter( buffer );
				break;
		}
	}

	/// <summary>
	/// Make <paramref name="handle"/>, a texture on this device that something else owns (a Borealis frame's target, now in
	/// <paramref name="layout"/>), the backing of <paramref name="texture"/>: drawing with the engine texture draws with it,
	/// and it isn't destroyed with the engine texture or this backend. A backing the texture had is released first.
	/// </summary>
	internal void Borrow( Texture texture, TextureHandle handle, ImageLayout layout )
	{
		if ( textures.TryGetValue( texture, out var backing ) )
		{
			if ( backing.Handle == handle )
			{
				context.Track( handle, layout );
				return;
			}

			Release( texture );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) );
		backingsVersion++;
		context.Track( handle, layout );
	}

	/// <summary>
	/// Stop tracking a borrowed texture's layout and drop its mip views (<see cref="Borrow"/>), as giving its engine texture
	/// another backing does: it isn't the block's any more, so finishing a block leaves it alone. Its engine texture keeps
	/// the backing, tracked again when it's next borrowed.
	/// </summary>
	internal void ForgetBorrowed( TextureHandle handle ) => context.Forget( handle );

	/// <summary>The layout an engine texture's backing is in, as the recording left it.</summary>
	internal ImageLayout LayoutOf( Texture texture ) => textures.TryGetValue( texture, out var backing ) ? context.LayoutOf( backing.Handle ) : ImageLayout.Undefined;

	/// <summary>
	/// A texture's backing. A render target is made to match, cleared as Source 2 clears a new one. Anything else comes
	/// from its compiled file through Borealis.Source2, or from the pixels it was made with, as RGBA8; failing both, one
	/// transparent pixel. A material's texture with no path of its own (a sky's cubemap) is the material's, as Borealis's draws read it.
	/// </summary>
	internal TextureHandle TextureOf( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );

		// What the device uses is what's in use (Texture.LastUsed): caches and animated textures go by it
		texture.UsedOnDevice();

		// A realtime probe's cube is captured into a cube of Borealis's: read that, not a copy nothing writes
		if ( texture.Depth == 6 && RenderedInto?.Invoke( this, texture ) is BackendTexture rendered ) return rendered.Handle;

		// One made already is looked up on any thread without waiting (a panel painting on the main thread asks for its
		// textures' indices while the render thread draws). What workers made is tracked before the main thread records with
		// it
		var mainThread = ThreadSafe.IsMainThread;
		var overlapping = mainThread && drawing && !ThreadSafe.IsRenderThread;
		if ( (!mainThread || overlapping || workerTracks.IsEmpty) && textures.TryGetValue( texture, out var known ) ) return known.Handle;

		// While the render thread draws, the main thread records nothing, so a made texture's layout tracking can wait for its
		// next recording, and a texture made meanwhile is made as a worker makes one (below), without waiting for the drawing
		var ownsContext = mainThread && !overlapping;
		if ( ownsContext ) FromMain();
		if ( ownsContext && !workerTracks.IsEmpty ) TrackWorkerTextures();

		if ( textures.TryGetValue( texture, out var backing ) ) return backing.Handle;

		// Looked up each time rather than kept: streaming swaps its image
		if ( MaterialTextureOf( texture ) is { } material ) return material.Handle;

		// Made once, under a lock: a sprite batch's workers ask for their sprites' indices (Texture.Index) in parallel,
		// while the main thread may be recording. A worker's writes go in a list of its own, sent with the next
		// submission (Device.DeferUploads), never in the main thread's lists; the main thread's too while the render thread
		// draws with them
		lock ( textureOwners )
		{
			using var deferred = ownsContext ? default : gpu.DeferUploads();

			// No description: a texture that failed to load, which Source 2 draws with its error texture (and without its
			// device has no description of at all)
			if ( texture.Width <= 0 || texture.Height <= 0 ) return ErrorTexture();
			return textures.TryGetValue( texture, out backing ) ? backing.Handle : Make( texture );
		}
	}

	/// <summary>
	/// The texture a draw or a copy reads for <paramref name="texture"/>: for a render target a view of Borealis's drew last
	/// (<see cref="Texture.DrawnByView"/>, a mirror's <c>DrawReflection</c>), the texture that view drew into
	/// (<see cref="RenderedInto"/>), as the scene's own draws read it; else <see cref="TextureOf"/>.
	/// </summary>
	TextureHandle SampledTextureOf( Texture texture )
	{
		if ( texture.DrawnByView && RenderedInto?.Invoke( this, texture ) is BackendTexture rendered )
		{
			texture.UsedOnDevice();
			return rendered.Handle;
		}

		return TextureOf( texture );
	}

	/// <summary>A texture attribute's binding: what a draw samples, or for a dispatch the engine's own copy.</summary>
	TextureHandle BoundTextureOf( Texture texture )
	{
		if ( !loweringDispatch ) return SampledTextureOf( texture );
		EngineWrites( texture );
		return TextureOf( texture );
	}

	/// <summary>
	/// The engine is about to draw into, copy into or clear <paramref name="texture"/>: from now on its own copy is the one
	/// read. Unless the engine overwrites all of it (<paramref name="whole"/>), what a view of Borealis's last drew for it is
	/// copied into it first, so what the engine draws lands over that.
	/// </summary>
	void EngineWrites( Texture texture, bool whole = false )
	{
		if ( texture is null || !texture.DrawnByView ) return;
		texture.DrawnByView = false;
		if ( whole || RenderedInto?.Invoke( this, texture ) is not BackendTexture drawn ) return;

		// The view's texture is left where Borealis's frames expect it: readable
		var layout = drawn.Layout;
		context.Track( drawn.Handle, layout );
		context.CopyTexture( drawn.Handle, TextureOf( texture ) );
		context.PrepareNative( drawn.Handle, layout, false );
	}

	/// <summary>Layouts of textures workers made, for the main thread's context (<see cref="Track"/>).</summary>
	readonly System.Collections.Concurrent.ConcurrentQueue<(TextureHandle Handle, ImageLayout Layout)> workerTracks = new();

	/// <summary>The layouts queued for the main thread's context and not taken yet, in order, for the tests.</summary>
	internal ImageLayout[] QueuedLayoutsForTests => workerTracks.Select( t => t.Layout ).ToArray();

	/// <summary>
	/// Tell the context a texture made here is in <paramref name="layout"/>: at once on the main thread, which records
	/// through it; from a worker, before the main thread next records or looks a texture up.
	/// </summary>
	void Track( TextureHandle handle, ImageLayout layout )
	{
		if ( ThreadSafe.IsMainThread && !Overlapping ) context.Track( handle, layout );
		else workerTracks.Enqueue( (handle, layout) );
	}

	/// <summary>
	/// The list a texture made on this thread writes into, if it has one of its own (<see cref="GpuDevice.DeferUploads"/>): a
	/// worker's, or the main thread's while the render thread draws with the main thread's lists and context.
	/// </summary>
	GpuCommandList OwnUploads => (!ThreadSafe.IsMainThread || Overlapping) ? gpu.DeferredList : null;

	void TrackWorkerTextures()
	{
		// Not one released meanwhile: its release had nothing to forget yet, and the end of every block would move it
		while ( workerTracks.TryDequeue( out var tracked ) )
		{
			if ( gpu.IsAlive( tracked.Handle ) ) context.Track( tracked.Handle, tracked.Layout );
		}
	}

	/// <summary>
	/// The texture of Borealis's on this device that an engine texture is rendered into, read in its place: a realtime probe's
	/// cube is captured into a cube of Borealis's (<c>MirrorContent.CaptureCube</c>), so what reads the probe's texture
	/// through the engine reads the capture. Null for any other texture. Set by Sandbox.Rendering.
	/// </summary>
	internal static Func<BorealisGpuRenderBackend, Texture, global::Borealis.GpuTexture> RenderedInto { get; set; }

	/// <summary>
	/// An engine texture was released (disposed, or its contents replaced): what was rendered into it for this device
	/// (<see cref="RenderedInto"/>, a realtime probe's cube) goes with it. Set by Sandbox.Rendering.
	/// </summary>
	internal static Action<BorealisGpuRenderBackend, Texture> Released { get; set; }

	/// <summary>
	/// The most multisampling up to <paramref name="wanted"/> this device's colour targets take, as Source 2's
	/// <c>GetBestMultisampleType</c> answers for its own device: under -borealisgpu Source 2's is the empty render system, which
	/// has none, so the game window's cameras would render without the multisampling the settings ask for.
	/// </summary>
	internal NativeEngine.RenderMultisampleType BestMultisampleType( NativeEngine.RenderMultisampleType wanted ) => BestMultisampleType( wanted, gpu.Capabilities.ColorSampleCounts );

	/// <summary>
	/// The most multisampling up to <paramref name="wanted"/> whose sample count is in <paramref name="supportedCounts"/> (a bit
	/// per count, as Vulkan lists them): 2x, 4x or 8x, as render targets draw (<see cref="SamplesOf"/>: 16x draws 8); none
	/// when there's none.
	/// </summary>
	internal static NativeEngine.RenderMultisampleType BestMultisampleType( NativeEngine.RenderMultisampleType wanted, int supportedCounts )
	{
		ReadOnlySpan<NativeEngine.RenderMultisampleType> fewer = [NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_8X, NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_4X, NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_2X];
		var most = Graphics.RenderMultiSampleToNum( wanted );
		foreach ( var type in fewer )
		{
			var samples = Graphics.RenderMultiSampleToNum( type );
			if ( samples <= most && (supportedCounts & samples) != 0 ) return type;
		}

		return NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
	}

	/// <summary>A render target's samples as its multisampling asks: what Vulkan has (6x is 4x, 16x is 8x).</summary>
	static int SamplesOf( NativeEngine.RenderMultisampleType type ) => type switch
	{
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_2X => 2,
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_4X or NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_6X => 4,
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_8X or NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_16X => 8,
		_ => 1,
	};

	TextureHandle Make( Texture texture )
	{
		// The pixels it's made from, let go after only if nothing replaced them meanwhile
		var madeFrom = texture.InitialPixels;
		TextureHandle handle;
		if ( texture.IsRenderTarget )
		{
			// Its samples (a multisampled one is still sampled, as Texture2DMS) and an array's layers, as it was made
			var samples = SamplesOf( texture.MultisampleType );
			var info = TextureInfo.RenderTarget( texture.Width, texture.Height, FormatOf( texture.ImageFormat ), samples, $"Engine target {texture.Width}x{texture.Height}" );
			info.Usage |= TextureUsage.Sampled;
			info.Mips = samples > 1 ? 1 : Math.Max( texture.Mips, 1 );
			if ( texture.Desc is { IsArray: true, IsCube: false } desc )
			{
				info.Layers = Math.Max( (int)desc.m_nDepth, 1 );
				info.Dimension = TextureDimension.Texture2DArray;
			}
			var depth = info.Format.IsDepth();
			if ( !depth && samples == 1 ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );

			// A target made with pixels - an image whose mips Source 2 generates, or one made for compute - starts with them
			if ( samples == 1 && info.Layers == 1 && (InitialPixels( texture, info.Format ) ?? (RawPixels( texture ) is { } raw && raw.Format == info.Format ? raw.Data : null)) is { } pixels )
			{
				Upload( handle, info, pixels, ImageLayout.Undefined );
			}
			else if ( samples == 1 && info.Layers > 1 && texture.InitialPixels is { } data && DataFormatOf( texture.ImageFormat ) == info.Format.Linear() && HoldsEveryMip( info, data ) is { } everyMip )
			{
				// An array's every layer
				Write( handle, info, data, everyMip );
			}
			else
			{
				if ( !depth && texture.InitialColor is { } clear ) ClearNew( handle, clear );
				else Track( handle, ImageLayout.Undefined );
			}
		}
		else if ( FromFile( texture ) is { } file )
		{
			handle = file;
		}
		else if ( FromData( texture ) is { } data )
		{
			handle = data;
		}
		else if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ) )
		{
			handle = VolumeOf( texture );
		}
		else if ( LayeredOf( texture ) is { } layered )
		{
			handle = layered;
		}
		else if ( InitialPixels( texture, GpuFormat.RGBA8Unorm ) is { } pixels )
		{
			// With its mips, as Source 2 generates them from the first, and storage when it was made for compute
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			Upload( handle, info, pixels, ImageLayout.Undefined );
		}
		else if ( RawPixels( texture ) is { } raw )
		{
			// Another format made with its first mip's data (float images, lookup tables): the same bytes, and storage
			// when it was made for compute
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, raw.Format, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			Upload( handle, info, raw.Data, ImageLayout.Undefined );
		}
		else if ( (texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) || (texture.InitialPixels is null && texture.InitialColor is not null)) && texture.Depth <= 1 && DataFormatOf( texture.ImageFormat ) is var storage && storage != GpuFormat.Unknown )
		{
			// Made empty, for compute to fill (a terrain's baked normals) or made at run time with no pixels: its own format,
			// storage when made for compute, cleared as a new target is, to the colour it was made or last cleared with
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, storage, Math.Max( texture.Mips, 1 ), $"Engine storage {texture.Width}x{texture.Height}" );
			if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			ClearNew( handle, texture.InitialColor ?? Color.Transparent );
		}
		else
		{
			handle = Unreadable( texture );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) { Collected = collectedTextures } );
		textureOwners.Add( handle );

		// Its copy is made, and nothing else reads the pixels it was made from but a device making its own copy: as the
		// only device, let them go (Texture.GetPixels reads the copy, and a texture taking this one's place copies it: Adopt).
		// Pixels another thread gave it since (CopyFrom) aren't these, and are kept
		if ( madeFrom is not null && RenderBackend.IsOnlyDevice( this ) ) texture.ForgetInitialPixels( madeFrom );
		return handle;
	}

	/// <summary>
	/// A volume (<see cref="Texture.CreateVolume"/>) as a 3D texture in its own format, storage when made for compute, with the
	/// pixels it was made with, or cleared. As a 2D texture, a compute shader's storage binding of it had no view and its
	/// sampled binding the wrong shape.
	/// </summary>
	TextureHandle VolumeOf( Texture texture )
	{
		var format = DataFormatOf( texture.ImageFormat );
		var known = format != GpuFormat.Unknown;
		var info = new TextureInfo
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = Math.Max( texture.Depth, 1 ),
			Layers = 1,
			Mips = 1,
			Samples = 1,
			Dimension = TextureDimension.Texture3D,
			Format = known ? format : GpuFormat.RGBA8Unorm,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = $"Engine volume {texture.Width}x{texture.Height}x{texture.Depth}",
		};
		if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;

		var handle = gpu.CreateTexture( info );
		var size = (long)info.Width * info.Height * info.Depth * info.Format.BytesPerBlock();
		if ( known && texture.InitialPixels is { } data && data.Length >= size )
		{
			Upload( handle, info, data.AsSpan( 0, (int)size ), ImageLayout.Undefined );
		}
		else
		{
			ClearNew( handle, Color.Transparent );
		}

		return handle;
	}

	TextureHandle errorTexture;

	/// <summary>Source 2's error texture: a 32x32 magenta and black checkerboard of 4 pixel squares.</summary>
	TextureHandle ErrorTexture()
	{
		if ( errorTexture.IsValid ) return errorTexture;

		const int size = 32;
		var pixels = new byte[size * size * 4];
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var magenta = ((x & 4) ^ (y & 4)) != 0;
				pixels[(y * size + x) * 4] = magenta ? (byte)255 : (byte)0;
				pixels[(y * size + x) * 4 + 2] = magenta ? (byte)255 : (byte)0;
				pixels[(y * size + x) * 4 + 3] = 255;
			}
		}

		var info = TextureInfo.Texture2D( size, size, GpuFormat.RGBA8Unorm, name: "Engine error texture" );
		errorTexture = gpu.CreateTexture( info );
		Upload( errorTexture, info, pixels, ImageLayout.Undefined );
		return errorTexture;
	}

	/// <summary>
	/// The material's own texture for one read from a material (<see cref="Material.GetTexture"/>), or null: borrowed from
	/// the device, which frees it with the worlds that hold the material, rather than copied here, where the copy stayed with
	/// the engine's texture after the map was unloaded.
	/// </summary>
	BackendTexture MaterialTextureOf( Texture texture )
	{
		if ( !Texture.MaterialOrigins.TryGetValue( texture, out var origin ) || origin is not Texture.MaterialOrigin o ) return null;
		return services.LoadedMaterialTexture( o.Material, o.Parameter ) as BackendTexture;
	}

	public override byte[] ReadCompiledPrefix( string path, long most )
	{
		try
		{
			return files.TryReadPrefix( path, most );
		}
		catch ( System.IO.IOException )
		{
			// A package mounting or unmounting while it reads: not found this time
			return null;
		}
	}

	/// <summary>A texture loaded from a compiled <c>.vtex</c>, read from that file.</summary>
	TextureHandle? FromFile( Texture texture )
	{
		var path = texture.ResourcePath;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) return null;
		if ( files.Find( path ) is not { } file ) return null;

		try
		{
			TextureFile read;
			try
			{
				read = TextureFile.Load( file );
			}
			catch ( System.IO.FileNotFoundException ) when ( files.Find( path ) is { } again && again != file )
			{
				// A web-streamed texture's downloaded part replaced while it was found
				read = TextureFile.Load( again );
			}

			var handle = read.Create( gpu, path, UploadList() );
			Track( handle, ImageLayout.ShaderRead );
			return handle;
		}
		catch ( NotSupportedException )
		{
			return null;
		}
		catch ( System.IO.InvalidDataException e )
		{
			// A file with no mip whose data is all there: made once, as unreadable, rather than read and thrown again in every
			// frame that draws it or failing what asked for it (a terrain's materials)
			Log.Warning( $"Borealis.Gpu: {path} can't be read: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, in <paramref name="format"/> (RGBA8 or BGRA8), when it was made in one
	/// of those.
	/// </summary>
	static byte[] InitialPixels( Texture texture, GpuFormat format )
	{
		if ( texture.InitialPixels is not { } data ) return null;
		if ( texture.ImageFormat is not (ImageFormat.RGBA8888 or ImageFormat.BGRA8888) ) return null;

		var size = texture.Width * texture.Height * 4;
		if ( data.Length < size ) return null;

		var pixels = data.Length == size ? data : data[..size];
		var swap = (texture.ImageFormat == ImageFormat.BGRA8888) != (format == GpuFormat.BGRA8Unorm);
		if ( !swap ) return pixels;

		pixels = pixels == data ? (byte[])data.Clone() : pixels;
		for ( int i = 0; i < size; i += 4 ) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
		return pixels;
	}

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, as they are, when its format has a Borealis.Gpu form and they're at
	/// least one mip's worth.
	/// </summary>
	static (GpuFormat Format, byte[] Data)? RawPixels( Texture texture )
	{
		if ( texture.InitialPixels is not { } data || texture.Depth > 1 ) return null;
		var format = DataFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown ) return null;
		var size = texture.Width * texture.Height * format.BytesPerBlock();
		if ( data.Length < size ) return null;
		return (format, data.Length == size ? data : data[..size]);
	}

	/// <summary>The Borealis.Gpu form of an uncompressed engine format, or Unknown.</summary>
	static GpuFormat DataFormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA8888 or ImageFormat.RGBX8888 or ImageFormat.RGBA8888_LINEAR or ImageFormat.LINEAR_RGBA8888 => GpuFormat.RGBA8Unorm,
		ImageFormat.BGRA8888 or ImageFormat.BGRX8888 or ImageFormat.BGRA8888_LINEAR or ImageFormat.BGRX8888_LINEAR or ImageFormat.LINEAR_BGRA8888 or ImageFormat.LINEAR_BGRX8888 => GpuFormat.BGRA8Unorm,
		ImageFormat.A8 or ImageFormat.I8 or ImageFormat.LINEAR_I8 => GpuFormat.R8Unorm,
		ImageFormat.IA88 => GpuFormat.RG8Unorm,
		ImageFormat.RGBA16161616 or ImageFormat.LINEAR_RGBA16161616 => GpuFormat.RGBA16Unorm,
		ImageFormat.BGRA1010102 => GpuFormat.BGR10A2Unorm,
		ImageFormat.RG3232 => GpuFormat.RG32Uint,
		ImageFormat.RGBA32323232 => GpuFormat.RGBA32Uint,
		ImageFormat.R32F => GpuFormat.R32Float,
		ImageFormat.RG3232F => GpuFormat.RG32Float,
		ImageFormat.RGBA32323232F => GpuFormat.RGBA32Float,
		ImageFormat.RGBA16161616F => GpuFormat.RGBA16Float,
		ImageFormat.RG1616F => GpuFormat.RG16Float,
		ImageFormat.I16F or ImageFormat.R16F => GpuFormat.R16Float,
		ImageFormat.R11G11B10F => GpuFormat.RG11B10Float,
		ImageFormat.RGBA1010102 => GpuFormat.RGB10A2Unorm,
		ImageFormat.R16 => GpuFormat.R16Unorm,
		ImageFormat.RG1616 => GpuFormat.RG16Unorm,
		ImageFormat.R32_UINT => GpuFormat.R32Uint,
		_ => GpuFormat.Unknown,
	};

	/// <summary>
	/// A texture with nothing to make it from: one transparent pixel, kept
	/// as its backing so it isn't made again each time it's asked for, and said once.
	/// </summary>
	TextureHandle Unreadable( Texture texture )
	{
		// Made for a write (Update): empty in its own format and size, which the write fills
		if ( makingForWrite && EmptyForWrite( texture ) is { } empty ) return empty;

		Log.Warning( $"Borealis.Gpu: {(string.IsNullOrEmpty( texture.ResourcePath ) ? $"a {texture.Width}x{texture.Height} {texture.ImageFormat} texture" : texture.ResourcePath)} has nothing to read, so it draws transparent" );
		var handle = gpu.CreateTexture( TextureInfo.Texture2D( 1, 1, GpuFormat.RGBA8Unorm, name: StandInName ) );
		ClearNew( handle, Color.Transparent );
		return handle;
	}

	/// <summary>The name of <see cref="Unreadable"/>'s transparent pixel, which a write replaces.</summary>
	const string StandInName = "Engine image without pixels";

	// While Update makes the backing it writes (MakeForWrite)
	bool makingForWrite;

	/// <summary>Make a texture's backing for a write to it: one with nothing to read is made empty, not as the stand-in.</summary>
	void MakeForWrite( Texture texture )
	{
		makingForWrite = true;
		try
		{
			TextureOf( texture );
		}
		finally
		{
			makingForWrite = false;
		}
	}

	/// <summary>
	/// A plain 2D texture with nothing to read, about to be written: in its own format, size and mips (storage when made for
	/// compute), cleared to transparent. Null for one in a format with no Borealis.Gpu form, or a volume, array or cube.
	/// </summary>
	TextureHandle? EmptyForWrite( Texture texture )
	{
		var format = DataFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown || texture.Depth > 1 || texture.Desc.IsArray || texture.Desc.IsCube ) return null;

		var info = TextureInfo.Texture2D( texture.Width, texture.Height, format, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
		if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
		var handle = gpu.CreateTexture( info );
		ClearNew( handle, Color.Transparent );
		return handle;
	}

	/// <summary>
	/// Write mip 0 of a texture (now in <paramref name="from"/>) and fill the rest from it, recorded where uploads go -
	/// the block being recorded, or the list of uploads submitted before the next block - rather than submitted and
	/// waited for. The texture ends in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void Upload( TextureHandle handle, in TextureInfo info, ReadOnlySpan<byte> pixels, ImageLayout from, int x = 0, int y = 0, int width = 0, int height = 0, int layer = 0 )
	{
		var target = UploadList();
		target.Barrier( new TextureBarrier
		{
			Texture = handle,
			OldLayout = from,
			NewLayout = ImageLayout.CopyDestination,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.Transfer,
			DestinationAccess = MemoryAccess.TransferWrite,
		} );

		target.UpdateTexture( handle, pixels, 0, layer, width > 0 ? width : info.Width, height > 0 ? height : info.Height, x, y );
		target.Barrier( MipBarrier( handle, 0, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		if ( info.Mips > 1 ) FillMips( target, handle, info, ImageLayout.CopyDestination, layer );
		Track( handle, ImageLayout.ShaderRead );
		TextureUploads++;
	}

	/// <summary>Texture writes recorded rather than submitted and waited for, for tests.</summary>
	public int TextureUploads { get; private set; }

	/// <summary>
	/// Where a write is recorded: the block being recorded (outside any rendering scope), or the list of uploads
	/// submitted before the next one; on a worker making a texture (<see cref="TextureOf"/>), the worker's own list.
	/// </summary>
	GpuCommandList UploadList()
	{
		if ( OwnUploads is { } deferred ) return deferred;
		FromMain();

		if ( list is not null )
		{
			context.MemoryBarrier();
			return list;
		}

		DropStaleUploads();
		if ( uploads is null ) BeginUploads();
		return uploads;
	}

	/// <summary>
	/// Fill mips 1 and down of a texture whose mip 0 is in <see cref="ImageLayout.ShaderRead"/> (and the rest in
	/// <paramref name="rest"/>) by halving each into the next with a linear blit (a box filter), every layer, as a texture
	/// made with only its first mip gets the rest; only <paramref name="onlyLayer"/>'s when one is given (the layer an
	/// update wrote). Leaves it all in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void FillMips( GpuCommandList mips, TextureHandle handle, in TextureInfo info, ImageLayout rest, int onlyLayer = -1 )
	{
		var filter = info.Format is GpuFormat.R32Float or GpuFormat.RG32Float or GpuFormat.RGBA32Float or GpuFormat.R32Uint or GpuFormat.RG32Uint or GpuFormat.RGBA32Uint ? Filter.Nearest : Filter.Linear;
		var first = onlyLayer >= 0 ? onlyLayer : 0;
		var layers = onlyLayer >= 0 ? onlyLayer + 1 : ArrayLayersOf( info );
		for ( int i = 1; i < info.Mips; i++ )
		{
			int sw = Math.Max( info.Width >> (i - 1), 1 ), sh = Math.Max( info.Height >> (i - 1), 1 );
			int dw = Math.Max( info.Width >> i, 1 ), dh = Math.Max( info.Height >> i, 1 );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.ShaderRead, ImageLayout.CopySource ) );
			mips.Barrier( MipBarrier( handle, i, rest, ImageLayout.CopyDestination ) );
			for ( int layer = first; layer < layers; layer++ ) mips.Blit( handle, (0, 0, sw, sh), handle, (0, 0, dw, dh), filter, i - 1, i, layer, layer );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.CopySource, ImageLayout.ShaderRead ) );
			mips.Barrier( MipBarrier( handle, i, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		}
	}

	/// <summary>
	/// A texture updated on Source 2's device: its copy here takes the same bytes, recorded where uploads go, rather than
	/// being dropped and copied back from Source 2's pixels. Mips past the first are filled again from it. An array or cube
	/// takes whole or partial layers (<paramref name="z"/> the first, <paramref name="depth"/> how many). False when the
	/// copy is in another format than the data, the layers are outside it, or the update is of part of a 3D texture's
	/// slices. A texture with no copy here yet gets one now, made as it would be when first drawn, so the bytes aren't
	/// lost: the engine forgets the pixels a texture was made with once it's updated, and Source 2's copy under
	/// -borealisgpu has nothing to read back.
	/// </summary>
	public override bool Update( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth )
	{
		// While the render thread draws, kept and written after it, in order with the main thread's buffer writes
		if ( Overlapping )
		{
			StageTextureUpdate( texture, data, x, y, z, width, height, depth );
			return true;
		}

		if ( ThreadSafe.IsMainThread ) FromMain();

		// The engine wrote it: its copy is what's read from now on
		texture.DrawnByView = false;

		if ( !textures.TryGetValue( texture, out var backing ) )
		{
			if ( !(texture.Desc.IsArray || texture.Desc.IsCube) && (z != 0 || depth > 1) ) return true;

			// From a worker: made with the update on the main thread, before the next block
			if ( !ThreadSafe.IsMainThread ) return QueueWorkerUpdate( texture, data, x, y, z, width, height, depth );

			// A material's texture or the error texture has no copy of its own to write
			MakeForWrite( texture );
			if ( !textures.TryGetValue( texture, out backing ) ) return true;
		}

		var info = gpu.InfoOf( backing.Handle );

		// Drawn before its first write (a web panel's page, its panel recorded while the render thread drew): the transparent
		// stand-in no write fits gives way to a copy made for the write, rather than the write being dropped
		if ( info.Name == StandInName )
		{
			if ( !ThreadSafe.IsMainThread ) return QueueWorkerUpdate( texture, data, x, y, z, width, height, depth );

			ReleaseNow( texture );
			MakeForWrite( texture );
			if ( !textures.TryGetValue( texture, out backing ) ) return false;
			info = gpu.InfoOf( backing.Handle );
		}

		if ( DataFormatOf( texture.ImageFormat ) != info.Format ) return false;

		// A volume takes every slice at once (CommandList.CopyBufferToTexture), so only an update of all of them; anything
		// else takes layers within its own (a 2D texture has one)
		var volume = info.Dimension == TextureDimension.Texture3D;
		var slices = volume ? info.Depth : 1;
		var layers = volume ? 1 : Math.Max( depth, 1 );
		if ( volume ? z != 0 || (depth > 1 && depth != slices) : z < 0 || z + layers > ArrayLayersOf( info ) ) return false;

		width = width > 0 ? width : info.Width;
		height = height > 0 ? height : info.Height;
		if ( slices > 1 && (x != 0 || y != 0 || width != info.Width || height != info.Height) ) return false;
		if ( x + width > info.Width || y + height > info.Height ) return false;
		var layerSize = width * height * slices * info.Format.BytesPerBlock();
		var size = layerSize * layers;
		if ( data.Length < size ) return false;

		// From a worker (an animated texture decodes and updates its frames on the thread pool): written at the start of the
		// next block, so nothing records into the main thread's lists from another thread
		if ( !ThreadSafe.IsMainThread ) return QueueWorkerUpdate( texture, data[..size], x, y, z, width, height, depth );

		var from = context.LayoutOf( backing.Handle );
		for ( int layer = 0; layer < layers; layer++ )
		{
			Upload( backing.Handle, info, data.Slice( layer * layerSize, layerSize ), from, x, y, width, height, z + layer );
			from = ImageLayout.ShaderRead;
		}

		return true;
	}

	// With what each texture was made from when the worker updated it, which the engine forgets once Update returns: UpdateKept
	readonly System.Collections.Concurrent.ConcurrentQueue<(Texture Texture, byte[] Data, int Size, byte[] InitialPixels, Color? InitialColor, int X, int Y, int Z, int Width, int Height, int Depth)> workerUpdates = new();

	bool QueueWorkerUpdate( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth )
	{
		var buffer = ArrayPool<byte>.Shared.Rent( data.Length );
		data.CopyTo( buffer );
		workerUpdates.Enqueue( (texture, buffer, data.Length, texture.InitialPixels, texture.InitialColor, x, y, z, width, height, depth) );
		return true;
	}

	/// <summary>
	/// <see cref="Update"/> of a texture with an update kept for later (made while the render thread drew, or on a worker).
	/// The engine forgot the pixels or colour it was made from once the update returned (<c>Texture.UpdateInternal</c>),
	/// so a texture with no copy here yet would be made from nothing; it's given back what it had then while the copy is
	/// made, then forgets it again.
	/// </summary>
	bool UpdateKept( Texture texture, ReadOnlySpan<byte> data, byte[] initialPixels, Color? initialColor, int x, int y, int z, int width, int height, int depth )
	{
		var restore = texture.InitialPixels is null && texture.InitialColor is null && (initialPixels is not null || initialColor is not null);
		if ( restore )
		{
			texture.InitialPixels = initialPixels;
			texture.InitialColor = initialColor;
		}

		try
		{
			return Update( texture, data, x, y, z, width, height, depth );
		}
		finally
		{
			if ( restore )
			{
				texture.InitialPixels = null;
				texture.InitialColor = null;
			}
		}
	}

	/// <summary>
	/// A texture cleared to one colour: its copy here is cleared, in the block being recorded or in a list of its own. From a
	/// worker, or for a copy a clear can't write (compressed, depth), false: the copy is made again when next used.
	/// </summary>
	public override bool Cleared( Texture texture, Color color )
	{
		// While the render thread draws, kept and cleared after it, in order with the main thread's other writes
		if ( Overlapping )
		{
			StageTextureClear( texture, color );
			return true;
		}

		if ( ThreadSafe.IsMainThread ) FromMain();
		texture.DrawnByView = false;
		if ( !textures.TryGetValue( texture, out var backing ) ) return true;
		if ( !ThreadSafe.IsMainThread ) return false;

		var format = gpu.InfoOf( backing.Handle ).Format;
		if ( format.IsCompressed() || format.IsDepth() ) return false;

		ClearNow( backing.Handle, color );
		return true;
	}

	/// <summary>Write the texture updates workers asked for since the last block, before a block starts recording.</summary>
	void WriteWorkerUpdates()
	{
		while ( workerUpdates.TryDequeue( out var update ) )
		{
			try
			{
				// Made (if it wasn't yet when the worker updated it) and written now, through the main thread's checks
				if ( update.Texture.IsValid && !UpdateKept( update.Texture, update.Data.AsSpan( 0, update.Size ), update.InitialPixels, update.InitialColor, update.X, update.Y, update.Z, update.Width, update.Height, update.Depth ) ) Release( update.Texture );
			}
			finally
			{
				ArrayPool<byte>.Shared.Return( update.Data );
			}
		}
	}

	static TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = PipelineStage.AllCommands,
		SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
		BaseMip = mip,
		MipCount = 1,
	};

	/// <summary>
	/// A texture just made, cleared to <paramref name="color"/>. Made where the context isn't this thread's, its layout is
	/// queued for the context once, as the clear leaves it: the context's thread takes queued layouts as each block begins,
	/// so a layout queued in two steps can reach it a block apart.
	/// </summary>
	void ClearNew( TextureHandle handle, Color color )
	{
		if ( OwnUploads is null ) Track( handle, ImageLayout.Undefined );
		ClearNow( handle, color );
	}

	/// <summary>Clear a new target now, in the block being recorded or on its own.</summary>
	void ClearNow( TextureHandle handle, Color color )
	{
		var value = new Num.Vector4( color.r, color.g, color.b, color.a );

		// A worker's new texture: cleared in its own list, without the main thread's context, and left readable, where the
		// context takes a texture it hasn't been told of yet to be
		if ( OwnUploads is { } deferred )
		{
			deferred.Barrier( TextureBarrier.Transition( handle, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			deferred.ClearTexture( handle, value );
			deferred.Barrier( TextureBarrier.Transition( handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
			Track( handle, ImageLayout.ShaderRead );
			return;
		}

		if ( list is not null )
		{
			context.ClearTexture( handle, value );
			return;
		}

		var clear = gpu.BeginCommandList( QueueType.Graphics, "Clear new target" );
		context.Begin( clear );
		context.ClearTexture( handle, value );
		context.Finish();
		gpu.Submit( clear );
	}

	const string UncompressedTangentFrame = "UncompressedTangentFrame";

	// An A8 target is one channel written and read as red (ambient occlusion's result), as its R8 image holds it. Depth with
	// stencil is 32-bit float depth, as the frame's depth is
	static GpuFormat FormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.D24S8 or ImageFormat.LINEAR_D24S8 or ImageFormat.D24FS8 or ImageFormat.D32FS8 => GpuFormat.D32FloatS8Uint,
		ImageFormat.D32 or ImageFormat.D24X8 => GpuFormat.D32Float,
		ImageFormat.D16 => GpuFormat.D16Unorm,
		_ => format is not ImageFormat.I8 && DataFormatOf( format ) is var mapped && mapped != GpuFormat.Unknown
			? mapped
			: throw new NotSupportedException( $"Render target format {format} on Borealis.Gpu" ),
	};

	/// <summary>
	/// A buffer's backing. One made while recording for another device starts with what that device's copy holds; one made in
	/// a recording of a game's drawing (<see cref="Rendering.RecordingRenderBackend"/>, which holds nothing) starts empty, as
	/// one of this device's does, so its first use can be in a block (the recording's replay on the render thread).
	/// </summary>
	internal BufferHandle BufferOf( GpuBuffer buffer )
	{
		ArgumentNullException.ThrowIfNull( buffer );
		if ( buffers.TryGetValue( buffer, out var backing ) ) return backing.Handle;

		var size = Math.Max( (long)buffer.ElementCount * buffer.ElementSize, 16 );
		var handle = gpu.CreateBuffer( new BufferInfo
		{
			Size = size,
			Usage = BufferUsage.Storage | BufferUsage.Index | BufferUsage.Vertex | BufferUsage.Indirect | BufferUsage.CopySource | BufferUsage.CopyDestination,
			Name = "Engine buffer",
		} );

		buffers.Add( buffer, new Backing<BufferHandle>( handle ) { Collected = collectedBuffers } );
		ownedBuffers.Add( handle );

		if ( buffer.Origin != this && buffer.Origin is not Rendering.RecordingRenderBackend )
		{
			var bytes = new byte[buffer.ElementCount * buffer.ElementSize];
			buffer.GetData<byte>( bytes );
			gpu.WriteBuffer( handle, bytes );
		}

		return handle;
	}

	/// <summary>The compiled shader a material draws with (its shader, without material values).</summary>
	// The shader each material names, worked out again only when its shader may have changed (a reload, or set in code)
	sealed class ShaderName
	{
		public string Path;
		public int ShaderVersion = -1;

		// The shader the path loaded, as of shadersVersion: found again without hashing the path
		public ShaderFile Shader;
		public int Loaded = -1;
	}

	// Moves on whenever the shaders found by path are forgotten (ShaderCompiled)
	int shadersVersion;

	readonly ConditionalWeakTable<Material, ShaderName> shaderNames = new();

	// Last few materials drawn with: a draw looks its material up first
	readonly Material[] nameKeys = new Material[4];
	readonly ShaderName[] nameValues = new ShaderName[4];
	int nameNext;

	ShaderFile ShaderOf( Material material )
	{
		ShaderName name = null;
		for ( int i = 0; i < nameKeys.Length; i++ )
		{
			if ( ReferenceEquals( nameKeys[i], material ) ) { name = nameValues[i]; break; }
		}

		if ( name is null )
		{
			name = shaderNames.GetOrCreateValue( material );
			(nameKeys[nameNext], nameValues[nameNext]) = (material, name);
			nameNext = (nameNext + 1) & 3;
		}

		if ( name.ShaderVersion != material.ShaderVersion )
		{
			// As code gave it, else as its compiled file names it; the material system only for one with no file of ours to
			// read (an error material keeps the error shader it was given)
			name.Path = material.ShaderNameFromCode ?? Material.FileShaderName( files, material.FilePath, reread: name.ShaderVersion >= 0 ) ?? material.native.GetString( "shader", "" );
			name.ShaderVersion = material.ShaderVersion;
			name.Loaded = -1;
		}

		if ( name.Loaded == shadersVersion ) return name.Shader;
		var path = name.Path;
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( shaders.TryGetValue( path, out var shader ) )
		{
			(name.Shader, name.Loaded) = (shader, shadersVersion);
			return shader;
		}

		var file = ShaderPath( path );

		// Compute shaders name their path under shaders/ (postprocess/postprocess_bloom_cs)
		shader = drawer.Shaders.Load( file ) ?? (file.StartsWith( "shaders/", StringComparison.OrdinalIgnoreCase ) ? null : drawer.Shaders.Load( "shaders/" + file ));
		shaders[path] = shader;
		if ( shader is not null ) AddNames( shader );
		(name.Shader, name.Loaded) = (shader, shadersVersion);
		return shader;
	}

	ShaderFile Shader( string path ) => shaders.TryGetValue( path, out var shader ) ? shader : shaders[path] = AddNames( drawer.Shaders.Load( path ) );

	/// <summary>
	/// A shader compiled while running isn't under this device's content (the engine links it into GAME, or keeps an
	/// anonymous one in memory): take its bytes by the name its materials give, and forget what materials resolved to before.
	/// </summary>
	public override void ShaderCompiled( string path, byte[] compiled )
	{
		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => ShaderCompiled( path, compiled ) );
			return;
		}

		FromMain();
		try
		{
			if ( services.Shaders.Add( path, compiled ) )
			{
				shaders.Clear();
				shadersVersion++;
				services.ReloadMaterialsOfShader( path );
			}
		}
		catch ( Exception e ) when ( e is System.IO.InvalidDataException or System.IO.IOException )
		{
			Log.Warning( $"Borealis.Gpu: {path} compiled, but doesn't load here: {e.Message}" );
		}
	}

	/// <summary>Learn the attribute and combo names a shader reads, so tokens can be turned back into them.</summary>
	ShaderFile AddNames( ShaderFile shader )
	{
		foreach ( var type in (ProgramType[])[ProgramType.Features, ProgramType.Vertex, ProgramType.Pixel, ProgramType.Compute] )
		{
			if ( shader[type] is not { } program ) continue;
			foreach ( var variable in program.Variables )
			{
				if ( !string.IsNullOrEmpty( variable.Name ) ) names.TryAdd( new StringToken( variable.Name ).Value, variable.Name );
				if ( !string.IsNullOrEmpty( variable.Attribute ) ) names.TryAdd( new StringToken( variable.Attribute ).Value, variable.Attribute );
			}

			foreach ( var combo in program.DynamicCombos ) names.TryAdd( new StringToken( combo.Name ).Value, combo.Name );
		}

		// A shader only its module describes has no compiled variables: the module's names instead
		if ( shader is { ModuleOnly: true } && drawer.Shaders.Modules is { } modules )
		{
			foreach ( var name in modules.BindingNames( shader ) ) names.TryAdd( new StringToken( name ).Value, name );
		}

		return shader;
	}

	string Name( uint token ) => names.TryGetValue( token, out var name ) ? name : StringToken.GetValue( token );

	// Lowering a token's value: its name, attribute id (no per-draw hashing) and its buffer's hidden counter id (-1 until lowered)
	sealed class TokenBinding( string name, int id )
	{
		public readonly string Name = name;
		public readonly int Id = id;
		public int CounterId = -1;
	}

	readonly Dictionary<uint, TokenBinding> tokenBindings = new();

	TokenBinding BindingOf( uint token )
	{
		if ( tokenBindings.TryGetValue( token, out var known ) ) return known;
		return Name( token ) is { } name ? tokenBindings[token] = new TokenBinding( name, AttributeIds.Of( name ) ) : null;
	}

	// Whether the attributes being lowered are a dispatch's (LowerForDispatch)
	bool loweringDispatch;

	/// <summary>
	/// <see cref="Lower(RenderAttributes)"/> for a dispatch, which may write what it binds: a texture a view of Borealis's drew
	/// last is copied into the engine's own copy first (<see cref="EngineWrites"/>), and the dispatch binds that. Kept as a
	/// draw's are (<see cref="LoweredFor"/>): a dispatch after another with the same attributes but a combo lowers that alone.
	/// </summary>
	ShaderAttributes LowerForDispatch( RenderAttributes attributes ) => LoweredFor( attributes, default, false, pending: false, dispatch: true );

	/// <summary>Lower managed attributes into what <see cref="ShaderDrawer"/> binds by name.</summary>
	ShaderAttributes Lower( RenderAttributes attributes )
	{
		lowered.Clear();

		// A native set that keeps a managed copy (a custom or dynamic object's) draws with the copy
		if ( attributes?.Managed is null && attributes?.ShadowSet is { } copy ) attributes = copy;
		if ( attributes?.Managed is null )
			throw new NotSupportedException( "Native render attributes can't be drawn with on Borealis.Gpu" );

		// A lent frame's transforms under everything, then a draw's attributes over the block's: a post process's blit reads
		// the frame grab its command list set on the block. The block's lowered set shows through under the draw's, not copied
		if ( blockAttributes?.Managed is { } block && !ReferenceEquals( block, attributes.Managed ) ) lowered.Under = LoweredBlock( block );
		else if ( lentTransforms.IsValid ) lowered.SetById( TransformBufferId, lentTransforms );
		Lower( attributes.Managed );
		return lowered;
	}

	// The block's attributes lowered once and shown under each draw's: lowered again only when the set changes (its version),
	// in a new device frame (streamed textures swap their images), or when the lent transforms or a backing change.
	readonly ShaderAttributes loweredBlock = new();
	(ManagedRenderAttributes Set, int Version, ulong Frame, BufferHandle Transforms, int Backings, int ViewDraws) loweredBlockKey;
	int backingsVersion;

	ShaderAttributes LoweredBlock( ManagedRenderAttributes block )
	{
		var key = (block, block.Version, gpu.FrameNumber, lentTransforms, backingsVersion, Texture.ViewDraws);
		if ( loweredBlockKey == key ) return loweredBlock;

		loweredBlock.Clear();
		if ( lentTransforms.IsValid ) loweredBlock.SetById( TransformBufferId, lentTransforms );
		Lower( block, loweredBlock );

		// Lowering may make backings (a texture's first use, an append buffer's counter): keyed after them
		loweredBlockKey = (block, block.Version, gpu.FrameNumber, lentTransforms, backingsVersion, Texture.ViewDraws);
		return loweredBlock;
	}

	void Lower( ManagedRenderAttributes managed ) => Lower( managed, lowered );

	/// <summary>Lower <paramref name="managed"/>'s values and combos set after its <see cref="ManagedRenderAttributes.Version"/> was <paramref name="since"/> (all of them by default).</summary>
	void Lower( ManagedRenderAttributes managed, ShaderAttributes lowered, int since = int.MinValue )
	{
		foreach ( var (token, value) in managed.ValuesSince( since ) )
		{
			if ( value is null || BindingOf( token ) is not { } binding ) continue;

			// Exact types first: a pattern switch over arrays and classes goes through cast helpers per value
			var type = value.GetType();
			if ( type == typeof( float[] ) || type == typeof( byte[] ) ) lowered.SetById( binding.Id, value );
			else if ( type == typeof( ManagedRenderAttributes.TextureValue ) ) LowerTexture( binding, (ManagedRenderAttributes.TextureValue)value, lowered );
			else if ( value is GpuBuffer buffer ) LowerBuffer( binding, buffer, lowered );
		}

		foreach ( var (token, value) in managed.CombosSince( since ) )
		{
			if ( BindingOf( token ) is { } binding ) lowered.SetComboById( binding.Id, value );
		}
	}

	/// <summary>Lower <paramref name="managed"/>'s textures again: the same texture can be backed by another image since (streaming, a view's drawing).</summary>
	void LowerTextures( ManagedRenderAttributes managed, ShaderAttributes lowered )
	{
		foreach ( var (token, value) in managed.Values )
		{
			if ( value is not null && value.GetType() == typeof( ManagedRenderAttributes.TextureValue ) && BindingOf( token ) is { } binding )
				LowerTexture( binding, (ManagedRenderAttributes.TextureValue)value, lowered );
		}
	}

	void LowerTexture( TokenBinding binding, ManagedRenderAttributes.TextureValue value, ShaderAttributes lowered )
	{
		var (texture, mip) = value;
		if ( texture is null ) return;
		loweredTextures++;
		if ( mip < 0 ) lowered.SetById( binding.Id, BoundTextureOf( texture ) );
		else lowered.SetById( binding.Id, context.View( BoundTextureOf( texture ), mip ) );
	}

	ShaderFile Require( Material material ) => ShaderOf( material ) ?? throw new NotSupportedException( $"{material} has no compiled shader Borealis.Gpu can load" );

	/// <summary>
	/// The shader a draw with <paramref name="material"/> draws with, and the material's compiled file as the drawer's
	/// material (<see cref="ShaderDrawer.Material"/>: its features, constants and textures), as native binds a material's mode.
	/// A copy reads its original's file and a material made in code its shader's defaults (<see cref="Material.MadeFromShader"/>),
	/// with what code set on it (<see cref="Material.SetInCode"/>) over them.
	/// </summary>
	ShaderFile Prepare( Material material ) => Prepare( material, Require( material ) );

	ShaderFile Prepare( Material material, ShaderFile shader )
	{
		// The shader by the material's name, which also learns the names its attributes lower to; the file's values only
		// when it names the same shader
		var values = MaterialValues ? MaterialFile( material ) : null;
		drawer.Material = values is not null && ReferenceEquals( values.Shader, shader ) ? values : null;
		return shader;
	}

	/// <summary>The shader to draw <paramref name="material"/> with, or for a surface-only material the scene's surface material. Throws as <see cref="Require"/> when it has neither.</summary>
	(ShaderFile Shader, SurfaceMaterial Surface) PrepareDraw( Material material )
	{
		var shader = ShaderOf( material );
		if ( shader is null || shader[global::Borealis.Source2.Shaders.ShaderProgramType.Vertex] is null )
		{
			if ( SurfaceOf( material ) is { } surface ) return (null, surface);
		}

		return (shader is null ? Prepare( material ) : Prepare( material, shader ), null);
	}

	/// <summary>
	/// The material the scene draws for an engine material on this device (<c>MirrorContent.Material</c>: its compiled file,
	/// or for one made or changed in code a material of its own with what code set on it, following later sets). Null when
	/// the scene has none on this device. Set by Sandbox.Rendering.
	/// </summary>
	internal static Func<BorealisGpuRenderBackend, Material, global::Borealis.GpuMaterial> SceneMaterial { get; set; }

	/// <summary>
	/// The surface a material's draws use where its shader is a surface alone: the very material the scene draws it with
	/// (<see cref="SceneMaterial"/>), so code's values, textures and features reach both alike. Null when it isn't one.
	/// </summary>
	SurfaceMaterial SurfaceOf( Material material ) => material is null ? null : SceneMaterial?.Invoke( this, material ) as SurfaceMaterial;

	/// <summary>Draw with materials' compiled values (the default); off draws their shaders' defaults, for comparisons.</summary>
	internal static bool MaterialValues { get; set; } = true;

	// A material's file as the drawer takes it, and with what code set on it, the version of those sets it has
	sealed class MaterialBacking
	{
		public BackendMaterial File;
		public int Version;

		/// <summary>The features set in code it last took.</summary>
		public IReadOnlyDictionary<string, int> Features;
	}

	readonly ConditionalWeakTable<Material, MaterialBacking> materialFiles = new();
	readonly Material[] backingKeys = new Material[4];
	readonly MaterialBacking[] backingValues = new MaterialBacking[4];
	int backingNext;

	BackendMaterial MaterialFile( Material material )
	{
		if ( material is null ) return null;
		MaterialBacking known = null;
		for ( int i = 0; i < backingKeys.Length; i++ )
		{
			if ( ReferenceEquals( backingKeys[i], material ) ) { known = backingValues[i]; break; }
		}

		if ( known is null && materialFiles.TryGetValue( material, out known ) )
		{
			(backingKeys[backingNext], backingValues[backingNext]) = (material, known);
			backingNext = (backingNext + 1) & 3;
		}

		if ( known is not null )
		{
			// Code set more on it since: its own copy of the file takes the new values and features
			if ( known.Version != material.SetInCodeVersion && known.File is not null )
			{
				// Each value and texture on its own, without lowering the set into dictionaries: code that animates a value
				// changes it every frame
				if ( material.SetInCode is Dictionary<string, object> changed )
				{
					foreach ( var (name, value) in changed )
					{
						if ( value is Vector4 v ) services.SetValueOnMaterial( known.File, name, [v.x, v.y, v.z, v.w] );
						else if ( value is Texture { IsValid: true } texture ) services.SetTextureOnMaterial( known.File, name, BorealisViewOf( texture ) );
					}
				}

				// Features only when they changed: each change replaces them whole
				if ( !ReferenceEquals( known.Features, material.FeaturesSetInCode ) ) services.SetFeaturesOnMaterial( known.File, material.FeaturesSetInCode );
				known.Features = material.FeaturesSetInCode;
				known.Version = material.SetInCodeVersion;
			}

			return known.File;
		}

		// A copy's path is its original's file (FilePath); as the .shader draws it, also where a surface stands in for its shader in
		// the scene. One with values or features set in code is a file of its own with them over the file's; one made in code from
		// a shader is that shader's defaults with them over
		var path = material.FilePath;
		BackendMaterial file = null;
		if ( material.MadeFromShader is { } shader )
		{
			// The shader draws with it (a package's shader compiled at run time is only known by its draw's name), so its
			// values reach the draw: Unicycle Frenzy's speed lines set their intensity to 0 at rest
			file = (ShaderOf( material ) is { } drawn ? services.CreateMaterial( material.Name ?? path ?? shader, drawn ) : ((Borealis.GpuDevice)services).CreateMaterial( material.Name ?? path ?? shader, ShaderPath( shader ) )) as BackendMaterial;
			if ( file?.Shader is null ) file = null;
			else if ( material.SetInCode is { Count: > 0 } set )
			{
				var (values, textures) = LowerSetInCode( set );
				services.SetOnMaterial( file, values, textures );
			}

			services.SetFeaturesOnMaterial( file, material.FeaturesSetInCode );
		}
		else if ( !string.IsNullOrEmpty( path ) && files.Find( path ) is not null )
		{
			if ( material.SetInCode is { Count: > 0 } || material.FeaturesSetInCode is { Count: > 0 } )
			{
				var (values, textures) = LowerSetInCode( material.SetInCode ?? new Dictionary<string, object>() );
				file = services.LoadMaterial( path, values, textures, $"{path} (set in code)" ) as BackendMaterial;
				services.SetFeaturesOnMaterial( file, material.FeaturesSetInCode );
			}
			else file = services.ShaderMaterial( path );
		}

		materialFiles.Add( material, new MaterialBacking { File = file, Version = material.SetInCodeVersion, Features = material.FeaturesSetInCode } );
		return file;
	}

	/// <summary>A material's shader name as a loadable shader path.</summary>
	internal static string ShaderPath( string shader )
	{
		if ( string.IsNullOrWhiteSpace( shader ) ) return "";
		shader = shader.Replace( '\\', '/' ).TrimStart( '/' );
		if ( shader.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) shader = shader[..^2];
		if ( shader.EndsWith( ".vfx", StringComparison.OrdinalIgnoreCase ) ) shader = shader[..^4] + ".shader";
		if ( !shader.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) shader += ".shader";
		return shader.Contains( '/' ) ? shader : $"shaders/{shader}";
	}

	/// <summary>Values set on a material in code as shader variables: vectors, and textures as this device's.</summary>
	(Dictionary<string, float[]> Values, Dictionary<string, Borealis.GpuTexture> Textures) LowerSetInCode( IReadOnlyDictionary<string, object> set )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase );
		var textures = new Dictionary<string, Borealis.GpuTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in set )
		{
			if ( value is Vector4 v ) values[name] = [v.x, v.y, v.z, v.w];
			else if ( value is Texture { IsValid: true } texture ) textures[name] = BorealisViewOf( texture );
		}

		return (values, textures);
	}

	/// <summary>Called on the main thread with no block open: work that runs at once on Source 2's device gets a block of its own.</summary>
	bool OutsideBlock
	{
		get
		{
			FromMain();
			return list is null && !Graphics.IsActive && ThreadSafe.IsMainThread;
		}
	}

	GpuCommandList Recording => list ?? throw new InvalidOperationException( "Borealis.Gpu draws need a render block (Graphics.Scope.CreateOn)." );

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport()
	{
		var v = context.Viewport;
		return new RenderViewport( new Rect( v.X, v.Y, v.Z, v.W ), DepthRange.Min, DepthRange.Max );
	}

	/// <summary>The rectangle, and the depth range draws map to (none given: 0 to 1), as Source 2's viewport has both.</summary>
	public override void SetViewport( in RenderViewport viewport )
	{
		SetViewport( viewport.Rect );
		DepthRange = viewport.MaxZ > viewport.MinZ ? (viewport.MinZ, viewport.MaxZ) : (0, 1);
	}

	public override void SetViewport( Rect rect ) => context.Viewport = new Num.Vector4( rect.Left, rect.Top, rect.Width, rect.Height );

	public override void BindRenderTarget( RenderTarget target ) => BindRenderTarget( target, false );

	/// <summary>
	/// Bind <paramref name="target"/>, its colour written through its sRGB view when <paramref name="srgbWrite"/> (and its
	/// format has one), as Source 2 writes an 8-bit texture a camera renders into.
	/// </summary>
	internal void BindRenderTarget( RenderTarget target, bool srgbWrite ) => BindRenderTarget( target, srgbWrite, 0 );

	/// <summary>
	/// <see cref="BindRenderTarget(RenderTarget, bool)"/>, drawing into layer <paramref name="depthSlice"/> of its depth. A target
	/// with no colour draws into its depth alone (a shadow map's slice).
	/// </summary>
	internal void BindRenderTarget( RenderTarget target, bool srgbWrite, int depthSlice )
	{
		_ = Recording;
		EngineWrites( target.ColorTarget );
		EngineWrites( target.DepthTarget );
		context.SetTarget( target.ColorTarget is { } color ? TextureOf( color ) : default, target.DepthTarget is { } depth ? TextureOf( depth ) : default, srgbWrite, depthSlice );
	}

	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil )
	{
		_ = Recording;
		if ( clearColor ) context.Clear( new Num.Vector4( color.r, color.g, color.b, color.a ) );
		if ( clearDepth || clearStencil ) context.ClearDepth();
	}

	public override void ClearTexture( Texture texture, Color color )
	{
		_ = Recording;
		EngineWrites( texture, whole: true );
		context.ClearTexture( TextureOf( texture ), new Num.Vector4( color.r, color.g, color.b, color.a ) );
	}

	//
	// Draws
	//

	static PrimitiveTopology Topology( Graphics.PrimitiveType type ) => type switch
	{
		Graphics.PrimitiveType.Points => PrimitiveTopology.PointList,
		Graphics.PrimitiveType.Lines => PrimitiveTopology.LineList,
		Graphics.PrimitiveType.LineStrip => PrimitiveTopology.LineStrip,
		Graphics.PrimitiveType.Triangles => PrimitiveTopology.TriangleList,
		Graphics.PrimitiveType.TriangleStrip => PrimitiveTopology.TriangleStrip,
		_ => throw new NotSupportedException( $"{type} on Borealis.Gpu" ),
	};

	public override unsafe void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType )
	{
		_ = Recording;
		var layout = LayoutOf( vertexType );
		if ( layout.Stride > vertexSize ) throw new ArgumentException( $"{vertexType} vertex layout is larger than the vertex type" );

		var (shader, surface) = PrepareDraw( material );
		var bytes = new ReadOnlySpan<byte>( (void*)vertices, vertexSize * vertexCount );
		// CPU vertices hold plain normals and tangents, as a native view tells its draws (UncompressedTangentFrame)
		var lowered = LoweredForPending( attributes );
		if ( indices == IntPtr.Zero )
		{
			if ( surface is not null ) context.DrawSurfaceVertices( surface, lowered, bytes, layout, vertexCount, Topology( primitiveType ) );
			else context.DrawVertices( shader, lowered, bytes, layout, vertexCount, Topology( primitiveType ) );
			return;
		}

		// Indexed: the vertices and 16-bit indices in this frame's upload memory
		var vertexUpload = list.AllocateUpload( bytes.Length, 16 );
		bytes.CopyTo( vertexUpload.Data );
		var indexBytes = new ReadOnlySpan<byte>( (void*)indices, indexCount * sizeof( ushort ) );
		var indexUpload = list.AllocateUpload( indexBytes.Length, 16 );
		indexBytes.CopyTo( indexUpload.Data );
		var geometry = new GeometryDraw
		{
			Vertices = vertexUpload.Buffer,
			VertexOffset = vertexUpload.Offset,
			Layout = layout,
			Indices = indexUpload.Buffer,
			IndexOffset = indexUpload.Offset,
			IndexFormat = IndexFormat.UInt16,
			Topology = Topology( primitiveType ),
			Count = indexCount,
		};
		if ( surface is not null ) context.DrawSurface( surface, lowered, geometry );
		else context.Draw( shader, lowered, geometry );
	}

	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType )
	{
		_ = Recording;
		pendingLayout = vertexType is null ? null : LayoutOf( vertexType );
		pendingMaterial = material;
		pendingShader = PrepareDraw( material ).Shader;
		pendingAttributes = attributes;
		return true;
	}

	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount ) => DrawPending( new GeometryDraw
	{
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		Count = indexBuffer.ElementCount,
		Instances = instanceCount,
	} );

	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount ) => DrawPending( new GeometryDraw
	{
		Vertices = BufferOf( vertexBuffer ),
		Layout = pendingLayout,
		Topology = Topology( primitiveType ),
		First = startVertex,
		Count = vertexCount,
	} );

	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount ) => DrawPending( new GeometryDraw
	{
		Vertices = BufferOf( vertexBuffer ),
		Layout = pendingLayout,
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		First = startIndex,
		Count = indexCount,
	} );

	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => DrawPending( new GeometryDraw
	{
		Vertices = vertexBuffer is null ? default : BufferOf( vertexBuffer ),
		Layout = vertexBuffer is null ? null : pendingLayout,
		Topology = Topology( primitiveType ),
		Arguments = BufferOf( indirectBuffer ),
		ArgumentsOffset = bufferOffset,
		DrawCount = (int)drawCount,
		Stride = (int)stride,
	} );

	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => DrawPending( new GeometryDraw
	{
		Vertices = vertexBuffer is null ? default : BufferOf( vertexBuffer ),
		Layout = vertexBuffer is null ? null : pendingLayout,
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		Arguments = BufferOf( indirectBuffer ),
		ArgumentsOffset = bufferOffset,
		DrawCount = (int)drawCount,
		Stride = (int)stride,
	} );

	//
	// Models
	//

	/// <summary>
	/// A model at a LOD (0 with none), an instance per transform: the transforms written as native writes them into the
	/// frame's transform buffer, which the model's shaders read through their instance ids.
	/// </summary>
	public override void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes )
	{
		_ = Recording;
		if ( transforms.IsEmpty ) return;
		var geometry = GeometryOf( model, lodLevel ?? 0 );
		if ( geometry.Draws.Length == 0 ) return;

		var (buffer, ids) = UploadTransforms( transforms );
		if ( CullView is { } view && DrawCulled( model, geometry, attributes, view, buffer, (int)(ids.Offset / sizeof( uint )), transforms.Length ) ) return;
		DrawModelGeometry( geometry, attributes, transforms.Length, transforms: buffer, ids: ids );
	}

	/// <summary>
	/// <paramref name="count"/> instances of each of the model's LOD 0 draw calls, every mesh group, with its material's
	/// shader: what the terrain clipmap draws its meshlets with, its shaders reading <c>SV_InstanceID</c>.
	/// </summary>
	public override void DrawModel( Model model, int count, RenderAttributes attributes )
	{
		_ = Recording;
		DrawModelGeometry( GeometryOf( model, 0 ), attributes, count );
	}

	/// <summary>The model's LOD 0 draw calls, each drawn with its indirect arguments, its shaders reading <c>SV_InstanceID</c>.</summary>
	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes )
	{
		_ = Recording;
		DrawModelGeometry( GeometryOf( model, 0 ), attributes, 1, BufferOf( arguments ), argumentsOffset );
	}

	/// <summary>
	/// The model's draw calls at a LOD, each drawn with its indirect arguments, instance i reading transform i of
	/// <paramref name="transforms"/> as <c>g_TransformBuffer</c>.
	/// </summary>
	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes )
	{
		_ = Recording;
		var geometry = GeometryOf( model, lodLevel );
		if ( geometry.Draws.Length == 0 ) return;
		DrawModelGeometry( geometry, attributes, 1, BufferOf( arguments ), argumentsOffset, BufferOf( transforms ), InstanceIds( 0, transforms.ElementCount ) );
	}

	//
	// Copies, grabs and mips
	//

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice )
	{
		if ( OutsideBlock )
		{
			// Outside a render block a copy is recorded with the update's uploads, before the next block draws
			using ( BeginUpdateWork() ) CopyTexture( source, destination, sourceMip, sourceSlice, destinationMip, destinationSlice );
			return;
		}

		_ = Recording;
		var from = SampledTextureOf( source );
		EngineWrites( destination );
		var to = TextureOf( destination );
		if ( sourceMip == destinationMip && sourceSlice == 0 && destinationSlice == 0 )
		{
			context.CopyTexture( from, to, sourceMip );
			return;
		}

		if ( sourceSlice >= ArrayLayersOf( gpu.InfoOf( from ) ) || destinationSlice >= ArrayLayersOf( gpu.InfoOf( to ) ) )
			throw new ArgumentOutOfRangeException( nameof( sourceSlice ), "Copies between array slices the textures don't have" );
		context.CopyTexture( from, sourceMip, sourceSlice, to, destinationMip, destinationSlice );
	}

	public override void ResolveColor( Texture destination, Rect viewport )
	{
		_ = Recording;
		EngineWrites( destination );
		var saved = context.Viewport;
		SetViewport( viewport );
		context.CopyTarget( TextureOf( destination ) );
		context.Viewport = saved;
	}

	[ThreadStatic] static Texture outputDepth;

	/// <summary>
	/// The finished frame's depth while the stages that draw over its output run, whose target has none: what
	/// <see cref="ResolveDepth"/> grabs on this thread (a game's own post processing at <c>AfterPostProcess</c>). Null otherwise.
	/// </summary>
	internal Texture OutputDepth
	{
		get => outputDepth;
		set => outputDepth = value;
	}

	/// <summary>
	/// Copy the viewport's part of the bound depth into <paramref name="destination"/>, as native resolves it. Multisampled depth
	/// into a single-sampled texture isn't done yet.
	/// </summary>
	public override void ResolveDepth( Texture destination, Rect viewport )
	{
		_ = Recording;
		EngineWrites( destination );

		// Over a frame's output (AfterPostProcess, AfterUI) no depth is bound: the frame's, as native's view still has it
		if ( !context.Depth.IsValid && OutputDepth is { } frameDepth )
		{
			var from = TextureOf( frameDepth );
			var to = TextureOf( destination );
			if ( !context.CopyDepth( from, to, new System.Numerics.Vector4( 0, 0, frameDepth.Width, frameDepth.Height ) ) )
			{
				var a = gpu.InfoOf( from );
				var b = gpu.InfoOf( to );
				throw new NotSupportedException( $"Copying the frame's depth ({a.Format} x{a.Samples} {a.Width}x{a.Height}) into {b.Format} x{b.Samples} {b.Width}x{b.Height} on Borealis.Gpu" );
			}
			return;
		}

		var saved = context.Viewport;
		SetViewport( viewport );
		var copied = context.CopyDepth( TextureOf( destination ) );
		context.Viewport = saved;
		if ( !copied ) throw new NotSupportedException( "Resolving multisampled depth on Borealis.Gpu" );
	}

	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip )
	{
		if ( OutsideBlock )
		{
			using ( BeginUpdateWork() ) GenerateMipMaps( texture, method, initialMip );
			return;
		}

		_ = Recording;
		EngineWrites( texture );
		switch ( method )
		{
			// Gaussian and GGX: downsample_cs, a mip a pass, its kernel by the method
			case Graphics.DownsampleMethod.GaussianBlur or Graphics.DownsampleMethod.GaussianBorder or Graphics.DownsampleMethod.GGX:
				context.GenerateMips( TextureOf( texture ), Shader( "shaders/downsample_cs.shader" ), (int)method, initialMip );
				break;

			// Box, min, max and min-max: downsample_spd, six mips a pass, as Source 2 runs them
			default:
				context.DownsampleSpd( TextureOf( texture ), Shader( "shaders/downsample_spd.shader" ), (int)method, initialMip );
				break;
		}
	}

	//
	// Compute
	//

	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ )
	{
		if ( OutsideBlock )
		{
			// Outside a render block a dispatch is recorded with the update's uploads (gizmo text from layout), before the next block
			using ( BeginUpdateWork() ) Dispatch( computeMaterial, attributes, threadsX, threadsY, threadsZ );
			return;
		}

		_ = Recording;
		drawer.Material = null;
		// Skipped while its variant is made or compiled (a scene object's, made on a worker), as a draw is; it runs once made
		if ( !context.Dispatch( Require( computeMaterial ), LowerForDispatch( attributes ), threadsX, threadsY, threadsZ ) && !context.DispatchPending )
			throw new InvalidOperationException( $"{computeMaterial} didn't dispatch on Borealis.Gpu" );
	}

	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset )
	{
		if ( OutsideBlock )
		{
			using ( BeginUpdateWork() ) DispatchIndirect( computeMaterial, attributes, indirectBuffer, bufferOffset );
			return;
		}

		_ = Recording;
		drawer.Material = null;
		if ( !context.DispatchIndirect( Require( computeMaterial ), LowerForDispatch( attributes ), BufferOf( indirectBuffer ), bufferOffset ) && !context.DispatchPending )
			throw new InvalidOperationException( $"{computeMaterial} didn't dispatch on Borealis.Gpu" );
	}

	/// <summary>
	/// Build a view's depth chain from <paramref name="depth"/> into <paramref name="chain"/> (RG32F with mips, made for
	/// compute) in the block being recorded, as Borealis's depth chain layer builds one on Source 2.
	/// </summary>
	internal void BuildDepthChain( Texture depth, Texture chain )
	{
		_ = Recording;
		context.BuildDepthChain( TextureOf( depth ), TextureOf( chain ), Shader( "shaders/downsample_spd.shader" ) );
	}

	/// <summary>
	/// <see cref="BuildDepthChain"/> once something in the block being recorded reads <paramref name="chain"/> or writes
	/// <paramref name="depth"/> (<see cref="GraphicsContext.DeferDepthChain"/>); not at all in a block where nothing does.
	/// </summary>
	internal void DeferDepthChain( Texture depth, Texture chain )
	{
		_ = Recording;
		context.DeferDepthChain( TextureOf( depth ), TextureOf( chain ), Shader( "shaders/downsample_spd.shader" ) );
	}

	//
	// Auto exposure
	//

	/// <summary>Auto exposure on this device: native's tonemap system, ported (<see cref="AutoExposure"/>).</summary>
	internal AutoExposure CreateExposure() => new( gpu );

	/// <summary>A camera's tonemap settings (<see cref="TonemapSystem"/>) as <see cref="AutoExposure"/> takes them.</summary>
	internal static AutoExposure.Settings Settings( in SceneTonemapParameters_t p ) => new()
	{
		Min = p.m_flAutoExposureMin,
		Max = p.m_flAutoExposureMax,
		Compensation = p.m_flExposureCompensationScalar,
		PercentTarget = p.m_flTonemapPercentTarget,
		PercentBrightPixels = p.m_flTonemapPercentBrightPixels,
		MinAverageLuminance = p.m_flTonemapMinAvgLum,
		Rate = p.m_flRate,
		AccelerateDown = p.m_flAccelerateExposureDown,
		SpeedUp = p.m_flExposureAdaptationSpeedUp,
		SpeedDown = p.m_flExposureAdaptationSpeedDown,
		EvSmoothingRange = p.m_flTonemapEVSmoothingRange,
	};

	/// <summary>
	/// Count this frame's luminance bucket over <paramref name="viewport"/> of <paramref name="color"/> (the frame's HDR
	/// colour, before tonemapping) in the block being recorded, as <c>RenderTools.RenderAutoExposure</c> does on Source 2.
	/// The bound colour target sets the size counted in and isn't drawn to. <paramref name="bucket"/>: the one
	/// <see cref="AutoExposure.Reserve"/> picked for the frame, or -1 to pick one now.
	/// </summary>
	internal void CountExposure( AutoExposure exposure, Texture color, Rect viewport, int samples = 1, int bucket = -1 )
	{
		_ = Recording;
		exposure.Draw( context, Shader( "shaders/tonemap_query.shader" ), TextureOf( color ), (int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height, samples, bucket );
	}

	//
	// Barriers: layouts are tracked, so a transition only orders memory
	//

	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after ) => Barrier();
	public override void UavBarrier( Texture texture ) => Barrier();
	public override void UavBarrier( GpuBuffer buffer ) => Barrier();

	void Barrier()
	{
		_ = Recording;
		context.MemoryBarrier();
	}

	//
	// Buffers
	//

	public override unsafe void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		// The main thread's write while the render thread draws doesn't wait for it (it may hold a lock the frame's drawing
		// takes): its bytes are kept and written, in order, once the frame is drawn
		if ( drawing && ThreadSafe.IsMainThread && !ThreadSafe.IsRenderThread )
		{
			StageBufferData( buffer, new ReadOnlySpan<byte>( (void*)data, size ), offset );
			return;
		}

		FromMain();
		WriteBufferData( buffer, new ReadOnlySpan<byte>( (void*)data, size ), offset );
	}

	void WriteBufferData( GpuBuffer buffer, ReadOnlySpan<byte> bytes, int offset )
	{
		var handle = BufferOf( buffer );

		// In a block, in order with its draws
		if ( list is not null )
		{
			context.UpdateBuffer( handle, bytes, offset );
			return;
		}

		// Outside one - a recording uploading its tables - copied once, into the upload memory of a list of their own that's
		// submitted before the next block, which is what draws with them: writing now would submit and wait for each
		DropStaleUploads();
		if ( uploads is null ) BeginUploads();

		// A second write to a buffer waits for the first, in case they overlap
		if ( !written.Add( handle ) )
		{
			uploads.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			written.Clear();
			written.Add( handle );
		}

		uploads.UpdateBuffer( handle, bytes, offset );
		UploadsRecorded++;
	}

	GpuCommandList uploads;
	readonly HashSet<BufferHandle> written = new();

	/// <summary>Buffer writes recorded outside blocks, for tests.</summary>
	public int UploadsRecorded { get; private set; }

	/// <summary>
	/// Begin the list of uploads submitted before the next block, in this engine frame's device frame. One started ahead on
	/// the frame thread (<see cref="HandOverFrame"/>) isn't joined for it: uploads need the frame to have switched, not the
	/// wait for the GPU after it, so the game's update-time writes (a painter's tables) don't wait for the GPU.
	/// </summary>
	void BeginUploads()
	{
		// The render thread records in the frame it draws; the main thread's waits for the next frame's switch
		if ( FrameAhead && !ThreadSafe.IsRenderThread ) WaitForAheadSwitch();
		else FollowFrame();
		uploads = gpu.BeginCommandList( QueueType.Graphics, "Engine uploads" );
		uploadsFrame = uploads.BeganInFrame;
		written.Clear();

		// Its writes wait for what was submitted before it: a buffer the game writes every update is still being read by the
		// last frame's draws, or written by its dispatches
		uploads.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
	}

	// The device frame the list of uploads began in: a list the device recycled and began again for someone else began later
	ulong uploadsFrame;

	/// <summary>Lists of uploads given up for the device having recycled them unsubmitted (<see cref="DropStaleUploads"/>), for tests.</summary>
	internal int StaleUploadLists { get; private set; }

	/// <summary>
	/// Forget the list of uploads when the device has taken it back: its frame's slot was recycled while it was still
	/// recording (it went unsubmitted for longer than the frames in flight), which ends it and returns it to the device's
	/// spares. Recording into it, or submitting it, would throw every frame from then on, or write into a list someone else
	/// began. Its writes are lost: said once, with where it began and where it was found, and the next write begins a new list.
	/// </summary>
	void DropStaleUploads()
	{
		if ( uploads is null || (uploads.IsRecording && uploads.BeganInFrame == uploadsFrame) ) return;

		StaleUploadLists++;
		if ( StaleUploadLists == 1 )
		{
			Log.Warning( $"Borealis.Gpu: the list of uploads begun in device frame {uploadsFrame} went unsubmitted until the device recycled it " +
				$"(found in frame {gpu.FrameNumber} on {(ThreadSafe.IsRenderThread ? "the render thread" : ThreadSafe.IsMainThread ? "the main thread" : "a worker")}, " +
				$"engine frame {Application.FrameCount}, {(drawing ? "while the render thread draws" : "nothing drawing")}); its writes and " +
				$"{uploadReadbacks.Count} readbacks are lost and a new list begins" );
		}

		// Its readbacks' copies went with it: their callbacks never come, and their buffers go back as a read one's do
		foreach ( var readback in uploadReadbacks ) readback.ReturnTo.Enqueue( readback.Buffer );
		uploadReadbacks.Clear();

		if ( ReferenceEquals( lendNext, uploads ) )
		{
			lendNext = null;
			inUploads = false;
		}

		uploads = null;
		written.Clear();
	}

	/// <summary>
	/// Submit the buffer writes made outside a block, visible to everything after them: before a block, a device frame,
	/// a readback, or freeing a buffer they may write.
	/// </summary>
	// Later: with the queue's next submission (the frame handed over's first, or the device frame's switch), in the same place
	// in its order, so the main thread doesn't submit, or wait for the queue behind a present, to hand a frame over
	void SubmitUploads( bool later = false )
	{
		DropStaleUploads();
		if ( uploads is null ) return;
		var list = uploads;
		uploads = null;
		try
		{
			list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		}
		catch
		{
			list.Discard();
			throw;
		}
		SubmittedReadbacks( uploadReadbacks, later ? gpu.SubmitLater( list ) : gpu.Submit( list ) );
	}

	/// <summary>Read a buffer's backing, after everything submitted so far. Not inside a block, which hasn't been submitted.</summary>
	public override void ReadBuffer( GpuBuffer buffer, Span<byte> data, int offset )
	{
		FromMain();
		if ( list is not null ) throw new NotSupportedException( "Buffer reads inside a render block on Borealis.Gpu" );
		if ( data.IsEmpty ) return;

		var handle = BufferOf( buffer );
		SubmitUploads();
		var readback = gpu.CreateBuffer( new BufferInfo { Size = data.Length, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			copy.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
			copy.CopyBuffer( handle, offset, readback, 0, data.Length );
			copy.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Host, MemoryAccess.HostRead );
			using ( RenderSync.Readback( "GpuBuffer.GetData" ) ) gpu.Wait( gpu.Submit( copy ) );
			gpu.Map( readback )[..data.Length].CopyTo( data );
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	//
	// Readback
	//

	/// <summary>
	/// A mip of a texture's backing as bytes, in its Borealis.Gpu format, after everything submitted so far.
	/// </summary>
	public byte[] ReadBytes( Texture texture, int mip = 0 )
	{
		FromMain();
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		var width = Math.Max( info.Width >> mip, 1 );
		var height = Math.Max( info.Height >> mip, 1 );
		var size = info.Format.RowPitch( width ) * info.Format.Rows( height );
		var readback = AcquirePixelReadback( size, out var kept );
		try
		{
			return ReadTextureToBuffer( handle, readback, mip )[..size].ToArray();
		}
		finally
		{
			if ( !kept ) gpu.Destroy( readback );
		}
	}

	/// <summary>The engine form of a backing's format, for converting what's read back, or null.</summary>
	internal static ImageFormat? ImageFormatOf( GpuFormat format ) => format.Linear() switch
	{
		GpuFormat.RGBA8Unorm => ImageFormat.RGBA8888,
		GpuFormat.BGRA8Unorm => ImageFormat.BGRA8888,
		GpuFormat.R8Unorm => ImageFormat.I8,
		GpuFormat.RG8Unorm => ImageFormat.IA88,
		GpuFormat.BGR10A2Unorm => ImageFormat.BGRA1010102,
		GpuFormat.RG32Uint => ImageFormat.RG3232,
		GpuFormat.RGBA32Uint => ImageFormat.RGBA32323232,
		GpuFormat.RGBA16Float => ImageFormat.RGBA16161616F,
		GpuFormat.RGBA16Unorm => ImageFormat.RGBA16161616,
		GpuFormat.RG16Float => ImageFormat.RG1616F,
		GpuFormat.R16Float => ImageFormat.R16F,
		GpuFormat.R16Unorm => ImageFormat.R16,
		GpuFormat.RG16Unorm => ImageFormat.RG1616,
		GpuFormat.R32Float => ImageFormat.R32F,
		GpuFormat.RG32Float => ImageFormat.RG3232F,
		GpuFormat.RGBA32Float => ImageFormat.RGBA32323232F,
		GpuFormat.R32Uint => ImageFormat.R32_UINT,
		GpuFormat.RGB10A2Unorm => ImageFormat.RGBA1010102,
		GpuFormat.RG11B10Float => ImageFormat.R11G11B10F,
		GpuFormat.BC1Unorm => ImageFormat.DXT1,
		GpuFormat.BC3Unorm => ImageFormat.DXT5,
		GpuFormat.BC4Unorm => ImageFormat.ATI1N,
		GpuFormat.BC5Unorm => ImageFormat.ATI2N,
		GpuFormat.BC6HUfloat => ImageFormat.BC6H,
		GpuFormat.BC7Unorm => ImageFormat.BC7,
		_ => null,
	};

	/// <summary>
	/// Mip 0 of an RGBA8 texture's backing, after everything submitted so far: what the device drew into it.
	/// </summary>
	public Color32[] ReadPixels( Texture texture )
	{
		FromMain();
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		if ( info.Format is not (GpuFormat.RGBA8Unorm or GpuFormat.RGBA8Srgb) ) throw new NotSupportedException( $"Reading {info.Format} back" );

		var readback = AcquirePixelReadback( (long)info.Width * info.Height * 4, out var kept );
		try
		{
			var bytes = ReadTextureToBuffer( handle, readback );
			var pixels = new Color32[info.Width * info.Height];
			var destination = MemoryMarshal.AsBytes( pixels.AsSpan() );
			bytes[..destination.Length].CopyTo( destination );
			return pixels;
		}
		finally
		{
			if ( !kept ) gpu.Destroy( readback );
		}
	}

	/// <summary>
	/// Start the device's next frame: what the frames before the last one used is recycled, and destroyed backings freed.
	/// </summary>
	public void NextFrame()
	{
		SubmitUploads();
		services.BeginFrame();
		PollReadbacks();
	}

	/// <summary>The renderer services' frame, which every device frame moves on: GPU-driven drawing recycles its per-frame memory by it.</summary>
	internal uint ServicesFrame => services.GpuDrivenFrame;

	public void Dispose()
	{
		StopFrameThread();
		BorealisGpuFrameLatency.Forget( gpu );
		Unregister( this );
		while ( workerUpdates.TryDequeue( out var update ) ) ArrayPool<byte>.Shared.Return( update.Data );
		if ( !lent ) list?.Discard();
		list = null;
		SubmitUploads();
		gpu.WaitIdle();
		DestroyReadbacks();

		foreach ( var handle in textureOwners )
		{
			context.Forget( handle );
			gpu.Destroy( handle );
		}
		foreach ( var handle in ownedBuffers ) gpu.Destroy( handle );
		if ( errorTexture.IsValid )
		{
			context.Forget( errorTexture );
			gpu.Destroy( errorTexture );
			errorTexture = default;
		}
		DestroyModelGeometry();
		FinishBusyRead();
		if ( services.PassTimer == busyTimer ) services.PassTimer = null;
		busyTimer?.Dispose();
		busyTimer = null;
		gpu.Destroy( pipelineTextures );
		foreach ( var package in mapVpks ) package.Dispose();
		mapVpks.Clear();
		foreach ( var (sampler, _) in samplers.Values ) gpu.Destroy( sampler );
		textureOwners.Clear();
		ownedBuffers.Clear();
		samplers.Clear();
		textures.Clear();
		buffers.Clear();

		context.Release();
		drawer.Dispose();
		services.Dispose();
		if ( ownsDevice ) gpu.Dispose();
	}
}
