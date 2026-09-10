using System;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace MovieMakerTests;

#nullable enable

[TestClass]
public sealed class SerializationTest : SceneTestBase
{
	/// <summary>
	/// Tests serializing a movie resource embedded inside a MoviePlayer, instead of
	/// as a referenced separate asset on disk.
	/// </summary>
	[TestMethod]
	public void EmbeddedSerialize()
	{
		var go = new GameObject();
		var player = go.AddComponent<MoviePlayer>();

		var compiled = MovieClip.FromTracks(
			MovieClip.RootGameObject( "Cube", id: Guid.Parse( "f9599d9c-ef0d-4456-8dc9-9921c7ce483c" ) ) );

		player.Resource = new EmbeddedMovieResource
		{
			Compiled = compiled,
			EditorData = Json.ParseToJsonNode(
				"""
				{
					"SampleRate": 30,
					"Duration": 18000,
					"Tracks": {
						"f9599d9c-ef0d-4456-8dc9-9921c7ce483c": {
							"$type": "Reference",
							"ReferenceId": "b4703c82-1ffa-4643-964b-81b15964a3ed",
							"Name": "Cube",
							"TargetType": "Sandbox.GameObject"
						}
					}
				}
				""" )
		};

		var serialized = player.Serialize();

		Assert.AreEqual( "Cube", serialized["Resource"]?["Compiled"]?["Tracks"]?[0]?["Name"]?.GetValue<string>() );
		Assert.AreEqual( 18000, serialized["Resource"]?["EditorData"]?["Duration"]?.GetValue<int>() );
	}

	/// <summary>
	/// Tests deserializing a movie resource embedded inside a MoviePlayer, instead of
	/// as a referenced separate asset on disk.
	/// </summary>
	[TestMethod]
	public void EmbeddedDeserialize()
	{
		const string moviePlayerJson =
			"""
			{
				"__type": "Sandbox.MovieMaker.MoviePlayer",
				"__guid": "a5af9b95-1d4f-4288-b06b-38d7b97c9f62",
				"__enabled": true,
				"Binder": {
					"GameObjects": [],
					"Components": []
				},
				"IsLooping": false,
				"IsPlaying": false,
				"PositionSeconds": 0,
				"Resource": {
					"Compiled": {
						"Tracks": [
							{
								"Kind": "Reference",
								"Name": "Cube",
								"Type": "Sandbox.GameObject",
								"Id": "f9599d9c-ef0d-4456-8dc9-9921c7ce483c",
								"ReferenceId": "b4703c82-1ffa-4643-964b-81b15964a3ed"
							}
						]
					},
					"EditorData": {
						"SampleRate": 30,
						"Duration": 18000,
						"Tracks": {
							"f9599d9c-ef0d-4456-8dc9-9921c7ce483c": {
								"$type": "Reference",
								"ReferenceId": "b4703c82-1ffa-4643-964b-81b15964a3ed",
								"Name": "Cube",
								"TargetType": "Sandbox.GameObject"
							}
						}
					}
				}
			}
			""";

		var go = new GameObject();
		var player = go.AddComponent<MoviePlayer>();

		player.DeserializeImmediately( Json.ParseToJsonObject( moviePlayerJson ) );

		Assert.IsInstanceOfType<EmbeddedMovieResource>( player.Resource );
		Assert.AreEqual( "Cube", player.Resource.Compiled!.Tracks.First().Name );
		Assert.AreEqual( 18000, player.Resource.EditorData?["Duration"]?.GetValue<int>() );
	}
}
