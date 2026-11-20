using Sandbox.Engine;
using Sandbox.Internal;

namespace HotloadTests;

/// <summary>
/// Hotloading with engine-wide caches, which are shared between tests, so these can't run in parallel.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GlobalCacheTest : HotloadFixtureTestBase
{
	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void Initialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedLibraries.Types;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	/// <summary>
	/// Adding a [Sync] attribute to a property across a hotload: the live instance must be swapped to the new
	/// version of the type, and querying sync properties on its type must pick up the added attribute once the
	/// type cache is cleared.
	/// </summary>
	[TestMethod]
	public async Task AddedSyncVar()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using Sandbox;

			public class AddedSyncVar : IHotloadTestFixture
			{
			-	public float MySyncFloat { get; set; }
			+	[Sync] public float MySyncFloat { get; set; }

				// Code generation for [Sync] calls these, like the ones on Component
				protected void __sync_SetValue<T>( in WrappedPropertySet<T> p ) => p.Setter?.Invoke( p.Value );
				protected T __sync_GetValue<T>( WrappedPropertyGet<T> p ) => p.Value;
			}
			""" );

		Assert.AreEqual( 0, ReflectionQueryCache.SyncProperties( fixture.OldInstance.GetType() ).Count() );

		await fixture.HotloadAsync( configure: x => x.WatchAssembly( "Sandbox.Engine", t => t.Name == nameof( ReflectionQueryCache ) ) );

		// Clear the type cache so next time we'll pick up the added [Sync] attribute

		ReflectionQueryCache.ClearTypeCache();

		Assert.AreEqual( 1, ReflectionQueryCache.SyncProperties( fixture.Instance.GetType() ).Count() );
	}

	/// <summary>
	/// Prove that <see cref="ReflectionQueryCache.ClearTypeCache"/> gets called
	/// in <see cref="GlobalContext.OnHotload"/>.
	/// </summary>
	[TestMethod]
	public void ReflectionQueryCacheClearOnHotload()
	{
		using var ctxScope = GlobalContext.GameScope();

		// Do something that populates the cache

		ReflectionQueryCache.OrderedSerializableMembers( typeof( GameObject ) );

		Assert.IsFalse( ReflectionQueryCache.IsEmpty );

		// This should clear the cache

		GlobalContext.Current.OnHotload();

		Assert.IsTrue( ReflectionQueryCache.IsEmpty );
	}
}
