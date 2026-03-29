using Microsoft.Extensions.Caching.Memory;
using Sandbox;
using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// We only want 1 instance of a Resource class in C# and we want that to have 1 strong handle to native.
/// So we need a WeakReference lookup everytime we get a Resource from native to match that class.
/// This way GC can work for us and free anything we're no longer using anywhere, fantastic!
/// 
/// However sometimes GC is very good at it's job and will free Resources we don't keep a strong reference to
/// in generation 0 or 1 immediately after usage. This can cause the resource to need to be loaded every frame.
/// Or worse be finalized at unpredictable times.
/// 
/// So we keep a sliding memory cache of the Resources - realistically these only need to live for an extra frame.
/// But it's probably nice to keep around for longer if they're going to be used on and off.
/// </summary>
internal static partial class NativeResourceCache
{
	const int ExpirationSeconds = 5;
	static readonly TimeSpan SlidingExpiration = TimeSpan.FromSeconds( ExpirationSeconds );
	static readonly MemoryCache MemoryCache = new( new MemoryCacheOptions() { } );

	/// <summary>
	/// We still want a WeakReference cache because we might have a strong reference somewhere to a resource
	/// that has been expired from the cache. And we absolutely only want 1 instance of the resource.
	/// </summary>
	static readonly ConcurrentDictionary<long, WeakReference> WeakTable = new();

	/// <summary>
	/// When <see cref="LeakTracking"/> is enabled, stores the callstack captured at the time each resource
	/// was added to the cache. This prevents WeakTable pruning so every allocation remains visible at
	/// shutdown.
	/// </summary>
	static readonly ConcurrentDictionary<long, string> CallstackTable = new();

	/// <summary>
	/// When enabled, disables WeakTable entry pruning and captures allocation callstacks so that
	/// resource leaks can be diagnosed at shutdown with full context.
	/// </summary>
	[ConVar( "resource_leak_tracking" )]
	public static bool LeakTracking
	{
		get => field;
		set
		{
			if ( value && !field )
			{
				Log.Warning( "resource_leak_tracking enabled — resources allocated before this point will not have callstacks and may not appear in the shutdown leak report." );
			}
			field = value;
		}
	} = false;

	private static Action<MemoryCache, DateTime> StartScanForExpiredItemsIfNeeded { get; } = typeof( MemoryCache )
		.GetMethod( nameof( StartScanForExpiredItemsIfNeeded ), BindingFlags.Instance | BindingFlags.NonPublic )
		.CreateDelegate<Action<MemoryCache, DateTime>>();

	internal static void Add( long key, object value )
	{
		var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration( SlidingExpiration );
		MemoryCache.Set( key, value, cacheEntryOptions );

		WeakTable[key] = new WeakReference( value );

		if ( LeakTracking )
		{
			CallstackTable[key] = new System.Diagnostics.StackTrace( skipFrames: 1, fNeedFileInfo: false ).ToString();
		}
	}

	/// <summary>
	/// Remove a key from both caches. Used when a resource is explicitly disposed
	/// so that a new instance can be created for the same native pointer.
	/// </summary>
	internal static void Remove( long key )
	{
		MemoryCache.Remove( key );
		WeakTable.TryRemove( key, out _ );
		CallstackTable.TryRemove( key, out _ );
	}

	internal static bool TryGetValue<T>( long key, out T value ) where T : class
	{
		if ( MemoryCache.TryGetValue( key, out value ) )
		{
			return true;
		}

		// If we missed the Cache, check our weak refs.
		// Read Target once to avoid TOCTOU race with GC.
		if ( WeakTable.TryGetValue( key, out var weakValue ) && weakValue.Target is T target )
		{
			value = target;

			// and add it back to the cache
			Add( key, value );

			return true;
		}

		return false;
	}

	static TimeSince LastScan = 0;

	/// <summary>
	/// Ticks the underlying MemoryCache to clear expired entries
	/// </summary>
	internal static void Tick()
	{
		if ( LastScan < ExpirationSeconds )
			return;

		LastScan = 0;

		// MemoryCache doesn't have its own timer for clearing anything...
		// This will get rid of any expired stuff
		StartScanForExpiredItemsIfNeeded( MemoryCache, DateTime.UtcNow );

		// Prune dead WeakTable entries to prevent unbounded growth from procedural resources.
		// Skipped when leak tracking is enabled so every allocation stays visible until shutdown.
		if ( !LeakTracking )
		{
			foreach ( var kvp in WeakTable )
			{
				if ( kvp.Value.Target is null )
				{
					WeakTable.TryRemove( kvp.Key, out _ );
				}
			}
		}

	}

	/// <summary>
	/// Returns stats about the NativeResourceCache for debug overlays.
	/// </summary>
	internal static NativeCacheStats GetStats()
	{
		var stats = new NativeCacheStats();

		foreach ( var kvp in WeakTable )
		{
			var target = kvp.Value.Target;
			var alive = target is not null;
			var typeName = alive ? target.GetType().Name : "(dead)";
			stats.Entries.TryGetValue( typeName, out var count );
			stats.Entries[typeName] = count + 1;
		}

		stats.WeakTableTotal = WeakTable.Count;
		stats.MemoryCacheCount = MemoryCache.Count;

		return stats;
	}

	internal struct NativeCacheStats
	{
		public Dictionary<string, int> Entries;
		public int WeakTableTotal;
		public int MemoryCacheCount;

		public NativeCacheStats()
		{
			Entries = new();
		}
	}

	/// <summary>
	/// Clear the cache when games are closed etc. ready for a <see cref="GC.Collect()"/>
	/// </summary>
	internal static void Clear()
	{
		ClearCache();

		// When leak tracking is enabled, preserve the WeakTable and CallstackTable across
		// game resets so that resources allocated before the reset remain visible to HandleShutdownLeaks() at shutdown.
		if ( !LeakTracking ) ClearWeakTable();
	}

	internal static void ClearCache()
	{
		MemoryCache.Clear();
	}

	private static void ClearWeakTable()
	{
		WeakTable.Clear();
		CallstackTable.Clear();
	}

	internal static void HandleShutdownLeaks()
	{
		int leaks = 0;

		foreach ( var kvp in WeakTable )
		{
			if ( kvp.Value.Target is Resource resource && resource.IsValid() )
			{
				var resourceName = resource.ResourceName;
				ulong resourceId = resource.ResourceIdLong;

				if ( resource is Texture tex )
				{
					resourceName = "RenderTarget";
					resourceId = (ulong)tex.native.self; // Texture resources can be render targets, which have a unique native pointer but have no name or path.
				}
				Log.Warning( $"NativeResourceCache: Resource still alive during shutown, this can indicate a leak: {resource.GetType().Name} [{resourceId}] {resourceName} ({resource.ResourcePath}) will be force destroyed." );
				if ( LeakTracking && CallstackTable.TryGetValue( kvp.Key, out var callstack ) )
				{
					Log.Warning( $"NativeResourceCache: Allocation callstack:\n{callstack}" );
				}
				leaks++;
			}
		}

		if ( leaks > 0 ) Log.Warning( $"NativeResourceCache: Total leaks: {leaks}" );

		// Force destory all resources and than clear the cache to prevent any resurrected resources from being reported as leaks.
		foreach ( var kvp in WeakTable )
		{
			if ( kvp.Value.Target is Resource resource && resource.IsValid() )
			{
				resource.Destroy();
			}
		}

		ClearWeakTable();
	}
}
