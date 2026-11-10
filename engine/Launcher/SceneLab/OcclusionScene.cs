namespace Sandbox.SceneLab;

/// <summary>
/// GPU occlusion culling's scene: a city driven through at street level.
/// </summary>
internal static class OcclusionScene
{
	const string BoxModel = "models/dev/box.vmdl";

	static void Heap( string name, int i, Vector3 position, Random random )
	{
		var prop = GameScenes.Prop( name, $"models/citizen_props/{HeavyScene.Models[i % HeavyScene.Models.Length]}.vmdl", position, scale: 0.8f + random.NextSingle() * 0.8f );
		prop.GameObject.WorldRotation = Rotation.FromYaw( random.NextSingle() * 360 );
		GameScenes.OnFloor( prop );

		if ( i % 2 == 0 ) prop.Tint = new ColorHsv( random.NextSingle() * 360, 0.4f, 1 ).ToColor();
		if ( i % 7 == 0 ) prop.MaterialOverride = Material.Load( HeavyScene.Materials[i / 7 % HeavyScene.Materials.Length] );
		if ( i % 3 == 0 ) prop.Attributes.Set( "scenelab_prop", i );
	}

	const int CityBlocks = 8;
	const float BlockSize = 600;
	const float StreetWidth = 300;
	const float BlockPitch = BlockSize + StreetWidth;
	const float CitySize = CityBlocks * BlockPitch - StreetWidth;
	const float CityOrigin = -CitySize / 2;
	const float WallThickness = 20;
	const int CourtyardProps = 40;
	const int StreetProps = 1500;
	const int CourtyardSculptures = 6;
	const float SculptureRadius = 70;
	static Model sculpture;

	// A lap takes the benchmark's default measured run, so every run measures the whole lap whatever frame it starts on
	const int LapFrames = 600;
	const float EyeHeight = 100;
	const float LookAhead = 400;
	static int cityFrame;

	/// <summary>
	/// Hollow buildings around courtyards full of props and dense sculptures, and more props along the pavements, driven
	/// through at street level (<see cref="Drive"/>): most of the city, and nearly all its triangles, are behind a wall at any
	/// moment, and every corner reveals a street. Culling should cut the GPU time sharply, so a regression shows.
	/// </summary>
	public static Scene BuildCity()
	{
		var scene = GameScenes.Stage( out var camera, Lap( 0 ), Lap( LookAhead ) );
		using var _ = scene.Push();

		// Far enough that walls, not the far plane, hide the other side of the city
		camera.ZFar = 2 * CitySize;
		var floorScale = 1.2f * CitySize / Model.Load( "models/dev/plane.vmdl" ).Bounds.Size.x;
		scene.Directory.FindByName( "Floor" ).First().WorldScale = new Vector3( floorScale, floorScale, 1 );

		var random = new Random( 1234 );
		var props = 0;
		for ( int i = 0; i < CityBlocks; i++ )
		{
			for ( int j = 0; j < CityBlocks; j++ )
			{
				var corner = new Vector2( CityOrigin + i * BlockPitch, CityOrigin + j * BlockPitch );
				Building( $"Building {i} {j}", corner, 200 + random.NextSingle() * 1000 );

				for ( int k = 0; k < CourtyardProps; k++ )
				{
					var position = corner + new Vector2( Inside( random ), Inside( random ) );
					Heap( $"Courtyard {i} {j} {k}", props++, new Vector3( position, 0 ), random );
				}

				for ( int k = 0; k < CourtyardSculptures; k++ )
					Sculpture( $"Sculpture {i} {j} {k}", corner + new Vector2( Inside( random ), Inside( random ) ) );
			}
		}

		for ( int i = 0; i < StreetProps; i++ )
			Heap( $"Street {i}", props++, Pavement( random ), random );

		cityFrame = 0;
		return scene;
	}

	static void Sculpture( string name, Vector2 position )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = new Vector3( position, SculptureRadius );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = sculpture ??= CreateSculpture();
		renderer.Tint = new Color( 0.9f, 0.75f, 0.5f );

