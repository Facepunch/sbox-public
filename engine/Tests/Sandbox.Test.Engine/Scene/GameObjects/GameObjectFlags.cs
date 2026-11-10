namespace SceneTests.GameObjects;

/// <summary>
/// Pins GameObjectFlags behaviour: bitwise add/remove via the Flags property,
/// the IsDeserializing convenience accessor, and HasFlagOrParent walking the
/// ancestor chain.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GameObjectFlagsTest : SceneTest
{
	/// <summary>
	/// Flags start at None, and can be set and cleared independently of each
	/// other with the usual bitwise operations.
	/// </summary>
	[TestMethod]
	public void FlagsAddAndRemove()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.AreEqual( GameObjectFlags.None, go.Flags );

		go.Flags |= GameObjectFlags.Hidden;
		go.Flags |= GameObjectFlags.NotSaved;

		Assert.IsTrue( go.Flags.Contains( GameObjectFlags.Hidden ) );
		Assert.IsTrue( go.Flags.Contains( GameObjectFlags.NotSaved ) );

		go.Flags &= ~GameObjectFlags.Hidden;

		Assert.IsFalse( go.Flags.Contains( GameObjectFlags.Hidden ) );
		Assert.IsTrue( go.Flags.Contains( GameObjectFlags.NotSaved ), "removing one flag must not clear others" );
	}

	/// <summary>
	/// IsDeserializing directly reflects the Deserializing flag bit.
	/// </summary>
	[TestMethod]
	public void IsDeserializingReflectsFlag()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.IsFalse( go.IsDeserializing );

		go.Flags |= GameObjectFlags.Deserializing;
		Assert.IsTrue( go.IsDeserializing );

		go.Flags &= ~GameObjectFlags.Deserializing;
		Assert.IsFalse( go.IsDeserializing );
	}

	/// <summary>
	/// HasFlagOrParent finds a flag set anywhere up the ancestor chain, on self,
	/// the parent or a grandparent - and returns false when nobody has it.
	/// </summary>
	[TestMethod]
	public void HasFlagOrParentWalksAncestors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		var parent = new GameObject( grandparent );
		var child = new GameObject( parent );

		Assert.IsFalse( child.HasFlagOrParent( GameObjectFlags.Hidden ) );

		grandparent.Flags |= GameObjectFlags.Hidden;

		Assert.IsTrue( child.HasFlagOrParent( GameObjectFlags.Hidden ) );
		Assert.IsTrue( parent.HasFlagOrParent( GameObjectFlags.Hidden ) );
		Assert.IsTrue( grandparent.HasFlagOrParent( GameObjectFlags.Hidden ), "the object's own flags count too" );

		child.Flags |= GameObjectFlags.Loading;
		Assert.IsTrue( child.HasFlagOrParent( GameObjectFlags.Loading ) );
		Assert.IsFalse( grandparent.HasFlagOrParent( GameObjectFlags.Loading ), "flags must not leak from children to parents" );
	}
}
