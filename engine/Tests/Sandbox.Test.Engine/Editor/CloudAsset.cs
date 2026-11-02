using Editor;

namespace EditorTests;

[TestClass]
public class CloudAssetTest
{
	/// <summary>
	/// Multiple references to the same package should collapse to one.
	/// </summary>
	[TestMethod]
	public void ExactDuplicatesCollapse()
	{
		var result = CloudAsset.DeduplicateReferences( new[] { "facepunch.props", "facepunch.props" } );

		CollectionAssert.AreEqual( new[] { "facepunch.props" }, result );
	}

	/// <summary>
	/// A version-pinned reference should win over an unpinned one to the same package,
	/// whichever order they appear in.
	/// </summary>
	[TestMethod]
	public void PinnedReferenceUpgradesUnpinned()
	{
		var pinnedLast = CloudAsset.DeduplicateReferences( new[] { "facepunch.props", "facepunch.props#5" } );
		var pinnedFirst = CloudAsset.DeduplicateReferences( new[] { "facepunch.props#5", "facepunch.props" } );

		CollectionAssert.AreEqual( new[] { "facepunch.props#5" }, pinnedLast );
		CollectionAssert.AreEqual( new[] { "facepunch.props#5" }, pinnedFirst );
	}

	/// <summary>
	/// Conflicting version pins should resolve to the newest version, whichever order
	/// they appear in.
	/// </summary>
	[TestMethod]
	public void ConflictingPinsResolveToNewest()
	{
		var ascending = CloudAsset.DeduplicateReferences( new[] { "facepunch.props#3", "facepunch.props#7" } );
		var descending = CloudAsset.DeduplicateReferences( new[] { "facepunch.props#7", "facepunch.props#3" } );

		CollectionAssert.AreEqual( new[] { "facepunch.props#7" }, ascending );
		CollectionAssert.AreEqual( new[] { "facepunch.props#7" }, descending );
	}

	/// <summary>
	/// The org/package and store-url ident forms refer to the same package as the
	/// dotted form, so they should merge and come out normalized to org.package.
	/// </summary>
	[TestMethod]
	public void IdentFormsNormalizeAndMerge()
	{
		var result = CloudAsset.DeduplicateReferences( new[]
		{
			"facepunch/props#3",
			"https://sbox.game/facepunch/props",
			"facepunch.props"
		} );

		CollectionAssert.AreEqual( new[] { "facepunch.props#3" }, result );
	}

	/// <summary>
	/// References that aren't valid package idents should be dropped rather than
	/// passed on to the installer.
	/// </summary>
	[TestMethod]
	public void UnparseableIdentsAreDropped()
	{
		var result = CloudAsset.DeduplicateReferences( new[] { "justaword", "too.many.dots", "", "facepunch.props#notaversion" } );

		Assert.AreEqual( 0, result.Length );
	}

	/// <summary>
	/// Distinct packages shouldn't interfere with each other.
	/// </summary>
	[TestMethod]
	public void DistinctPackagesAreKept()
	{
		var result = CloudAsset.DeduplicateReferences( new[] { "facepunch.props#2", "garry.tools", "facepunch.citizen" } );

		CollectionAssert.AreEquivalent( new[] { "facepunch.props#2", "garry.tools", "facepunch.citizen" }, result );
	}

	/// <summary>
	/// No references in, no references out.
	/// </summary>
	[TestMethod]
	public void EmptyInputYieldsEmpty()
	{
		Assert.AreEqual( 0, CloudAsset.DeduplicateReferences( System.Array.Empty<string>() ).Length );
	}

	static string WriteTempResource( string json )
	{
		var file = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"cloudasset_{System.Guid.NewGuid():N}.sound" );
		System.IO.File.WriteAllText( file, json );
		return file;
	}

	static string[] References( string file ) => CloudAsset.GetCachedReferences( file, () => System.IO.File.ReadAllText( file ) );

	/// <summary>
	/// Saving a resource with a new cloud reference shows up on the next scan, even though the
	/// previous result for that file is cached.
	/// </summary>
	[TestMethod]
	public void CachedReferencesPicksUpAddedReference()
	{
		var file = WriteTempResource( """{ "__references": [ "facepunch.props" ] }""" );
		try
		{
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, References( file ) );

			System.IO.File.WriteAllText( file, """{ "__references": [ "facepunch.props", "garry.tools" ] }""" );
			CollectionAssert.AreEqual( new[] { "facepunch.props", "garry.tools" }, References( file ) );
		}
		finally
		{
			System.IO.File.Delete( file );
		}
	}

	/// <summary>
	/// Two saves can land on the same write time (file system timestamp granularity). Adding or
	/// removing a reference changes the file length, so it's still picked up.
	/// </summary>
	[TestMethod]
	public void CachedReferencesPicksUpChangesWithinSameWriteTime()
	{
		var file = WriteTempResource( """{ "__references": [] }""" );
		try
		{
			var writeTime = System.IO.File.GetLastWriteTimeUtc( file );
			Assert.AreEqual( 0, References( file ).Length );

			System.IO.File.WriteAllText( file, """{ "__references": [ "facepunch.props" ] }""" );
			System.IO.File.SetLastWriteTimeUtc( file, writeTime );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, References( file ), "Added reference" );

			System.IO.File.WriteAllText( file, """{ "__references": [] }""" );
			System.IO.File.SetLastWriteTimeUtc( file, writeTime );
			Assert.AreEqual( 0, References( file ).Length, "Removed reference" );
		}
		finally
		{
			System.IO.File.Delete( file );
		}
	}

	/// <summary>
	/// The cache is saved with the project, so the next session's scan gets unchanged files' references
	/// without reading them again, while files changed in between are still read.
	/// </summary>
	[TestMethod]
	public void SavedReferenceCacheSkipsUnchangedFilesNextSession()
	{
		var unchanged = WriteTempResource( """{ "__references": [ "facepunch.props" ] }""" );
		var changed = WriteTempResource( """{ "__references": [] }""" );
		var storeFolder = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"cloudasset_{System.Guid.NewGuid():N}" );
		System.IO.Directory.CreateDirectory( storeFolder );

		try
		{
			var store = new LocalFileSystem( storeFolder );
			CloudAsset.LoadReferenceCache( store );
			References( unchanged );
			References( changed );
			CloudAsset.SaveReferenceCache( store, new( System.StringComparer.OrdinalIgnoreCase ) { unchanged, changed } );

			System.IO.File.WriteAllText( changed, """{ "__references": [ "garry.tools" ] }""" );

			// Next session, nothing in memory
			CloudAsset.LoadReferenceCache( null );
			CloudAsset.LoadReferenceCache( new LocalFileSystem( storeFolder ) );

			var unchangedRefs = CloudAsset.GetCachedReferences( unchanged, () => throw new System.InvalidOperationException( "Unchanged file was read again" ) );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, unchangedRefs );
			CollectionAssert.AreEqual( new[] { "garry.tools" }, References( changed ) );
		}
		finally
		{
			CloudAsset.LoadReferenceCache( null );
			System.IO.File.Delete( unchanged );
			System.IO.File.Delete( changed );
			System.IO.Directory.Delete( storeFolder, true );
		}
	}
}
