using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SystemTests;

/// <summary>
/// Reflection caches must preserve live results without owning collectible assemblies.
/// </summary>
[TestClass]
public class ReflectionCacheTests
{
	/// <summary>
	/// A cached value can refer back to its collectible key, including constructed generic types.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CollectibleKeysAndCyclicValuesAreReleased( bool constructedGeneric )
	{
		var cache = new ReflectionCache<Type, Type[]>( type => [type] );
		var references = PopulateCollectibleCache( cache, constructedGeneric );

		for ( var attempt = 0; attempt < 12 && (references.Type.IsAlive || references.Assembly.IsAlive); attempt++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		Assert.IsFalse( references.Type.IsAlive, "The cache retained the collectible type." );
		Assert.IsFalse( references.Assembly.IsAlive, "The cache retained the collectible assembly." );
		GC.KeepAlive( cache );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static (WeakReference Type, WeakReference Assembly) PopulateCollectibleCache( ReflectionCache<Type, Type[]> cache, bool constructedGeneric )
	{
		var type = CreateCollectibleType();
		var assembly = type.Assembly;
		if ( constructedGeneric )
		{
			type = typeof( List<> ).MakeGenericType( type );
			Assert.IsFalse( type.Assembly.IsCollectible, "This case must test collectible arguments in a permanent assembly." );
		}

		Assert.IsTrue( type.IsCollectible );
		var value = cache[type];
		Assert.AreSame( type, value[0] );
		Assert.AreSame( value, cache[type] );
		Assert.IsFalse( cache.TryAdd( type, [] ) );
		return (new WeakReference( type ), new WeakReference( assembly ));
	}

	/// <summary>
	/// Hotload invalidates both storage paths and reinstates explicitly supplied defaults.
	/// </summary>
	[TestMethod]
	public void HotloadClearsCollectibleAndPermanentEntries()
	{
		var evaluations = 0;
		var cache = new ReflectionCache<Type, int>( _ => ++evaluations, new KeyValuePair<Type, int>( typeof( void ), -1 ) );
		var collectibleType = CreateCollectibleType();

		Assert.AreEqual( 1, cache[typeof( string )] );
		Assert.AreEqual( 2, cache[collectibleType] );
		Assert.AreEqual( -1, cache[typeof( void )] );
		Assert.AreEqual( 2, evaluations );

		((IHotloadManaged)cache).Persisted();

		Assert.AreEqual( 3, cache[typeof( string )] );
		Assert.AreEqual( 4, cache[collectibleType] );
		Assert.AreEqual( -1, cache[typeof( void )] );
		Assert.IsTrue( cache.TryAdd( typeof( int ), 42 ) );
		Assert.AreEqual( 42, cache[typeof( int )] );
	}

	private static Type CreateCollectibleType()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( $"ReflectionCache_{Guid.NewGuid():N}" ), AssemblyBuilderAccess.RunAndCollect );
		return assembly.DefineDynamicModule( "Main" ).DefineType( "Collectible", TypeAttributes.Public ).CreateType();
	}
}
