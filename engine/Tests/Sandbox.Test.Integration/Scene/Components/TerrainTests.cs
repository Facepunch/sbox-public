using System;

namespace SceneTests.Components;

[TestClass]
public class TerrainComponentTest
{
	/// <summary>
	/// Builds a small TerrainStorage for component tests: 64x64 heightmap, 6400 world
	/// units across (100 units per cell) and 1000 units of height range, so a raw
	/// height of ushort.MaxValue maps to exactly 1000 world units.
	/// </summary>
	static TerrainStorage CreateSmallStorage()
	{
		var storage = new TerrainStorage();
		storage.SetResolution( 64 );
		storage.TerrainSize = 6400.0f;
		storage.TerrainHeight = 1000.0f;
		return storage;
	}

	/// <summary>
	/// Raises the square vertex region [16..47] on both axes of a 64x64 heightmap to the
	/// maximum raw height. The region is symmetric so the result is identical regardless
	/// of row/column transposition.
	/// </summary>
	static void RaiseCenterPlateau( TerrainStorage storage )
	{
		for ( int row = 16; row < 48; row++ )
		{
			for ( int col = 16; col < 48; col++ )
			{
				storage.HeightMap[row * 64 + col] = ushort.MaxValue;
			}
		}
	}

	/// <summary>
	/// CompactTerrainMaterial packs base texture id (5 bits), overlay texture id (5 bits),
	/// blend factor (8 bits) and the hole flag (1 bit) into one uint: the packed value is
	/// bit-exact, a value round trips through the packed constructor, field writes clip to
	/// their bit width without disturbing neighboring fields, and material 0 packs to zero.
	/// </summary>
	[TestMethod]
	public void CompactMaterialBitPacking()
	{
		var material = new CompactTerrainMaterial( baseTextureId: 3, overlayTextureId: 5, blendFactor: 200, isHole: true );

		Assert.AreEqual( (byte)3, material.BaseTextureId );
		Assert.AreEqual( (byte)5, material.OverlayTextureId );
		Assert.AreEqual( (byte)200, material.BlendFactor );
		Assert.IsTrue( material.IsHole );
		Assert.AreEqual( 3u | (5u << 5) | (200u << 10) | (1u << 18), material.Packed, "Packed layout is base|overlay<<5|blend<<10|hole<<18" );

		var unpacked = new CompactTerrainMaterial( material.Packed );
		Assert.AreEqual( (byte)3, unpacked.BaseTextureId, "Packed value should round trip" );
		Assert.AreEqual( (byte)5, unpacked.OverlayTextureId );
		Assert.AreEqual( (byte)200, unpacked.BlendFactor );
		Assert.IsTrue( unpacked.IsHole );

		unpacked.BaseTextureId = 255;
		Assert.AreEqual( (byte)31, unpacked.BaseTextureId, "Base texture id is 5 bits, writes clip to 31" );
		Assert.AreEqual( (byte)5, unpacked.OverlayTextureId, "Writing one field should not disturb the others" );
		Assert.IsTrue( unpacked.IsHole );

		Assert.AreEqual( 0u, new CompactTerrainMaterial( 0 ).Packed, "Material 0 packs to zero" );
	}

	/// <summary>
	/// A programmatically created TerrainStorage starts as a 512 resolution terrain that is
	/// 20000 units across and 10000 units tall, with full-size height and control maps -
	/// the control map filled with packed material 0 (zero) and no materials. SetResolution
	/// reallocates both maps to the new square size.
	/// </summary>
	[TestMethod]
	public void StorageDefaultsAndResolution()
	{
		var storage = new TerrainStorage();

		Assert.AreEqual( 512, storage.Resolution, "Default resolution is pinned" );
		Assert.AreEqual( 20000.0f, storage.TerrainSize );
		Assert.AreEqual( 10000.0f, storage.TerrainHeight );
		Assert.AreEqual( 512 * 512, storage.HeightMap.Length );
		Assert.AreEqual( 512 * 512, storage.ControlMap.Length );
		Assert.AreEqual( (ushort)0, storage.HeightMap[0], "Heights start flat at zero" );
		Assert.AreEqual( 0u, storage.ControlMap[0], "Control map starts as packed material 0" );
		Assert.AreEqual( 0, storage.Materials.Count, "No materials by default" );
		Assert.IsNotNull( storage.MaterialSettings );
		Assert.IsTrue( storage.MaterialSettings.HeightBlendEnabled, "Height blending defaults on" );
		Assert.AreEqual( 0.87f, storage.MaterialSettings.HeightBlendSharpness, 0.001f );

		storage.SetResolution( 64 );

		Assert.AreEqual( 64, storage.Resolution );
		Assert.AreEqual( 64 * 64, storage.HeightMap.Length, "SetResolution reallocates the height map" );
		Assert.AreEqual( 64 * 64, storage.ControlMap.Length, "SetResolution reallocates the control map" );
	}

