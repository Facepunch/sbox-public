using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.IO;

namespace ResourceTests;

/// <summary>
/// Linking files over a resident resource reloads it from the link. Models with a different number of
/// draw calls stand in for different file contents.
/// </summary>
[TestClass]
public class SymlinkReloadTests
{
	string _assetsPath;
	string _folder;
	string _targets;
	readonly List<string> _links = new();

	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();

		var project = Project.AddFromFile( "unittest/addons/resources/.sbproj" );
		_assetsPath = project.GetAssetsPath();

		_folder = $"rc_symlink_{Guid.NewGuid():N}";
		_targets = Path.Combine( Path.GetTempPath(), _folder );
		Directory.CreateDirectory( _targets );

		NativeEngine.FullFileSystem.AddProjectPath( "unittest", _assetsPath.ToLowerInvariant() );

		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( _assetsPath ) );

		// Anything a previous test linked and nobody reloaded yet
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		foreach ( var link in _links )
			NativeEngine.FullFileSystem.RemoveSymLink( link, "GAME" );

		Project.Clear();

		Directory.Delete( _targets, recursive: true );
	}

	[TestMethod]
	public void NewLinkReloadsResidentResource()
	{
		var path = ResidentModel( "a", 3 );

		Link( path + "_c", WriteTarget( "b.vmdl_c", 5 ) );
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		Assert.AreEqual( 5, DrawCalls( path ) );
	}

	/// <summary>
	/// The same resource queued several times in one drain (its source file, its _c, and a second
	/// package linking the same _c) still ends up reloaded from the link.
	/// </summary>
	[TestMethod]
	public void NameQueuedTwiceInOneDrainReloads()
	{
		var path = ResidentModel( "a", 3 );
		var target = WriteTarget( "b.vmdl_c", 5 );

		Link( path, WriteTarget( "b.vmdl", 0 ) );
		Link( path + "_c", target );
		Link( path + "_c", target );
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		Assert.AreEqual( 5, DrawCalls( path ) );
	}

	/// <summary>
	/// Two resources in one drain are both reloaded.
	/// </summary>
	[TestMethod]
	public void EveryQueuedNameReloads()
	{
		var first = ResidentModel( "a", 3 );
		var second = ResidentModel( "c", 4 );

		Link( first + "_c", WriteTarget( "b.vmdl_c", 5 ) );
		Link( second + "_c", WriteTarget( "d.vmdl_c", 6 ) );
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		Assert.AreEqual( 5, DrawCalls( first ) );
		Assert.AreEqual( 6, DrawCalls( second ) );
	}

	/// <summary>
	/// A model loaded through a link that is then dropped, so it stays resident and the next link to it is new.
	/// Search paths win over links, so it can't come from the project's assets.
	/// </summary>
	string ResidentModel( string name, int drawCalls )
	{
		var path = $"{_folder}/{name}.vmdl";

		Link( path + "_c", WriteTarget( $"{name}_resident.vmdl_c", drawCalls ) );
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
		Assert.AreEqual( drawCalls, Model.Load( path ).MeshInfo.TotalDrawCalls );

		Unlink( path + "_c" );
		return path;
	}

	string WriteTarget( string name, int drawCalls )
	{
		var target = Path.Combine( _targets, name );
		File.WriteAllBytes( target, drawCalls > 0 ? CreateModel( drawCalls ).SaveToVmdl() : Array.Empty<byte>() );
		return target;
	}

	void Link( string path, string target )
	{
		NativeEngine.FullFileSystem.AddSymLink( path, "GAME", target );
		_links.Add( path );
	}

	void Unlink( string path )
	{
		NativeEngine.FullFileSystem.RemoveSymLink( path, "GAME" );
		_links.Remove( path );
	}

	static int DrawCalls( string path )
	{
		return Model.FromNative( NativeGlue.Resources.GetModel( path, Guid.Empty ) ).MeshInfo.TotalDrawCalls;
	}

	static Model CreateModel( int drawCalls )
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 3, new Vertex[]
		{
			new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 32, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 0, 32, 0 ), Normal = Vector3.Up }
		} );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 32, 32, 0 ) );
		for ( int i = 1; i < drawCalls; i++ )
			mesh.AddSubMesh( material, 0, 3, 0, 3 );

		return Model.Builder.AddMesh( mesh ).Create();
	}
}
