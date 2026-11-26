using Sandbox.Internal;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SceneTests.Trace;

[TestClass]
public class TraceProviderHotloadTests
{
	[TestMethod]
	public void InterfaceChangesReachListedAndUnlistedScenesBeforeHotloadCallbacks()
	{
		var original = EmitSystem( 1, false );
		var addedProvider = EmitSystem( 2, true );
		var removedProvider = EmitSystem( 3, false );
		var oldTypeLibrary = Game.TypeLibrary;
		var oldFastHotload = HotloadManager.hotload_fast;
		using var restore = new DisposeAction( () =>
		{
			Game.TypeLibrary = oldTypeLibrary;
			HotloadManager.hotload_fast = oldFastHotload;
		} );
		HotloadManager.hotload_fast = false;
		var library = new TypeLibrary();
		library.AddAssembly( typeof( Scene ).Assembly, false );
		library.AddAssembly( original.Assembly, true );
		Game.TypeLibrary = library;

		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		var unlisted = new Scene { IsUnlisted = true };
		using var destroyUnlisted = new DisposeAction( unlisted.Destroy );
		using var scope = scene.Push();
		Assert.IsTrue( Scene.All.Contains( scene ) );
		Assert.IsFalse( Scene.All.Contains( unlisted ) );
		var scenes = new[] { scene, unlisted };

		using var hotload = new HotloadManager( "TraceProviderTest" );
		hotload.Hotload.WatchInstance( scenes );
		CheckScenes( original, false );
		SwapAndCheck( original, addedProvider, true );
		SwapAndCheck( addedProvider, removedProvider, false );

		void SwapAndCheck( Type before, Type after, bool expectHit )
		{
			library.RemoveAssembly( before.Assembly );
			library.AddAssembly( after.Assembly, true );
			hotload.Replace( before.Assembly, after.Assembly );
			var checkedScenes = false;
			Assert.IsTrue( hotload.DoSwap( () =>
			{
				CheckScenes( after, expectHit );
				checkedScenes = true;
			} ) );
			Assert.IsTrue( checkedScenes );
		}

		void CheckScenes( Type systemType, bool expectHit )
		{
			foreach ( var current in scenes )
			{
				Assert.IsTrue( current.GetSystems().Any( system => system.GetType() == systemType ) );
				var ray = current.Trace.Ray( Vector3.Zero, Vector3.Forward * 300 ).UsePhysicsWorld( false );
				Assert.AreEqual( expectHit, ray.Run().Hit );
				Assert.AreEqual( expectHit ? 1 : 0, ray.RunAll().Count() );
			}
		}
	}

	static Type EmitSystem( int version, bool provider )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "TraceProviderMigrationFixture" )
		{
			Version = new Version( version, 0 )
		}, AssemblyBuilderAccess.RunAndCollect );
		var baseType = typeof( MigratingTraceSystem );
		var type = assembly.DefineDynamicModule( "Main" ).DefineType( "FixtureTraceSystem", TypeAttributes.Public, baseType );
		if ( provider ) type.AddInterfaceImplementation( typeof( GameObjectSystem.ITraceProvider ) );
		var ctor = type.DefineConstructor( MethodAttributes.Public, CallingConventions.Standard, [typeof( Scene )] );
		var il = ctor.GetILGenerator();
		il.Emit( OpCodes.Ldarg_0 );
		il.Emit( OpCodes.Ldarg_1 );
		il.Emit( OpCodes.Call, baseType.GetConstructor( [typeof( Scene )] ) );
		il.Emit( OpCodes.Ret );
		return type.CreateType();
	}

	public abstract class MigratingTraceSystem : GameObjectSystem
	{
		public MigratingTraceSystem( Scene scene ) : base( scene ) { }

		public virtual SceneTraceResult? DoTrace( in SceneTrace trace ) => new SceneTraceResult { Scene = Scene, Hit = true, Fraction = 0.5f };

		public virtual void DoTrace( in SceneTrace trace, List<SceneTraceResult> results ) => results.Add( DoTrace( trace ).Value );
	}
}
