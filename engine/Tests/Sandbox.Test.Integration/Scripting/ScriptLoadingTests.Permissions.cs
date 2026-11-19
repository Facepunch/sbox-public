using Sandbox.Engine;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// A visible fixture annotation proves generator-dependent contracts are rejected even
	/// when the attribute itself passes the ordinary host exposure rules.
	/// </summary>
	[Expose, AttributeUsage( AttributeTargets.Property )]
	[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapPropertySet, "FixtureGeneratedSetter" )]
	public sealed class GeneratedContractAttribute : Attribute
	{
	}

	/// <summary>
	/// A compiled callback loses native permissions after hotload notification, and temporary
	/// disabled TypeLibrary scopes reject entry before the native API has side effects.
	/// </summary>
	[TestMethod]
	public void NativeCallbacksRespectRevokedAndTemporarilyDisabledPermissions()
	{
		var api = CreatePermissionApi();
		context.TypeLibrary.AddAssembly( api.Assembly, true );
		try
		{
			var component = CreateCallbackComponent( "Value = PermissionApi.Read();" );
			var callback = NativeCallback( component );
			var calls = api.GetField( "Calls" );
			callback();
			Assert.AreEqual( 1, calls.GetValue( null ) );

			context.TypeLibrary.RemoveAssembly( api.Assembly );
			context.OnHotload();
			Assert.ThrowsException<Breen.ScriptException>( callback );
			Assert.AreEqual( 1, calls.GetValue( null ) );

			context.TypeLibrary.AddAssembly( api.Assembly, true );
			context.OnHotload();
			using ( context.DisableTypelibraryScope( "Callback permission regression" ) )
			{
				Assert.ThrowsException<InvalidOperationException>( callback );
			}
			Assert.AreEqual( 1, calls.GetValue( null ) );

			callback();
			Assert.AreEqual( 2, calls.GetValue( null ), "Restored permissions must allow the same retained callback again." );
		}
		finally
		{
			context.TypeLibrary.RemoveAssembly( api.Assembly );
			context.OnHotload();
		}
	}

	/// <summary>
	/// Real delegates cannot bypass ownership checks by entering outside the context or thread.
	/// </summary>
	[TestMethod]
	public void NativeCallbacksRequireOwningThreadAndContext()
	{
		var component = CreateCallbackComponent( "Value++;" );
		var callback = NativeCallback( component );
		var type = Game.TypeLibrary.GetType( component.GetType() );
		callback();

		Task.Run( () => Assert.ThrowsException<InvalidOperationException>( callback ) ).GetAwaiter().GetResult();
		GlobalContext.Current = new GlobalContext { TypeLibrary = context.TypeLibrary };
		try
		{
			Assert.ThrowsException<InvalidOperationException>( callback );
		}
		finally
		{
			GlobalContext.Current = context;
		}

		Assert.AreEqual( 1, type.GetValue( component, "Value" ) );
		callback();
		Assert.AreEqual( 2, type.GetValue( component, "Value" ) );
	}

	/// <summary>
	/// Contracts implemented by the C# generator are explicitly rejected before replacing live code.
	/// </summary>
	[TestMethod]
	public void CodeGeneratorContractsRejectEditAndPreserveLiveState()
	{
		context.TypeLibrary.AddAssembly( typeof( GeneratedContractAttribute ).Assembly, false );

		var component = CreateCallbackComponent( "Value++;" );
		var type = Game.TypeLibrary.GetType( component.GetType() );
		var callback = NativeCallback( component );
		callback();

		Assert.IsFalse( Game.Scripting.Load( """
			class CallbackComponent : Component
			{
				public int Value { get; set; }
				[GeneratedContract] public int GeneratedValue { get; set; }
				protected override void OnUpdate() { Value += 100; }
			}
			""", "safety.csx" ) );
		Assert.IsNotNull( Game.Scripting.LastError );
		StringAssert.Contains( Game.Scripting.LastError.Message, "requires C# code generation" );
		Assert.AreSame( type, Game.TypeLibrary.GetType( component.GetType() ) );
		callback();
		Assert.AreEqual( 2, type.GetValue( component, "Value" ) );
	}

	private Component CreateCallbackComponent( string callback )
	{
		LoadSource( "safety.csx", $$"""
			class CallbackComponent : Component
			{
				[Property] public int Value { get; set; }
				protected override void OnUpdate() { {{callback}} }
			}
			""" );
		scene = new Scene();
		using var scope = scene.Push();
		return scene.CreateObject().Components.Create( Game.TypeLibrary.GetType( "CallbackComponent" ) );
	}

	private static Action NativeCallback( Component component )
	{
		return component.GetType().GetMethod( "OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic ).CreateDelegate<Action>( component );
	}

	/// <summary>
	/// Provides one removable native permission without revoking the component base or engine catalog.
	/// </summary>
	private static Type CreatePermissionApi()
	{
		var name = new AssemblyName( "Scripting.PermissionTest." + Guid.NewGuid().ToString( "N" ) );
		var assembly = AssemblyBuilder.DefineDynamicAssembly( name, AssemblyBuilderAccess.RunAndCollect );
		var type = assembly.DefineDynamicModule( name.Name ).DefineType( "PermissionApi",
			TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed );
		var calls = type.DefineField( "Calls", typeof( int ), FieldAttributes.Public | FieldAttributes.Static );
		var read = type.DefineMethod( "Read", MethodAttributes.Public | MethodAttributes.Static, typeof( int ), Type.EmptyTypes );
		var il = read.GetILGenerator();
		il.Emit( OpCodes.Ldsfld, calls );
		il.Emit( OpCodes.Ldc_I4_1 );
		il.Emit( OpCodes.Add );
		il.Emit( OpCodes.Dup );
		il.Emit( OpCodes.Stsfld, calls );
		il.Emit( OpCodes.Ret );
		return type.CreateType();
	}
}
