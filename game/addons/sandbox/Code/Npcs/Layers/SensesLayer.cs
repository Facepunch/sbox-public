namespace Sandbox.Npcs.Layers;

/// <summary>
/// Handles awareness and environmental scanning.
/// Scans for all objects matching <see cref="ScanTags"/> and caches them by tag.
/// Whether a scanned object is a hostile target is decided by the NPC's disposition
/// toward it (see <see cref="Npc.GetDisposition"/>), not by tags.
/// </summary>
public class SensesLayer : BaseNpcLayer
{
	public float ScanInterval { get; set; } = 0.1f; // Scan every 100ms

	[Property]
	public float SightRange { get; set; } = 500f;

	[Property]
	public float HearingRange { get; set; } = 300f;

	/// <summary>
	/// Total horizontal field of view in degrees. Targets outside this cone aren't seen
	/// (they can still be heard, or noticed if they're right next to the NPC).
	/// </summary>
	[Property]
	public float FieldOfView { get; set; } = 200f;

	public float PersonalSpace { get; set; } = 80f;

	/// <summary>
	/// Hostiles moving slower than this can't be heard -- creep (crouch-walk) to sneak up on someone.
	/// </summary>
	[Property]
	public float QuietSpeed { get; set; } = 120f;

	/// <summary>
	/// Which agents the NPC scans for and caches: "npc" for other NPCs, "player" for players.
	/// This is just the broad-phase filter -- disposition decides who counts as hostile.
	/// </summary>
	[Property]
	public TagSet ScanTags { get; set; } = ["player", "npc"];

	// Hostile-only lists (entities this NPC is disposed Hostile toward)
	public GameObject Nearest { get; private set; }
	public float DistanceToNearest { get; private set; } = float.MaxValue;
	public List<GameObject> VisibleTargets { get; private set; } = new();
	public List<GameObject> AudibleTargets { get; private set; } = new();

	/// <summary>Visible entities this NPC is disposed Fearful toward (things to flee from).</summary>
	public List<GameObject> VisibleThreats { get; private set; } = new();

	// All visible scanned entities, regardless of disposition.
	private readonly List<GameObject> _visibleAll = new();

	// Tag-bucketed caches
	private readonly Dictionary<string, List<GameObject>> _visibleByTag = new();
	private readonly Dictionary<string, List<GameObject>> _audibleByTag = new();

	[Sync( SyncFlags.FromHost )]
	private TimeSince _lastScan { get; set; }

	/// <summary>
	/// The most relevant disturbance (gunshot, death, etc.) this NPC currently senses, if any.
	/// </summary>
	public Stimulus? Disturbance { get; private set; }

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		// The Npc can be gone while we're being torn down (death destroys the hierarchy).
		if ( !Npc.IsValid() ) return;

