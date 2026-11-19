using Sandbox.Npcs;
using Sandbox.UI;
using System.Text.Json.Nodes;

/// <summary>
/// Enforces configurable limits on spawning and tool usage.
/// Maintains a per-player tracked object list populated from post-events.
/// Limit checks iterate only the player's objects, not the entire scene.
/// Every limit is a row in <see cref="Limits"/>, add new conditions there there.
/// </summary>
internal sealed class LimitsSystem : GameObjectSystem<LimitsSystem>, Global.ISpawnEvents, IToolActionEvents
{
	[Range( -1, 1024 )]
	[Title( "Max Props" ), Group( "Limits" ), Order( 0 )]
	[ConVar( "sandbox.limit.props", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum props per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxPropsPerPlayer { get; set; } = -1;

	[Range( -1, 16 )]
	[Title( "Max Explosives" ), Group( "Limits" ), Order( 1 )]
	[ConVar( "sandbox.limit.explosives", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum explosive props per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxExplosivesPerPlayer { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Balloons" ), Group( "Limits" ), Order( 3 )]
	[ConVar( "sandbox.limit.balloons", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum balloons per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxBalloons { get; set; } = -1;

	[Range( -1, 512 )]
	[Title( "Max Constraints" ), Group( "Limits" ), Order( 2 )]
	[ConVar( "sandbox.limit.constraints", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum constraints per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxConstraints { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Emitters" ), Group( "Limits" ), Order( 4 )]
	[ConVar( "sandbox.limit.emitters", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum emitters per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxEmitters { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Thrusters" ), Group( "Limits" ), Order( 5 )]
	[ConVar( "sandbox.limit.thrusters", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum thrusters per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxThrusters { get; set; } = -1;

	[Range( -1, 32 )]
	[Title( "Max Hoverballs" ), Group( "Limits" ), Order( 6 )]
	[ConVar( "sandbox.limit.hoverballs", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum hoverballs per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxHoverballs { get; set; } = -1;

	[Range( -1, 32 )]
	[Title( "Max Wheels" ), Group( "Limits" ), Order( 7 )]
	[ConVar( "sandbox.limit.wheels", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum wheels per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxWheels { get; set; } = -1;

	[Range( -1, 256 )]
	[Title( "Max Texts" ), Group( "Limits" ), Order( 8 )]
	[ConVar( "sandbox.limit.texts", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum placed texts per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxTexts { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Speakers" ), Group( "Limits" ), Order( 9 )]
	[ConVar( "sandbox.limit.speakers", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum speakers per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxSpeakers { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Cameras" ), Group( "Limits" ), Order( 10 )]
	[ConVar( "sandbox.limit.cameras", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum cameras per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxCameras { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Dynamite" ), Group( "Limits" ), Order( 11 )]
	[ConVar( "sandbox.limit.dynamite", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum dynamite per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxDynamite { get; set; } = -1;

	[Range( -1, 128 )]
	[Title( "Max Lights" ), Group( "Limits" ), Order( 12 )]
	[ConVar( "sandbox.limit.lights", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum lights (bulbs and spot lights) per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxLights { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max NPCs" ), Group( "Limits" ), Order( 13 )]
	[ConVar( "sandbox.limit.npcs", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum NPCs per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxNpcs { get; set; } = -1;

	[Range( -1, 32 )]
	[Title( "Max Vehicles" ), Group( "Limits" ), Order( 14 )]
	[ConVar( "sandbox.limit.vehicles", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum vehicles per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxVehicles { get; set; } = -1;

	[Range( -1, 64 )]
	[Title( "Max Ragdolls" ), Group( "Limits" ), Order( 15 )]
	[ConVar( "sandbox.limit.ragdolls", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum ragdolls per player. -1 = unlimited, 0 = none allowed." )]
	public static int MaxRagdolls { get; set; } = -1;

	[Range( -1, MaxBuilderBlocksCap )]
	[Title( "Max Building Blocks Per Player" ), Group( "Limits" ), Order( 16 )]
	[ConVar( "sandbox.limit.builder", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Maximum building blocks per player, from the Builder tool, spawn menu, dupes and stacking. -1 = unlimited, 0 = none allowed." )]
	public static int MaxBuilderBlocks
	{
		get => _maxBuilderBlocks;
		set => _maxBuilderBlocks = Math.Clamp( value, -1, MaxBuilderBlocksCap );
	}
	private static int _maxBuilderBlocks = 4096;
	private const int MaxBuilderBlocksCap = 8196;

	/// <summary>
	/// One GameObject as far as the limits care: its own component types (not its children's), its prop's model, and
	/// whether it's tagged as a constraint. The same whether it's a player's live object, a spawn menu entry or a node
	/// of a dupe's saved JSON, so each limit only needs saying once.
	/// </summary>
	private readonly record struct LimitedObject( HashSet<Type> Types, Model PropModel, bool IsConstraint )
	{
		public bool Has<T>() => Types.Any( x => x.IsAssignableTo( typeof( T ) ) );
		public bool IsRagdoll => (PropModel?.Physics?.Parts?.Count ?? 0) > 1;
		public bool IsExplosive => PropModel?.Data?.Explosive == true;

		public static LimitedObject Of( GameObject go ) => new(
			go.Components.GetAll().Select( x => x.GetType() ).ToHashSet(),
			go.GetComponent<Prop>( true )?.Model,
			go.Tags.Has( "constraint" ) );

		public static LimitedObject OfProp( Model model ) => new( [typeof( Prop )], model, false );
	}

	/// <summary>
	/// A per-player limit: its setting, what the notice calls it, which objects it counts, and which tool actions place
	/// one (spawns and dupes are checked by what they contain, so they need nothing extra).
	/// </summary>
	private sealed record Limit( Func<int> Max, string Category, Func<LimitedObject, bool> Counts, Func<ToolMode, bool> PlacedBy = null );

	private static readonly Limit BuilderBlocks = new( () => MaxBuilderBlocks, "#limit.builder", x => x.Has<BuilderProp>(), tool => tool is BuilderTool );

	private static readonly Limit[] Limits =
	[
		BuilderBlocks,
		new( () => MaxPropsPerPlayer, "#limit.props", x => x.Has<Prop>() ),
		new( () => MaxExplosivesPerPlayer, "#limit.explosives", x => x.IsExplosive ),
		new( () => MaxRagdolls, "#limit.ragdolls", x => x.IsRagdoll ),
		new( () => MaxConstraints, "#limit.constraints", x => x.IsConstraint, tool => tool is BaseConstraintToolMode or KeepUprightTool ),
		new( () => MaxBalloons, "#limit.balloons", x => x.Has<BalloonEntity>(), tool => tool is BalloonTool ),
		new( () => MaxThrusters, "#limit.thrusters", x => x.Has<ThrusterEntity>(), tool => tool is ThrusterTool ),
		new( () => MaxEmitters, "#limit.emitters", x => x.Has<EmitterEntity>(), tool => tool is EmitterTool ),
		new( () => MaxHoverballs, "#limit.hoverballs", x => x.Has<HoverballEntity>(), tool => tool is HoverballTool ),
		new( () => MaxWheels, "#limit.wheels", x => x.Has<WheelEntity>(), tool => tool is WheelTool ),
		new( () => MaxTexts, "#limit.texts", x => x.Has<TextEntity>(), tool => tool is TextTool ),
		new( () => MaxSpeakers, "#limit.speakers", x => x.Has<SpeakerEntity>(), tool => tool is SpeakerTool ),
		new( () => MaxCameras, "#limit.cameras", x => x.Has<CameraEntity>(), tool => tool is CameraTool ),
		new( () => MaxDynamite, "#limit.dynamite", x => x.Has<DynamiteEntity>() ),
		new( () => MaxLights, "#limit.lights", x => x.Has<PointLightEntity>() || x.Has<SpotLightEntity>(), tool => tool is LightTool { PlacingNew: true } ),
		new( () => MaxNpcs, "#limit.npcs", x => x.Has<Npc>() ),
		new( () => MaxVehicles, "#limit.vehicles", x => x.Has<FourWheelVehicle>() ),
	];

	/// <summary>
	/// Per-player tracked objects keyed by SteamId.
	/// </summary>
	private readonly Dictionary<long, List<GameObject>> _tracked = new();

	/// <summary>
	/// Fast lookup to check if a GameObject is already tracked by any player.
	/// </summary>
	private readonly HashSet<GameObject> _allTracked = new();

	public LimitsSystem( Scene scene ) : base( scene )
	{
	}

	private void Track( long steamId, GameObject go )
	{
		if ( !go.IsValid() ) return;
		if ( !_allTracked.Add( go ) ) return;

		if ( !_tracked.TryGetValue( steamId, out var list ) )
			_tracked[steamId] = list = new List<GameObject>();

		list.Add( go );
	}

	/// <summary>
	/// What a player has out, pruning destroyed objects.
	/// </summary>
	private List<LimitedObject> Owned( long steamId )
	{
		var objects = new List<LimitedObject>();
		if ( !_tracked.TryGetValue( steamId, out var list ) ) return objects;

		for ( int i = list.Count - 1; i >= 0; i-- )
		{
			var go = list[i];
			if ( !go.IsValid() )
			{
				_allTracked.Remove( go );
				list.RemoveAt( i );
				continue;
			}

			objects.Add( LimitedObject.Of( go ) );
		}

		return objects;
	}

	/// <summary>
	/// Whether adding what <paramref name="adding"/> says to each of <paramref name="limits"/> would take
	/// <paramref name="player"/> over any of them. Tells them about the first one it would.
	/// </summary>
	private bool WouldExceed( Connection player, IEnumerable<Limit> limits, Func<Limit, int> adding )
	{
		List<LimitedObject> owned = null;

		foreach ( var limit in limits )
		{
			var max = limit.Max();
			if ( max < 0 ) continue;

			var added = adding( limit );
			if ( added <= 0 ) continue;

			owned ??= Owned( (long)player.SteamId );
			if ( owned.Count( limit.Counts ) + added <= max ) continue;

			NotifyLimit( player, limit.Category, max );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Everything a spawn would create, a whole dupe's worth or a single entry.
	/// </summary>
	private static List<LimitedObject> ObjectsIn( ISpawner spawner ) => spawner switch
	{
		DuplicatorSpawner dupe => ObjectsInDupe( dupe ),
		PropSpawner prop => [LimitedObject.OfProp( prop.Model )],
		MountSpawner mount => [LimitedObject.OfProp( mount.Model )],
		BuilderSpawner => [new LimitedObject( [typeof( BuilderProp )], null, false )],
		_ when spawner?.Prefab is { } prefab => prefab.GetAllObjects( true ).Select( LimitedObject.Of ).ToList(),
		_ => []
	};

	/// <summary>
	/// Every GameObject in a dupe, read from its saved JSON before anything is spawned.
	/// </summary>
	private static List<LimitedObject> ObjectsInDupe( DuplicatorSpawner spawner )
	{
		var objects = new List<LimitedObject>();

		void Read( JsonObject go )
		{
			var types = new HashSet<Type>();
			Model model = null;

			foreach ( var component in (go["Components"] as JsonArray)?.OfType<JsonObject>() ?? [] )
			{
				var type = Text( component["__type"] ) is { } name ? TypeLibrary.GetType( name )?.TargetType : null;
				if ( type is null ) continue;

				types.Add( type );
				if ( type.IsAssignableTo( typeof( Prop ) ) && ModelPath( component["Model"] ) is { } path )
					model = Model.Load( path );
			}

			var constraint = Text( go["Tags"] )?.Split( ',' ).Any( x => x.Trim() == "constraint" ) == true;
			objects.Add( new LimitedObject( types, model, constraint ) );

			foreach ( var child in (go["Children"] as JsonArray)?.OfType<JsonObject>() ?? [] )
				Read( child );
		}

		foreach ( var root in spawner.Dupe?.Objects?.OfType<JsonObject>() ?? [] )
			Read( root );

		return objects;
	}

	static string Text( JsonNode node ) => node is JsonValue value && value.TryGetValue<string>( out var text ) ? text : null;

	// a model reference is saved either as its path or as an object holding it
	static string ModelPath( JsonNode node ) => Text( node ) ?? Text( (node as JsonObject)?["Path"] );

	void Global.ISpawnEvents.OnSpawn( Global.ISpawnEvents.SpawnData e )
	{
		if ( e.Player is null ) return;

		// a dupe is refused whole if what's in it would take you over any limit
		var spawning = ObjectsIn( e.Spawner );
		if ( WouldExceed( e.Player, Limits, limit => spawning.Count( limit.Counts ) ) )
			e.Cancelled = true;
	}

	void Global.ISpawnEvents.OnPostSpawn( Global.ISpawnEvents.PostSpawnData e )
	{
		if ( e.Player is null || e.Objects is null ) return;

		var steamId = (long)e.Player.SteamId;

		foreach ( var root in e.Objects )
		{
			if ( !root.IsValid() ) continue;
			Track( steamId, root );

			// a dupe's constraints are children of the objects they join, and building blocks can be parented too, track
			// them so they count towards their limits
			foreach ( var go in root.GetAllObjects( true ) )
			{
				if ( go != root && (go.Tags.Has( "constraint" ) || go.GetComponent<BuilderProp>().IsValid()) )
					Track( steamId, go );
			}
		}
	}

	void IToolActionEvents.OnToolAction( IToolActionEvents.ActionData e )
	{
		if ( e.Input == ToolInput.Reload ) return;
		if ( e.Player is null ) return;

		// a tool action places one more of whatever each limit it's listed against counts
		var placing = Limits.Where( x => x.PlacedBy?.Invoke( e.Tool ) == true );
		if ( WouldExceed( e.Player, placing, _ => 1 ) )
			e.Cancelled = true;
	}

	void IToolActionEvents.OnPostToolAction( IToolActionEvents.PostActionData e )
	{
		if ( e.Player is null || e.CreatedObjects is not { Count: > 0 } ) return;

		foreach ( var go in e.CreatedObjects )
			Track( (long)e.Player.SteamId, go );
	}

	/// <summary>
	/// True if <paramref name="player"/> can create <paramref name="adding"/> more building blocks, otherwise tells them.
	/// Host only. For tools that copy blocks without going through the spawn or Builder tool events, like the Stacker.
	/// </summary>
	public bool CheckBuilderLimit( Connection player, int adding )
	{
		if ( player is null ) return true;
		return !WouldExceed( player, [BuilderBlocks], _ => adding );
	}

	/// <summary>
	/// True if <paramref name="player"/> can spawn the whole dupe within every limit, otherwise tells them which.
	/// A dupe that waits before spawning checks again once it's ready.
	/// </summary>
	public bool CheckDupeLimits( Connection player, DuplicatorSpawner dupe )
	{
		if ( player is null ) return true;

		var spawning = ObjectsInDupe( dupe );
		return !WouldExceed( player, Limits, limit => spawning.Count( limit.Counts ) );
	}

	/// <summary>
	/// Count objects towards <paramref name="player"/>'s limits now, rather than at the post-spawn event.
	/// For spawners that wait before creating, so a second request in the meantime sees the first one's objects.
	/// </summary>
	public void TrackNow( Connection player, List<GameObject> objects )
	{
		if ( player is null || objects is null ) return;

		foreach ( var go in objects )
			Track( (long)player.SteamId, go );
	}

	private static void NotifyLimit( Connection player, string category, int limit )
	{
		if ( player is null ) return;

		Notices.SendLocalizedNotice( player, "block", Color.Red, "#limit.reached", new()
		{
			{ "category", category },
			{ "limit", limit.ToString() }
		}, 3 );
	}
}
