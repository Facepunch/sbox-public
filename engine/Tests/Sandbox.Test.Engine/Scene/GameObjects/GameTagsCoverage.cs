using System;
using System.Collections.Generic;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the parts of GameTags not covered by Tags.cs / TagInheritance.cs:
/// tag validation and normalization, Set/Toggle, the HasAny/HasAll overload
/// family, SetAll/CloneFrom, the token cache across reparenting, suggestions,
/// and enumeration.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GameTagsCoverageTest : SceneTest
{
	/// <summary>
	/// Tags containing whitespace, punctuation or more than 32 characters are
	/// rejected with a warning instead of being stored; valid characters
	/// (alphanumerics, dot, underscore, dash) are accepted.
	/// </summary>
	[TestMethod]
	public void InvalidTagsAreRejected()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.Tags.Add( "has space" );
		go.Tags.Add( "bang!" );
		go.Tags.Add( new string( 'a', 33 ) );
		go.Tags.Add( "" );
		go.Tags.Add( "   " );
		go.Tags.Add( (string)null );

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );

		go.Tags.Add( "ok-tag_1.2" );

		Assert.AreEqual( 1, go.Tags.TryGetAll().Count() );
		Assert.IsTrue( go.Tags.Has( "ok-tag_1.2" ) );
	}

	/// <summary>
	/// Tags are normalized to lowercase when stored, so the enumerated set
	/// contains the lowercase form regardless of how the tag was added.
	/// </summary>
	[TestMethod]
	public void TagsAreStoredLowercase()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "LOUD" );

		CollectionAssert.Contains( go.Tags.TryGetAll().ToList(), "loud" );
	}

	/// <summary>
	/// The params Add overload adds each tag once, ignoring duplicates within
	/// the call, and tolerates a null array.
	/// </summary>
	[TestMethod]
	public void AddParamsIgnoresDuplicates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "a", "b", "A" );

		Assert.AreEqual( 2, go.Tags.TryGetAll().Count() );

		go.Tags.Add( (string[])null );
		Assert.AreEqual( 2, go.Tags.TryGetAll().Count() );
	}

	/// <summary>
	/// Adding a tag the object already inherits from an ancestor is a no-op:
	/// the tag is not duplicated into the child's own set.
	/// </summary>
	[TestMethod]
	public void AddingInheritedTagIsNoOp()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "red" );

		Assert.IsTrue( child.Tags.Has( "red" ) );
		Assert.AreEqual( 0, child.Tags.TryGetAll( false ).Count(), "the inherited tag must not be copied into the child's own set" );
	}

	/// <summary>
	/// Set adds or removes based on the state argument, and Toggle flips the
	/// tag's presence.
	/// </summary>
	[TestMethod]
	public void SetAndToggle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.Tags.Set( "flag", true );
		Assert.IsTrue( go.Tags.Has( "flag" ) );

		go.Tags.Set( "flag", false );
		Assert.IsFalse( go.Tags.Has( "flag" ) );

		go.Tags.Toggle( "flag" );
		Assert.IsTrue( go.Tags.Has( "flag" ) );

		go.Tags.Toggle( "flag" );
		Assert.IsFalse( go.Tags.Has( "flag" ) );
	}

	/// <summary>
	/// The HasAny / HasAll overloads (HashSet, params, other tag set) all
	/// evaluate against the object's tags.
	/// </summary>
	[TestMethod]
	public void HasAnyHasAllOverloads()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "red", "small" );

		Assert.IsTrue( go.Tags.HasAny( new HashSet<string> { "red", "nope" } ) );
		Assert.IsFalse( go.Tags.HasAny( new HashSet<string> { "nope", "missing" } ) );

		Assert.IsTrue( go.Tags.HasAny( "nope", "small" ) );
		Assert.IsFalse( go.Tags.HasAny( "nope" ) );

		Assert.IsTrue( go.Tags.HasAll( "red", "small" ) );
		Assert.IsFalse( go.Tags.HasAll( "red", "nope" ) );

		var other = scene.CreateObject();
		other.Tags.Add( "red" );

		Assert.IsTrue( go.Tags.HasAny( other.Tags ) );
		Assert.IsTrue( go.Tags.HasAll( other.Tags ) );

		other.Tags.Add( "huge" );
		Assert.IsFalse( go.Tags.HasAll( other.Tags ) );
	}

	/// <summary>
	/// SetAll replaces the entire tag set with the tags parsed from a space
	/// separated string.
	/// </summary>
	[TestMethod]
	public void SetAllReplacesEverything()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "old" );

		go.Tags.SetAll( "one two three" );

		Assert.AreEqual( 3, go.Tags.TryGetAll().Count() );
		Assert.IsFalse( go.Tags.Has( "old" ) );
		Assert.IsTrue( go.Tags.Has( "one" ) );
		Assert.IsTrue( go.Tags.Has( "two" ) );
		Assert.IsTrue( go.Tags.Has( "three" ) );

		go.Tags.SetAll( "four" );

		Assert.AreEqual( 1, go.Tags.TryGetAll().Count() );
		Assert.IsTrue( go.Tags.Has( "four" ) );
	}

	/// <summary>
	/// RemoveAll clears every own tag, and is safe to call on an object that
	/// never had tags (the lazy sets are still null).
	/// </summary>
	[TestMethod]
	public void RemoveAllClears()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var untouched = scene.CreateObject();
		untouched.Tags.RemoveAll();
		Assert.AreEqual( 0, untouched.Tags.TryGetAll().Count() );

		var go = scene.CreateObject();
		go.Tags.Add( "a", "b" );

		go.Tags.RemoveAll();

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );
		Assert.IsFalse( go.Tags.Has( "a" ) );
	}

	/// <summary>
	/// Cloning a GameObject copies its own tags onto the clone.
	/// </summary>
	[TestMethod]
	public void CloneCopiesTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "red" );

		var clone = go.Clone();

		Assert.IsTrue( clone.Tags.Has( "red" ) );
	}

	/// <summary>
	/// CloneFrom replaces the destination's own tags with the source's own tags,
	/// and clears the destination when the source has none.
	/// </summary>
	[TestMethod]
	public void CloneFromReplacesOwnTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var source = scene.CreateObject();
		source.Tags.Add( "new" );

		var dest = scene.CreateObject();
		dest.Tags.Add( "old" );

		dest.Tags.CloneFrom( source.Tags );

		Assert.IsTrue( dest.Tags.Has( "new" ) );
		Assert.IsFalse( dest.Tags.Has( "old" ) );

		var empty = scene.CreateObject();
		dest.Tags.CloneFrom( empty.Tags );

		Assert.AreEqual( 0, dest.Tags.TryGetAll().Count(), "cloning from an empty set must clear the destination" );
	}

	/// <summary>
	/// GetTokens merges the object's own tokens with its ancestors', and the
	/// cached merge is invalidated when tags change or the object is reparented.
	/// </summary>
	[TestMethod]
	public void GetTokensFollowsHierarchy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var redParent = scene.CreateObject();
		redParent.Tags.Add( "red" );

		var child = new GameObject( redParent );

		// With no own tags the child reports exactly the parent's tokens
		Assert.IsTrue( child.Tags.GetTokens().SetEquals( redParent.Tags.GetTokens() ) );
		Assert.AreEqual( 1, child.Tags.GetTokens().Count );

		// Own tags merge with inherited ones
		child.Tags.Add( "own" );
		Assert.AreEqual( 2, child.Tags.GetTokens().Count );

		// Reparenting must rebuild the cached token set
		var blueParent = scene.CreateObject();
		blueParent.Tags.Add( "blue", "green" );

		child.Parent = blueParent;

		Assert.AreEqual( 3, child.Tags.GetTokens().Count );
		Assert.IsTrue( child.Tags.GetTokens().IsSupersetOf( blueParent.Tags.GetTokens() ) );
	}

	/// <summary>
	/// An object with no tags anywhere in its chain reports an empty token set.
	/// </summary>
	[TestMethod]
	public void GetTokensEmptyWhenNoTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.AreEqual( 0, go.Tags.GetTokens().Count );
	}

	/// <summary>
	/// GetSuggested offers tags already used by other objects in the scene.
	/// </summary>
	[TestMethod]
	public void GetSuggestedIncludesSceneTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var tagged = scene.CreateObject();
		tagged.Tags.Add( "alpha" );

		var asking = scene.CreateObject();
		var suggested = asking.Tags.GetSuggested().ToList();

		CollectionAssert.Contains( suggested, "alpha" );
	}

	/// <summary>
	/// TryGetAll with includeAncestors=false returns only the object's own tags,
	/// while includeAncestors=true merges in the parent chain.
	/// </summary>
	[TestMethod]
	public void TryGetAllIncludeAncestorsFlag()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );

		Assert.AreEqual( 0, child.Tags.TryGetAll( false ).Count() );
		CollectionAssert.Contains( child.Tags.TryGetAll( true ).ToList(), "red" );
	}

	/// <summary>
	/// GameTags is enumerable - foreach yields the full tag set - and ToString
	/// renders the tags.
	/// </summary>
	[TestMethod]
	public void EnumerationAndToString()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "solo" );

		var seen = new List<string>();
		foreach ( var tag in go.Tags )
		{
			seen.Add( tag );
		}

		CollectionAssert.AreEqual( new[] { "solo" }, seen );
		Assert.AreEqual( "solo", go.Tags.ToString() );
	}

	/// <summary>
	/// Mutating the tags of a destroyed object must not throw - the dirty
	/// notification path bails out when the target is no longer valid.
	/// </summary>
	[TestMethod]
	public void MutatingDestroyedObjectTagsDoesNotThrow()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "old" );
		go.DestroyImmediate();

		go.Tags.Add( "late" );
		go.Tags.Remove( "old" );
		go.Tags.RemoveAll();
	}
}
