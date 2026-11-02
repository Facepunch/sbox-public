using Sandbox.Internal;
using Sandbox.Utility;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;

/// <summary>
/// Creates a resource from a json definition
/// </summary>
[Expose]
public abstract class ResourceGenerator
{
	public struct Options
	{
		/// <summary>
		/// True if we're compiling this resource to write to disk
		/// </summary>
		public bool ForDisk { get; set; }

		/// <summary>
		/// Will be set to the compiler that is currently compiling this resource. Or null, if we're generating in another method.
		/// </summary>
		public ResourceCompiler Compiler { get; set; }

		public static Options Default => new Options { };
	}

	/// <summary>
	/// If true then the generation will create a real resource and store it on disk.
	/// Use this if creating the resource takes a while, or you won't be shipping the generator
	/// with the game, or if it relies on data that won't be available in the shipped game.
	/// </summary>
	[JsonIgnore, Hide]
	public virtual bool CacheToDisk => false;

	/// <summary>
	/// Other assets whose compiled output generating this reads, by path, known without generating it.
	/// </summary>
	internal virtual IEnumerable<string> CompiledReads => [];

	/// <summary>
	/// Create a ResourceGenerator by name
	/// </summary>
	public static ResourceGenerator<T> Create<T>( string generatorName ) where T : Resource
	{
		var typeLibrary = TypeLibrary.Editor ?? Game.TypeLibrary;
		if ( typeLibrary is null ) return default;

		var t = typeLibrary.GetType<ResourceGenerator<T>>( generatorName );
		if ( t == null ) return default;

		return t.Create<ResourceGenerator<T>>();
	}

	/// <summary>
	/// Create a ResourceGenerator by name and deserialize it
	/// </summary>
	public static ResourceGenerator<T> Create<T>( EmbeddedResource serialized ) where T : Resource
	{
		// find the generator name
		if ( string.IsNullOrEmpty( serialized.ResourceGenerator ) ) return default;

		// create the generator type
		var generator = Create<T>( serialized.ResourceGenerator );
		if ( generator is null ) return default;

		// fill it with json
		generator.Deserialize( serialized.Data );

		return generator;
	}

	public static T CreateResource<T>( EmbeddedResource obj, Options options ) where T : Resource
	{
		// create the generator type
		var generator = Create<T>( obj );
		if ( generator is null ) return default;

		// create the resource
		return generator.FindOrCreate( options );
	}

	/// <summary>
	/// Create a resource from an embedded resource with a given <see cref="Type"/>
	/// </summary>
	/// <param name="obj"></param>
	/// <param name="options"></param>
	/// <param name="type"></param>
	/// <returns></returns>
	public static Resource CreateResource( EmbeddedResource obj, Options options, Type type )
	{
		var typeLibrary = TypeLibrary.Editor ?? Game.TypeLibrary;
		if ( typeLibrary is null ) return default;

		var t = typeLibrary.GetType<ResourceGenerator>( obj.ResourceGenerator );
		if ( t == null ) return default;

		var generator = t.Create<ResourceGenerator>();

		// fill it with json
		generator.Deserialize( obj.Data );

		// create the resource
		return generator.FindOrCreateObject( options ) as Resource;
	}

	/// <summary>
	/// Copy properties from obj to us
	/// </summary>
	public virtual void Deserialize( JsonObject obj )
	{
		if ( obj is null ) return;

		Json.DeserializeToObject( this, obj );
	}

	/// <summary>
	/// Returns a hash to be used when loading/saving. We use this to determine if the resource has changed.
	/// By default we serialize the generator to a json string and return the CRC64 of that value. You can
	/// override this in your generator if you need to make it faster, or ignore some stuff.
	/// </summary>
	public virtual ulong GetHash()
	{
		var jsonString = Json.Serialize( this );
		return Crc64.FromString( jsonString );
	}

	/// <summary>
	/// If we generated this before, then find the current cache'd value.
	/// If not, then generate a new one.
	/// </summary>
	public abstract ValueTask<Resource> FindOrCreateObjectAsync( Options options, CancellationToken token );

	/// <summary>
	/// Find or create the resource (blocking) 
	/// </summary>
	/// <param name="options"></param>
	/// <returns></returns>
	public abstract Resource FindOrCreateObject( Options options );
}

/// <summary>
/// A resource generator targetting a specific type
/// </summary>
public abstract class ResourceGenerator<T> : ResourceGenerator where T : Resource
{
	static WeakDictionary<ulong, T> cache = new();

	/// <summary>
	/// If true then the generation will avoid creating duplicate resources by checking
	/// hash codes of previously generated resources and re-using them if possible.
	/// </summary>
	[Hide, JsonIgnore]
	public virtual bool UseMemoryCache => true;

	/// <summary>
	/// Find a previously created of this resource
	/// </summary>
	public virtual T FindCached()
	{
		if ( !UseMemoryCache )
			return default;

		var hash = GetHash();

		if ( cache.TryGetValue( hash, out var value ) )
		{
			return value;
		}

		return default;
	}

	/// <summary>
	/// Add this resource to the cache for our current hash
	/// </summary>
	public void AddToCache( T val )
	{
		if ( !UseMemoryCache ) return;
		if ( val == default ) return;

		cache.Set( GetHash(), val );
	}

	/// <summary>
	/// If we generated this before, then find the current cache'd value.
	/// If not, then generate a new one.
	/// </summary>
	public virtual T FindOrCreate( Options options )
	{
		if ( FindCached() is { } cached )
			return cached;

		var x = Create( options );
		AddToCache( x );
		return x;
	}

	public override async ValueTask<Resource> FindOrCreateObjectAsync( Options options, CancellationToken token )
		=> await FindOrCreateAsync( options, token );

	public override Resource FindOrCreateObject( Options options ) => FindOrCreate( options );

	/// <summary>
	/// If we generated this before, then find the current cache'd value.
	/// If not, then generate a new one.
	/// </summary>
	public virtual async ValueTask<T> FindOrCreateAsync( Options options, CancellationToken token )
	{
		if ( FindCached() is { } cached )
			return cached;

		var hash = GetHash();
		var v = await CreateAsync( options, token );

		// If the cache value changed while we were generating
		// then throw this value away!
		if ( hash == GetHash() )
		{
			AddToCache( v );
		}

		return v;
	}

	/// <summary>
	/// Create the resource blocking
	/// </summary>
	public abstract T Create( Options options );

	/// <summary>
	/// Create the resource asyncronously
	/// </summary>
	public abstract ValueTask<T> CreateAsync( Options options, CancellationToken token );
}


class WeakDictionary<TKey, TValue> where TValue : class
{
	private readonly ConcurrentDictionary<TKey, WeakReference<TValue>> _table = new();

	public void Set( TKey key, TValue value )
	{
		var weakValue = new WeakReference<TValue>( value );
		_table[key] = weakValue;
		Cleanup();
	}

	public bool TryGetValue( TKey key, out TValue value )
	{
		Cleanup();

		value = default;

		if ( !_table.TryGetValue( key, out var weak ) )
			return false;

		return weak.TryGetTarget( out value );
	}

	private void Cleanup()
	{
		foreach ( var kvp in _table )
		{
			if ( !kvp.Value.TryGetTarget( out _ ) )
			{
				_table.TryRemove( kvp.Key, out _ );
			}
		}
	}
}
