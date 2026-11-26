namespace Sandbox.Npcs.Layers;

/// <summary>Environmental awareness and target detection for an NPC. Disabling the layer clears its results.</summary>
public class SensesLayer : BaseNpcLayer
{
	/// <summary>
	/// Which agents the layer scans for.
	/// </summary>
	[Expose]
	[Flags]
	public enum SenseTargets
	{
		None = 0,
		Players = 1 << 0,
		Npcs = 1 << 1,
	}

	internal float ScanInterval { get; set; } = 0.1f;

	/// <summary>
	/// How far the NPC can see.
	/// </summary>
	[Property]
	public float SightRange { get; set; } = 500f;

	/// <summary>
	/// How far the NPC can hear.
	/// </summary>
	[Property]
	public float HearingRange { get; set; } = 300f;

	/// <summary>
	/// Total horizontal field of view in degrees. Targets outside this cone aren't seen
	/// (they can still be heard, or noticed if they're right next to the NPC).
	/// </summary>
	[Property]
	public float FieldOfView { get; set; } = 200f;

	private float _cachedFieldOfView = float.NaN;
	private float _fieldOfViewCos;

	private const float PersonalSpace = 80f;

	/// <summary>
	/// Hostiles moving slower than this can't be heard -- creep (crouch-walk) to sneak up on someone.
	/// </summary>
	[Property]
	public float QuietSpeed { get; set; } = 120f;

	/// <summary>
	/// Which agents the NPC scans for. This is just the broad-phase filter -- disposition decides who counts as hostile.
	/// </summary>
	[Property]
	public SenseTargets Targets { get; set; } = SenseTargets.Players | SenseTargets.Npcs;

	/// <summary>Visible entities this NPC is disposed Hostile toward.</summary>
	public IReadOnlyList<GameObject> VisibleHostiles => _visibleTargets;

	/// <summary>Entities this NPC is disposed Hostile toward, within hearing range.</summary>
	internal IReadOnlyList<GameObject> AudibleTargets => _audibleTargets;

	/// <summary>Visible entities this NPC is disposed Fearful toward (things to flee from).</summary>
	public IReadOnlyList<GameObject> VisibleThreats => _visibleThreats;

	private readonly List<GameObject> _visibleTargets = [];
	private readonly List<GameObject> _audibleTargets = [];
	private readonly List<GameObject> _visibleThreats = [];

	// Everything confirmed visible, whatever the disposition.
	private readonly List<GameObject> _visible = [];

	// Neutral candidates wait until a query needs their visibility.
	private readonly List<GameObject> _unchecked = [];

	private GameObject _pushingPlayer;
	private GameObject _heardHostile;

	private TimeSince _lastScan;

	/// <summary>
	/// The nearest disturbance (gunshot, death, etc.) this NPC currently senses, if any.
	/// </summary>
	public Stimulus? Disturbance { get; private set; }

	protected override void OnStart()
	{
		_lastScan = Game.Random.Float( 0f, ScanInterval );
	}

	protected override void OnDisabled()
	{
		ClearScan();
	}

	internal override string GetDebugString()
	{
		if ( _visibleTargets.Count == 0 && _audibleTargets.Count == 0 ) return null;
		return $"Senses: {_visibleTargets.Count} visible, {_audibleTargets.Count} audible";
	}

	private bool CanScan => this.IsValid() && Active && Npc.IsValid() && Npc.Active;

	internal bool IsScanDue( BaseNpc owner, bool force ) => Npc == owner && StartCalled && CanScan && (force || !(_lastScan <= ScanInterval));

	/// <summary>
	/// Find the nearest world stimulus this NPC can sense (ignoring its own noise).
	/// </summary>
	private void PerceiveStimuli()
	{
		Disturbance = null;

		var system = Npc.Scene?.GetSystem<NpcSystem>();
		if ( system is null )
			return;

		Disturbance = system.FindNearestStimulus( Npc.WorldPosition, Npc.GameObject );
	}

	private static readonly string[] SightIgnoreTags = ["trigger"];

	private bool CanSee( GameObject target )
	{
		if ( !CanScan || !target.IsValid() )
			return false;

		var distance = Npc.WorldPosition.Distance( target.WorldPosition );
		return IsInViewCone( target, distance ) && HasLineOfSight( target );
	}

