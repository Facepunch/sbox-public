namespace SceneTests.GameObjects;

/// <summary>
/// ITagSet.Contains is Has, not LINQ's case-sensitive Enumerable.Contains.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TagSetContainsTest : SceneTest
{
	[TestMethod]
	public void TagSetContainsMatchesHas()
	{
		var tags = new TagSet( new[] { "solid", "Player" } );

		foreach ( var tag in new[] { "solid", "SOLID", "player", "Player", "debris", "" } )
		{
			Assert.AreEqual( tags.Has( tag ), tags.Contains( tag ), tag );
		}

		Assert.IsTrue( tags.Contains( "PLAYER" ) );
		Assert.IsFalse( tags.Contains( "debris" ) );
	}

	[TestMethod]
	public void GameTagsContainsIncludesAncestors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		foreach ( var tag in new[] { "red", "RED", "small", "Small", "blue" } )
		{
			Assert.AreEqual( child.Tags.Has( tag ), child.Tags.Contains( tag ), tag );
		}

		Assert.IsTrue( child.Tags.Contains( "Red" ) );
		Assert.IsFalse( parent.Tags.Contains( "small" ) );
	}
}
