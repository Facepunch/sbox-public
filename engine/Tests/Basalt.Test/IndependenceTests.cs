using Basalt;
using System;

namespace BasaltTests;

/// <summary>
/// Basalt needs nothing of the engine but Sandbox.System, so it can render anywhere a host supplies a device.
/// </summary>
[TestClass]
public class IndependenceTests
{
	/// <summary>
	/// This project only references Basalt and Sandbox.System, so an engine type in Basalt fails its build - unless Basalt
	/// references the engine itself, which this catches.
	/// </summary>
	[TestMethod]
	public void BasaltReferencesOnlySandboxSystem()
	{
		var references = typeof( RenderSystem ).Assembly.GetReferencedAssemblies()
			.Select( a => a.Name )
			.Where( n => !n.StartsWith( "System", StringComparison.Ordinal ) && !n.StartsWith( "Microsoft", StringComparison.Ordinal ) && n != "netstandard" )
			.ToArray();

		CollectionAssert.AreEquivalent( new[] { "Sandbox.System" }, references );
	}

	/// <summary>
	/// The tests render through the headless device, never the engine's.
	/// </summary>
	[TestMethod]
	public void RendersOnTheHeadlessDevice()
	{
		Assert.IsInstanceOfType<Headless.HeadlessDevice>( RenderHost.Current.Device );
		Assert.IsFalse( AppDomain.CurrentDomain.GetAssemblies().Any( a => a.GetName().Name is "Sandbox.Engine" or "Sandbox.Rendering" ), "nothing loaded the engine" );
	}
}
