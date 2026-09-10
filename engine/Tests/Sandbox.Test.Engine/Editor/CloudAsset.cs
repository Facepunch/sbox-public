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
}