	/// <summary>
	/// TerrainMaterialSettings raises OnChanged only when a property actually changes
	/// value - writing the current value back is silent for the bool, the float and
	/// the sampler state.
	/// </summary>
	[TestMethod]
	public void StorageMaterialSettingsChangeEvents()
	{
		var storage = new TerrainStorage();
		var changes = 0;
		storage.MaterialSettings.OnChanged += () => changes++;

		storage.MaterialSettings.HeightBlendEnabled = true;
		Assert.AreEqual( 0, changes, "Writing the same bool value should not notify" );

		storage.MaterialSettings.HeightBlendEnabled = false;
		Assert.AreEqual( 1, changes, "Changing the bool should notify" );

		storage.MaterialSettings.HeightBlendSharpness = 0.87f;
		Assert.AreEqual( 1, changes, "Writing the same float value should not notify" );

		storage.MaterialSettings.HeightBlendSharpness = 0.25f;
		Assert.AreEqual( 2, changes, "Changing the float should notify" );
	}

	/// <summary>
	/// A programmatic TerrainMaterial has pinned defaults - the default source images, unit
	/// scales and no surface. NoTiling is the only flag source, HasHeightTexture flips once
	/// the height image differs from the default, and the materials list on the storage is
	/// plain in-memory state.
	/// </summary>
	[TestMethod]
	public void StorageMaterialsListState()
	{
		var storage = new TerrainStorage();
		var material = new TerrainMaterial();

		Assert.AreEqual( "materials/default/default_color.tga", material.AlbedoImage, "Default albedo image is pinned" );
		Assert.AreEqual( 1.0f, material.UVScale );
		Assert.AreEqual( 0.0f, material.Metalness );
		Assert.AreEqual( 1.0f, material.NormalStrength );
		Assert.AreEqual( 1.0f, material.HeightBlendStrength );
		Assert.AreEqual( 0.0f, material.DisplacementScale );
		Assert.IsFalse( material.NoTiling );
		Assert.AreEqual( TerrainFlags.None, material.Flags );
		Assert.IsFalse( material.HasHeightTexture, "The default height image does not count as a height texture" );
		Assert.IsNull( material.Surface );
		Assert.IsNull( material.BCRTexture, "Generated textures only exist for materials loaded from disk" );
		Assert.IsNull( material.NHOTexture );

		material.NoTiling = true;
		Assert.AreEqual( TerrainFlags.NoTile, material.Flags, "NoTiling maps onto the NoTile flag" );

		material.HeightImage = "materials/custom/height.tga";
		Assert.IsTrue( material.HasHeightTexture, "A non-default height image enables displacement" );

		storage.Materials.Add( material );
		Assert.AreEqual( 1, storage.Materials.Count );
		Assert.AreSame( material, storage.Materials[0] );
	}

