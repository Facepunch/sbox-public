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
	public CommandList PendingBake;

	private Frustum _cullFrustum;
	private Vector2 _clipCameraLocal;

	private Tier[] _tiers = [];

	private struct Tier
	{
		public Model Mesh;
		public Meshlet[] Meshlets;
		public GpuBuffer<Meshlet> FullBuffer;    // the whole layout, drawn by shadow passes
		public GpuBuffer<Meshlet> VisibleBuffer; // frustum-culled subset, refreshed once per frame
		public Meshlet[] Visible;
		public int VisibleCount;
	}

	public TerrainClipmapSceneObject( SceneWorld world ) : base( world ) { }

	private static Tier GenerateTierMesh( Meshlet[] meshlets, int density, int blockSize, Material material )
	{
		var fullBuffer = new GpuBuffer<Meshlet>( meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainMeshlets" );
		fullBuffer.SetData( meshlets );

		return new Tier
		{
			Mesh = Model.Builder.AddMesh( BuildBlockMesh( blockSize, density, material ) ).Create(),
			Meshlets = meshlets,
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
	}

	public void UpdateView( Frustum frustum, Vector3 cameraWorld )
	{
		_cullFrustum = frustum;

		var cameraLocal = Transform.PointToLocal( cameraWorld );
		_clipCameraLocal = new Vector2( cameraLocal.x, cameraLocal.y );

		Attributes.Set( "ClipCameraLocal", _clipCameraLocal );

		foreach ( ref var tier in _tiers.AsSpan() )
			Cull( ref tier );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		// Run a queued normal bake once, before anything draws this frame
		var bake = PendingBake;
		PendingBake = null;
		bake?.ExecuteOnRenderThread();

		// Shadow passes need off-screen casters, so they draw the whole clipmap;
		// camera passes draw the culled subset. Graphics.Attributes is per-pass,
		// so these don't stomp other passes recording at the same time.
		bool shadow = Graphics.LayerType == SceneLayerType.Shadow;

		Attributes.MergeTo( Graphics.Attributes );
		Graphics.Attributes.Set( "TerrainShadowPass", shadow );

		foreach ( ref var tier in _tiers.AsSpan() )
		{
			int count = shadow ? tier.Meshlets.Length : tier.VisibleCount;
			if ( count <= 0 ) continue;

			Graphics.Attributes.Set( "TerrainMeshlets", shadow ? tier.FullBuffer : tier.VisibleBuffer );
			Graphics.DrawModelInstanced( tier.Mesh, count );
		}
	}

	private void Cull( ref Tier tier )
	{
		var terrainTransform = Transform;

		// Meshlets are level-ordered, so per-level constants only need recomputing when the level changes
		int level = -1;
		float vertexStep = 0, increment = 0;
		Vector2 center = default;

		int vis = 0;
		for ( int i = 0; i < tier.Meshlets.Length; i++ )
		{
			ref readonly var m = ref tier.Meshlets[i];

			if ( m.Level != level )
			{
				level = m.Level;
				vertexStep = UnitsPerTexel * (1 << level);
				increment = vertexStep * 2.0f;

				// Per-level snap matching the shader's roundToIncrement, so the AABBs track the drawn geometry
				center = _clipCameraLocal.SnapToGrid( increment );
			}

			if ( _cullFrustum.IsInside( GetMeshletAABB( in m, center, vertexStep, increment, terrainTransform ), partially: true ) )
				tier.Visible[vis++] = m;
		}

		if ( vis > 0 )
			tier.VisibleBuffer.SetData( tier.Visible.AsSpan( 0, vis ) );

		tier.VisibleCount = vis;
	}

	private BBox GetMeshletAABB( in Meshlet m, Vector2 center, float vertexStep, float increment, in Transform terrainTransform )
	{
		float ox = center.x + m.BlockOffset.x * vertexStep;
		float oy = center.y + m.BlockOffset.y * vertexStep;
		float ext = BlockSize * vertexStep;

		// Grow by one snap increment so sub-cell rounding / vertex displacement never culls an on-screen block
		var localBox = new BBox(
			new Vector3( ox - increment, oy - increment, -increment ),
			new Vector3( ox + ext + increment, oy + ext + increment, HeightScale + increment ) );

		return localBox.Transform( terrainTransform );
	}

	private void DisposeBuffers()
	{
		foreach ( var tier in _tiers )
		{
			tier.FullBuffer?.Dispose();
			tier.VisibleBuffer?.Dispose();
		}

		_tiers = [];
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		base.OnNativeDestroy();
	}
}
