namespace GameObjects;

[TestClass]
public class Transforms
{
	[TestMethod]
	public void LocalTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.LocalTransform = Transform.Zero;
		Assert.AreEqual( go.LocalTransform, Transform.Zero );

		go.LocalPosition = new Vector3( 10, 10, 10 );
		Assert.AreEqual( go.LocalTransform, Transform.Zero.WithPosition( new Vector3( 10, 10, 10 ) ) );
		Assert.AreEqual( go.LocalPosition, new Vector3( 10, 10, 10 ) );
	}

	/// <summary>
	/// Test an object's <see cref="GameObject.WorldPosition"/> updating
	/// when it moves. Make sure it works when it moves while inactive too.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WorldTransform( bool moveWhileInactive )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var obj = new GameObject( name: "Example" );

		// WorldPosition gets cached here

		Assert.AreEqual( Vector3.Zero, obj.WorldPosition );

		// Move object, optionally while it's inactive

		obj.Enabled = !moveWhileInactive;
		obj.LocalPosition = new Vector3( 100f, 0f, 0f );
		obj.Enabled = true;

		// Make sure WorldPosition is updated

		Assert.AreEqual( new Vector3( 100f, 0f, 0f ), obj.WorldPosition );
	}

	/// <summary>
	/// Test a child object's <see cref="GameObject.WorldPosition"/> updating
	/// when its parent moves. Make sure it works when the parent moves while
	/// inactive too.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChildWorldTransform( bool moveWhileInactive )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent" );
		var child = new GameObject( parent, name: "Child" );

		// WorldPosition gets cached here

		Assert.AreEqual( Vector3.Zero, child.WorldPosition );

		// Move parent, optionally while it's inactive

		parent.Enabled = !moveWhileInactive;
		parent.LocalPosition = new Vector3( 100f, 0f, 0f );
		parent.Enabled = true;

		// Make sure WorldPosition is updated

		Assert.AreEqual( new Vector3( 100f, 0f, 0f ), child.WorldPosition );
	}
}
