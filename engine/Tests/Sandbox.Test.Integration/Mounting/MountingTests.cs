using System;
using System.Collections.Generic;
using System.Linq;

using Sandbox.Mounting;

namespace MountingTests;

[TestClass]
public partial class MountingTest
{
	Configuration Config => new();

	[TestMethod]
	public async Task Initialization()
	{
		var system = new MountHost( Config );
		Assert.AreEqual( 0, system.All.Count );

		system.RegisterTypes( GetType().Assembly );

		Assert.AreEqual( 1, system.All.Count );

		var quake = system.GetSource( "testgame" );
		Assert.IsNotNull( quake );
		Assert.IsFalse( quake.IsMounted, "Test Game is mounted, when it shouldn't be" );

		Assert.IsTrue( quake.IsInstalled, "Test Game should be installed - because we have included it locally" );

		await system.Mount( "testgame" );

		Assert.IsTrue( quake.IsMounted, "We tried to mount testgame but it didn't mount" );

		foreach ( var file in quake.Resources )
		{
			if ( file.Type == ResourceType.Texture )
			{
				Texture texture = (Texture)await file.GetOrCreate();
				Assert.IsNotNull( texture );
			}
			else if ( file.Type == ResourceType.Model )
			{
				Model model = (Model)await file.GetOrCreate();
				Assert.IsNotNull( model );
			}
		}

		system.Dispose();
		Assert.AreEqual( 0, system.All.Count );
	}

	/// <summary>
	/// Calling Load should load a single resource, even if we're calling it while it's loading
	/// </summary>
	[TestMethod]
	public async Task CachedResource()
	{
		using var system = new MountHost( Config );
		system.RegisterTypes( GetType().Assembly );
		var quake = system.GetSource( "testgame" );
		await system.Mount( "testgame" );

		// get the first texture
		var target = quake.Resources.Where( x => x.Type == ResourceType.Texture ).FirstOrDefault();

		Assert.IsNotNull( target, "texture is null - no textures??" );

		var a = (Texture)await target.GetOrCreate();
		var b = (Texture)await target.GetOrCreate();

		Assert.AreEqual( a, b, "Loading should always return the same resource" );
	}

	/// <summary>
	/// Calling Load should load a single resource, even if we're calling it while it's loading
	/// </summary>
	[TestMethod]
	public async Task SingleResource()
	{
		using var system = new MountHost( Config );
		system.RegisterTypes( GetType().Assembly );
		var quake = system.GetSource( "testgame" );
		await system.Mount( "testgame" );

		// get the first texture
		var target = quake.Resources.Where( x => x.Type == ResourceType.Texture ).FirstOrDefault();

		Assert.IsNotNull( target, "texture is null - no textures??" );

		List<Task<Texture>> tasks = new();

		for ( int i = 0; i < 32; i++ )
		{
			var taskIndex = i;
			tasks.Add( Task.Run( async () =>
			{
				try
				{
					return (Texture)await target.GetOrCreate();
				}
				catch ( Exception ex )
				{
					// Log the exception
					System.Diagnostics.Debug.WriteLine( $"Exception in task {taskIndex}: {ex}" );
					throw; // Re-throw to fail the test properly
				}
			} ) );
		}

		try
		{
			var results = await Task.WhenAll( tasks );

			Assert.AreEqual( 32, results.Count(), "Didn't get 32 textures" );
			Assert.AreEqual( 1, results.Distinct().Count(), "Got different textures" );
		}
		catch ( AggregateException ae )
		{
			// This properly handles the exceptions from the tasks
			foreach ( var ex in ae.InnerExceptions )
			{
				System.Diagnostics.Debug.WriteLine( $"Task exception: {ex}" );
			}
			throw; // Re-throw to fail the test
		}
	}

}
