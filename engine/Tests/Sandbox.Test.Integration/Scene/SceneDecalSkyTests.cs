using System;
using System.Linq;
using Sandbox.Rendering;

namespace SceneTests;

/// <summary>
/// Sky boxes and aggregates start with the state native's objects for them started with and follow the same changes (checked
/// against native's values, <see cref="NativeGolden"/>), and leave their world (and its skies) when deleted, as decals do.
/// </summary>
[TestClass]
public class SceneDecalSkyTests
{
	static void Same( SceneObject so, string what, string more = "" ) => NativeGolden.Check( $"sky/{what}", $"{NativeGolden.Object( so )}{more}" );

	static string Sky( SceneSkyBox sky ) => $"; material={sky.Material?.Name} tint={NativeGolden.C( sky.SkyTint )} fog={sky.FogParams.FogType} {NativeGolden.N( sky.FogParams.FogMinStart )} {NativeGolden.N( sky.FogParams.FogMinEnd )} {NativeGolden.N( sky.FogParams.FogMaxStart )} {NativeGolden.N( sky.FogParams.FogMaxEnd )}";

	[TestMethod]
	public void SkiesAndAggregatesStartAndChangeAsNatives()
	{
		var world = new SceneWorld();
		try
		{
			// A sky as the sky component makes it
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = new SceneSkyBox( world, material );
			Same( sky, "new sky", Sky( sky ) );
			lock ( world.Skies ) Assert.IsTrue( world.Skies.Contains( sky ), "in the world's skies" );
			Assert.AreSame( sky, world.GetSky(), "the newest sky is the world's" );
			sky.SkyTint = Color.Red;
			sky.FogParams = new SceneSkyBox.FogParamInfo { FogType = SceneSkyBox.FogType.Angular, FogMinStart = 1, FogMinEnd = 2, FogMaxStart = 3, FogMaxEnd = 4 };
			Same( sky, "changed sky", Sky( sky ) );

			// An aggregate as the aggregate renderer makes it
			var model = AggregateModel( 3 );
			var fragments = Enumerable.Range( 0, 3 ).Select( i => new AggregateFragment
			{
				Transform = new Transform( new Vector3( i * 100, 0, 0 ) ),
				BoundsMin = new Vector3( i * 100, 0, 0 ),
				BoundsMax = new Vector3( i * 100 + 32, 32, 0 ),
				Tint = Color.White,
				DrawDescriptorIndex = i,
			} ).ToArray();
			var aggregate = new SceneAggregateObject( world, model, fragments, true );
			Same( aggregate, "new aggregate", $"; bounds {NativeGolden.B( aggregate.Bounds )}" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void SkiesAndAggregatesLeaveTheirWorldWhenDeleted()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = new SceneSkyBox( world, material );
			var fragments = new[] { new AggregateFragment { Transform = Transform.Zero, BoundsMax = new Vector3( 32 ), Tint = Color.White, DrawDescriptorIndex = 0 } };
			var aggregate = new SceneAggregateObject( world, AggregateModel( 1 ), fragments, true );
			var decal = new DecalSceneObject( world );

			sky.Delete();
			aggregate.Delete();
			decal.Delete();
			Assert.IsFalse( aggregate.IsValid() );
			aggregate.Delete(); // twice is fine, as before

			world.DeletePendingObjects();
			lock ( world.Skies ) Assert.IsFalse( world.Skies.Contains( sky ), "gone from the world's skies" );
			lock ( world.InternalSceneObjects )
			{
				Assert.IsFalse( world.InternalSceneObjects.Contains( sky ) );
				Assert.IsFalse( world.InternalSceneObjects.Contains( aggregate ) );
				Assert.IsFalse( world.InternalSceneObjects.Contains( decal ) );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	static Model AggregateModel( int drawCalls )
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 3, new Vertex[]
		{
			new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 32, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 0, 32, 0 ), Normal = Vector3.Up }
		} );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 32, 32, 0 ) );
		for ( int i = 1; i < drawCalls; i++ )
			mesh.AddSubMesh( material, 0, 3, 0, 3 );

		return Model.Builder.AddMesh( mesh ).Create();
	}
}
