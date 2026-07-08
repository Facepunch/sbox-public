using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins GameObject serialization behaviour not covered by the existing
/// serialize tests: the NotSaved flag, serialize options, which flags persist
/// through a round trip, flag-dependent transform serialization, transform
/// overrides on deserialize, and id/enabled/tags round trips.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SerializeCoverageTest : SceneTest
{
	/// <summary>
	/// An object flagged NotSaved serializes to null - it is omitted from disk
	/// saves entirely.
	/// </summary>
	[TestMethod]
	public void NotSavedRootSerializesToNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.NotSaved;

		Assert.IsNull( go.Serialize() );
	}

	/// <summary>
	/// A NotSaved child is silently omitted from its parent's serialized
	/// Children array.
	/// </summary>
	[TestMethod]
	public void NotSavedChildOmittedFromChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		var saved = new GameObject( parent, true, "Saved" );
		var skipped = new GameObject( parent, true, "Skipped" );
		skipped.Flags |= GameObjectFlags.NotSaved;

		var node = parent.Serialize();
		var children = node["Children"].AsArray();

		Assert.AreEqual( 1, children.Count );
		Assert.AreEqual( "Saved", children[0]["Name"].GetValue<string>() );
	}

	/// <summary>
	/// The IgnoreChildren / IgnoreComponents serialize options drop the
	/// corresponding keys from the output entirely.
	/// </summary>
	[TestMethod]
	public void IgnoreChildrenAndComponentsOptions()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<SerializeCoverageProbe>();
		_ = new GameObject( go, true, "Child" );

		var full = go.Serialize();
		Assert.IsTrue( full.ContainsKey( "Children" ) );
		Assert.IsTrue( full.ContainsKey( "Components" ) );

		var slim = go.Serialize( new GameObject.SerializeOptions { IgnoreChildren = true, IgnoreComponents = true } );

		Assert.IsFalse( slim.ContainsKey( "Children" ) );
		Assert.IsFalse( slim.ContainsKey( "Components" ) );
	}

	/// <summary>
	/// The serialized object carries the core identity keys: guid, format
	/// version 2, name, enabled state and the local transform.
	/// </summary>
	[TestMethod]
	public void SerializeWritesCoreKeys()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = new GameObject( true, "Core" );
		go.LocalPosition = new Vector3( 1, 2, 3 );

		var node = go.Serialize();

		Assert.AreEqual( go.Id, node["__guid"].Deserialize<Guid>() );
		Assert.AreEqual( 2, node["__version"].GetValue<int>() );
		Assert.AreEqual( "Core", node["Name"].GetValue<string>() );
		Assert.IsTrue( node["Enabled"].GetValue<bool>() );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), node["Position"].GetValue<Vector3>() );
	}

	/// <summary>
	/// Only the object's own tags are serialized - inherited tags belong to the
	/// ancestor - and they come back on deserialize.
	/// </summary>
	[TestMethod]
	public void OnlyOwnTagsSerializedAndRestored()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Tags.Add( "red" );

		var child = new GameObject( parent );
		child.Tags.Add( "small" );

		var node = child.Serialize();
		Assert.AreEqual( "small", node["Tags"].GetValue<string>() );

		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.IsTrue( restored.Tags.Has( "small" ) );
		Assert.IsFalse( restored.Tags.Has( "red" ), "inherited tags must not be baked into the child" );
	}

	/// <summary>
	/// Deserializing minimal JSON applies the documented defaults: the name is
	/// kept, the object ends up disabled (no Enabled key means false) and the
	/// transform resets to identity.
	/// </summary>
	[TestMethod]
	public void MinimalJsonLeavesDefaults()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = new GameObject( true, "KeepMe" );
		go.LocalPosition = new Vector3( 5, 5, 5 );

		var json = Json.ParseToJsonObject( $$"""{ "__guid": "{{Guid.NewGuid()}}", "__version": 2 }""" );
		go.Deserialize( json );

		Assert.AreEqual( "KeepMe", go.Name, "a missing Name key keeps the current name" );
		Assert.IsFalse( go.Enabled, "a missing Enabled key means disabled" );
		Assert.AreEqual( Vector3.Zero, go.LocalPosition );
		Assert.AreEqual( Vector3.One, go.LocalTransform.Scale );
	}

	/// <summary>
	/// Only the persistent flags survive a serialize/deserialize round trip;
	/// runtime flags like DontDestroyOnLoad are dropped.
	/// </summary>
	[TestMethod]
	public void FlagsRoundTripKeepsOnlyPersistentFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.Hidden | GameObjectFlags.Absolute | GameObjectFlags.DontDestroyOnLoad;

		var node = go.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.IsTrue( restored.Flags.Contains( GameObjectFlags.Hidden ) );
		Assert.IsTrue( restored.Flags.Contains( GameObjectFlags.Absolute ) );
		Assert.IsFalse( restored.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ), "runtime-only flags must not survive the round trip" );
		Assert.IsFalse( restored.Flags.Contains( GameObjectFlags.Deserializing ), "the deserializing flag must be cleared when done" );
	}

	// Loading and friends shouldn't end up on disk, they break prefab diffs if they do.
	[TestMethod]
	public void TransientFlagsAreNotSerializedForDisk()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.Hidden | GameObjectFlags.Loading;

		var flags = (GameObjectFlags)(long)go.Serialize()[GameObject.JsonKeys.Flags];

		Assert.IsTrue( flags.Contains( GameObjectFlags.Hidden ) );
		Assert.IsFalse( flags.Contains( GameObjectFlags.Loading ) );
	}

	// Network is the exception, it sends everything.
	[TestMethod]
	public void NetworkSerializeKeepsFullFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.Hidden | GameObjectFlags.Loading;

		var flags = (GameObjectFlags)(long)go.Serialize( new GameObject.SerializeOptions { SceneForNetwork = true } )[GameObject.JsonKeys.Flags];

		Assert.IsTrue( flags.Contains( GameObjectFlags.Hidden ) );
		Assert.IsTrue( flags.Contains( GameObjectFlags.Loading ) );
	}

	// With every flag set, a disk serialize must write precisely PersistedFlags and nothing else.
	[TestMethod]
	public void OnlyPersistedFlagsAreWrittenToDisk()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		// NotSaved omits the whole object from disk, so leave it off.
		go.Flags = (GameObjectFlags)(~0) & ~GameObjectFlags.NotSaved;

		var flags = (GameObjectFlags)(long)go.Serialize()[GameObject.JsonKeys.Flags];

		Assert.AreEqual( GameObject.PersistedFlags, flags, "disk serialize must contain exactly the persisted flags" );
	}

	/// <summary>
	/// EditorOnly objects are destroyed on the spot when deserialized into a
	/// non-editor scene - they never appear in game.
	/// </summary>
	[TestMethod]
	public void EditorOnlyObjectDestroyedOnDeserialize()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.EditorOnly;

		var node = go.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.IsFalse( restored.IsValid );
		Assert.IsFalse( scene.Children.Contains( restored ) );
	}

	/// <summary>
	/// Attachment-flagged objects don't persist their transform - they
	/// serialize an identity transform because an animation drives them.
	/// </summary>
	[TestMethod]
	public void AttachmentSerializesIdentityTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.Attachment;
		go.LocalPosition = new Vector3( 5, 6, 7 );

		var node = go.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.AreEqual( Vector3.Zero, restored.LocalPosition );
	}

	/// <summary>
	/// PhysicsBone objects serialize their parent-relative transform computed
	/// from world space, and deserialize back through the parent to the same
	/// world position.
	/// </summary>
	[TestMethod]
	public void PhysicsBoneTransformRoundTrips()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 100, 0, 0 );

		var bone = new GameObject( parent, true, "Bone" );
		bone.Flags |= GameObjectFlags.PhysicsBone;
		bone.WorldPosition = new Vector3( 150, 0, 0 );

		var node = parent.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node );

		var restoredBone = restored.Children[0];

		Assert.IsTrue( restored.WorldPosition.AlmostEqual( new Vector3( 100, 0, 0 ) ), $"{restored.WorldPosition}" );
		Assert.IsTrue( restoredBone.LocalPosition.AlmostEqual( new Vector3( 50, 0, 0 ) ), $"{restoredBone.LocalPosition}" );
		Assert.IsTrue( restoredBone.WorldPosition.AlmostEqual( new Vector3( 150, 0, 0 ) ), $"{restoredBone.WorldPosition}" );
	}

	/// <summary>
	/// DeserializeOptions.TransformOverride repositions the root object only -
	/// children keep their serialized local transforms relative to it.
	/// </summary>
	[TestMethod]
	public void TransformOverrideAppliesToRootOnly()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.LocalPosition = new Vector3( 10, 0, 0 );

		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 5, 0, 0 );

		var node = parent.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var restored = new GameObject();
		restored.Deserialize( node, new GameObject.DeserializeOptions { TransformOverride = new Transform( new Vector3( 50, 0, 0 ) ) } );

		Assert.IsTrue( restored.WorldPosition.AlmostEqual( new Vector3( 50, 0, 0 ) ), $"{restored.WorldPosition}" );

		var restoredChild = restored.Children[0];
		Assert.IsTrue( restoredChild.LocalPosition.AlmostEqual( new Vector3( 5, 0, 0 ) ), $"{restoredChild.LocalPosition}" );
		Assert.IsTrue( restoredChild.WorldPosition.AlmostEqual( new Vector3( 55, 0, 0 ) ), $"{restoredChild.WorldPosition}" );
	}

	/// <summary>
	/// When no guid collision is in play - deserializing into a fresh scene -
	/// the serialized id is preserved exactly.
	/// </summary>
	[TestMethod]
	public void IdPreservedWhenDeserializingInFreshScene()
	{
		JsonObject node;
		Guid originalId;

		{
			var sceneA = new Scene();
			using var scopeA = sceneA.Push();

			var go = sceneA.CreateObject();
			go.Name = "Original";

			originalId = go.Id;
			node = go.Serialize();
		}

		var sceneB = new Scene();
		using var scopeB = sceneB.Push();

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.AreEqual( originalId, restored.Id );
		Assert.AreEqual( restored, sceneB.Directory.FindByGuid( originalId ) );
	}

	/// <summary>
	/// The enabled state round trips in both directions.
	/// </summary>
	[TestMethod]
	public void EnabledStateRoundTrips()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var enabled = scene.CreateObject();
		var enabledNode = enabled.Serialize();
		SceneUtility.MakeIdGuidsUnique( enabledNode );

		var restoredEnabled = new GameObject();
		restoredEnabled.Deserialize( enabledNode );
		Assert.IsTrue( restoredEnabled.Enabled );

		var disabled = scene.CreateObject( false );
		var disabledNode = disabled.Serialize();
		SceneUtility.MakeIdGuidsUnique( disabledNode );

		var restoredDisabled = new GameObject();
		restoredDisabled.Deserialize( disabledNode );
		Assert.IsFalse( restoredDisabled.Enabled );
	}
}

/// <summary>
/// Bare component used to populate the Components array in the serialize
/// options tests.
/// </summary>
public class SerializeCoverageProbe : Component
{
}
