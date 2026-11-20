using Sandbox.Upgraders;
using System;

namespace HotloadTests;

/// <summary>
/// Behaviour of a <see cref="Sandbox.Hotload"/> instance, rather than what it does to the instances it upgrades.
/// </summary>
[TestClass]
public class HotloadInstanceTest : HotloadFixtureTestBase
{
	private const string EmptyFixture =
		"""
		public class EmptyFixture : IHotloadTestFixture;

		-public class RemovedClass;
		+public class AddedClass;
		""";

	/// <summary>
	/// After a hotload, the most recent replacing assembly must be auto-watched.
	/// </summary>
	[TestMethod]
	public async Task WatchNewAssembly()
	{
		using var fixture = await CreateFixtureAsync( EmptyFixture );
		await fixture.BuildNewAsync();

		var hotload = fixture.CreateHotload();

		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.NewAssembly ) );

		hotload.UpdateReferences();

		Assert.IsTrue( hotload.WatchedAssemblies.Contains( fixture.NewAssembly ) );
	}

	/// <summary>
	/// After a hotload, any replaced assemblies must not be auto-watched.
	/// </summary>
	[TestMethod]
	public async Task DontWatchOldAssembly()
	{
		using var fixture = await CreateFixtureAsync( EmptyFixture );
		await fixture.BuildNewAsync();

		var hotload = fixture.CreateHotload();

		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.OldAssembly ) );

		hotload.UpdateReferences();

		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.OldAssembly ) );
	}

	/// <summary>
	/// After a hotload, any assemblies replaced with null should not be watched.
	/// </summary>
	[TestMethod]
	public async Task UnloadAssembly()
	{
		using var fixture = await CreateFixtureAsync( EmptyFixture );
		await fixture.BuildNewAsync();

		var hotload = fixture.CreateHotload();

		hotload.UpdateReferences();
		hotload.ReplacingAssembly( fixture.NewAssembly, null );
		hotload.UpdateReferences();

		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.OldAssembly ) );
		Assert.IsFalse( hotload.WatchedAssemblies.Contains( fixture.NewAssembly ) );
	}

	/// <summary>
	/// Delegates must not be processed by the DefaultUpgrader, they contain fields that throw when accessed.
	/// </summary>
	[TestMethod]
	public async Task DefaultUpgraderRejectsDelegate()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			public class DefaultUpgraderRejectsDelegate : IHotloadTestFixture;

			public static class StaticHandlerClass
			{
				public static void Handler()
				{
					System.Console.WriteLine( "Hello, world!" );
				}
			}
			""" );

		await fixture.BuildNewAsync();

		var hotload = fixture.CreateHotload();

		hotload.UpdateReferences();

		var deleg = fixture.OldAssembly.GetType( "StaticHandlerClass" )!.GetMethod( "Handler" )!.CreateDelegate<Action>();

		Assert.IsFalse( hotload.GetUpgrader<DefaultUpgrader>().TryUpgradeInstance( deleg, deleg ) );
	}
}
