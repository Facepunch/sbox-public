using Sandbox.Engine;
using System;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Resource startup binds forward references and publishes once; simultaneous caller/member edits migrate once.
	/// </summary>
	[TestMethod]
	public void ResourceScanAndWatcherPublishCoherentBatches()
	{
		files.WriteAllText( "a-caller.csx", "[Global] int Started = 0; Started = ResourceArt.Old(); class ResourceCaller { public int Read() { return ResourceArt.Old(); } }" );
		files.WriteAllText( "z-art.csx", "class ResourceArt { public static int Old() { return 17; } }" );
		var replace = context.ReplaceScriptAssembly;
		int publications = 0;
		context.ReplaceScriptAssembly = ( previous, incoming, register ) =>
		{
			publications++;
			replace( previous, incoming, register );
		};
		try
		{
			ResourceLoader.LoadAllGameResource( files );
			Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
			Assert.AreEqual( 1, publications );
			Assert.AreEqual( 17, Game.Scripting.Globals.Get<int>( "Started" ) );
			Assert.AreEqual( 17, Game.Scripting.Run<int>( "return new ResourceCaller().Read();" ) );

			files.WriteAllText( "a-caller.csx", "[Global] int Started = 0; Started = ResourceArt.New(); class ResourceCaller { public int Read() { return ResourceArt.New(); } }" );
			files.WriteAllText( "z-art.csx", "class ResourceArt { public static int New() { return 42; } }" );
			DispatchResourceChanges( "a-caller.csx", "z-art.csx" );

			Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
			Assert.AreEqual( 2, publications );
			Assert.AreEqual( 42, Game.Scripting.Globals.Get<int>( "Started" ) );
			Assert.AreEqual( 42, Game.Scripting.Run<int>( "return new ResourceCaller().Read();" ) );
		}
		finally
		{
			context.ReplaceScriptAssembly = replace;
		}
	}

	/// <summary>
	/// Invalid source keeps its accepted definitions while unrelated valid edits publish and run.
	/// </summary>
	[TestMethod]
	public void ResourceBatchIsolatesRejectedReplacement()
	{
		files.WriteAllText( "a.csx", "class ResourceAccepted { public int Read() { return 3; } }" );
		files.WriteAllText( "b.csx", "[Global] int ResourceValue = 0; ResourceValue = 1;" );
		ResourceLoader.LoadAllGameResource( files );
		files.WriteAllText( "a.csx", "class Invalid { public int = ; }" );
		files.WriteAllText( "b.csx", "[Global] int ResourceValue = 0; ResourceValue = 9;" );
		DispatchResourceChanges( "a.csx", "b.csx" );
		Assert.IsFalse( Game.Scripting.RequiresReload );
		Assert.AreEqual( 3, Game.Scripting.Run<int>( "return new ResourceAccepted().Read();" ) );
		Assert.AreEqual( 9, Game.Scripting.Globals.Get<int>( "ResourceValue" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "Invalid" ) );
	}

	/// <summary>
	/// Removing a parent and its dependent type in the same watcher notification retires both declarations.
	/// </summary>
	[TestMethod]
	public void ResourceWatcherRemovesRelatedFilesTogether()
	{
		files.WriteAllText( "parent.csx", "class ResourceParent {}" );
		files.WriteAllText( "child.csx", "class ResourceChild : ResourceParent {}" );
		ResourceLoader.LoadAllGameResource( files );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "ResourceChild" ) );
		files.DeleteFile( "parent.csx" );
		files.DeleteFile( "child.csx" );
		DispatchResourceChanges( "parent.csx", "child.csx" );
		Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
		Assert.IsNull( Game.TypeLibrary.GetType( "ResourceParent" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "ResourceChild" ) );
	}

	/// <summary>
	/// Scanning an additional package preserves previously loaded files and their captured state.
	/// </summary>
	[TestMethod]
	public void ResourceScanKeepsOtherPackageFiles()
	{
		files.WriteAllText( "first.csx", "int count = 0; int NextResourceCount() { return ++count; } class FirstResource {}" );
		ResourceLoader.LoadAllGameResource( files );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return NextResourceCount();" ) );
		var package = new MemoryFileSystem();
		try
		{
			package.WriteAllText( "second.csx", "class SecondResource {}" );
			ResourceLoader.LoadAllGameResource( package );
			Assert.IsNotNull( Game.TypeLibrary.GetType( "FirstResource" ) );
			Assert.IsNotNull( Game.TypeLibrary.GetType( "SecondResource" ) );
			Assert.AreEqual( 2, Game.Scripting.Run<int>( "return NextResourceCount();" ) );
		}
		finally
		{
			package.Dispose();
		}
	}
}
