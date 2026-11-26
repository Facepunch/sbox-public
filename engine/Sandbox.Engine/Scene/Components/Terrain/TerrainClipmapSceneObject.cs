using GpuDevice = Borealis.Gpu.Device;
using Sandbox.Rendering;
using static Sandbox.TerrainClipmap;

namespace Sandbox;

/// <summary>
/// Renders the terrain clipmap as instanced meshlets, frustum culled on the CPU.
/// </summary>
internal sealed class TerrainClipmapSceneObject : SceneCustomObject
{
	public int BlockSize;
	public float UnitsPerTexel;
	public float HeightScale;

	// A queued normal bake, run on the render thread before we draw so every pass this frame samples fresh data
	public void QueueBake( CommandList bake )
	{
		lock ( _bakeLock )
		{
			_pendingBake = bake;
			_pendingBakeEpoch = RenderSync.Overlapping && !RenderSync.OnRenderThread ? RenderSync.Epoch : -1;
		}
	}

	// The bake, and the hand-over it was queued after while a frame handed over could be drawing (else -1): that frame's
	// drawing leaves it to the next frame, whose height map updates it reads are written once the frame is drawn
	private CommandList _pendingBake;
	private long _pendingBakeEpoch = -1;
	private readonly System.Threading.Lock _bakeLock = new();

	/// <summary>The queued bake the frame drawing on this thread runs, taken once; null if none, or it's the next frame's.</summary>
	internal CommandList TakeBake()
	{
		lock ( _bakeLock )
		{
			if ( _pendingBake is null ) return null;
			if ( _pendingBakeEpoch >= 0 && _pendingBakeEpoch == RenderSync.Epoch && RenderSync.OnRenderThread ) return null;

			var bake = _pendingBake;
			_pendingBake = null;
			return bake;
		}
	}

	// The view's frustum in terrain-local space, refreshed whenever the view is culled
	private readonly Plane[] _cullPlanes = new Plane[6];

	// The main camera's clipmap centre, which shadow passes draw around. A camera pass uses its own camera's
	private Vector2 _clipCameraLocal;

	// What the visible meshlets were culled for, so a view's later passes - its prepass, then its opaque pass - reuse them
	// Native records a view's passes on job threads at once, so one culls while the others wait for it
	private (Frustum Frustum, Vector2 Camera, Transform Transform, float UnitsPerTexel, float HeightScale, int BlockSize)? _culledFor;
	private readonly System.Threading.Lock _cullLock = new();

	private Material _material;
	private ShadowFrame[] _shadowFrames;

	private struct ShadowFrame
	{
		public ulong Number;
		public int Used;
		public List<GpuBuffer<Meshlet>[]> Views;
	}

	private Tier[] _tiers = [];

	private struct Tier
	{
		public Model Mesh;
		public TerrainClipmapCuller Culler; // owns the layout, in upload order
		public GpuBuffer<Meshlet> FullBuffer;    // unchanged layout for fully visible tiers and shaders with unknown bounds
		public GpuBuffer<Meshlet> VisibleBuffer; // camera culls
		public Meshlet[] Visible;
		public int VisibleCount;
	}

	public TerrainClipmapSceneObject( SceneWorld world ) : base( world ) { }