	/// <summary>
	/// Scan the candidates and sort them by the NPC's disposition toward each into the visible and audible lists.
	/// </summary>
	internal void ScanEnvironment( Span<NpcSensingSnapshot.Target> targets )
	{
		_lastScan = 0;
		ClearScan();
		var position = Npc.WorldPosition;
		var forward = Npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		var senseRange = MathF.Max( SightRange, HearingRange );
		// Padding retains targets whose float distance rounds onto the range boundary.
		var extent = senseRange >= 0f && float.IsFinite( senseRange ) ? MathF.BitIncrement( senseRange ) : float.PositiveInfinity;
		var rangeSquared = extent * extent;
		var heardDistance = float.MaxValue;
		foreach ( ref var target in targets )
		{
			if ( target.Object == Npc.GameObject || (Targets & target.Kind) == 0 ) continue;
			var delta = target.Position - position;
			var squared = delta.LengthSquared;
			if ( target.Kind == SenseTargets.Npcs && squared > rangeSquared ) continue;
			var distance = MathF.Sqrt( squared );
			if ( distance > senseRange ) continue;
			if ( !CanScan ) break;
			if ( !target.IsLive ) continue;
			ScanCandidate( ref target, delta, distance, forward, ref heardDistance );
		}
		if ( CanScan ) PerceiveStimuli();
	}

	private void ScanCandidate( ref NpcSensingSnapshot.Target target, Vector3 delta, float distance, Vector3 forward,
		ref float heardDistance )
	{
		var obj = target.Object;
		var disposition = Npc.GetDisposition( obj );
		if ( !CanScan || !target.IsLive ) return;
		var isHostile = disposition == Disposition.Hostile;
		var isFeared = disposition == Disposition.Fearful;

		if ( isHostile && distance <= HearingRange )
		{
			_audibleTargets.Add( obj );

			if ( distance < heardDistance && target.Speed >= QuietSpeed )
			{
				heardDistance = distance;
				_heardHostile = obj;
			}
		}

		if ( distance > SightRange ) return;

		var horizontal = delta.WithZ( 0 );
		var isPushing = target.IsPlayer && !(target.Speed < 100f) && !_pushingPlayer.IsValid()
			&& !(horizontal.Length > PersonalSpace) && target.Velocity.Normal.Dot( -horizontal.Normal ) >= 0.5f;

		// Sight is only worked out for the ones that matter now. The rest wait until someone asks.
		if ( !isHostile && !isFeared && !isPushing )
		{
			_unchecked.Add( obj );
			return;
		}

		UpdateFieldOfView();
		if ( !IsInViewCone( delta, distance, forward, PersonalSpace, _fieldOfViewCos ) ) return;
		var endpoint = target.CalculateEyePosition();
		if ( !CanScan || !target.IsLive ) return;
		var visible = HasLineOfSight( obj, endpoint );
		if ( !visible || !CanScan || !target.IsLive ) return;

		_visible.Add( obj );
		if ( isHostile ) _visibleTargets.Add( obj );
		if ( isFeared ) _visibleThreats.Add( obj );
		if ( isPushing ) _pushingPlayer = obj;
	}

	/// <summary>
	/// Work out sight for the unchecked candidates that match, moving the visible ones into the visible list.
	/// </summary>
	private void CheckLineOfSight( string tag = null, Disposition? disposition = null, string faction = null )
	{
		for ( var i = _unchecked.Count - 1; i >= 0; i-- )
		{
			var obj = _unchecked[i];
			if ( !obj.IsValid() )
			{
				_unchecked.RemoveAt( i );
				continue;
			}

			if ( tag is not null && !obj.Tags.Has( tag ) ) continue;
			if ( disposition.HasValue && Npc.GetDisposition( obj ) != disposition.Value ) continue;
			if ( faction is not null && BaseNpc.GetFactionOf( obj ) != faction ) continue;

			_unchecked.RemoveAt( i );
			if ( CanSee( obj ) )
				_visible.Add( obj );

			if ( !CanScan ) return;
		}
	}

	private bool HasLineOfSight( GameObject target )
	{
		var targetPosition = target.GetComponent<BaseNpc>() is { } npc ? npc.EyePosition : target.GetBounds().Center;
		return HasLineOfSight( target, targetPosition );
	}

	private bool HasLineOfSight( GameObject target, Vector3 targetPosition )
	{
		SceneTraceResult trace;
		try
		{
			trace = Npc.Scene.Trace.Ray( Npc.EyePosition, targetPosition )
				.IgnoreGameObjectHierarchy( Npc.GameObject )
				.WithoutTags( SightIgnoreTags )
				.Run();
		}
		catch
		{
			SceneTrace.ClearTraceFilter();
			throw;
		}

		return !trace.Hit || trace.GameObject == target || (trace.GameObject is { } hit && target.IsDescendant( hit ));
	}

