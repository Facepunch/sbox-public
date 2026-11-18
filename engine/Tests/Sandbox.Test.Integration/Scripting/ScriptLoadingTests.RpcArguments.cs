using System;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Script RPCs forward engine objects, whether or not the parameter's type is sealed.
	/// </summary>
	[TestMethod]
	[DataRow( "GameObject", "target.Name" )]
	[DataRow( "Component", "target.GameObject.Name" )]
	[DataRow( "ModelRenderer", "target.GameObject.Name" )]
	[DataRow( "Rigidbody", "target.GameObject.Name" )]
	public void ScriptRpcsForwardEngineObjects( string parameterType, string readName )
	{
		var source = $$"""
			public class ObjectRpc : Component
			{
				public int Calls;
				public string Last;
				[Rpc.Host] public void Send( {{parameterType}} target ) { Calls++; Last = {{readName}}; }
			}
			""";
		var loaded = Game.Scripting.LoadFile( "object-rpc.csx", source );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene ) { Name = "rpc-target" };
		var component = gameObject.Components.Create( Game.TypeLibrary.GetType( "ObjectRpc" ) );
		object argument = parameterType switch
		{
			"GameObject" => gameObject,
			"Component" => component,
			"ModelRenderer" => gameObject.Components.Create<ModelRenderer>(),
			_ => gameObject.Components.Create<Rigidbody>()
		};

		var type = component.GetType();
		type.GetMethod( "Send" ).Invoke( component, [argument] );
		type.GetMethod( "Send" ).Invoke( component, [argument] );

		Assert.AreEqual( 2, type.GetField( "Calls" ).GetValue( component ) );
		Assert.AreEqual( "rpc-target", type.GetField( "Last" ).GetValue( component ) );
	}

	/// <summary>
	/// A parameter declared with a base type still receives a derived component.
	/// </summary>
	[TestMethod]
	public void ScriptRpcsForwardDerivedComponents()
	{
		var loaded = Game.Scripting.LoadFile( "derived-rpc.csx", """
			public class DerivedRpc : Component
			{
				public string Last;
				[Rpc.Broadcast] public void Send( Component target ) { Last = target.GameObject.Name; }
			}
			""" );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene ) { Name = "derived-target" };
		var component = gameObject.Components.Create( Game.TypeLibrary.GetType( "DerivedRpc" ) );
		var type = component.GetType();

		type.GetMethod( "Send" ).Invoke( component, [gameObject.Components.Create<ModelRenderer>()] );
		Assert.AreEqual( "derived-target", type.GetField( "Last" ).GetValue( component ) );
	}

	/// <summary>
	/// Script RPCs forward components declared by the same script, sealed or not.
	/// </summary>
	[TestMethod]
	[DataRow( "" )]
	[DataRow( "sealed " )]
	public void ScriptRpcsForwardScriptComponents( string modifiers )
	{
		var source = $$"""
			public {{modifiers}}class RpcPayload : Component
			{
				public int Value;
			}

			public class PayloadRpc : Component
			{
				public int Total;
				[Rpc.Host] public void Send( RpcPayload payload ) { Total += payload.Value; }
			}
			""";
		var loaded = Game.Scripting.LoadFile( "payload-rpc.csx", source );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene );
		var component = gameObject.Components.Create( Game.TypeLibrary.GetType( "PayloadRpc" ) );
		var payload = gameObject.Components.Create( Game.TypeLibrary.GetType( "RpcPayload" ) );
		payload.GetType().GetField( "Value" ).SetValue( payload, 21 );

		var type = component.GetType();
		type.GetMethod( "Send" ).Invoke( component, [payload] );
		type.GetMethod( "Send" ).Invoke( component, [payload] );

		Assert.AreEqual( 42, type.GetField( "Total" ).GetValue( component ) );
	}

	/// <summary>
	/// An object argument can be combined with ordinary values, and a filtered RPC still does not run.
	/// </summary>
	[TestMethod]
	public void ScriptRpcsForwardObjectsWithOtherArguments()
	{
		var loaded = Game.Scripting.LoadFile( "mixed-rpc.csx", """
			public class MixedRpc : Component
			{
				public int Total;
				public string Last;
				[Rpc.Broadcast] public void Send( GameObject target, int amount, string label ) { Total += amount; Last = target.Name + label; }
			}
			""" );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene ) { Name = "mixed" };
		var component = gameObject.Components.Create( Game.TypeLibrary.GetType( "MixedRpc" ) );
		var type = component.GetType();
		var send = type.GetMethod( "Send" ).CreateDelegate<Action<GameObject, int, string>>( component );

		using ( Rpc.FilterExclude( _ => true ) )
		{
			send( gameObject, 100, "-filtered" );
		}

		Assert.AreEqual( 0, type.GetField( "Total" ).GetValue( component ) );

		send( gameObject, 5, "-sent" );
		Assert.AreEqual( 5, type.GetField( "Total" ).GetValue( component ) );
		Assert.AreEqual( "mixed-sent", type.GetField( "Last" ).GetValue( component ) );
	}
}
