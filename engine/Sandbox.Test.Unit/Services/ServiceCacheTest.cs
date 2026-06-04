using System;
using System.Collections.Generic;

namespace Services;

[TestClass]
public class ServiceCacheTest
{
	static MemoryFileSystem _fs;

	[ClassInitialize]
	public static void Init( TestContext _ )
	{
		_fs = new MemoryFileSystem();
		Sandbox.Services.ServiceCache.FilesystemOverride = _fs;
	}

	[ClassCleanup]
	public static void Cleanup()
	{
		Sandbox.Services.ServiceCache.FilesystemOverride = null;
	}

	class Payload
	{
		public string Name { get; set; }
		public int Count { get; set; }
	}

	[TestMethod]
	public async Task DiskRoundTrip()
	{
		var key = $"rt_{Guid.NewGuid():N}";

		await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "alice", Count = 42 } ) );

		// Fetch throws → must come back from disk
		var fetched = await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => throw new Exception( "backend down" ) );

		Assert.IsNotNull( fetched );
		Assert.AreEqual( "alice", fetched.Name );
		Assert.AreEqual( 42, fetched.Count );
	}

	[TestMethod]
	public async Task TryFetchAsync_PrefersBackendOverDisk()
	{
		var key = $"prefer_{Guid.NewGuid():N}";

		// Seed disk with old data
		await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "old" } ) );

		// Backend succeeds — we should get backend value, not disk
		var fetched = await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "fresh" } ) );

		Assert.AreEqual( "fresh", fetched.Name );
	}

	[TestMethod]
	public async Task TryFetchAsync_ReturnsDefaultWhenBothFail()
	{
		var key = $"empty_{Guid.NewGuid():N}";

		var fetched = await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => throw new Exception( "backend down" ) );

		Assert.IsNull( fetched );
	}

	[TestMethod]
	public async Task LoadAsync_AppliesDiskThenBackend()
	{
		var key = $"load_{Guid.NewGuid():N}";

		// Seed disk
		await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "stale" } ) );

		var appliedOrder = new List<string>();

		await Sandbox.Services.ServiceCache.LoadAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "fresh" } ),
			p => appliedOrder.Add( p.Name ) );

		Assert.AreEqual( 2, appliedOrder.Count );
		Assert.AreEqual( "stale", appliedOrder[0] );
		Assert.AreEqual( "fresh", appliedOrder[1] );
	}

	[TestMethod]
	public async Task LoadAsync_BackendFailureKeepsDiskValue()
	{
		var key = $"loadfail_{Guid.NewGuid():N}";

		await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "from-disk" } ) );

		var appliedOrder = new List<string>();

		await Sandbox.Services.ServiceCache.LoadAsync<Payload>(
			key,
			() => throw new Exception( "backend down" ),
			p => appliedOrder.Add( p.Name ) );

		// Only the disk apply should have fired
		Assert.AreEqual( 1, appliedOrder.Count );
		Assert.AreEqual( "from-disk", appliedOrder[0] );
	}

	[TestMethod]
	public async Task LoadAsync_NoDiskNoBackend_AppliesNothing()
	{
		var key = $"none_{Guid.NewGuid():N}";
		var applied = 0;

		await Sandbox.Services.ServiceCache.LoadAsync<Payload>(
			key,
			() => throw new Exception( "backend down" ),
			_ => applied++ );

		Assert.AreEqual( 0, applied );
	}

	[TestMethod]
	public async Task KeyIsCaseInsensitiveOnDisk()
	{
		var key = $"case_{Guid.NewGuid():N}";

		await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key,
			() => Task.FromResult( new Payload { Name = "x" } ) );

		// A different-cased key must hit the same disk entry
		var fetched = await Sandbox.Services.ServiceCache.TryFetchAsync<Payload>(
			key.ToUpperInvariant(),
			() => throw new Exception( "backend down" ) );

		Assert.IsNotNull( fetched );
		Assert.AreEqual( "x", fetched.Name );
	}
}
