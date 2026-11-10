using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Renders a water surface with scene lighting, reflections and refraction.
/// The surface lies in the object's local XY plane, with a fluid volume below it.
/// </summary>
[Expose, Title( "Water Body" ), Category( "Rendering" ), Icon( "water" )]
public sealed partial class WaterBody : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Extend the surface to the horizon without a boundary. The visible patch follows the viewing camera
	/// while the water plane and texture coordinates remain fixed in the world.
	/// </summary>
	[Property, Group( "Surface" ), Order( -300 )]
	public bool Ocean { get; set; }

	/// <summary>
	/// Width and length of the surface in local inches.
	/// </summary>
	[Property, Group( "Surface" ), Order( -299 ), HideIf( nameof( Ocean ), true ), HideIf( nameof( UsesMeshBounds ), true )]
	public Vector2 Size { get; set; } = new( 2048, 2048 );

	/// <summary>
	/// Tag on generated surfaces, allowing cameras to exclude water.
	/// </summary>
	public const string SurfaceTag = "water-surface";

	GameObject _surface;
	string _surfaceTag;
	Material _builtMaterial;
	Vector2 _renderCenter;
	Vector2 _renderHalfSize;
	Vector2 _builtHalfSize;
	ClipmapLayout _builtLayout;
	float _builtRise;
	float _builtHorizontal;

	internal float CaptureDisplacement => RenderWaves ? MathF.Max( 0, WaveHeight ) : 0;
	float RenderHorizontalDisplacement => RenderWaves
		? MathF.Max( 4, MathF.Max( WaveLength, WaveWidth ) ) * Math.Clamp( WaveSteepness, 0, 1 ) / MathF.Tau : 0;

	ModelRenderer _renderer;
	CommandList[] _captureLists;
	int _captureList;
	TagSet _captureExclusions;
	static readonly List<CameraComponent> _cameras = [];

	/// <summary>
	/// Create the owned rendering surface. Dedicated servers do not create graphics resources.
	/// </summary>
	protected override void OnEnabled()
	{
		UpdateMeshBounds();
		UpdateVolume();
		if ( Application.IsDedicatedServer )
			return;
		UpdateCaustics();
		UpdateUnderwater();

		Scene.GetSystem<WaterEffectsSystem>();

		_surface = new GameObject( GameObject, false, "Water surface" );
		_surface.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
		_surfaceTag = Id.ToString( "N" );
		_surface.Tags.Add( SurfaceTag );
		_surface.Tags.Add( _surfaceTag );
		_renderer = _surface.Components.Create<ModelRenderer>();
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_captureLists = [new( "Water captures" ), new( "Water captures" )];
		_captureExclusions = new TagSet( [Id.ToString( "N" )] );
		_surface.Enabled = true;
		UpdateSurface( ViewingCamera() );
	}

	void UpdateSurface( CameraComponent viewer )
	{
		// A unit world transform cannot be represented beneath a collapsed parent scale.
		_surface.Enabled = WorldScale.x != 0 && WorldScale.y != 0 && WorldScale.z != 0;
		if ( !_surface.Enabled )
		{
			_renderer.ExecuteBefore = null;
			DestroyReflectionPlane();
			return;
		}

		var layout = GetClipmap( viewer );
		UpdateRenderDomain( viewer, layout );
		float rise = MathF.Max( 0.1f, CaptureDisplacement );
		float horizontal = RenderHorizontalDisplacement;
		var material = ActiveMaterial;
		if ( _builtHalfSize != _renderHalfSize || _builtLayout != layout
			|| _builtRise != rise || _builtHorizontal != horizontal
			|| _builtMaterial != material )
			RebuildMesh( layout, rise, horizontal, material );

		_surface.WorldTransform = new Transform( FromSurface( _renderCenter ), WorldRotation, 1 );
		var attributes = _renderer.Attributes;
		attributes.Set( "Origin", SurfaceOrigin );
		attributes.Set( "HalfSize", new Vector2( MathF.Max( 0.05f, MathF.Abs( Size.x * WorldScale.x ) * 0.5f ),
			MathF.Max( 0.05f, MathF.Abs( Size.y * WorldScale.y ) * 0.5f ) ) );
		attributes.Set( "WaterOcean", Ocean );
		attributes.Set( "DetailTime", DetailTime );
		attributes.Set( "GridCamera", viewer.IsValid() ? viewer.WorldPosition : SurfaceOrigin );
		attributes.Set( "ClipmapLevels", _builtLayout.Levels );
		attributes.Set( "ClipmapCells", _builtLayout.Cells );
		attributes.Set( "ClipmapBaseCell", _builtLayout.BaseCell );
		SetWaveAttributes( attributes );
		attributes.Set( "AxisX", WorldRotation.Forward );
		attributes.Set( "AxisY", WorldRotation.Left );
		attributes.Set( "SurfaceNormal", WorldRotation.Up );
		SetReflectionAttributes( attributes );
		UpdateReflectionPlane( attributes );
		SetOverlayAttributes( attributes );
	}

	/// <summary>
	/// Follow the view while preserving the body's mean plane and wave coordinates.
	/// An ocean's bounds are centred on the camera, quantized so camera movement doesn't rebuild the mesh.
	/// </summary>
	void UpdateRenderDomain( CameraComponent viewer, ClipmapLayout layout )
	{
		_renderCenter = Vector2.Zero;
		_renderHalfSize = new Vector2( MathF.Max( 0.05f, MathF.Abs( Size.x * WorldScale.x ) * 0.5f ),
			MathF.Max( 0.05f, MathF.Abs( Size.y * WorldScale.y ) * 0.5f ) );
		if ( !Ocean )
			return;

		if ( viewer.IsValid() )
		{
			var eye = ToSurface( viewer.WorldPosition );
			_renderCenter = new Vector2( MathF.Round( eye.x / 256 ) * 256, MathF.Round( eye.y / 256 ) * 256 );
		}

		// The coarsest level snaps to twice its cell size, so allow for that and the quantized centre.
		float half = (layout.Extent + 2) * layout.BaseCell * MathF.Pow( 2, layout.Levels - 1 ) + 256;
		_renderHalfSize = new Vector2( half, half );
	}

	/// <summary>
	/// Upload the surface clipmap twice with one material: once facing up, and once with flipped winding and
	/// TexCoord1.y set for the faces looking down, which the shader shades as the underside.
	/// Bounds include horizontal crest compression and the complete vertical displacement.
	/// </summary>
	void RebuildMesh( ClipmapLayout layout, float rise, float horizontal, Material material )
	{
		var half = _renderHalfSize;
		var (vertices, indices) = BuildClipmap( layout, half );
		var bounds = new BBox( new Vector3( -half.x - horizontal, -half.y - horizontal, -rise ),
			new Vector3( half.x + horizontal, half.y + horizontal, rise ) );

		Mesh Side( Vertex[] vertices, int[] indices )
		{
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
			mesh.CreateIndexBuffer( indices.Length, indices );
			mesh.Bounds = bounds;
			return mesh;
		}

		var (underVertices, underIndices) = BuildUnderside( vertices, indices );
		_renderer.Model = Model.Builder.AddMesh( Side( vertices, indices ) ).AddMesh( Side( underVertices, underIndices ) ).Create();
		_builtHalfSize = half;
		_builtLayout = layout;
		_builtRise = rise;
		_builtHorizontal = horizontal;
		_builtMaterial = material;
	}

	/// <summary>
	/// Record surface captures for the editor viewport or main camera.
	/// Simultaneous independent viewing cameras and custom-projection captures are not supported yet.
	/// </summary>
	protected override void OnPreRender()
	{
		if ( !_renderer.IsValid() )
			return;

		var viewer = ViewingCamera();
		UpdateSurface( viewer );
		if ( !_surface.Enabled || !viewer.IsValid() )
		{
			_renderer.ExecuteBefore = null;
			return;
		}

		var normal = WorldRotation.Up;
		float eyeHeight = Vector3.Dot( viewer.WorldPosition - SurfaceOrigin, normal );
		bool below = ViewSurfaceDepth( viewer.WorldPosition ) > 0;
		if ( below )
			normal = -normal;

		var captures = _captureLists[_captureList ^= 1];
		captures.Reset();
		captures.Attributes.Set( "HasRefractionTexture", false );
		if ( viewer.CustomProjectionMatrix is null )
		{
			bool unclipped = viewer.Orthographic || MathF.Abs( eyeHeight ) <= MathF.Max( 0.5f, CaptureDisplacement ) + viewer.ZNear * 2;
			if ( MaterialOpacity( ActiveMaterial, below ) < 1 )
				AddRefractionCapture( captures, viewer, normal, unclipped );
		}

		_renderer.ExecuteBefore = captures;
	}

	CameraComponent ViewingCamera() => FindViewingCamera( Scene );

	/// <summary>
	/// The camera water captures render for: the editor viewport's camera in the editor, else the scene's main camera.
	/// </summary>
	internal static CameraComponent FindViewingCamera( Scene scene )
	{
		if ( scene.IsEditor )
		{
			_cameras.Clear();
			scene.GetAll( _cameras );
			foreach ( var camera in _cameras )
			{
				if ( (camera.GameObject.Flags & GameObjectFlags.EditorOnly) == 0 )
					continue;

				_cameras.Clear();
				return camera;
			}
			_cameras.Clear();
		}
		return scene.Camera;
	}

	/// <summary>
	/// Clamp direct assignments as well as console input to the supported capture divisors.
	/// </summary>
	internal static int CaptureSizeFactor( int downsample ) => Math.Clamp( downsample, 1, 8 );

	/// <summary>
	/// Release the generated surface and capture commands.
	/// </summary>
	protected override void OnDisabled()
	{
		DestroyVolume();
		DestroyCaustics();
		DestroyUnderwater();
		DestroyReflectionPlane();
		if ( _renderer.IsValid() )
			_renderer.ExecuteBefore = null;
		_surface?.Destroy();
		_surface = null;
		_renderer = null;
		_builtMaterial = null;
		_builtHalfSize = default;
		_builtLayout = default;
		_captureLists = null;
		_captureExclusions = null;
		_reflectionSkies.Clear();
		DestroyOverlays();
		_boundsUndoScope?.Dispose();
		_boundsUndoScope = null;
		_boundsDragFrame = null;
	}

	/// <summary>
	/// Draw the surface footprint and fluid depth.
	/// </summary>
	protected override void DrawGizmos()
	{
		if ( !Gizmo.Pressed.Any )
		{
			_boundsUndoScope?.Dispose();
			_boundsUndoScope = null;
			_boundsDragFrame = null;
		}

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		// The bounds control accumulates its drag from the box captured at the press, so the box
		// must stay in that press's frame. Following the moving origin would reapply the offset every frame.
		var frame = _boundsDragFrame ?? new Transform( SurfaceOrigin, WorldRotation, 1 );
		Gizmo.Draw.Color = new Color( 0.1f, 0.65f, 0.85f, 0.8f );
		Gizmo.Transform = frame;
		if ( Ocean )
		{
			Gizmo.Draw.Line( Vector3.Forward * -128, Vector3.Forward * 128 );
			Gizmo.Draw.Line( Vector3.Left * -128, Vector3.Left * 128 );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Down * WorldDepth );
			return;
		}
		var top = frame.PointToLocal( SurfaceOrigin );
		var half = new Vector3( MathF.Abs( Size.x * WorldScale.x ) * 0.5f, MathF.Abs( Size.y * WorldScale.y ) * 0.5f, 0 );
		var bounds = new BBox( top + (-half).WithZ( -WorldDepth ), top + half );
		Gizmo.Draw.LineBBox( bounds );
		// A mesh sets the bounds; it is edited with the mesh tools instead.
		if ( Gizmo.IsSelected && !UsesMeshBounds && Gizmo.Control.BoundingBox( "Bounds", bounds, out var newBounds ) )
		{
			_boundsUndoScope ??= Scene.Editor?.UndoScope( "Resize Water Body" )
				.WithComponentChanges( this ).WithGameObjectChanges( GameObject, GameObjectUndoFlags.Properties ).Push();
			_boundsDragFrame ??= frame;
			ResizeVolume( newBounds, frame );
		}
	}
}
