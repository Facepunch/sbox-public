using Sandbox.Navigation;

namespace Sandbox.Engine.Resources;

/// <summary>
/// Defines a navigation area resource for use in navigation meshes.
/// </summary>
[AssetType( Name = "NavArea Definition", Extension = "navarea", Category = "World" )]
public sealed class NavMeshAreaDefinition : GameResource
{
	/// <summary>
	/// Debug color for this Area.
	/// </summary>
	public Color Color { get; set; } = Color.Random;

	internal const float CostMultiplierMax = 100f;
	internal const float CostMultiplierMin = 1f;

	/// <summary>
	/// How much costlier it is to cross this Area.
	/// Will be clamped.
	/// </summary>
	[Range( CostMultiplierMin, CostMultiplierMax )]
	public float CostMultiplier { get => _costMultiplier; set => _costMultiplier = MathX.Clamp( value, CostMultiplierMin, CostMultiplierMax ); }
	private float _costMultiplier = 1.0f;

	/// <summary>
	/// Gets or sets the priority level for the area definition.
	/// Higher values take precedence if multiple areas overlap.
	/// </summary>
	/// <remarks>Changing this value may trigger updates to the navigation mesh.</remarks>
	public int Priority
	{
		get => _priority;
		set
		{
			_priority = value;
			UpdateAreaIds();
		}
	}
	private int _priority = 0;

	protected override void PostLoad()
	{
		PostReload();
	}

	protected override void PostReload()
	{
		UpdateAreaIds();
	}

	// Area ids follow the priorities, in the navmesh of every scene. Editor scenes hear of it through the event.
	private static void UpdateAreaIds()
	{
		IToolsDll.Current?.RunEvent<NavMesh.IEventListener>( x => x.OnAreaDefinitionChanged() );
		foreach ( var scene in Scene.AllIncludingUnlisted )
		{
			scene.NavMesh?.UpdateAreaIds();
		}
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "explore", width, height );
	}
}
