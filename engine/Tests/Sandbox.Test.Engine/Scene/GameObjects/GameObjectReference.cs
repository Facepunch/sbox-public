using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins how GameObject references serialize to and from JSON: the reference
/// object form, the legacy guid string form, unknown references resolving to
/// null, destroyed objects writing null, and reference fixup through a
/// serialize/clone round trip.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GameObjectReferenceTest : SceneTest
{
	/// <summary>
	/// Reads a GameObject from a JSON snippet through the production
	/// <see cref="GameObject.JsonRead"/> entry point.
	/// </summary>
	static GameObject ReadGameObjectJson( string json )
	{
		var bytes = Encoding.UTF8.GetBytes( json );
		var reader = new Utf8JsonReader( bytes );
		reader.Read();

		return (GameObject)GameObject.JsonRead( ref reader, typeof( GameObject ) );
	}

	/// <summary>
	/// Writes a value through the production <see cref="GameObject.JsonWrite"/>
	/// entry point and returns the produced JSON text.
	/// </summary>
	static string WriteGameObjectJson( object value )
	{
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream ) )
		{
			GameObject.JsonWrite( value, writer );
		}

		return Encoding.UTF8.GetString( stream.ToArray() );
	}

	/// <summary>
	/// The reference object form ({"_type":"gameobject","go":guid}) resolves to
	/// the live instance in the active scene.
	/// </summary>
	[TestMethod]
	public void ReferenceObjectFormResolves()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var resolved = ReadGameObjectJson( $$"""{ "_type": "gameobject", "go": "{{go.Id}}" }""" );

		Assert.AreSame( go, resolved );
	}

	/// <summary>
	/// A reference to a guid that doesn't exist in the scene resolves to null
	/// (with a warning) instead of throwing - this happens routinely when
	/// deserializing networked data referencing objects we don't know about.
	/// </summary>
	[TestMethod]
	public void UnknownReferenceResolvesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var resolved = ReadGameObjectJson( $$"""{ "_type": "gameobject", "go": "{{Guid.NewGuid()}}" }""" );

		Assert.IsNull( resolved );
	}

	/// <summary>
	/// The legacy form - a plain guid string - still resolves to the instance.
	/// </summary>
	[TestMethod]
	public void LegacyGuidStringResolves()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var resolved = ReadGameObjectJson( $"\"{go.Id}\"" );

		Assert.AreSame( go, resolved );
	}

	/// <summary>
	/// A legacy guid string for an unknown object resolves to null with a
	/// warning.
	/// </summary>
	[TestMethod]
	public void LegacyUnknownGuidStringResolvesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var resolved = ReadGameObjectJson( $"\"{Guid.NewGuid()}\"" );

		Assert.IsNull( resolved );
	}

	/// <summary>
	/// A reference object with an unknown "_type" is rejected loudly - silent
	/// nulls here would hide data corruption.
	/// </summary>
	[TestMethod]
	public void UnknownReferenceTypeThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.ThrowsException<Exception>( () =>
		{
			var bytes = Encoding.UTF8.GetBytes( """{ "_type": "banana" }""" );
			var reader = new Utf8JsonReader( bytes );
			reader.Read();
			GameObject.JsonRead( ref reader, typeof( GameObject ) );
		} );
	}

	/// <summary>
	/// Writing a valid GameObject produces the reference object form containing
	/// its guid.
	/// </summary>
	[TestMethod]
	public void WriteValidGameObjectProducesReference()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var json = WriteGameObjectJson( go );

		StringAssert.Contains( json, "gameobject" );
		StringAssert.Contains( json, go.Id.ToString() );
	}

	/// <summary>
	/// A destroyed GameObject serializes as JSON null, so stale references
	/// don't leak invalid guids into saved data.
	/// </summary>
	[TestMethod]
	public void WriteDestroyedGameObjectIsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.DestroyImmediate();

		Assert.AreEqual( "null", WriteGameObjectJson( go ) );
	}

	/// <summary>
	/// JsonWrite only accepts GameObjects - anything else is a programming
	/// error and throws.
	/// </summary>
	[TestMethod]
	public void WriteNonGameObjectThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.ThrowsException<NotImplementedException>( () => WriteGameObjectJson( 5 ) );
	}

	/// <summary>
	/// A component property referencing a sibling GameObject survives a
	/// serialize / make-unique / deserialize round trip: the copy's property
	/// points at the copied sibling, not the original.
	/// </summary>
	[TestMethod]
	public void ComponentPropertyReferenceRemapsOnRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var childA = new GameObject( root, true, "A" );
		var childB = new GameObject( root, true, "B" );

		var holder = childB.Components.Create<RefHolderComponent>();
		holder.Target = childA;

		var node = root.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var copy = new GameObject();
		copy.Deserialize( node );

		var copyA = copy.Children.First( x => x.Name == "A" );
		var copyB = copy.Children.First( x => x.Name == "B" );
		var copyHolder = copyB.Components.Get<RefHolderComponent>( true );

		Assert.IsNotNull( copyHolder );
		Assert.AreSame( copyA, copyHolder.Target );
		Assert.AreNotSame( childA, copyHolder.Target );
	}

	/// <summary>
	/// Deserializing a component whose GameObject reference can't be found in
	/// the scene leaves the property null instead of failing the whole object.
	/// </summary>
	[TestMethod]
	public void ComponentPropertyUnknownReferenceBecomesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var json = Json.ParseToJsonObject( $$"""
			{
				"__guid": "{{Guid.NewGuid()}}",
				"__version": 2,
				"Name": "Holder",
				"Enabled": true,
				"Components": [
					{
						"__type": "{{typeof( RefHolderComponent ).FullName}}",
						"__guid": "{{Guid.NewGuid()}}",
						"Target": { "_type": "gameobject", "go": "{{Guid.NewGuid()}}" }
					}
				]
			}
			""" );

		var go = new GameObject();
		go.Deserialize( json );

		var holder = go.Components.Get<RefHolderComponent>( true );
		Assert.IsNotNull( holder, "the component itself must still deserialize" );
		Assert.IsNull( holder.Target );
	}

	/// <summary>
	/// Serializing a component whose referenced GameObject has been destroyed
	/// writes a null value for the property.
	/// </summary>
	[TestMethod]
	public void DestroyedTargetSerializesAsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var holder = go.Components.Create<RefHolderComponent>();

		var target = scene.CreateObject();
		holder.Target = target;
		target.DestroyImmediate();

		var node = go.Serialize();
		var compJson = node["Components"].AsArray()[0].AsObject();

		Assert.IsTrue( compJson.ContainsKey( "Target" ) );
		Assert.IsNull( compJson["Target"], "the destroyed reference must serialize as JSON null" );
	}
}

/// <summary>
/// Component with a serialized GameObject reference property, used to test
/// reference resolution. Top level so JSON type lookup by full name works.
/// </summary>
public class RefHolderComponent : Component
{
	/// <summary>
	/// The referenced GameObject under test.
	/// </summary>
	[Property] public GameObject Target { get; set; }
}