		if ( _lastScan > ScanInterval )
		{
			ScanEnvironment();
			PerceiveStimuli();
			_lastScan = 0;
		}
	}

	/// <summary>
	/// Find the nearest world stimulus this NPC can sense (ignoring its own noise).
	/// </summary>
	private void PerceiveStimuli()
	{
		Disturbance = null;

		var system = Npc.Scene?.GetSystem<NpcStimulusSystem>();
		if ( system is null )
			return;

		float nearestDist = float.MaxValue;

		foreach ( var stimulus in system.Near( Npc.WorldPosition ) )
		{
			if ( stimulus.Source == Npc.GameObject )
				continue;

			var dist = Npc.WorldPosition.Distance( stimulus.Position );
			if ( dist < nearestDist )
			{
				nearestDist = dist;
				Disturbance = stimulus;
			}
		}
	}

	public override string GetDebugString()
	{
		if ( VisibleTargets.Count == 0 && AudibleTargets.Count == 0 ) return null;
		return $"Senses: {VisibleTargets.Count} visible, {AudibleTargets.Count} audible";
	}

	/// <summary>
	/// Scan for all objects matching <see cref="ScanTags"/>, bucket by tag, and populate
	/// the hostile-filtered <see cref="VisibleTargets"/>/<see cref="AudibleTargets"/> using
	/// the NPC's disposition toward each.
	/// </summary>
	private void ScanEnvironment()
	{
		VisibleTargets.Clear();
		AudibleTargets.Clear();
		VisibleThreats.Clear();
		_visibleAll.Clear();
		ClearTagCache( _visibleByTag );
		ClearTagCache( _audibleByTag );
		Nearest = null;
		DistanceToNearest = float.MaxValue;

		var senseRange = MathF.Max( SightRange, HearingRange );

		// TODO: spatial query or something, this sucks ass
		if ( ScanTags.Has( "npc" ) )
		{
			foreach ( var npc in Npc.Scene.GetAll<Npc>() )
				ScanCandidate( npc.GameObject, senseRange );
		}

		if ( !NpcConVars.NoTarget && ScanTags.Has( "player" ) )
		{
			foreach ( var player in Npc.Scene.GetAll<Player>() )
				ScanCandidate( player.GameObject, senseRange );
		}
	}

	private void ScanCandidate( GameObject obj, float senseRange )
	{
		if ( obj == Npc.GameObject ) return;
		if ( !obj.Tags.HasAny( ScanTags ) ) return;

		var distance = Npc.WorldPosition.Distance( obj.WorldPosition );
		if ( distance > senseRange ) return;

		var disposition = Npc.GetDisposition( obj );
		bool isHostile = disposition == Disposition.Hostile;
		bool isFeared = disposition == Disposition.Fearful;

		// Track nearest hostile target
		if ( isHostile && distance < DistanceToNearest )
		{
			DistanceToNearest = distance;
			Nearest = obj;
		}

		bool isAudible = distance <= HearingRange;
		bool isVisible = distance <= SightRange && IsInViewCone( obj, distance ) && HasLineOfSight( obj );

		if ( isAudible )
		{
			AddToTagCache( _audibleByTag, obj );
			if ( isHostile ) AudibleTargets.Add( obj );
		}

		if ( isVisible )
		{
			_visibleAll.Add( obj );
			AddToTagCache( _visibleByTag, obj );
			if ( isHostile ) VisibleTargets.Add( obj );
			if ( isFeared ) VisibleThreats.Add( obj );
		}
	}

	/// <summary>
	/// Check if we have line of sight to target
	/// </summary>
	private bool HasLineOfSight( GameObject target )
	{
		var eyePosition = Npc.WorldPosition + Vector3.Up * 64f; // Eye height
		var targetPosition = target.WorldPosition + Vector3.Up * 32f; // Target center

		var trace = Npc.Scene.Trace.Ray( eyePosition, targetPosition )
			.IgnoreGameObjectHierarchy( Npc.GameObject )
			.WithoutTags( "trigger" )
			.Run();

		return !trace.Hit || trace.GameObject == target || target.IsDescendant( trace.GameObject );
	}

	/// <summary>
	/// True if a target is within the NPC's forward field of view. Anything inside personal
	/// space is always in view -- you notice someone right next to you, even behind.
	/// </summary>
	private bool IsInViewCone( GameObject target, float distance )
	{
		if ( distance <= PersonalSpace )
			return true;

		var toTarget = (target.WorldPosition - Npc.WorldPosition).WithZ( 0 );
		if ( toTarget.LengthSquared < 0.01f )
			return true;

		var forward = Npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		var dot = forward.Dot( toTarget.Normal );
		var threshold = MathF.Cos( FieldOfView * 0.5f * MathF.PI / 180f );
		return dot >= threshold;
	}

	/// <summary>
	/// Get the nearest visible hostile target (anything this NPC is disposed Hostile toward).
	/// </summary>
	public GameObject GetNearestVisible()
	{
		return GetNearestIn( VisibleTargets );
	}

	/// <summary>
	/// Get the visible hostile this NPC should engage first: highest relationship priority,
	/// nearest breaking ties.
	/// </summary>
	public GameObject GetBestTarget()
	{
		GameObject best = null;
		int bestPriority = int.MinValue;
		float bestDist = float.MaxValue;

		foreach ( var obj in VisibleTargets )
		{
			if ( !obj.IsValid() ) continue;

			var priority = Npc.GetRelationship( obj ).Priority;
			var dist = Npc.WorldPosition.Distance( obj.WorldPosition );

			if ( priority > bestPriority || (priority == bestPriority && dist < bestDist) )
			{
				best = obj;
				bestPriority = priority;
				bestDist = dist;
			}
		}

		return best;
	}

	/// <summary>
	/// Get the nearest visible object with a specific tag.
	/// </summary>
	public GameObject GetNearestVisible( string tag )
	{
		return GetNearestIn( GetVisible( tag ) );
	}

	/// <summary>
	/// Get the nearest visible entity this NPC regards with the given disposition
	/// (e.g. the nearest thing it's afraid of).
	/// </summary>
	public GameObject GetNearestVisible( Disposition disposition )
	{
		GameObject nearest = null;
		float nearestDist = float.MaxValue;

		foreach ( var obj in _visibleAll )
		{
			if ( !obj.IsValid() ) continue;
			if ( Npc.GetDisposition( obj ) != disposition ) continue;

			var dist = Npc.WorldPosition.Distance( obj.WorldPosition );
			if ( dist < nearestDist )
			{
				nearestDist = dist;
				nearest = obj;
			}
		}

		return nearest;
	}

	/// <summary>
	/// The nearest hostile we can hear moving -- inside <see cref="HearingRange"/> and going faster
	/// than <see cref="QuietSpeed"/>. Null when nobody's making noise.
	/// </summary>
	public GameObject GetHeardHostile()
	{
		GameObject nearest = null;
		float nearestDist = float.MaxValue;

		foreach ( var obj in AudibleTargets )
		{
			if ( !obj.IsValid() ) continue;
			if ( GetVelocity( obj ).WithZ( 0 ).Length < QuietSpeed ) continue;

			var dist = Npc.WorldPosition.Distance( obj.WorldPosition );
			if ( dist < nearestDist )
			{
				nearestDist = dist;
				nearest = obj;
			}
		}

		return nearest;
	}

	/// <summary>
	/// A player inside our personal space who's moving toward us -- they're trying to
	/// get past and we're in their way. Returns null when nobody is barging in.
	/// </summary>
	public GameObject GetPushingPlayer()
	{
		foreach ( var obj in GetVisible( "player" ) )
		{
			if ( !obj.IsValid() ) continue;

			var toUs = (Npc.WorldPosition - obj.WorldPosition).WithZ( 0 );
			if ( toUs.Length > PersonalSpace ) continue;

			// They have to actually be walking at us -- someone stood close doesn't count
			var velocity = GetVelocity( obj ).WithZ( 0 );
			if ( velocity.Length < 100f ) continue;
			if ( velocity.Normal.Dot( toUs.Normal ) < 0.5f ) continue;

			return obj;
		}

		return null;
	}

	/// <summary>
	/// Resolve a scanned object's velocity from whatever movement source it has.
	/// </summary>
	public static Vector3 GetVelocity( GameObject obj )
	{
		if ( !obj.IsValid() )
			return Vector3.Zero;

		if ( obj.Root.GetComponent<PlayerController>() is { } controller )
			return controller.Velocity;

		// NPCs walk on the navmesh -- their rigidbody sits still
		if ( obj.GetComponent<NavMeshAgent>() is { } agent )
			return agent.Velocity;

		if ( obj.GetComponent<Rigidbody>() is { } rb )
			return rb.Velocity;

		return Vector3.Zero;
	}

	/// <summary>
	/// Get all visible objects with a specific tag from the cache.
	/// </summary>
	public List<GameObject> GetVisible( string tag )
	{
		return _visibleByTag.TryGetValue( tag, out var list ) ? list : _empty;
	}

	/// <summary>
	/// Get all audible objects with a specific tag from the cache.
	/// </summary>
	public List<GameObject> GetAudible( string tag )
	{
		return _audibleByTag.TryGetValue( tag, out var list ) ? list : _empty;
	}

	public override void ResetLayer()
	{
		VisibleTargets.Clear();
		AudibleTargets.Clear();
		VisibleThreats.Clear();
		_visibleAll.Clear();
		ClearTagCache( _visibleByTag );
		ClearTagCache( _audibleByTag );
		Nearest = null;
		DistanceToNearest = float.MaxValue;
		Disturbance = null;
	}

	private GameObject GetNearestIn( List<GameObject> list )
	{
		GameObject nearest = null;
		float nearestDist = float.MaxValue;

		foreach ( var obj in list )
		{
			if ( !obj.IsValid() ) continue; // target may have been destroyed since the last scan

			var dist = Npc.WorldPosition.Distance( obj.WorldPosition );
			if ( dist < nearestDist )
			{
				nearestDist = dist;
				nearest = obj;
			}
		}

		return nearest;
	}

	private static void AddToTagCache( Dictionary<string, List<GameObject>> cache, GameObject obj )
	{
		foreach ( var tag in obj.Tags )
		{
			if ( !cache.TryGetValue( tag, out var list ) )
			{
				list = new List<GameObject>();
				cache[tag] = list;
			}
			list.Add( obj );
		}
	}

	private static void ClearTagCache( Dictionary<string, List<GameObject>> cache )
	{
		foreach ( var list in cache.Values )
			list.Clear();
	}

	private static readonly List<GameObject> _empty = new();
}
