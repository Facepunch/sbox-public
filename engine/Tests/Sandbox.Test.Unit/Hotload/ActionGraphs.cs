using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using System;

namespace HotloadTests;

/// <summary>
/// Delegates implemented by an <see cref="ActionGraph"/> get special handling when hotloading. The graphs share
/// <see cref="SharedLibraries.Nodes"/>, but hotload skips that, so these can run in parallel.
/// </summary>
[TestClass]
public class ActionGraphTest : HotloadFixtureTestBase
{
	private const string Fixture =
		"""
		using System;
		using System.Threading.Tasks;

		public class ActionGraphFixture : IHotloadTestFixture
		{
			public Func<Task> Func;
		}
		""";

	private static Func<Task> CreateHelloWorldGraph( string message = "World" )
	{
		var graph = ActionGraph.CreateDelegate<Func<Task>>( SharedLibraries.Nodes );
		var start = graph.Graph.InputNode!;
		var log = graph.Graph.AddNode( LogNodes.Info );

		log.Inputs.Signal.SetLink( start.Outputs.Signal );
		log.Inputs["format"].Value = "Hello, {0}!";
		log.Inputs["args"].Value = new object[] { message };

		return graph;
	}

	[TestMethod]
	public async Task SimpleActionGraph()
	{
		using var fixture = await CreateFixtureAsync( Fixture );

		var func = CreateHelloWorldGraph();

		SetField( fixture.OldInstance, "Func", func );

		await func();

		await fixture.HotloadAsync();

		var newFunc = (Func<Task>)GetField( fixture.Instance, "Func" );

		Assert.IsNotNull( newFunc );

		await newFunc();
	}

	/// <summary>
	/// Make sure multicast action graph delegates are preserved.
	/// </summary>
	[TestMethod]
	public async Task MulticastActionGraph()
	{
		using var fixture = await CreateFixtureAsync( Fixture );

		var func = CreateHelloWorldGraph( "A" ) + CreateHelloWorldGraph( "B" ) + CreateHelloWorldGraph( "C" );

		SetField( fixture.OldInstance, "Func", func );

		Assert.AreEqual( 3, func.GetInvocationList().Length );

		await func();

		await fixture.HotloadAsync();

		var newFunc = (Func<Task>)GetField( fixture.Instance, "Func" );

		Assert.IsNotNull( newFunc );
		Assert.AreEqual( 3, newFunc.GetInvocationList().Length );

		await newFunc();
	}
}
