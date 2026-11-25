namespace Sandbox.Engine.Resources;

/// <summary>
/// A kind of navigation link, such as a jump or a ladder. Agents allow or refuse links by it, paths are priced by it, and game code picks how to cross a link by it.
/// The engine ships the ones below so that agents and levels from different games agree on them. Games add their own for anything else.
/// </summary>
[AssetType( Name = "NavLink Type", Extension = "navlink", Category = "World" )]
public sealed class NavMeshLinkType : GameResource
{
	/// <summary>
	/// Debug color for links of this type.
	/// </summary>
	public Color Color { get; set; } = Color.Random;

	/// <summary>
	/// How much costlier it is to cross a link of this type than to walk its length.
	/// Will be clamped.
	/// </summary>
	[Range( NavMeshAreaDefinition.CostMultiplierMin, NavMeshAreaDefinition.CostMultiplierMax )]
	public float CostMultiplier { get => _costMultiplier; set => _costMultiplier = MathX.Clamp( value, NavMeshAreaDefinition.CostMultiplierMin, NavMeshAreaDefinition.CostMultiplierMax ); }
	private float _costMultiplier = 1.0f;

	/// <summary>
	/// How much further in world units an agent would rather walk than take a link of this type.
	/// </summary>
	public float Penalty { get => _penalty; set => _penalty = value > 0 && float.IsFinite( value ) ? value : 0; }
	private float _penalty = 0.0f;

	/// <summary>A ballistic arc from start to end, up, down or across. Generated jumps are of this type.</summary>
	public static NavMeshLinkType Jump => ResourceLibrary.Get<NavMeshLinkType>( "navigation/jump.navlink" );

	/// <summary>Step off and fall to the end. Generated drops are of this type.</summary>
	public static NavMeshLinkType Drop => ResourceLibrary.Get<NavMeshLinkType>( "navigation/drop.navlink" );

	/// <summary>Move along a ladder and step off at the end.</summary>
	public static NavMeshLinkType Ladder => ResourceLibrary.Get<NavMeshLinkType>( "navigation/ladder.navlink" );

	/// <summary>Go up or down a face, then onto the end.</summary>
	public static NavMeshLinkType Climb => ResourceLibrary.Get<NavMeshLinkType>( "navigation/climb.navlink" );

	/// <summary>A low passage crossed crouched or prone.</summary>
	public static NavMeshLinkType Crawl => ResourceLibrary.Get<NavMeshLinkType>( "navigation/crawl.navlink" );

	/// <summary>Something the agent operates before passing, such as a door, button or lift.</summary>
	public static NavMeshLinkType Interact => ResourceLibrary.Get<NavMeshLinkType>( "navigation/interact.navlink" );

	/// <summary>An instant move with no motion in between.</summary>
	public static NavMeshLinkType Teleport => ResourceLibrary.Get<NavMeshLinkType>( "navigation/teleport.navlink" );

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "link", width, height );
	}
}
