namespace Sandbox;

public partial class Surface
{
	/// <summary>
	/// Holds a dictionary of common prefabs associated with a surface
	/// </summary>
	public struct SurfacePrefabCollection
	{
		/// <summary>
		/// A prefab to spawn when this surface is hit by a bullet. The prefab should be spawned facing the same direction as the hit normal. It should be parented to the surface that it hit and become orphaned if that surface is destroyed.
		/// </summary>
		public GameObject BulletImpact { get; set; }

		/// <summary>
		/// A decal prefab to spawn when this surface is hit by a bullet. It should be parented to the surface that it hit and destroyed with it.
		/// </summary>
		public GameObject BulletImpactDecal { get; set; }

		/// <summary>
		/// A prefab to spawn when this surface is hit by something blunt. The prefab should be spawned facing the same direction as the hit normal. It could include decals and particle effects. It should be parented to the surface that it hit.
		/// </summary>
		public GameObject BluntImpact { get; set; }

	}

	/// <summary>
	/// Common prefabs for this surface material
	/// </summary>
	[InlineEditor, Title( "Prefabs" )]
	public SurfacePrefabCollection PrefabCollection { get; set; }
}
