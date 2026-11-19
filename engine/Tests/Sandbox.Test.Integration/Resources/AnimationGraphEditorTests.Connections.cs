using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ResourceTests;

public partial class AnimationGraphEditorTests
{
	object AddGraphNode( object graph, string type )
	{
		var node = Activator.CreateInstance( EditorType( $"Nodes.{type}" ) );
		Call( graph, "AddNode", node );
		return node;
	}

	static object Plug( object node, string direction, string name )
		=> Get<IEnumerable>( node, direction ).Cast<object>().Single( plug => Get<string>( plug, "Identifier" ) == name );

	static PropertyInfo ConnectedOutputProperty( object input )
		=> input.GetType().GetInterface( "Editor.NodeEditor.IPlugIn" ).GetProperty( "ConnectedOutput" );

	static object ConnectedOutput( object input ) => ConnectedOutputProperty( input ).GetValue( input );
	static void Connect( object input, object output ) => ConnectedOutputProperty( input ).SetValue( input, output );

	[TestMethod]
	[DataRow( "ReferencePoseNode", "Result", "LayerBlendNode", "Base", true )]
	[DataRow( "PoseGroupNode", "Result", "LayerBlendNode", "Base", true )]
	[DataRow( "LocalLayerNode", "Layer", "LayerBlendNode", "Layer0", true )]
	[DataRow( "ConstFloatNode", "Result", "LocalLayerNode", "Weight", false )]
	public void ConnectingOutputsReplacesOnlyPoseAndLayerConsumers( string sourceType, string outputName, string targetType, string inputName, bool replace )
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, sourceType );
		var first = Plug( AddGraphNode( graph, targetType ), "Inputs", inputName );
		var second = Plug( AddGraphNode( graph, targetType ), "Inputs", inputName );
		var output = Plug( source, "Outputs", outputName );

		Connect( first, output );
		Connect( second, output );
		Assert.AreEqual( replace ? null : output, ConnectedOutput( first ) );
		Assert.AreEqual( output, ConnectedOutput( second ) );

		Connect( second, output );
		Assert.AreEqual( output, ConnectedOutput( second ) );
		Connect( second, null );
		Assert.IsNull( ConnectedOutput( second ) );
		Assert.AreEqual( replace ? null : output, ConnectedOutput( first ) );
	}

	[TestMethod]
	public void PoseConnectionReplacementIncludesDynamicInputsOnTheSameNode()
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, "ReferencePoseNode" );
		var target = AddGraphNode( graph, "VelocityBlendNode" );
		var output = Plug( source, "Outputs", "Result" );
		var first = Plug( target, "Inputs", "Input0" );
		var second = Plug( target, "Inputs", "Input1" );
		Connect( first, output );
		Connect( second, output );
		Assert.IsNull( ConnectedOutput( first ) );
		Assert.AreEqual( output, ConnectedOutput( second ) );
	}

	[TestMethod]
	[DataRow( "ReferencePoseNode", "Base", true )]
	[DataRow( "ConstFloatNode", "Weight", false )]
	public void ReroutesPreserveOutputConnectionLimits( string sourceType, string inputName, bool replace )
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, sourceType );
		var route = AddGraphNode( graph, "RerouteNode" );
		var next = AddGraphNode( graph, "RerouteNode" );
		Connect( Plug( route, "Inputs", "Input" ), Plug( source, "Outputs", "Result" ) );
		Connect( Plug( next, "Inputs", "Input" ), Plug( route, "Outputs", "Result" ) );
		var output = Plug( next, "Outputs", "Result" );
		var targetType = replace ? "LayerBlendNode" : "LocalLayerNode";
		var first = Plug( AddGraphNode( graph, targetType ), "Inputs", inputName );
		var second = Plug( AddGraphNode( graph, targetType ), "Inputs", inputName );

		Connect( first, output );
		Connect( second, output );
		Assert.AreEqual( replace ? null : output, ConnectedOutput( first ) );
		Assert.AreEqual( output, ConnectedOutput( second ) );
	}

	[TestMethod]
	public void ConnectingPoseToUnboundRerouteRemovesDownstreamFanOut()
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, "ReferencePoseNode" );
		var route = AddGraphNode( graph, "RerouteNode" );
		var next = AddGraphNode( graph, "RerouteNode" );
		Connect( Plug( next, "Inputs", "Input" ), Plug( route, "Outputs", "Result" ) );
		var output = Plug( next, "Outputs", "Result" );
		var first = Plug( AddGraphNode( graph, "LayerBlendNode" ), "Inputs", "Base" );
		var second = Plug( AddGraphNode( graph, "LayerBlendNode" ), "Inputs", "Base" );
		Connect( first, output );
		Connect( second, output );
		Assert.AreEqual( output, ConnectedOutput( first ) );
		Assert.AreEqual( output, ConnectedOutput( second ) );

		Connect( Plug( route, "Inputs", "Input" ), Plug( source, "Outputs", "Result" ) );
		Assert.AreEqual( output, ConnectedOutput( first ) );
		Assert.IsNull( ConnectedOutput( second ) );
	}

	[TestMethod]
	[DataRow( "ReferencePoseNode", "Result", "Base", "Pose" )]
	[DataRow( "LocalLayerNode", "Layer", "Layer0", "Layer" )]
	public void LoadingAndCloningLegacyFanOutPreservesConnectionsAndWarns( string sourceType, string outputName, string inputName, string kind )
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var connection = $$"""{"Identifier":"source","Output":"{{outputName}}"}""";
		var inputs = kind == "Pose" ? $"\"Base\":{connection}" : $"\"Layers\":[{connection}]";
		var otherInputs = inputs.Replace( "\"source\"", "\"other\"" );
		Call( graph, "Deserialize", $$"""
			{"nodes":[
				{"_class":"{{sourceType}}","Identifier":"source"},
				{"_class":"LayerBlendNode","Identifier":"first",{{inputs}}},
				{"_class":"LayerBlendNode","Identifier":"second",{{inputs}}},
				{"_class":"{{sourceType}}","Identifier":"other"},
				{"_class":"LayerBlendNode","Identifier":"third",{{otherInputs}}},
				{"_class":"LayerBlendNode","Identifier":"fourth",{{otherInputs}}}
			]}
			""" );
		foreach ( var candidate in new[] { graph, Call( graph, "Clone", true ) } )
		{
			var nodes = Get<IEnumerable>( candidate, "Nodes" ).Cast<object>().ToArray();
			var output = Plug( nodes[0], "Outputs", outputName );
			Assert.AreEqual( output, ConnectedOutput( Plug( nodes[1], "Inputs", inputName ) ) );
			Assert.AreEqual( output, ConnectedOutput( Plug( nodes[2], "Inputs", inputName ) ) );
			var result = EditorType( "Compiler" ).GetMethod( "Compile", [EditorType( "Data" )] ).Invoke( null, [candidate] );
			var warnings = Get<IEnumerable>( result, "Warnings" ).Cast<object>()
				.Where( warning => Get<string>( warning, "Message" ).StartsWith( $"{kind} output '{outputName}' has multiple connections." ) ).ToArray();
			Assert.AreEqual( 2, warnings.Length );
			Assert.AreEqual( 2, warnings.Select( warning => Get<object>( warning, "Node" ) ).Distinct().Count() );
			Assert.IsTrue( warnings.All( warning => !Get<bool>( warning, "IsError" ) ) );
		}
	}

	[TestMethod]
	public void PastingConsumerOfExistingPoseOutputReplacesPreviousConnection()
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, "ReferencePoseNode" );
		var output = Plug( source, "Outputs", "Result" );
		var first = Plug( AddGraphNode( graph, "LayerBlendNode" ), "Inputs", "Base" );
		Connect( first, output );
		var pasted = (IEnumerable)Call( graph, "DeserializeNodes", $$$"""
			{"nodes":[{"_class":"LayerBlendNode","Identifier":"pasted","Base":{"Identifier":"{{{Get<string>( source, "Identifier" )}}}","Output":"Result"}}]}
			""" );
		Assert.IsNull( ConnectedOutput( first ) );
		Assert.AreEqual( output, ConnectedOutput( Plug( pasted.Cast<object>().Single(), "Inputs", "Base" ) ) );
	}

	[TestMethod]
	public void HumanGraphHasNoSharedPoseOrLayerOutputs()
	{
		var path = Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ),
			"addons", "citizen", "Assets", "models", "citizen_human", "animgraphs", "human.ag" );
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		Call( graph, "Deserialize", File.ReadAllText( path ) );
		var shared = Get<IEnumerable>( graph, "AllNodes" ).Cast<object>()
			.SelectMany( node => Get<IEnumerable>( node, "Inputs" ).Cast<object>() )
			.Select( ConnectedOutput )
			.Where( output => output is not null && Get<Type>( output, "Type" ).Name is "PoseLink" or "LayerLink" )
			.GroupBy( output => output )
			.Where( group => group.Count() > 1 )
			.Select( group => Get<string>( Get<object>( group.Key, "Node" ), "Identifier" ) )
			.ToArray();
		Assert.AreEqual( 0, shared.Length, $"Human graph shares pose outputs: {string.Join( ", ", shared )}" );
	}

	[TestMethod]
	public void AutomationConnectionValidatesBeforeReplacingPoseConsumer()
	{
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		var source = AddGraphNode( graph, "ReferencePoseNode" );
		var first = AddGraphNode( graph, "LocalLayerNode" );
		var second = AddGraphNode( graph, "LocalLayerNode" );
		var output = Plug( source, "Outputs", "Result" );
		var operation = Activator.CreateInstance( EditorType( "McpTools+EditOperation" ) );
		Set( operation, "FromNode", Get<string>( source, "Identifier" ) );
		Set( operation, "FromOutput", "Result" );
		var connect = EditorType( "McpTools" ).GetMethod( "Connect", BindingFlags.Static | BindingFlags.NonPublic );
		void Apply( object target, string input )
		{
			Set( operation, "ToNode", Get<string>( target, "Identifier" ) );
			Set( operation, "ToInput", input );
			connect.Invoke( null, [graph, operation, new Dictionary<string, string>()] );
		}

		Apply( first, "Input" );
		var exception = Assert.ThrowsException<TargetInvocationException>( () => Apply( second, "Weight" ) );
		StringAssert.Contains( exception.InnerException.Message, "Cannot connect" );
		Assert.AreEqual( output, ConnectedOutput( Plug( first, "Inputs", "Input" ) ) );
		Apply( second, "Input" );
		Assert.IsNull( ConnectedOutput( Plug( first, "Inputs", "Input" ) ) );
		Assert.AreEqual( output, ConnectedOutput( Plug( second, "Inputs", "Input" ) ) );
	}
}
