using NativeEngine;

namespace Sandbox;

/// <summary>
/// Lights lit sprites for every camera from froxels, 4x4x2 to a light cluster, lit from the clusters' lights around the lit
/// sprites and denoised against last frame's (<c>particle_lighting_cs.shader</c>), instead of per pixel.
/// </summary>
internal sealed class ParticleLightingSystem : GameObjectSystem<ParticleLightingSystem>, Component.IRenderThread
{
	// ClusteredCullingLayer's grid, and the froxels in each cluster as the shader's group
	const int ClustersX = 24, ClustersY = 16, ClustersZ = 48;
	const int FroxelsX = 4, FroxelsY = 4, FroxelsZ = 2;

	[ConVar( "debug_particle_lighting", ConVarFlags.Protected | ConVarFlags.Cheat )]
	static bool Debug { get; set; }

	sealed class Froxels : IDisposable
	{
		public readonly Texture Lighting = Volume( "lighting" );
		public readonly Texture[] History = [Volume( "history 0" ), Volume( "history 1" )];
		public int Current;
		public Matrix? LastWorldToProjection;
		public Vector3Int First, Count;

		public void Dispose()
		{
			Lighting.Dispose();
			History[0].Dispose();
			History[1].Dispose();
		}
	}

	/// <summary>
	/// What views this doesn't light bind instead, so lit sprites in them draw unlit.
	/// </summary>
	internal static Texture Unlit { get; } = Texture.CreateVolume( 1, 1, 1 ).WithData( [255, 255, 255, 255] ).WithName( "particle lighting unlit" ).Finish();

	readonly Dictionary<CameraComponent, Froxels> froxels = [];
	readonly List<CameraComponent> removed = [];
	ComputeShader shader;
	BBox? litBounds;

	public ParticleLightingSystem( Scene scene ) : base( scene )
	{
		// After the sprites upload, which finds their bounds
		Listen( Stage.FinishUpdate, 2, Update, "ParticleLighting" );
	}

	static Texture Volume( string name )
	{
		var texture = Texture.CreateVolume( ClustersX * FroxelsX, ClustersY * FroxelsY, ClustersZ * FroxelsZ, ImageFormat.R11G11B10F ).WithUAVBinding().WithName( $"particle {name}" ).Finish();
		texture.Clear( Color.Transparent );
		return texture;
	}

	void Update()
	{
		foreach ( var camera in froxels.Keys )
			if ( !camera.IsValid() ) removed.Add( camera );

		foreach ( var camera in removed )
		{
			froxels.Remove( camera, out var f );
			f.Dispose();
		}
		removed.Clear();

		litBounds = Graphics.IsAvailable ? Scene.GetSystem<SceneSpriteSystem>()?.LitBounds : null;
		if ( litBounds is null ) return;

		if ( Debug ) DrawDebug();

		shader ??= new ComputeShader( "shaders/particle_lighting_cs.shader" );

		foreach ( var f in froxels.Values )
			f.Current ^= 1;

		foreach ( var camera in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( camera.SceneCamera is not { } sceneCamera ) continue;

			// The editor viewport runs the scene camera's stages, so it reads the scene camera's froxels
			var owner = camera.IsSceneEditorCamera ? Scene.Camera ?? camera : camera;
			if ( !froxels.TryGetValue( owner, out var f ) )
				froxels[owner] = f = new Froxels();

			sceneCamera.Attributes.Set( "ParticleLighting", f.History[f.Current] );
		}
	}

	void Component.IRenderThread.OnRenderStage( CameraComponent camera, Sandbox.Rendering.Stage stage )
	{
		// After the shadow maps and light clusters, before translucency
		if ( stage != Sandbox.Rendering.Stage.AfterOpaque || litBounds is not { } bounds || !froxels.TryGetValue( camera, out var f ) )
			return;

		var frustum = Graphics.ViewFrustum;
		var worldToProjection = frustum.GetReverseZViewProjTranspose();
		var lastWorldToProjection = f.LastWorldToProjection ?? worldToProjection;
		f.LastWorldToProjection = worldToProjection;

		if ( !TryGetClusters( frustum, bounds, out var first, out var count ) )
			return;

		(f.First, f.Count) = (first, count);

		var attributes = Graphics.Attributes;
		attributes.Set( "ParticleLightingFirstCluster", first );
		attributes.Set( "ParticleLightingClusterCount", count );
		attributes.Set( "ParticleLightingRaw", f.Lighting );
		attributes.Set( "ParticleLightingOutRaw", f.Lighting );
		attributes.Set( "ParticleLightingHistory", f.History[f.Current ^ 1] );
		attributes.Set( "ParticleLightingOut", f.History[f.Current] );
		attributes.Set( "ParticleLightingLastWorldToProjection", lastWorldToProjection );
		attributes.Set( "ParticleLightingJitter", new Vector2Int( Random.Shared.Next( 256 ), Random.Shared.Next( 256 ) ) );

		// A group per cluster: light, then denoise
		for ( int pass = 0; pass < 2; pass++ )
		{
			attributes.SetCombo( "D_PASS", pass );
			shader.DispatchWithAttributes( attributes, count.x * FroxelsX, count.y * FroxelsY, count.z * FroxelsZ );
		}
		attributes.SetCombo( "D_PASS", 0 );
	}

