using Editor;
using System;
using System.IO;

namespace Tools;

[TestClass, DoNotParallelize]
public class EditorUtilityCopyTests
{
	string directory;
	float suppression;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine( Path.GetTempPath(), $"sbox-asset-copy-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		suppression = FileWatch.SuppressWatchers;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileWatch.SuppressWatchers = suppression;
		Directory.Delete( directory, true );
	}

	[TestMethod]
	[DataRow( "scene", false, false )]
	[DataRow( "scene", true, false )]
	[DataRow( "vmat", false, false )]
	[DataRow( "vmat", true, false )]
	[DataRow( "scene", false, true )]
	[DataRow( "scene", true, true )]
	[DataRow( "vmat", false, true )]
	[DataRow( "vmat", true, true )]
	public void SourceCopiesOnlyOmitCompiledOutputWhenRebuilding( string extension, bool overwrite, bool rebuildFromSource )
	{
		var source = Path.Combine( directory, $"original.{extension}" );
		var target = Path.Combine( directory, $"copy.{extension}" );
		File.WriteAllText( source, "source" );
		File.WriteAllBytes( source + "_d", [0, 127, 255] );
		File.WriteAllText( source + ".meta", "metadata" );
		File.WriteAllText( source + "_c", "original compiled output" );
		if ( overwrite )
		{
			File.WriteAllText( target, "previous source" );
			File.WriteAllText( target + "_d", "previous binary" );
			File.WriteAllText( target + ".meta", "previous metadata" );
			File.WriteAllText( target + "_c", "stale compiled output" );
		}

		var asset = new CopyAsset( source, true );
		Assert.IsTrue( rebuildFromSource
			? EditorUtility.CopyAsset( asset, target, overwrite, rebuildFromSource: true )
			: EditorUtility.CopyAsset( asset, target, overwrite ) );

		Assert.AreEqual( "source", File.ReadAllText( target ) );
		CollectionAssert.AreEqual( new byte[] { 0, 127, 255 }, File.ReadAllBytes( target + "_d" ) );
		Assert.AreEqual( "metadata", File.ReadAllText( target + ".meta" ) );
		if ( rebuildFromSource )
			Assert.IsFalse( File.Exists( target + "_c" ) );
		else
			Assert.AreEqual( "original compiled output", File.ReadAllText( target + "_c" ) );
		Assert.AreEqual( "original compiled output", File.ReadAllText( source + "_c" ) );
	}

	[TestMethod]
	[DataRow( "scene", false, false )]
	[DataRow( "scene", true, false )]
	[DataRow( "vmat", false, false )]
	[DataRow( "vmat", true, false )]
	[DataRow( "scene", false, true )]
	[DataRow( "scene", true, true )]
	[DataRow( "vmat", false, true )]
	[DataRow( "vmat", true, true )]
	public void CompiledOnlyCopiesRetainCompiledOutput( string extension, bool overwrite, bool rebuildFromSource )
	{
		var source = Path.Combine( directory, $"original.{extension}_c" );
		var target = Path.Combine( directory, $"copy.{extension}_c" );
		File.WriteAllBytes( source, [0, 127, 255] );
		if ( overwrite ) File.WriteAllText( target, "previous compiled output" );

		Assert.IsTrue( EditorUtility.CopyAsset( new CopyAsset( source, false ), target, overwrite, rebuildFromSource ) );

		CollectionAssert.AreEqual( File.ReadAllBytes( source ), File.ReadAllBytes( target ) );
		Assert.IsFalse( File.Exists( target[..^2] ) );
	}

	[TestMethod]
	[DataRow( "", false )]
	[DataRow( "_c", false )]
	[DataRow( "_d", false )]
	[DataRow( ".meta", false )]
	[DataRow( "", true )]
	[DataRow( "_c", true )]
	[DataRow( "_d", true )]
	[DataRow( ".meta", true )]
	public void RelatedFileCollisionDoesNotCreatePartialCopy( string suffix, bool rebuildFromSource )
	{
		var source = Path.Combine( directory, "original.scene" );
		var target = Path.Combine( directory, "copy.scene" );
		File.WriteAllText( source, "source" );
		File.WriteAllText( source + "_d", "binary" );
		File.WriteAllText( source + ".meta", "metadata" );
		File.WriteAllText( target + suffix, "existing data" );

		Assert.IsFalse( EditorUtility.CopyAsset( new CopyAsset( source, true ), target, overwrite: false, rebuildFromSource ) );

		Assert.AreEqual( "existing data", File.ReadAllText( target + suffix ) );
		CollectionAssert.AreEquivalent( new[] { target + suffix }, Directory.GetFiles( directory, "copy.*" ) );
	}

	[TestMethod]
	public void CopyFailureDoesNotLeaveFileWatchersSuppressed()
	{
		var source = Path.Combine( directory, "original.scene" );
		var target = Path.Combine( directory, "missing", "copy.scene" );
		File.WriteAllText( source, "source" );

		Assert.ThrowsException<DirectoryNotFoundException>( () => EditorUtility.CopyAsset( new CopyAsset( source, true ), target ) );

		Assert.IsTrue( FileWatch.SuppressWatchers <= RealTime.Now );
	}

	sealed class CopyAsset : MountAsset
	{
		public CopyAsset( string path, bool hasSource ) : base( 0, null, null )
		{
			AbsolutePath = path;
			HasSourceFile = hasSource;
		}

		public override bool HasSourceFile { get; }
	}
}
