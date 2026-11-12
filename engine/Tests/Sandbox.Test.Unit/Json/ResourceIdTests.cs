using System;

namespace ResourceTests;

[TestClass]
public class ResourceIdTests
{
	[TestMethod]
	public void LegacyPathsAndEmptyReferencesRoundTripWithoutNativeInterop()
	{
		var legacy = Json.Deserialize<ResourceId>( "\"animations/walk.anim\"" );
		Assert.IsNull( legacy.Guid );
		Assert.AreEqual( "animations/walk.anim", legacy.Path );
		Assert.AreEqual( "\"animations/walk.anim\"", Json.Serialize( legacy ) );

		foreach ( var json in new[] { "null", "\"\"", "{}", "{\"Id\":\"00000000-0000-0000-0000-000000000000\"}" } )
			Assert.IsTrue( Json.Deserialize<ResourceId>( json ).IsEmpty, json );

		Assert.IsTrue( new ResourceId { Guid = Guid.Empty }.IsEmpty );
		Assert.IsFalse( new ResourceId { Guid = Guid.Empty, Path = "walk.anim" }.IsEmpty );
		Assert.IsFalse( new ResourceId { Guid = Guid.NewGuid() }.IsEmpty );
	}

	[TestMethod]
	public void UnavailableIdentitiesAndFallbackPathsSurviveSerialization()
	{
		var guid = Guid.NewGuid();
		foreach ( var path in new[] { null, "", "old/location.skel" } )
		{
			var original = new ResourceId { Guid = guid, Path = path };
			var copy = Json.Deserialize<ResourceId>( Json.Serialize( original ) );
			Assert.AreEqual( guid, copy.Guid );
			Assert.AreEqual( path, copy.Path );
		}

		var fallback = Json.Deserialize<ResourceId>( """{"Id":"not-a-guid","Path":"legacy.skel","FutureField":true}""" );
		Assert.IsNull( fallback.Guid );
		Assert.AreEqual( "legacy.skel", fallback.Path );
		Assert.AreEqual( "\"legacy.skel\"", Json.Serialize( fallback ) );
	}
}
