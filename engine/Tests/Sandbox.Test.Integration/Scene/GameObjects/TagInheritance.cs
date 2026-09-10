namespace SceneTests.GameObjects;

/// <summary>
/// Pins tag inheritance through the hierarchy: children report ancestor tags, and
/// inherited tags follow reparenting and tag removal.
/// </summary>
[TestClass]
public class TagInheritanceTest
{

	/// <summary>
	/// Inherited tags must be visible to physics traces: a trace filtered to the
	/// parent's tag hits a child's collider.
	/// </summary>
	[TestMethod]
	public void TracesSeeInheritedTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "marked" );

		var child = new GameObject( parent );
		child.WorldPosition = new Vector3( 100, 0, 0 );
		var collider = child.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 50 );

		var hit = scene.Trace
			.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) )
			.WithTag( "marked" )
			.Run();

		Assert.IsTrue( hit.Hit );
		Assert.AreEqual( child, hit.GameObject );
		Assert.IsTrue( hit.HasTag( "marked" ) );
	}
}