	/// <summary>
	/// TerrainStorage uses its own save format: the height and control maps serialize into
	/// a binary blob sidecar (BinaryData) referenced from the JSON by a $blob guid, while
	/// the scalar settings live in the JSON. Serialize + LoadFromJson with the captured
	/// blob bytes reproduces resolution, sizes, settings and bit-exact map contents.
	/// </summary>
	[TestMethod]
	public void StorageBlobSerializationRoundTrip()
	{
		var storage = new TerrainStorage();
		storage.SetResolution( 32 );
		storage.TerrainSize = 4321.0f;
		storage.TerrainHeight = 765.0f;
		storage.MaterialSettings.HeightBlendEnabled = false;
		storage.MaterialSettings.HeightBlendSharpness = 0.25f;

		for ( int i = 0; i < storage.HeightMap.Length; i++ )
		{
			storage.HeightMap[i] = (ushort)((i * 7) % 65536);
			storage.ControlMap[i] = (uint)(i * 13);
		}

		var json = storage.Serialize().ToJsonString();
		var blob = storage.BinaryData;

		Assert.IsNotNull( blob, "Serializing should capture the maps into binary blob data" );
		Assert.IsTrue( blob.Length > 0 );
		Assert.IsTrue( json.Contains( "$blob" ), "The JSON should reference the maps by blob guid" );

		var loaded = new TerrainStorage();
		loaded.BinaryData = blob;
		loaded.LoadFromJson( json );

		Assert.AreEqual( 32, loaded.Resolution );
		Assert.AreEqual( 4321.0f, loaded.TerrainSize );
		Assert.AreEqual( 765.0f, loaded.TerrainHeight );
		Assert.IsFalse( loaded.MaterialSettings.HeightBlendEnabled );
		Assert.AreEqual( 0.25f, loaded.MaterialSettings.HeightBlendSharpness, 0.001f );
		Assert.AreEqual( 0, loaded.Materials.Count );
		Assert.IsTrue( loaded.HeightMap.SequenceEqual( storage.HeightMap ), "Height map should round trip bit-exact through the blob" );
		Assert.IsTrue( loaded.ControlMap.SequenceEqual( storage.ControlMap ), "Control map should round trip bit-exact through the blob" );
	}

