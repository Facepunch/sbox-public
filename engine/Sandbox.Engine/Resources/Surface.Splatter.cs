namespace Sandbox;

public partial class Surface
{
	/// <summary>
	/// Decals drawn behind a hit on this surface - traced on from the hit along the way it was travelling, onto whatever is within reach
	/// </summary>
	public struct SplatterData
	{
		/// <summary>
		/// Decals that will be drawn as splatter. You can include multiple decals which will be picked randomly per splatter.
		/// </summary>
		public List<DecalDefinition> Decals { get; set; }

		/// <summary>
		/// How far past the hit to look for something to splatter onto.
		/// </summary>
		[DefaultValue( 96.0f )]
		public float Distance { get; set; } = 96.0f;

		/// <summary>
		/// How large these decals will be?
		/// </summary>
		public RangedFloat Scale { get; set; } = 1.0f;

		/// <summary>
		/// How long the decal's height coverage takes to grow in. 0 shows it with no fade-in transition. Decals must have a valid heightmap to use this effect.
		/// </summary>
		[DefaultValue( 0.25f )]
		public float FadeIn { get; set; } = 0.25f;

		/// <summary>
		/// Softness of height coverage mask
		/// </summary>
		[DefaultValue( 0.33f ), Range( 0.0f, 0.5f )]
		public float CoverageRange { get; set; } = 0.33f;

		public SplatterData() { }
	}

	/// <summary>
	/// Decals drawn behind hits on this surface. See <see cref="PlaceSplatter(Vector3, Vector3, GameObject)"/>.
	/// </summary>
	[InlineEditor, Title( "Splatter" )]
	public SplatterData Splatter { get; set; } = new();

	/// <summary>
	/// Draw this surface's <see cref="Splatter"/>, traced from <paramref name="position"/> along provided
	/// <paramref name="direction"/>, ignoring <paramref name="hitObject"/>, and put a decal where it lands, if there's any traceable surface within given distance.
	/// Total splatter count depends on the max amount of decals limit.
	/// </summary>
	/// <param name="position">Where the hit landed.</param>
	/// <param name="direction">The direction the hit was travelling.</param>
	/// <param name="hitObject">What was hit, which the trace passes through. Its whole model for a ragdoll.</param>
	/// <returns>The decal's GameObject, or null if none was placed.</returns>
	public GameObject PlaceSplatter( Vector3 position, Vector3 direction, GameObject hitObject = null )
	{
		if ( Application.IsDedicatedServer || direction.IsNearlyZero() )
			return null;

		var splatter = HasSplatter ? Splatter : GetBaseSurface()?.Splatter ?? default;
		return PlaceSplatter( splatter, position, direction, hitObject );
	}

	bool HasSplatter => Splatter.Decals is { Count: > 0 };

	/// <summary>
	/// This surface's splatter, or the base surface's when this one has no decals, for callers that already looked the base surface up.
	/// </summary>
	internal SplatterData GetSplatter( Surface baseSurface ) => HasSplatter ? Splatter : baseSurface?.Splatter ?? default;

	internal static GameObject PlaceSplatter( in SplatterData splatter, Vector3 position, Vector3 direction, GameObject hitObject )
	{
		if ( Application.IsDedicatedServer || direction.IsNearlyZero() )
			return null;

		if ( splatter.Decals is not { Count: > 0 } || splatter.Distance <= 0 )
			return null;

		var scene = hitObject.IsValid() ? hitObject.Scene : Game.ActiveScene;
		if ( !scene.IsValid() )
			return null;

		var definition = Random.Shared.FromList( splatter.Decals );
		if ( definition is null )
			return null;

		direction = direction.Normal;

		var trace = scene.Trace.Ray( position, position + direction * splatter.Distance );
		var tr = TraceBehind( scene, trace, position, direction, hitObject );
		if ( !tr.Hit )
			return null;

		var go = scene.CreateObject( false );
		go.Name = "splatter";
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldTransform = new Transform( tr.HitPosition, Rotation.LookAt( -tr.Normal ) );
		go.AddComponent<TemporaryEffect>();

		var decal = go.AddComponent<Decal>();
		decal.Decals = [definition];
		decal.Scale = splatter.Scale.GetValue();
		decal.CoverageFadeIn = splatter.FadeIn;
		decal.CoverageRange = splatter.CoverageRange;
		decal.Transient = true;

		BaseCombatWeapon.AttachToHit( go, tr.GameObject, tr.HitPosition );
		go.Enabled = true;

		return go;
	}

	/// <summary>
	/// The first thing behind <paramref name="hitObject"/> along the trace.
	/// </summary>
	static SceneTraceResult TraceBehind( Scene scene, SceneTrace trace, Vector3 position, Vector3 direction, GameObject hitObject )
	{
		if ( !hitObject.IsValid() )
			return trace.Run();

		if ( hitObject.Components.Get<MapCollider>() is not null )
		{
			var entry = scene.Trace.Ray( position - direction, position + direction ).Run().Shape;

			foreach ( var hit in trace.RunAll() )
			{
				if ( hit.Hit && !hit.StartedSolid && hit.Shape != entry && hit.Distance > 0.5f )
					return hit;
			}

			return default;
		}

		var ignore = hitObject.GetComponentInParent<ModelPhysics>( true )?.GameObject ?? hitObject;
		return trace.IgnoreGameObjectHierarchy( ignore ).Run();
	}
}
