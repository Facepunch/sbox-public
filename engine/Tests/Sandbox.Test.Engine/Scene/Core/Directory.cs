using System;

namespace SceneTests.Core;

/// <summary>
/// Pins the scene object directory: id lookups, name lookups, and what happens when
/// two objects claim the same guid.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DirectoryTest : SceneTest
{
	/// <summary>
	/// Objects and components are addressable by guid, and disappear from the
	/// directory when destroyed.
	/// </summary>
	[TestMethod]
	public void GuidLookups()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirectoryProbe>();

		Assert.AreEqual( go, scene.Directory.FindByGuid( go.Id ) );
		Assert.AreEqual( comp, scene.Directory.FindComponentByGuid( comp.Id ) );
		Assert.IsNull( scene.Directory.FindByGuid( Guid.NewGuid() ) );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsNull( scene.Directory.FindByGuid( go.Id ) );
		Assert.IsNull( scene.Directory.FindComponentByGuid( comp.Id ) );
	}

	/// <summary>
	/// FindByName returns every match and honors case insensitivity.
	/// </summary>
	[TestMethod]
	public void NameLookups()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();
		a.Name = "Target";
		var b = scene.CreateObject();
		b.Name = "target";
		var c = scene.CreateObject();
		c.Name = "Other";

		Assert.AreEqual( 2, scene.Directory.FindByName( "target" ).Count() );
		Assert.AreEqual( 1, scene.Directory.FindByName( "target", caseinsensitive: false ).Count() );
	}

	/// <summary>
	/// When a deserialized object collides with an existing guid, the incumbent keeps
	/// its id and the newcomer is reassigned a fresh one.
	/// </summary>
	[TestMethod]
	public void DuplicateGuidReassignsNewcomer()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var original = scene.CreateObject();
		original.Name = "Original";
		var originalId = original.Id;

		var node = original.Serialize();

		var copy = new GameObject();
		copy.Deserialize( node );

		Assert.AreNotEqual( originalId, copy.Id, "the colliding newcomer must get a fresh guid" );
		Assert.AreEqual( original, scene.Directory.FindByGuid( originalId ), "the incumbent keeps its id" );
		Assert.AreEqual( copy, scene.Directory.FindByGuid( copy.Id ) );
	}

	/// <summary>
	/// FindAllWithTag matches own and inherited tags; FindAllWithTags requires all of
	/// the given tags.
	/// </summary>
	[TestMethod]
	public void TagQueries()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		var other = scene.CreateObject();
		other.Tags.Add( "red" );

		var reds = scene.FindAllWithTag( "red" ).ToList();
		CollectionAssert.Contains( reds, parent );
		CollectionAssert.Contains( reds, child ); // inherited
		CollectionAssert.Contains( reds, other );

		var redAndSmall = scene.FindAllWithTags( new[] { "red", "small" } ).ToList();
		Assert.AreEqual( 1, redAndSmall.Count );
		Assert.AreEqual( child, redAndSmall[0] );
	}

	public class DirectoryProbe : Component { }
}