	private static Tier GenerateTierMesh( Meshlet[] meshlets, int density, int blockSize, Material material )
	{
		var culler = new TerrainClipmapCuller( meshlets, blockSize );

		var fullBuffer = new GpuBuffer<Meshlet>( meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainMeshlets" );
		fullBuffer.SetData( culler.Meshlets );

		return new Tier
		{
			Mesh = Model.Builder.AddMesh( BuildBlockMesh( blockSize, density, material ) ).Create(),
			Culler = culler,
			FullBuffer = fullBuffer,
			VisibleBuffer = new GpuBuffer<Meshlet>( meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainMeshlets" ),
			Visible = new Meshlet[meshlets.Length],
		};
	}

	/// <summary>
	/// Build the render meshes from a meshlet layout. The first <paramref name="displacedLevels"/> LOD levels
	/// get a mesh subdivided <paramref name="density"/> times for displacement detail.
	/// </summary>
	public void Build( Meshlet[] meshlets, int blockSize, int density, int displacedLevels, Material material )
	{
		DisposeBuffers();
		_material = material;
		BlockSize = blockSize;

		// Two tiers: near is subdivided for displacement, far isn't. Either can be empty,
		// and a zero-length buffer is invalid, so skip the empty ones.
		var nearMeshlets = meshlets.Where( m => m.Level < displacedLevels ).ToArray();
		var farMeshlets = meshlets.Where( m => m.Level >= displacedLevels ).ToArray();

		var tiers = new List<Tier>( 2 );

		if ( nearMeshlets.Length > 0 )
			tiers.Add( GenerateTierMesh( nearMeshlets, density, blockSize, material ) );
		if ( farMeshlets.Length > 0 )
			tiers.Add( GenerateTierMesh( farMeshlets, 1, blockSize, material ) );

		_tiers = [.. tiers];
		lock ( _cullLock ) _culledFor = null;
	}

	internal void UpdateClipCamera( Vector3 cameraWorld )
	{
		var cameraLocal = Transform.PointToLocal( cameraWorld );
		var clipCamera = new Vector2( cameraLocal.x, cameraLocal.y );
		lock ( _cullLock ) _clipCameraLocal = clipCamera;
		Attributes.Set( "ClipCameraLocal", clipCamera );
	}

	private Vector2 UpdateView( Frustum frustum, Vector3 cameraWorld, Span<int> counts )
	{
		var cameraLocal = Transform.PointToLocal( cameraWorld );
		var clipCamera = new Vector2( cameraLocal.x, cameraLocal.y );

		// A view's passes all see the same meshlets: cull them once, not per pass
		var key = (frustum, clipCamera, Transform, UnitsPerTexel, HeightScale, BlockSize);

		lock ( _cullLock )
		{
			if ( _culledFor != key )
			{
				TerrainClipmapCuller.ToLocalPlanes( frustum, Transform, _cullPlanes );

				foreach ( ref var tier in _tiers.AsSpan() )
					Cull( ref tier, clipCamera );

				_culledFor = key;
			}

			// Every pass uploads the list itself. An upload runs where its pass does on the GPU, so one recorded by a
			// later pass comes too late for an earlier pass, which then draws the list of the view before
			for ( int i = 0; i < _tiers.Length; i++ )
			{
				ref var tier = ref _tiers[i];
				counts[i] = tier.VisibleCount;

				if ( counts[i] > 0 )
					tier.VisibleBuffer.SetData( tier.Visible.AsSpan( 0, counts[i] ) );
			}

			return clipCamera;
		}
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		// Run a queued normal bake once, before anything draws this frame
		TakeBake()?.ExecuteOnRenderThread();

		// Graphics.Attributes belongs to this pass. Use the same clipmap centre for both its cull and its shader.
		bool shadow = Graphics.LayerType == SceneLayerType.Shadow;

		Span<int> counts = stackalloc int[_tiers.Length];
		Vector2 clipCamera;
		if ( shadow )
		{
			lock ( _cullLock ) clipCamera = _clipCameraLocal;
		}
		else
		{
			clipCamera = UpdateView(
				Graphics.Frustum.Scaled( Terrain.MeshletFrustumScale, Graphics.CameraPosition, Graphics.FieldOfView > 0.0f ),
				Graphics.CameraPosition, counts );
		}

		Attributes.MergeTo( Graphics.Attributes );
		Graphics.Attributes.Set( "ClipCameraLocal", clipCamera );
		Graphics.Attributes.Set( "TerrainShadowPass", shadow );

		// Built-in terrain shaders skip vertex displacement in shadow passes. Other shaders may move vertices
		// outside the meshlet bounds, so their shadow passes must keep the whole layout.
		if ( shadow && Graphics.Backend is BorealisGpuRenderBackend backend && UnitsPerTexel > 0
			&& _material is { anonymousShader: null } material
			&& (material.ShaderNameFromCode ?? material.ShaderName) is "shaders/terrain.shader" or "shaders/terrain_surface.shader" )
		{
			DrawCulledShadow( backend.Device, clipCamera );
			return;
		}

		for ( int i = 0; i < _tiers.Length; i++ )
		{
			ref readonly var tier = ref _tiers[i];
			int count = shadow ? tier.Culler.Meshlets.Length : counts[i];
			if ( count <= 0 ) continue;

			DrawTier( tier.Mesh, shadow ? tier.FullBuffer : tier.VisibleBuffer, count );
		}
	}

	private void DrawCulledShadow( GpuDevice device, Vector2 clipCamera )
	{
		// Shadow depth may be clamped. Only the clip-space side planes can rule out a caster.
		Span<Plane> planes = stackalloc Plane[4];
		TerrainClipmapCuller.ToLocalPlanes( Graphics.Frustum, Transform, planes );

		Span<int> counts = stackalloc int[_tiers.Length];
		GpuBuffer<Meshlet>[] buffers = null;
		lock ( _cullLock )
		{
			for ( int i = 0; i < _tiers.Length; ++i )
			{
				ref readonly var tier = ref _tiers[i];
				// The scratch array can be reused once the upload has copied its bytes into this draw's list.
				int count = tier.Culler.Cull( planes, clipCamera, UnitsPerTexel, HeightScale, tier.Visible );
				counts[i] = count;
				if ( count == 0 || count == tier.Culler.Meshlets.Length ) continue;

				buffers ??= AcquireShadowBuffers( device );
				var buffer = buffers[i] ??= new GpuBuffer<Meshlet>( tier.Culler.Meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainShadowMeshlets" );
				buffer.SetData( tier.Visible.AsSpan( 0, count ) );
			}
		}

		for ( int i = 0; i < _tiers.Length; ++i )
		{
			int count = counts[i];
			if ( count == 0 ) continue;

			ref readonly var tier = ref _tiers[i];
			DrawTier( tier.Mesh, count == tier.Culler.Meshlets.Length ? tier.FullBuffer : buffers[i], count );
		}
	}

	private GpuBuffer<Meshlet>[] AcquireShadowBuffers( GpuDevice device )
	{
		// A draw has exclusive buffers until the GPU completes its frame slot. Even identical frusta can't
		// share a fresh upload: their command lists may execute in a different order than they were recorded.
		_shadowFrames ??= new ShadowFrame[device.FramesInFlight];
		ulong number = device.FrameNumber;
		ref var frame = ref _shadowFrames[(int)(number % (ulong)_shadowFrames.Length)];
		if ( frame.Number != number )
		{
			frame.Number = number;
			frame.Used = 0;
		}

		frame.Views ??= new();
		if ( frame.Used == frame.Views.Count )
			frame.Views.Add( new GpuBuffer<Meshlet>[_tiers.Length] );

		return frame.Views[frame.Used++];
	}

	private static void DrawTier( Model mesh, GpuBuffer<Meshlet> buffer, int count )
	{
		Graphics.Attributes.Set( "TerrainMeshlets", buffer );
		Graphics.DrawModelInstanced( mesh, count );
	}

	private void Cull( ref Tier tier, Vector2 clipCamera )
	{
		tier.VisibleCount = tier.Culler.Cull( _cullPlanes, clipCamera, UnitsPerTexel, HeightScale, tier.Visible );
	}

	private void DisposeBuffers()
	{
		foreach ( var tier in _tiers )
		{
			tier.FullBuffer?.Dispose();
			tier.VisibleBuffer?.Dispose();
		}

		if ( _shadowFrames is not null )
		{
			foreach ( var frame in _shadowFrames )
			{
				if ( frame.Views is null ) continue;
				foreach ( var buffers in frame.Views )
				{
					foreach ( var buffer in buffers )
						buffer?.Dispose();
				}
			}

			_shadowFrames = null;
		}

		_tiers = [];
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		base.OnNativeDestroy();
	}
}
