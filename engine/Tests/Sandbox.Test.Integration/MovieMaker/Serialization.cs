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

	private static MovieClip DeserializeComponentTrack( string type, string fallbackType ) => Json.Deserialize<MovieClip>(
		$$"""
		{
			"Tracks": [
				{
					"Kind": "Reference",
					"Name": "Map",
					"Type": "Sandbox.GameObject",
					"Id": "f9599d9c-ef0d-4456-8dc9-9921c7ce483c",
					"Children": [
						{
							"Kind": "Reference",
							"Name": "MapLoader",
							"Type": "{{type}}",
							"Id": "e67d2256-395a-4398-bc54-74647b36fabf",
							"Metadata": {
								"ReferenceId": "fdc35dc7-be73-4153-b247-52b8474f4173",
								"FallbackType": "{{fallbackType}}"
							}
						}
					]
				}
			]
		}
		""" );

	/// <summary>
	/// A component track whose type doesn't exist here, like one declared in another game's code,
	/// loads as its <see cref="TrackMetadata.FallbackType"/>.
	/// </summary>
	[TestMethod]
	public void MissingComponentTypeUsesFallback()
	{
		var clip = DeserializeComponentTrack( "TerrorTown.TerrorTownHammerMapLoader", "Sandbox.MapInstance" );
		var track = clip.Tracks.OfType<IReferenceTrack>().Single( x => x.Name == "MapLoader" );

		Assert.AreEqual( typeof( MapInstance ), track.TargetType );
		Assert.AreEqual( typeof( MapInstance ), track.Metadata?.FallbackType );
	}

	/// <summary>
	/// A component track whose type exists ignores its <see cref="TrackMetadata.FallbackType"/>.
	/// </summary>
	[TestMethod]
	public void ExistingComponentTypeIgnoresFallback()
	{
		var clip = DeserializeComponentTrack( "Sandbox.SkinnedModelRenderer", "Sandbox.ModelRenderer" );
		var track = clip.Tracks.OfType<IReferenceTrack>().Single( x => x.Name == "MapLoader" );

		Assert.AreEqual( typeof( SkinnedModelRenderer ), track.TargetType );
	}

	/// <summary>
	/// A component track whose type doesn't exist, and has no fallback, is skipped without failing the whole movie.
	/// </summary>
	[TestMethod]
	public void MissingComponentTypeWithoutFallbackIsSkipped()
	{
		var clip = DeserializeComponentTrack( "TerrorTown.TerrorTownHammerMapLoader", "TerrorTown.Missing" );

		Assert.AreEqual( "Map", clip.Tracks.Single().Name );
	}

	/// <summary>
	/// <see cref="TrackMetadata.Flags"/> survive serialization, and are left out when there aren't any.
	/// </summary>
	[TestMethod]
	public void SerializeTrackFlags()
	{
		var mapTrack = MovieClip.RootGameObject( "Map" );
		var propTrack = mapTrack.GameObject( "Prop", metadata: new TrackMetadata( Flags: TrackFlags.MapObject ) );
		var effectTrack = mapTrack.GameObject( "Effect", metadata: new TrackMetadata( Order: 1 ) );

		var json = Json.Serialize( MovieClip.FromTracks( propTrack, effectTrack ) );

		Console.WriteLine( json );

		Assert.AreEqual( 1, System.Text.RegularExpressions.Regex.Matches( json, "\"Flags\"" ).Count );

		var clip = Json.Deserialize<MovieClip>( json )!;
		var tracks = clip.Tracks.OfType<IReferenceTrack>().ToDictionary( x => x.Name );

		Assert.AreEqual( TrackFlags.MapObject, tracks["Prop"].Metadata?.Flags );
		Assert.AreEqual( TrackFlags.None, tracks["Effect"].Metadata?.Flags );
	}
}