	// Lit bounds in yellow, the clusters lit in each camera in cyan
	void DrawDebug()
	{
		var overlay = DebugOverlaySystem.Get( Scene );
		overlay.Box( litBounds.Value, Color.Yellow );

		foreach ( var (camera, f) in froxels )
		{
			if ( f.Count == default ) continue;

			var near = MathF.Max( camera.ZNear, 0.0001f );
			var far = MathF.Max( camera.ZFar, near + 0.01f );
			var forward = camera.WorldRotation.Forward;

			Vector3 Corner( int x, int y, int z )
			{
				var ray = camera.ScreenNormalToRay( new Vector3( x / (float)ClustersX, y / (float)ClustersY, 0 ) );
				var depth = near * MathF.Pow( far / near, z / (float)ClustersZ );
				return ray.Position + ray.Forward * (depth / Vector3.Dot( ray.Forward, forward ));
			}

			var (a, b) = (f.First, f.First + f.Count);
			Vector3 C( int i ) => Corner( (i & 1) != 0 ? b.x : a.x, (i & 2) != 0 ? b.y : a.y, (i & 4) != 0 ? b.z : a.z );

			// Cube edges join corners that differ in one bit
			for ( int i = 0; i < 8; i++ )
				for ( int bit = 1; bit < 8; bit <<= 1 )
					if ( (i & bit) == 0 ) overlay.Line( C( i ), C( i | bit ), Color.Cyan );
		}
	}

	/// <summary>
	/// The clusters a world box touches in this view, one wider each way, sliced as ClusteredCullingLayer slices them.
	/// </summary>
	static bool TryGetClusters( CFrustum frustum, BBox box, out Vector3Int first, out Vector3Int count )
	{
		first = count = default;

		var near = MathF.Max( frustum.GetCameraNearPlane(), 0.0001f );
		var far = MathF.Max( frustum.GetCameraFarPlane(), near + 0.01f );
		var position = frustum.GetCameraPosition();
		var forward = frustum.GetCameraAngles().Forward;
		System.Numerics.Matrix4x4 worldToProjection = frustum.GetReverseZViewProjTranspose();

		var uvMin = new Vector2( 1 );
		var uvMax = new Vector2( 0 );
		var depthMin = float.MaxValue;
		var depthMax = float.MinValue;
		var behind = false;

		for ( int i = 0; i < 8; i++ )
		{
			var corner = new Vector3( (i & 1) != 0 ? box.Maxs.x : box.Mins.x, (i & 2) != 0 ? box.Maxs.y : box.Mins.y, (i & 4) != 0 ? box.Maxs.z : box.Mins.z );
			var depth = Vector3.Dot( corner - position, forward );
			depthMin = MathF.Min( depthMin, depth );
			depthMax = MathF.Max( depthMax, depth );

			var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( corner, 1 ), worldToProjection );
			if ( clip.W <= 0 )
			{
				behind = true;
				continue;
			}

			var uv = new Vector2( clip.X / clip.W * 0.5f + 0.5f, 0.5f - clip.Y / clip.W * 0.5f );
			uvMin = Vector2.Min( uvMin, uv );
			uvMax = Vector2.Max( uvMax, uv );
		}

		if ( behind )
		{
			uvMin = new Vector2( 0 );
			uvMax = new Vector2( 1 );
		}

		if ( depthMax < near || depthMin > far || uvMax.x < 0 || uvMax.y < 0 || uvMin.x > 1 || uvMin.y > 1 )
			return false;

		int Slice( float depth ) => (int)(ClustersZ * MathF.Log( Math.Clamp( depth, near, far ) / near ) / MathF.Log( far / near ));

		first = new Vector3Int(
			Math.Max( (int)MathF.Floor( uvMin.x * ClustersX ) - 1, 0 ),
			Math.Max( (int)MathF.Floor( uvMin.y * ClustersY ) - 1, 0 ),
			Math.Max( Slice( depthMin ) - 1, 0 ) );

		var last = new Vector3Int(
			Math.Min( (int)MathF.Floor( uvMax.x * ClustersX ) + 1, ClustersX - 1 ),
			Math.Min( (int)MathF.Floor( uvMax.y * ClustersY ) + 1, ClustersY - 1 ),
			Math.Min( Slice( depthMax ) + 1, ClustersZ - 1 ) );

		count = new Vector3Int( last.x - first.x + 1, last.y - first.y + 1, last.z - first.z + 1 );
		return true;
	}

	public override void Dispose()
	{
		foreach ( var f in froxels.Values )
			f.Dispose();

		froxels.Clear();
		base.Dispose();
	}
}
