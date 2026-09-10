namespace SceneTests.GameObjects;

/// <summary>
/// Pins tag inheritance through the hierarchy: children report ancestor tags, and
/// inherited tags follow reparenting and tag removal.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TagInheritanceTest : SceneTest
{
	/// <summary>
	/// A child reports its ancestors' tags as well as its own.
	/// </summary>
	[TestMethod]
	public void ChildInheritsAncestorTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		var grandchild = new GameObject( child );

		Assert.IsTrue( child.Tags.Has( "red" ) );
		Assert.IsTrue( child.Tags.Has( "small" ) );
		Assert.IsTrue( grandchild.Tags.Has( "red" ) );
		Assert.IsTrue( grandchild.Tags.Has( "small" ) );
		Assert.IsFalse( parent.Tags.Has( "small" ) );
	}

	/// <summary>
	/// Removing a tag from an ancestor removes it from the descendants' view too.
	/// </summary>
	[TestMethod]
	public void RemovingAncestorTagPropagates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		Assert.IsTrue( child.Tags.Has( "red" ) );

		parent.Tags.Remove( "red" );
		Assert.IsFalse( child.Tags.Has( "red" ) );
	}

	/// <summary>
	/// Reparenting re-evaluates inherited tags: tags from the old parent disappear,
	/// tags from the new parent appear, own tags stay.
	/// </summary>
	[TestMethod]
	public void ReparentingReevaluatesInheritedTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var redParent = scene.CreateObject();
		redParent.Tags.Add( "red" );

		var blueParent = scene.CreateObject();
		blueParent.Tags.Add( "blue" );

		var child = new GameObject( redParent );
		child.Tags.Add( "own" );

		Assert.IsTrue( child.Tags.Has( "red" ) );
		Assert.IsFalse( child.Tags.Has( "blue" ) );

		child.Parent = blueParent;

		Assert.IsFalse( child.Tags.Has( "red" ) );
		Assert.IsTrue( child.Tags.Has( "blue" ) );
		Assert.IsTrue( child.Tags.Has( "own" ) );
	}

	/// <summary>
	/// HasAny and HasAll evaluate against the full inherited tag set.
	/// </summary>
	[TestMethod]
	public void HasAnyHasAllIncludeInherited()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		Assert.IsTrue( child.Tags.HasAny( new[] { "red", "nope" } ) );
		Assert.IsTrue( child.Tags.HasAll( new[] { "red", "small" } ) );
		Assert.IsFalse( child.Tags.HasAll( new[] { "red", "nope" } ) );
	}

	/// <summary>
	/// TryGetAll returns the merged set of own and inherited tags, and the merged set
	/// is rebuilt when an ancestor's tags change between calls.
	/// </summary>
	[TestMethod]
	public void TryGetAllMergesAncestors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		var merged = child.Tags.TryGetAll()?.ToList();
		Assert.IsNotNull( merged );
		CollectionAssert.Contains( merged, "red" );
		CollectionAssert.Contains( merged, "small" );

		// Mutating the ancestor must invalidate the merged view
		parent.Tags.Add( "fast" );
		var refreshed = child.Tags.TryGetAll()?.ToList();
		CollectionAssert.Contains( refreshed, "fast" );

		parent.Tags.Remove( "red" );
		var afterRemove = child.Tags.TryGetAll()?.ToList();
		CollectionAssert.DoesNotContain( afterRemove, "red" );
		CollectionAssert.Contains( afterRemove, "small" );
	}

}
