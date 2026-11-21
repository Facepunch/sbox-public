namespace Sandbox;

public abstract class MapLoader
{
	/// <summary>
	/// Holds key values for the map object
	/// </summary>
	public readonly struct ObjectEntry
	{
		public string TypeName { get; init; }
		public string TargetName { get; init; }
		public string ParentName { get; init; }
		public Vector3 Position { get; init; }
		public Angles Angles { get; init; }
		public Rotation Rotation { get; init; }
		public Vector3 Scales { get; init; }
		public Transform Transform { get; init; }
		public Vector3 WorldOrigin { get; init; }
		public ITagSet Tags { get; init; }

		private readonly Dictionary<uint, string> values;

		/// <summary>
		/// An entity read from the map's entity lump by Borealis.Source2, its values as the strings native's key values give
		/// (<see cref="Borealis.Source2.World.MapEntity.Format"/>).
		/// </summary>
		internal ObjectEntry( Borealis.Source2.World.MapEntity entity, Vector3 origin ) : this( ValuesOf( entity ), origin )
		{
		}

		static Dictionary<uint, string> ValuesOf( Borealis.Source2.World.MapEntity entity )
		{
			var values = new Dictionary<uint, string>( entity.Count );
			foreach ( var (key, value) in entity.Values )
				values[key] = Borealis.Source2.World.MapEntity.Format( value );

			return values;
		}

		/// <summary>An entity from its values alone, placed at the world's origin.</summary>
		internal ObjectEntry( Dictionary<uint, string> keyValues ) : this( keyValues, Vector3.Zero )
		{
		}

		ObjectEntry( Dictionary<uint, string> values, Vector3 origin )
		{
			this.values = values;

			TypeName = GetValueString( "classname", null );
			TargetName = GetValueString( "targetname", null );
			ParentName = GetValueString( "parentname", null );
			WorldOrigin = origin;
			Position = GetValue<Vector3>( "origin" ) + WorldOrigin;
			Angles = GetValue<Angles>( "angles" );
			Scales = GetValue( "scales", Vector3.One );
			Rotation = Angles.ToRotation();
			Transform = new( Position, Rotation, Scales );

			var tags = GetValue<string>( "tags" ) ?? string.Empty;
			Tags = new ReadOnlyTagSet( tags.Split( ",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) );
		}

		private string GetValueString( string key, string defaultValue )
		{
			if ( values.TryGetValue( StringToken.FindOrCreate( key ), out var value ) )
			{
				return value;
			}

			return defaultValue;
		}

		public readonly T GetValue<T>( string key, T defaultValue = default )
		{
			var value = GetValueString( key, null );
			if ( string.IsNullOrWhiteSpace( value ) )
				return defaultValue;

			return (T)value.ToType( typeof( T ) );
		}

		public readonly T GetResource<T>( string key, T defaultValue = default ) where T : Resource
		{
			var value = GetValueString( key, null );
			if ( string.IsNullOrWhiteSpace( value ) )
				return defaultValue;

			return Resource.Load( typeof( T ), value ) as T;
		}

		public readonly string GetString( string key, string defaultValue = default )
		{
			return GetValueString( key, defaultValue );
		}
	}

	public SceneWorld World { get; private set; }
	public PhysicsWorld PhysicsWorld { get; private set; }
	public Vector3 WorldOrigin { get; private set; }

	protected readonly List<SceneObject> SceneObjects = new();

	public MapLoader( SceneWorld world, PhysicsWorld physics, Vector3 origin = default )
	{
		World = world;
		PhysicsWorld = physics;
		WorldOrigin = origin;
	}

	/// <summary>
	/// Create an object from a serialized object entry
	/// </summary>
	protected abstract void CreateObject( ObjectEntry kv );

	/// <summary>
	/// Create the objects for a map's entities read from its entity lump by Borealis.Source2.
	/// </summary>
	internal void CreateEntities( IReadOnlyList<Borealis.Source2.World.MapEntity> entities )
	{
		if ( entities.Count == 0 )
			return;

		var entries = new List<ObjectEntry>( entities.Count );
		foreach ( var entity in entities )
			entries.Add( new ObjectEntry( entity, WorldOrigin ) );

		CreateEntities( entries );
	}

	void CreateEntities( List<ObjectEntry> entries )
	{
		entries.Sort( ( a, b ) =>
		{
			if ( string.IsNullOrEmpty( a.ParentName ) && string.IsNullOrEmpty( b.ParentName ) ) return 0;
			if ( string.IsNullOrEmpty( a.ParentName ) ) return -1;
			if ( string.IsNullOrEmpty( b.ParentName ) ) return 1;
			if ( a.ParentName == b.TargetName ) return 1;
			if ( b.ParentName == a.TargetName ) return -1;
			return string.Compare( a.TargetName, b.TargetName, StringComparison.Ordinal );
		} );

		foreach ( var kv in entries )
		{
			try
			{
				CreateObject( kv );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when trying to create entity ({kv.TypeName})" );
			}
		}
	}
}