	/// <summary>
	/// Terrain component defaults and property clamps, exercised on a disabled component so
	/// no scene object or GPU resources are touched: it is always a concave static collider,
	/// shadows default to Off (unlike ModelRenderer), the clipmap properties clamp to their
	/// documented ranges except SubdivisionLodCount which is unclamped, and the size
	/// properties read zero / ignore writes until a storage is assigned, after which they
	/// write through to the storage.
	/// </summary>
	[TestMethod]
	public void ComponentDefaultsAndClampsWhileDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );

		Assert.IsTrue( terrain.IsConcave, "Terrain is always concave" );
		Assert.IsTrue( terrain.Static, "Concave colliders are forced static" );
		terrain.Static = false;
		Assert.IsTrue( terrain.Static, "The static flag cannot be turned off on a concave collider" );

		Assert.IsNull( terrain.Storage );
		Assert.IsTrue( terrain.EnableCollision );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.Off, terrain.RenderType, "Terrain defaults to not casting shadows" );
		Assert.AreEqual( 6, terrain.ClipMapLodLevels );
		Assert.AreEqual( 256, terrain.ClipMapLodExtentTexels );
		Assert.AreEqual( 1, terrain.SubdivisionFactor );

		Assert.AreEqual( 0.0f, terrain.TerrainSize, "Size reads zero with no storage" );
		Assert.AreEqual( 0.0f, terrain.TerrainHeight );
		terrain.TerrainSize = 5000.0f;
		Assert.AreEqual( 0.0f, terrain.TerrainSize, "Size writes are ignored with no storage" );

		terrain.ClipMapLodLevels = 0;
		Assert.AreEqual( 1, terrain.ClipMapLodLevels, "LOD levels clamp to a minimum of 1" );
		terrain.ClipMapLodLevels = 12;
		Assert.AreEqual( 8, terrain.ClipMapLodLevels, "LOD levels clamp to a maximum of 8" );

		terrain.ClipMapLodExtentTexels = 8;
		Assert.AreEqual( 16, terrain.ClipMapLodExtentTexels, "Extent texels clamp to a minimum of 16" );
		terrain.ClipMapLodExtentTexels = 4096;
		Assert.AreEqual( 2048, terrain.ClipMapLodExtentTexels, "Extent texels clamp to a maximum of 2048" );

		terrain.SubdivisionFactor = 0;
		Assert.AreEqual( 1, terrain.SubdivisionFactor, "Subdivision factor clamps to 1..4" );
		terrain.SubdivisionFactor = 9;
		Assert.AreEqual( 4, terrain.SubdivisionFactor );

		var storage = CreateSmallStorage();
		terrain.Storage = storage;

		Assert.AreEqual( 6400.0f, terrain.TerrainSize, "Size reads through to the storage" );
		Assert.AreEqual( 1000.0f, terrain.TerrainHeight );
		terrain.TerrainSize = 1234.0f;
		terrain.TerrainHeight = 777.0f;
		Assert.AreEqual( 1234.0f, storage.TerrainSize, "Size writes through to the storage" );
		Assert.AreEqual( 777.0f, storage.TerrainHeight );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Terrain component configuration - collision switch, clipmap settings, shadow mode,
	/// material override and inherited collider surface overrides - survives a GameObject
	/// serialize/deserialize round trip, including the disabled state. The storage was
	/// created in code so it has no resource path: it serializes as a null reference and
	/// the deserialized component comes back with no storage.
	/// </summary>
	[TestMethod]
	public void ComponentSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var mat = Material.Load( "materials/default/white.vmat" );

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = CreateSmallStorage();
		terrain.EnableCollision = false;
		terrain.ClipMapLodLevels = 4;
		terrain.ClipMapLodExtentTexels = 128;
		terrain.SubdivisionFactor = 2;
		terrain.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		terrain.MaterialOverride = mat;
		terrain.Friction = 0.25f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<Terrain>( true );

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a Terrain" );
		Assert.IsFalse( loaded.Enabled, "The disabled state should round trip" );
		Assert.IsNull( loaded.Storage, "A pathless programmatic storage serializes as null and does not survive" );
		Assert.IsFalse( loaded.EnableCollision );
		Assert.AreEqual( 4, loaded.ClipMapLodLevels );
		Assert.AreEqual( 128, loaded.ClipMapLodExtentTexels );
		Assert.AreEqual( 2, loaded.SubdivisionFactor );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.ShadowsOnly, loaded.RenderType );
		Assert.AreEqual( mat.Name, loaded.MaterialOverride?.Name, "Material override should round trip by path" );
		Assert.AreEqual( 0.25f, loaded.Friction );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Enabling a Terrain with a storage builds a heightfield physics shape on a static
	/// keyframe body. The collider's local bounds come from the heightfield content: the
	/// ground plane spans (resolution-1) x cell size on the two ground axes and the raised
	/// plateau reaches the full terrain height on the up axis. In the test host the render
	/// system is the empty device, so the clipmap scene object is never created - GpuBuffer
	/// handles are invalid and the buffer upload inside OnEnabled throws, which the enable
	/// callback batch swallows after the collider was already built - but the CPU-side
	/// height and control map textures do get created. Disabling tears down the shapes,
	/// the keyframe body and the texture maps.
	/// </summary>
	[TestMethod]
	public void EnableWithStorageBuildsHeightfieldCollider()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var storage = CreateSmallStorage();
		RaiseCenterPlateau( storage );

		var sceneObjectBaseline = scene.SceneWorld.SceneObjects.Count();

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = storage;

		Assert.AreEqual( 0, terrain.Shapes.Count, "No shapes while disabled" );

		terrain.Enabled = true;

		Assert.IsTrue( terrain.Active );
		Assert.AreEqual( 1, terrain.Shapes.Count, "Enabling should build one heightfield shape" );

		var shape = terrain.Shapes[0];
		Assert.IsTrue( shape.IsValid() );
		Assert.AreSame( terrain, shape.Collider, "The shape should link back to the terrain collider" );
		Assert.IsFalse( shape.IsTrigger );

		Assert.IsTrue( terrain.KeyBody.IsValid(), "Terrain collides through a keyframe body" );
		Assert.AreEqual( PhysicsBodyType.Static, terrain.KeyBody.BodyType, "A concave collider is a static body" );

		// Body-space heightfield bounds: 63 cells x 100 units on the ground axes, plateau
		// height of exactly TerrainHeight on the up axis (raw 65535 * 1000 / 65535).
		Assert.AreEqual( 0.0f, terrain.LocalBounds.Mins.x, 1.0f );
		Assert.AreEqual( 0.0f, terrain.LocalBounds.Mins.y, 1.0f );
		Assert.AreEqual( 0.0f, terrain.LocalBounds.Mins.z, 1.0f );
		Assert.AreEqual( 6300.0f, terrain.LocalBounds.Maxs.x, 1.0f, "Ground axis spans (resolution-1) * cell size" );
		Assert.AreEqual( 1000.0f, terrain.LocalBounds.Maxs.y, 1.0f, "The plateau reaches the full terrain height" );
		Assert.AreEqual( 6300.0f, terrain.LocalBounds.Maxs.z, 1.0f );

		Assert.AreEqual( sceneObjectBaseline + 1, scene.SceneWorld.SceneObjects.Count(), "The clipmap scene object is created even on the empty render device" );

		Assert.IsNotNull( terrain.HeightMap, "The CPU-side heightmap texture is created" );
		Assert.AreEqual( 64, terrain.HeightMap.Width );
		Assert.AreEqual( 64, terrain.HeightMap.Height );
		Assert.IsNotNull( terrain.ControlMap );

		scene.GameTick();

		Assert.IsTrue( shape.IsValid(), "The shape survives ticking" );

		terrain.Enabled = false;

		Assert.AreEqual( 0, terrain.Shapes.Count, "Disabling should destroy the shapes" );
		Assert.IsNull( terrain.KeyBody, "Disabling should destroy the keyframe body" );
		Assert.IsNull( terrain.HeightMap, "Disabling should dispose the heightmap texture" );
		Assert.IsNull( terrain.ControlMap );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// CPU height edits reach the collider in place: UpdateCollision pushes the new heights
	/// into the live heightfield shape and expands its cached local AABB so raised terrain is
	/// no longer culled by the broadphase or the height field's narrowphase early-out. The
	/// expansion is one-directional, so lowering does not retighten the bounds until the shape
	/// is rebuilt from storage by toggling EnableCollision.
	/// </summary>
	[TestMethod]
	public void HeightEditsReachColliderOnRebuild()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var storage = CreateSmallStorage();

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = storage;
		terrain.Enabled = true;

		Assert.AreEqual( 1, terrain.Shapes.Count );
		Assert.AreEqual( 0.0f, terrain.Shapes[0].LocalBounds.Maxs.y, 1.0f, "A flat terrain has no height extent" );
		Assert.AreEqual( 6300.0f, terrain.Shapes[0].LocalBounds.Maxs.x, 1.0f );

		// Raise the entire terrain to maximum height and push it into the live shape.
		for ( int i = 0; i < storage.HeightMap.Length; i++ )
		{
			storage.HeightMap[i] = ushort.MaxValue;
		}

		terrain.UpdateCollision( Terrain.SyncFlags.Height, new RectInt( 0, 0, 64, 64 ) );

		Assert.AreEqual( 1, terrain.Shapes.Count, "In-place updates keep the same shape" );
		Assert.AreEqual( 1000.0f, terrain.Shapes[0].LocalBounds.Maxs.y, 1.0f, "In-place height edits expand the shape's cached bounds to the raised heights" );

		terrain.EnableCollision = false;

		Assert.AreEqual( 0, terrain.Shapes.Count, "Turning collision off destroys the shapes" );

		terrain.EnableCollision = true;

		Assert.AreEqual( 1, terrain.Shapes.Count, "Turning collision back on rebuilds from storage" );
		Assert.AreEqual( 1000.0f, terrain.LocalBounds.Maxs.y, 1.0f, "The rebuilt shape sees the raised heights" );
		Assert.AreEqual( 1000.0f, terrain.LocalBounds.Mins.y, 1.0f, "A uniformly raised terrain has its floor at full height too" );
		Assert.AreEqual( 6300.0f, terrain.LocalBounds.Maxs.x, 1.0f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// RayIntersects casts against the CPU heightmap without needing the component enabled.
	/// The heightfield occupies half a cell to (resolution - 0.5) cells on the local X and Y
	/// axes with height along local Z: a downward ray over the raised plateau hits at the
	/// full terrain height, a ray over the untouched base hits at zero, and the reported
	/// position stays in terrain-local space even when the GameObject is moved.
	/// </summary>
	[TestMethod]
	public void RayIntersectsReportsLocalHeightfieldHits()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var storage = CreateSmallStorage();
		RaiseCenterPlateau( storage );

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = storage;

		// Straight down onto the middle of the plateau.
		var hitPlateau = terrain.RayIntersects( new Ray( new Vector3( 3200, 3200, 1500 ), Vector3.Down ), 4000.0f, out var plateauPosition );

		Assert.IsTrue( hitPlateau, "A downward ray over the plateau should hit" );
		Assert.AreEqual( 3200.0f, plateauPosition.x, 1.0f, "A vertical ray keeps its horizontal position" );
		Assert.AreEqual( 3200.0f, plateauPosition.y, 1.0f );
		Assert.AreEqual( 1000.0f, plateauPosition.z, 1.0f, "The plateau surface sits at the full terrain height" );

		// Straight down onto the flat base region away from the plateau.
		var hitBase = terrain.RayIntersects( new Ray( new Vector3( 800, 800, 1500 ), Vector3.Down ), 4000.0f, out var basePosition );

		Assert.IsTrue( hitBase, "A downward ray over the base should hit" );
		Assert.AreEqual( 800.0f, basePosition.x, 1.0f );
		Assert.AreEqual( 800.0f, basePosition.y, 1.0f );
		Assert.AreEqual( 0.0f, basePosition.z, 1.0f, "The untouched base sits at height zero" );

		// Moving the GameObject up moves the surface in world space, but the reported
		// position stays local to the terrain.
		go.WorldPosition = new Vector3( 0, 0, 500 );

		var hitElevated = terrain.RayIntersects( new Ray( new Vector3( 3200, 3200, 2500 ), Vector3.Down ), 4000.0f, out var elevatedPosition );

		Assert.IsTrue( hitElevated, "The ray accounts for the GameObject transform" );
		Assert.AreEqual( 1000.0f, elevatedPosition.z, 1.0f, "The hit position is local to the terrain, not world space" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// RayIntersects guard rails: no storage means a quiet miss, a ray cast away from the
	/// terrain misses, a non-positive resolution throws, and a heightmap whose length does
	/// not match resolution squared is rejected as invalid terrain data.
	/// </summary>
	[TestMethod]
	public void RayIntersectsGuardsAgainstBadStorage()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );

		Assert.IsFalse( terrain.RayIntersects( new Ray( new Vector3( 100, 100, 1000 ), Vector3.Down ), 2000.0f, out _ ), "No storage should be a quiet miss" );

		var storage = CreateSmallStorage();
		terrain.Storage = storage;

		Assert.IsFalse( terrain.RayIntersects( new Ray( new Vector3( 3200, 3200, 1500 ), Vector3.Up ), 4000.0f, out _ ), "A ray cast away from the terrain should miss" );

		storage.HeightMap = new ushort[16];

		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			terrain.RayIntersects( new Ray( new Vector3( 3200, 3200, 1500 ), Vector3.Down ), 4000.0f, out _ );
		}, "A heightmap that does not match resolution squared is invalid" );

		storage.SetResolution( 0 );

		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			terrain.RayIntersects( new Ray( new Vector3( 3200, 3200, 1500 ), Vector3.Down ), 4000.0f, out _ );
		}, "A non-positive resolution is rejected" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// FindClosestPoint resolves a query against the heightfield collider instead of falling
	/// back to the body origin. A point high above the centre plateau snaps straight down onto
	/// the plateau surface, keeping its horizontal position and landing at the full terrain
	/// height, and a point over the flat base snaps down to height zero - two distinct world
	/// queries return two distinct surface points rather than the same constant.
	/// </summary>
	[TestMethod]
	public void FindClosestPointSnapsToHeightfieldSurface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var storage = CreateSmallStorage();
		RaiseCenterPlateau( storage );

		var go = scene.CreateObject();
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = storage;
		terrain.Enabled = true;

		Assert.IsTrue( terrain.KeyBody.IsValid(), "The terrain needs a keyframe body to query against" );

		var overPlateau = terrain.FindClosestPoint( new Vector3( 3150, 5000, 3150 ) );

		Assert.AreEqual( 3150.0f, overPlateau.x, 75.0f, "The closest point keeps the query's ground position" );
		Assert.AreEqual( 1000.0f, overPlateau.y, 25.0f, "The closest point sits on the plateau surface, not the body origin" );
		Assert.AreEqual( 3150.0f, overPlateau.z, 75.0f );

		var overBase = terrain.FindClosestPoint( new Vector3( 800, 300, 800 ) );

		Assert.AreEqual( 800.0f, overBase.x, 75.0f );
		Assert.AreEqual( 0.0f, overBase.y, 25.0f, "The flat base sits at height zero" );
		Assert.AreEqual( 800.0f, overBase.z, 75.0f );

		Assert.AreNotEqual( overPlateau, overBase, "Distinct queries resolve to distinct surface points, not one constant" );

		go.Destroy();
		scene.ProcessDeletes();
	}
}
