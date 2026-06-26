using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Diagnostics;
using NodeLibrary = Facepunch.ActionGraphs.NodeLibrary;

namespace ActionGraphTests
{
	[AssetType( Name = "Example", Extension = "example" )]
	public class ExampleResource : GameResource
	{
		public Func<Task> DoSomething { get; set; }
	}

	[TestClass]
	public class SerializationTest
	{
		private NodeLibrary _oldNodeLibrary;

		public TypeLibrary TypeLibrary;
		public NodeLibrary Nodes => Game.NodeLibrary;

		[TestInitialize]
		public void Initialize()
		{
			TypeLibrary = new TypeLibrary();
			TypeLibrary.AddAssembly( typeof( LogNodes ).Assembly, false );

			_oldNodeLibrary = Nodes;

			Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => TypeLibrary ), new GraphLoader() );

			var result = Nodes.AddAssembly( typeof( LogNodes ).Assembly );

			foreach ( var (method, e) in result.Errors )
			{
				Debug.WriteLine( $"{method.ToSimpleString()}: {e}" );
			}

			Assert.IsFalse( result.AlreadyAdded );
			Assert.AreEqual( 0, result.Errors.Count );
		}

		[TestCleanup]
		public void Cleanup()
		{
			Game.NodeLibrary = _oldNodeLibrary;
		}

		private Func<Task> CreateHelloWorldGraph()
		{
			var graph = ActionGraph.CreateDelegate<Func<Task>>( Nodes );
			var start = graph.Graph.InputNode!;
			var log = graph.Graph.AddNode( LogNodes.Info );

			log.Inputs.Signal.SetLink( start.Outputs.Signal );
			log.Inputs["format"].Value = "Hello, {0}!";
			log.Inputs["args"].Value = new object[] { "World" };

			return graph;
		}

		[TestMethod]
		public async Task SerializeSimple()
		{
			var resource = new ExampleResource { DoSomething = CreateHelloWorldGraph() };
			var json = Json.SerializeAsObject( resource ).ToJsonString( Json.options );

			Console.WriteLine( json );

			resource.DoSomething = null;

			Json.DeserializeToObject( resource, json );

			await resource.DoSomething!();
		}
	}
}
