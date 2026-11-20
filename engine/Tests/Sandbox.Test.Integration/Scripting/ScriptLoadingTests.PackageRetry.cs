using Sandbox.Internal;
using System;
using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// An interrupted package walk retries once, includes edits queued before that retry, and enrolls each package once.
	/// </summary>
	[TestMethod]
	[DataRow( false, false, null )]
	[DataRow( true, false, null )]
	[DataRow( false, true, null )]
	[DataRow( false, false, "source" )]
	[DataRow( false, false, "package" )]
	public void InterruptedPackageWalkRetriesOnce( bool conflicting, bool keepInterrupting, string queuedEdit )
	{
		var original = EmitNativeScriptBase( 1 );
		using var enroller = EnrollNativeScriptBase( original );
		LoadSource( "retry.csx", "class NativeDerivedScript : NativeScriptBase { public int Saved; }" );
		scene = new Scene();
		using var scope = scene.Push();
		var component = scene.CreateObject().Components.Create( Game.TypeLibrary.GetType( "NativeDerivedScript" ) );
		component.GetType().GetField( "Saved" ).SetValue( component, 42 );
		var id = component.Id;
		reloadRoots = [component];

		var registrations = new List<int>();
		enroller.OnAssemblyAdded += loaded => registrations.Add( loaded.Version.Major );

		void QueuePackage( Type type )
		{
			var name = type.Assembly.GetName();
			scriptPackageLoader.AddAssembly( new LoadedAssembly { Name = name.Name, Version = name.Version, Assembly = type.Assembly } );
		}

		var attempts = 0;
		void InterruptWalk()
		{
			attempts++;
			if ( attempts == 1 || keepInterrupting ) throw new InvalidOperationException( "Interrupted package walk" );
		}

		var replacement = EmitNativeScriptBase( 2, conflicting );
		var previousFastHotload = HotloadManager.hotload_fast;
		scriptPackageLoader.HotloadManager.PreSwap += InterruptWalk;
		try
		{
			HotloadManager.hotload_fast = false;
			QueuePackage( replacement );
			scriptPackageLoader.Tick();
			Assert.AreSame( component, reloadRoots[0] );
			CollectionAssert.AreEqual( new[] { 2 }, registrations );
			Assert.AreSame( replacement, Game.TypeLibrary.GetType( "NativeScriptBase" ).TargetType );
			Assert.AreSame( replacement, TypeLibrary.Editor.GetType( "NativeScriptBase" ).TargetType );

			if ( queuedEdit == "source" )
			{
				LoadSource( "retry.csx", "class NativeDerivedScript : NativeScriptBase { public int Saved; public int Added = 7; }" );
			}
			else if ( queuedEdit == "package" )
			{
				replacement = EmitNativeScriptBase( 3 );
				QueuePackage( replacement );
			}

			Assert.AreEqual( 1, attempts );
			scriptPackageLoader.Tick();
			scriptPackageLoader.Tick();
			Assert.AreEqual( 2, attempts );
			CollectionAssert.AreEqual( queuedEdit == "package" ? new[] { 2, 3 } : new[] { 2 }, registrations );

			var live = (Component)reloadRoots[0];
			Assert.AreSame( conflicting || keepInterrupting ? original : replacement, live.GetType().BaseType );
			Assert.AreEqual( id, live.Id );
			Assert.AreEqual( 42, live.GetType().GetField( "Saved" ).GetValue( live ) );
			if ( queuedEdit == "source" ) Assert.AreEqual( 7, live.GetType().GetField( "Added" ).GetValue( live ) );
			if ( !conflicting && !keepInterrupting )
			{
				Assert.AreSame( live.GetType(), Game.TypeLibrary.GetType( "NativeDerivedScript" ).TargetType );
				Assert.AreSame( live.GetType(), TypeLibrary.Editor.GetType( "NativeDerivedScript" ).TargetType );
			}
		}
		finally
		{
			scriptPackageLoader.HotloadManager.PreSwap -= InterruptWalk;
			HotloadManager.hotload_fast = previousFastHotload;
		}
	}
}
