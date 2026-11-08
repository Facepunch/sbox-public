using static Sandbox.ModelRenderer;

namespace Sandbox;

/// <summary>
/// Added automatically by Hammer to GameObjects that have a map mesh tied to them.
/// When a map is compiled the Model property is populated by the generated model.
/// </summary>
[Expose]
[Hide]
[Title( "Hammer Mesh" )]
[Category( "World" )]
[Icon( "hardware" )]
[HelpUrl( "https://docs.facepunch.com/s/sbox-dev/doc/hammer-mesh-PAmuywcUyo" )]
public class HammerMesh : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Gets populated at compile time, will be valid when loading from compiled map
	/// </summary>
	[Property, Hide]
	public Model Model { get; set; }

	/// <summary>
	/// Local bounds of the tied meshes while they're edited in Hammer, before a compile fills <see cref="Model"/>.
	/// Hammer only measures them for components that take their extent from the mesh, like a <see cref="WaterBody"/>.
	/// </summary>
	internal BBox? EditorBounds { get; set; }

	[Property, FeatureEnabled( "Renderer" )] public bool UseRenderer { get; set; } = true;
	[Property, FeatureEnabled( "Collision" )] public bool UseCollision { get; set; } = true;

	[Property, Feature( "Renderer", Icon = "free_breakfast" )]
	public Color Tint { get; set; } = Color.White;

	[Property, Feature( "Renderer" ), Title( "Cast Shadows" ), Category( "Lighting" )]
	public ShadowRenderType RenderType { get; set; } = ShadowRenderType.On;

	[Property, Feature( "Collision", Icon = "check_box_outline_blank" )]
	public bool Static { get; set; } = false;

	[Property, Feature( "Collision" ), Range( 0, 1 ), Group( "Surface Properties" )]
	public float? Friction { get; set; }

	[Property, Feature( "Collision" )]
	public Surface Surface { get; set; }

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	[Property, Feature( "Collision" ), Title( "Velocity" ), Group( "Surface Properties" )]
	public Vector3 SurfaceVelocity { get; set; }

	[Property, Feature( "Collision" )]
	public bool IsTrigger { get; set; }

	/// <summary>
	/// Called when a collider enters this trigger
	/// </summary>
	[Feature( "Collision" )]
	[Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<Collider> OnTriggerEnter { get; set; }

	/// <summary>
	/// Called when a collider exits this trigger
	/// </summary>
	[Feature( "Collision" )]
	[Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<Collider> OnTriggerExit { get; set; }

	private ModelRenderer _modelRenderer;
	private ModelCollider _modelCollider;

	protected override void OnEnabled()
	{
		if ( Model is null )
			return;

		if ( UseRenderer )
		{
			_modelRenderer = GetOrAddComponent<ModelRenderer>();
			_modelRenderer.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved;
			_modelRenderer.Model = Model;
			_modelRenderer.Tint = Tint;
			_modelRenderer.RenderType = RenderType;
		}

		if ( UseCollision )
		{
			_modelCollider = GetOrAddComponent<ModelCollider>();
			_modelCollider.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved;
			_modelCollider.Model = Model;
			_modelCollider.Friction = Friction;
			_modelCollider.Static = Static;
			_modelCollider.Surface = Surface;
			_modelCollider.IsTrigger = IsTrigger;
			_modelCollider.SurfaceVelocity = SurfaceVelocity;
			_modelCollider.OnTriggerEnter = OnTriggerEnter;
			_modelCollider.OnTriggerExit = OnTriggerExit;
		}
	}

	protected override void OnDisabled()
	{
		_modelRenderer?.Destroy();
		_modelRenderer = null;

		_modelCollider?.Destroy();
		_modelCollider = null;
	}
}