		// Shadow maps don't occlusion cull, so keeping these out of them leaves their cost to what culling can skip
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	/// <summary>
	/// A rippled sphere of about 200k triangles with no LODs: the dense hidden geometry occlusion culling is for.
	/// </summary>
	static Model CreateSculpture()
	{
		const int rings = 192, segments = 512;
		var vertices = new List<Vertex>( (rings + 1) * (segments + 1) );
		var indices = new List<int>( rings * segments * 6 );

		for ( int r = 0; r <= rings; r++ )
		{
			var phi = MathF.PI * r / rings;
			for ( int s = 0; s <= segments; s++ )
			{
				var theta = MathF.Tau * s / segments;
				var normal = new Vector3( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Sin( phi ) * MathF.Sin( theta ), MathF.Cos( phi ) );
				var radius = SculptureRadius * (1 + 0.08f * MathF.Sin( 9 * theta ) * MathF.Sin( 7 * phi ));
				var tangent = MathF.Abs( normal.z ) < 0.9f ? Vector3.Cross( Vector3.Up, normal ).Normal : Vector3.Forward;
				vertices.Add( new Vertex( normal * radius, normal, tangent, Vector4.Zero ) { Color = Color32.White } );
			}
		}

		for ( int r = 0; r < rings; r++ )
		{
			for ( int s = 0; s < segments; s++ )
			{
				int a = r * (segments + 1) + s, b = a + segments + 1;
				indices.AddRange( [a, b, a + 1, a + 1, b, b + 1] );
			}
		}

		var mesh = new Mesh( Material.Load( "materials/dev/primary_white.vmat" ) );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = BBox.FromPositionAndSize( Vector3.Zero, SculptureRadius * 2.2f );
		return Model.Builder.WithName( "occlusion_sculpture" ).AddMesh( mesh ).Create();
	}

	/// <summary>
	/// Moves the camera a frame along its lap, by frame count so every run is the same, looking ahead round the corners.
	/// </summary>
	public static void Drive( Scene scene )
	{
		var distance = (float)(cityFrame++ % LapFrames) / LapFrames * LapLength;
		var position = Lap( distance );
		scene.Camera.WorldPosition = position;
		scene.Camera.WorldRotation = Rotation.LookAt( Lap( distance + LookAhead ) - position );
	}

	static void Building( string name, Vector2 corner, float height )
	{
		var middle = corner + new Vector2( BlockSize / 2, BlockSize / 2 );
		var across = BlockSize / 2 - WallThickness / 2;
		var tint = new ColorHsv( 30, 0.1f, 0.6f + height / 3000 ).ToColor();

		Box( $"{name} North", new Vector3( middle.x, middle.y + across, height / 2 ), new Vector3( BlockSize, WallThickness, height ) ).Tint = tint;
		Box( $"{name} South", new Vector3( middle.x, middle.y - across, height / 2 ), new Vector3( BlockSize, WallThickness, height ) ).Tint = tint;
		Box( $"{name} East", new Vector3( middle.x + across, middle.y, height / 2 ), new Vector3( WallThickness, BlockSize, height ) ).Tint = tint;
		Box( $"{name} West", new Vector3( middle.x - across, middle.y, height / 2 ), new Vector3( WallThickness, BlockSize, height ) ).Tint = tint;
	}

	/// <summary>
	/// A distance into a block that keeps a prop clear of its walls.
	/// </summary>
	static float Inside( Random random ) => 80 + random.NextSingle() * (BlockSize - 160);

	/// <summary>
	/// A point on a pavement beside the middle of a street, away from the junctions the camera drives through.
	/// </summary>
	static Vector3 Pavement( Random random )
	{
		var across = Street( random.Next( CityBlocks - 1 ) ) + (random.Next( 2 ) * 2 - 1) * (StreetWidth / 2 - 40);
		var along = CityOrigin + Inside( random ) + random.Next( CityBlocks ) * BlockPitch;
		return random.Next( 2 ) == 0 ? new Vector3( across, along, 0 ) : new Vector3( along, across, 0 );
	}

	/// <summary>
	/// The middle of the street after block <paramref name="index"/>.
	/// </summary>
	static float Street( int index ) => CityOrigin + index * BlockPitch + BlockSize + StreetWidth / 2;

	static float LapStart => Street( 1 );
	static float LapSide => Street( CityBlocks - 3 ) - LapStart;
	static float LapLength => 4 * LapSide;

	/// <summary>
	/// A point on the lap: a square of streets around the middle of the city.
	/// </summary>
	static Vector3 Lap( float distance )
	{
		distance = (distance % LapLength + LapLength) % LapLength;
		var leg = (int)(distance / LapSide);
		var along = distance - leg * LapSide;
		var near = LapStart;
		var far = LapStart + LapSide;

		var point = leg switch
		{
			0 => new Vector2( near + along, near ),
			1 => new Vector2( far, near + along ),
			2 => new Vector2( far - along, far ),
			_ => new Vector2( near, far - along ),
		};

		return new Vector3( point, EyeHeight );
	}

	static ModelRenderer Box( string name, Vector3 position, Vector3 size )
	{
		var box = GameScenes.Prop( name, BoxModel, position );
		box.GameObject.WorldScale = size / Model.Load( BoxModel ).Bounds.Size.x;
		return box;
	}
}
