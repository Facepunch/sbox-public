using NativeEngine;

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

		internal ObjectEntry( CEntityKeyValues keyValues, Vector3 origin )
			: this( ReadValues( keyValues ) )
		{
			WorldOrigin = origin;
			Position = GetValue<Vector3>( "origin" ) + origin;
			Angles = GetValue<Angles>( "angles" );
			Scales = GetValue( "scales", Vector3.One );
			Rotation = Angles.ToRotation();
			Transform = new( Position, Rotation, Scales );
		}

		static Dictionary<uint, string> ReadValues( CEntityKeyValues keyValues )
		{
			var values = new Dictionary<uint, string>();
			var keyCount = keyValues.GetKeyCount();
			for ( int i = 0; i < keyCount; i++ )
			{
				var key = keyValues.GetKey( i );
				values.Add( key, keyValues.GetValueString( key, null ) );
			}
			return values;
		}

		internal ObjectEntry( Dictionary<uint, string> keyValues )
		{
			values = keyValues;
			TypeName = GetValueString( "classname", null );
			TargetName = GetValueString( "targetname", null );
			ParentName = GetValueString( "parentname", null );

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

	internal void CreateEntities( IWorldReference worldRef, string entityLumpName )
	{
		var entityCount = worldRef.GetEntityCount( entityLumpName );
		if ( entityCount == 0 )
			return;

		var entries = new List<ObjectEntry>( entityCount );

		for ( int i = 0; i < entityCount; ++i )
		{
			var kv = worldRef.GetEntityKeyValues( entityLumpName, i );
			if ( !kv.IsValid )
				continue;

			entries.Add( new ObjectEntry( kv, WorldOrigin ) );
		}

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
