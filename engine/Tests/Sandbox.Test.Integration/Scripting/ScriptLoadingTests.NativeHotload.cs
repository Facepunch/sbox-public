using Sandbox.Internal;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Replacing a native base assembly must rebuild dependent script types before migrating
	/// their native and script storage, scene membership and references.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NativeBaseHotloadRebuildsScriptTypesAndPreservesState( bool namedFiles )
	{
		var originalBase = EmitNativeScriptBase( 1 );
		using var enroller = EnrollNativeScriptBase( originalBase );

		const string source = """
			int captured = 100;
			int NextCaptured() { return ++captured; }
			class NativeDerivedScript : NativeScriptBase
			{
				[Property] public int ScriptValue { get; set; } = 7;
				public NativeScriptBase NativePeer { get; set; }
				public int ReadGeneration() { return NativeGeneration(); }
			}
			""";
		if ( namedFiles )
		{
			var result = Game.Scripting.LoadFiles( new System.Collections.Generic.Dictionary<string, string> { ["native-base.csx"] = source } );
			Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
		}
		else
		{
			LoadSource( "native-base.csx", source );
		}
		Assert.AreEqual( 101, Game.Scripting.Run<int>( "return NextCaptured();" ) );
		Assert.AreEqual( 102, Game.Scripting.Run<int>( "return NextCaptured();" ) );
		scene = new Scene();
		using var scope = scene.Push();
		var description = Game.TypeLibrary.GetType( "NativeDerivedScript" );
		var first = scene.CreateObject().Components.Create( description );
		var second = scene.CreateObject().Components.Create( description );
		var firstId = first.Id;
		var secondId = second.Id;
		originalBase.GetField( "NativeValue" ).SetValue( first, 41 );
		originalBase.GetField( "NativeValue" ).SetValue( second, 42 );
		originalBase.GetField( "Other" ).SetValue( first, second );
		originalBase.GetField( "Other" ).SetValue( second, first );
		description.GetProperty( "ScriptValue" ).SetValue( first, 81 );
		description.GetProperty( "ScriptValue" ).SetValue( second, 82 );
		description.TargetType.GetProperty( "NativePeer" ).SetValue( first, second );
		description.TargetType.GetProperty( "NativePeer" ).SetValue( second, first );
		Assert.AreEqual( 1, description.GetMethod( "ReadGeneration" ).InvokeWithReturn<int>( first ) );
		reloadRoots = [first, second];

		var replacementBase = EmitNativeScriptBase( 2 );
		var previousFastHotload = HotloadManager.hotload_fast;
		try
		{
			// Emitted fixtures have no assembly bytes for the IL fast-hotload path.
			HotloadManager.hotload_fast = false;
			scriptPackageLoader.AddAssembly( new LoadedAssembly
			{
				Name = replacementBase.Assembly.GetName().Name,
				Version = replacementBase.Assembly.GetName().Version,
				Assembly = replacementBase.Assembly
			} );
			scriptPackageLoader.Tick();
		}
		finally
		{
			HotloadManager.hotload_fast = previousFastHotload;
		}

		first = (Component)reloadRoots[0];
		second = (Component)reloadRoots[1];
		Assert.IsNotNull( first, "Hotloading the native base must preserve the first derived script component." );
		Assert.IsNotNull( second, "Hotloading the native base must preserve the second derived script component." );
		Assert.AreSame( replacementBase, first.GetType().BaseType,
			"The script CLR type must stop deriving from the outgoing native assembly." );
		Assert.AreSame( replacementBase, second.GetType().BaseType );
		Assert.AreEqual( firstId, first.Id );
		Assert.AreEqual( secondId, second.Id );
		Assert.AreSame( first, first.GameObject.Components.Get( first.GetType() ) );
		Assert.AreSame( second, second.GameObject.Components.Get( second.GetType() ) );
		Assert.AreEqual( 41, replacementBase.GetField( "NativeValue" ).GetValue( first ) );
		Assert.AreEqual( 42, replacementBase.GetField( "NativeValue" ).GetValue( second ) );
		Assert.AreSame( second, replacementBase.GetField( "Other" ).GetValue( first ) );
		Assert.AreSame( first, replacementBase.GetField( "Other" ).GetValue( second ) );
		description = Game.TypeLibrary.GetType( "NativeDerivedScript" );
		Assert.AreSame( first.GetType(), description.TargetType );
		var nativePeer = description.TargetType.GetProperty( "NativePeer" );
		Assert.AreSame( replacementBase, nativePeer.PropertyType );
		Assert.AreSame( second, nativePeer.GetValue( first ) );
		Assert.AreSame( first, nativePeer.GetValue( second ) );
		Assert.AreEqual( 81, description.GetProperty( "ScriptValue" ).GetValue( first ) );
		Assert.AreEqual( 82, description.GetProperty( "ScriptValue" ).GetValue( second ) );
		Assert.AreEqual( 2, description.GetMethod( "ReadGeneration" ).InvokeWithReturn<int>( first ) );
		Assert.AreEqual( 103, Game.Scripting.Run<int>( "return NextCaptured();" ),
			"Native hotload must preserve captured file state without replaying startup." );
		if ( namedFiles )
		{
			// A subsequent named update must still find the file registry after native hotload.
			var result = Game.Scripting.LoadFiles( new System.Collections.Generic.Dictionary<string, string> { ["native-base.csx"] = source + "\n// updated" } );
			Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
		}
	}

	/// <summary>
	/// Incoming native names must be validated before migrating live script references.
	/// A failed native replacement prevents further script execution until the context is reset.
	/// </summary>
	[TestMethod]
	public void NativeNameCollisionStopsMigrationAndRequiresContextReset()
	{
		var originalBase = EmitNativeScriptBase( 1 );
		using var enroller = EnrollNativeScriptBase( originalBase );
		LoadSource( "native-base.csx", "class NativeDerivedScript : NativeScriptBase {}" );
		scene = new Scene();
		using var scope = scene.Push();
		var description = Game.TypeLibrary.GetType( "NativeDerivedScript" );
		var component = scene.CreateObject().Components.Create( description );
		originalBase.GetField( "NativeValue" ).SetValue( component, 41 );
		reloadRoots = [component];

		var replacementBase = EmitNativeScriptBase( 2, conflicting: true );
		var previousFastHotload = HotloadManager.hotload_fast;
		try
		{
			HotloadManager.hotload_fast = false;
			scriptPackageLoader.AddAssembly( new LoadedAssembly
			{
				Name = replacementBase.Assembly.GetName().Name,
				Version = replacementBase.Assembly.GetName().Version,
				Assembly = replacementBase.Assembly
			} );
			var error = Assert.ThrowsException<InvalidOperationException>( () => scriptPackageLoader.Tick() );
			StringAssert.Contains( error.Message, "NativeDerivedScript" );
		}
		finally
		{
			HotloadManager.hotload_fast = previousFastHotload;
		}

		Assert.AreSame( component, reloadRoots[0] );
		Assert.AreSame( originalBase, component.GetType().BaseType );
		Assert.AreSame( component, component.GameObject.Components.Get( component.GetType() ) );
		Assert.AreEqual( 41, originalBase.GetField( "NativeValue" ).GetValue( component ) );
		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.Run<int>( "return 42;" ) );
		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.Load( "" ) );
	}

	/// <summary>
	/// Enrolls fixture assemblies through the same registration callbacks as native packages.
	/// </summary>
	private PackageLoader.Enroller EnrollNativeScriptBase( Type type )
	{
		var enroller = scriptPackageLoader.CreateEnroller( "native-script-base" );
		enroller.OnAssemblyAdded = loaded =>
		{
			Game.TypeLibrary.AddAssembly( loaded.Assembly, true );
			TypeLibrary.Editor.AddAssembly( loaded.Assembly, true );
		};
		enroller.OnAssemblyRemoved = loaded =>
		{
			Game.TypeLibrary.RemoveAssembly( loaded.Assembly );
			TypeLibrary.Editor.RemoveAssembly( loaded.Assembly );
		};
		enroller.Add( scriptPackageLoader.AddAssembly( new LoadedAssembly
		{
			Name = type.Assembly.GetName().Name,
			Version = type.Assembly.GetName().Version,
			Assembly = type.Assembly
		} ) );
		return enroller;
	}

	/// <summary>
	/// Two ordinary CLR assembly generations model a compiled user component base without
	/// involving script metadata or changing the integration test's own assembly.
	/// </summary>
	private static Type EmitNativeScriptBase( int version, bool conflicting = false )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "ScriptNativeBaseFixture" )
		{
			Version = new Version( version, 0 )
		}, AssemblyBuilderAccess.RunAndCollect );
		var module = assembly.DefineDynamicModule( "Main" );
		if ( conflicting )
		{
			module.DefineType( "NativeDerivedScript", TypeAttributes.Public ).CreateType();
		}
		var type = module.DefineType( "NativeScriptBase", TypeAttributes.Public, typeof( Component ) );
		type.DefineDefaultConstructor( MethodAttributes.Public );
		// No native initializers: avoid asking the Cecil fallback to read an emitted assembly.
		var uninitialized = new CustomAttributeBuilder( typeof( Sandbox.Upgraders.InitializedByAttribute ).GetConstructor( [typeof( string )] ), [null] );
		type.DefineField( "NativeValue", typeof( int ), FieldAttributes.Public ).SetCustomAttribute( uninitialized );
		type.DefineField( "Other", typeof( Component ), FieldAttributes.Public ).SetCustomAttribute( uninitialized );
		var method = type.DefineMethod( "NativeGeneration", MethodAttributes.Public, typeof( int ), Type.EmptyTypes );
		var il = method.GetILGenerator();
		il.Emit( OpCodes.Ldc_I4, version );
		il.Emit( OpCodes.Ret );
		return type.CreateType();
	}
}