	/// <summary>
	/// True if a target is within the NPC's forward field of view. Anything inside personal
	/// space is always in view -- you notice someone right next to you, even behind.
	/// </summary>
	private bool IsInViewCone( GameObject target, float distance )
	{
		UpdateFieldOfView();
		return IsInViewCone( target.WorldPosition - Npc.WorldPosition, distance,
			Npc.WorldRotation.Forward.WithZ( 0 ).Normal, PersonalSpace, _fieldOfViewCos );
	}

	private static bool IsInViewCone( Vector3 delta, float distance, Vector3 forward, float personalSpace, float fieldOfViewCos )
	{
		if ( distance <= personalSpace ) return true;
		var horizontal = delta.WithZ( 0 );
		return horizontal.LengthSquared < 0.01f || forward.Dot( horizontal.Normal ) >= fieldOfViewCos;
	}

	private void UpdateFieldOfView()
	{
		// Hotload can restore the property's backing field without calling its setter.
		if ( _cachedFieldOfView != FieldOfView )
		{
			_cachedFieldOfView = FieldOfView;
			_fieldOfViewCos = MathF.Cos( FieldOfView * 0.5f * MathF.PI / 180f );
		}
	}

	/// <summary>
	/// Find the visible hostile this NPC should engage first: highest relationship priority,
	/// nearest breaking ties.
	/// </summary>
	public GameObject FindBestTarget()
	{
		GameObject best = null;
		int bestPriority = int.MinValue;
		float bestDist = float.MaxValue;

		foreach ( var obj in _visibleTargets )
		{
			// It may have died or gone since the last scan
			if ( !BaseNpc.IsLiveTarget( obj ) ) continue;

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
	/// Find the nearest visible object with a specific tag. May perform visibility traces for unchecked candidates.
	/// </summary>
	public GameObject FindNearestVisible( string tag )
	{
		CheckLineOfSight( tag );
		return NearestVisible( tag, static ( obj, tag ) => obj.Tags.Has( tag ) );
	}

	/// <summary>
	/// Find the nearest visible entity this NPC regards with the given disposition
	/// (e.g. the nearest thing it's afraid of). May perform visibility traces for unchecked candidates.
	/// </summary>
	public GameObject FindNearestVisible( Disposition disposition )
	{
		CheckLineOfSight( disposition: disposition );
		return NearestVisible( (Npc, disposition), static ( obj, state ) => state.Npc.GetDisposition( obj ) == state.disposition );
	}

	/// <summary>
	/// Find the nearest visible member of a faction, as <see cref="BaseNpc.GetFactionOf"/> tells it, such as
	/// <see cref="Factions.Player"/>.
	/// </summary>
	internal GameObject FindNearestVisibleOfFaction( string faction )
	{
		CheckLineOfSight( faction: faction );
		return NearestVisible( faction, static ( obj, faction ) => BaseNpc.GetFactionOf( obj ) == faction );
	}

	private GameObject NearestVisible<T>( T state, Func<GameObject, T, bool> match )
	{
		GameObject nearest = null;
		var nearestDistance = float.MaxValue;

		foreach ( var obj in _visible )
		{
			if ( !obj.IsValid() || !match( obj, state ) ) continue;

			var distance = Npc.WorldPosition.Distance( obj.WorldPosition );
			if ( distance < nearestDistance )
			{
				nearestDistance = distance;
				nearest = obj;
			}
		}

		return nearest;
	}

	/// <summary>
	/// The nearest hostile we could hear moving at the last scan -- inside <see cref="HearingRange"/> and going faster
	/// than <see cref="QuietSpeed"/>. Null when nobody's making noise.
	/// </summary>
	internal GameObject GetHeardHostile() => BaseNpc.IsLiveTarget( _heardHostile ) ? _heardHostile : null;

	/// <summary>
	/// A player inside our personal space who was moving toward us at the last scan -- they're trying to
	/// get past and we're in their way. Returns null when nobody is barging in.
	/// </summary>
	public GameObject GetPushingPlayer() => _pushingPlayer.IsValid() ? _pushingPlayer : null;

	internal void ClearScan()
	{
		Disturbance = null;
		_visibleTargets.Clear();
		_audibleTargets.Clear();
		_visibleThreats.Clear();
		_visible.Clear();
		_unchecked.Clear();
		_pushingPlayer = null;
		_heardHostile = null;
	}
}
