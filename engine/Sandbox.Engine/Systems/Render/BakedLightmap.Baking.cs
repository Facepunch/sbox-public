using Editor;
using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox;

public sealed partial class BakedLightmap : ISceneCompile
{
	// GPULight, lightbinner_standard.h
	const int GpuLightSize = 156;

	// How far from a triangle a bilinear tap on it reaches. Charts are padded by more
	const float Reach = 1.42f;

	// Luxel in lightmap_bake_cs.shader
	[StructLayout( LayoutKind.Explicit, Size = 64 )]
	struct Luxel
	{
		[FieldOffset( 0 )] public Vector3 Position;

		// Where it is in the lightmap, x and y 16 bits each, for the live preview
		[FieldOffset( 12 )] public uint Pixel;

		[FieldOffset( 16 )] public Vector3 Normal;

		// A luxel across and down the surface
		[FieldOffset( 32 )] public Vector3 Du;
		[FieldOffset( 48 )] public Vector3 Dv;
	}

	// Tangent is the material's, with the side its bitangent is on
	readonly record struct BakeVertex( Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 Uv );

	// Surface in lightmap_bake_cs.shader
	readonly record struct BakeSurface( Vector4 Albedo, Vector4 Emission );

	// A traced triangle: its surface, and its corners in the lightmap with Uv0.x below 0 if it isn't in it
	readonly record struct BakeTriangle( uint Surface, Vector2 Uv0, Vector2 Uv1, Vector2 Uv2 );

	[StructLayout( LayoutKind.Sequential )]
	struct BakeLight
	{
		public Vector3 ToSun;
		public uint Channel;
		public float SunSize;
		public Vector3 Fog;
		public uint NoShadows;
	}

	// What a bake lights and the textures it lights it from, which it writes into as it traces. A probe volume's are its
	// ambient light, indices and strengths. With no volume they're the fog's: the light each probe scatters
	sealed record Lightmap( LightmapTextures Textures, Light[] Lights, (LightProbeVolume Volume, Texture[] Textures)[] Probes );

	// The bake that's running
	CancellationTokenSource running;

	/// <summary>
	/// The current bake stage and fraction of tracing passes completed. Null when the bake is idle.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public (string Stage, float Fraction)? BakeProgress { get; private set; }

	/// <summary>
	/// Cancel the bake that's running, if one is.
	/// </summary>
	public void Stop() => running?.Cancel();

	// What one buffer upload takes, 2GB of luxels, less room for probes. An 8192 lightmap fills half. Upload past 2GB to fill it
	const long MaxLuxels = 30_000_000;

	/// <summary>
	/// Bake every mesh and lightmapped model in the scene into one lightmap, saved beside the scene. Throws if it
	/// can't or it's cancelled.
	/// </summary>
	public Task Bake( CancellationToken ct = default ) => Bake( ct, null );

	Task Bake( CancellationToken ct, IProgressSection progress )
	{
		var folder = Scene.Editor?.GetSceneFolder() ?? throw new InvalidOperationException( "Save the scene before baking lightmaps." );
		return Bake( folder.WriteFile, ct, progress );
	}

	SceneCompileWork ISceneCompile.GetWork() => new( "Lightmap", "Bake lightmap", context => Bake( context.CancellationToken, context.Progress ) )
	{
		Outputs = BakedFiles(),
		Clear = _ => Clear()
	};

	// Lazy, so a finished bake reports the files it just wrote
	IEnumerable<string> BakedFiles()
	{
		Texture[] textures = [Textures?.Indices, Textures?.Strengths, Textures?.Irradiance, Textures?.Direction, Scene.Get<VolumetricFogController>()?.BakedFogTexture,
			.. Scene.GetAll<LightProbeVolume>().SelectMany( x => new[] { x.Texture, x.IndicesTexture, x.ScalarsTexture } )];

		foreach ( var path in textures.Select( x => x?.ResourcePath ).Where( x => x?.Contains( "_scene_data/lightmaps/" ) == true ) )
			yield return $"{path[(path.IndexOf( "_scene_data/" ) + "_scene_data/".Length)..]}_c";
	}

	/// <summary>
	/// Light the scene from a lightmap and index the lights baked into it. Its textures are saved through write, as they
	/// are or block compressed, or kept in memory without it.
	/// </summary>
	async Task Apply( Lightmap lightmap, Func<string, byte[], string> write, CancellationToken ct = default )
	{
		// Left to pick a format, a vtex compresses it, and light indices don't survive that
		// Probe and fog volumes stay uncompressed, a compressed volume streamed the wrong size on load. Compress them once that's fixed
		// Saved one at a time off the main thread, the live textures lighting the scene until all are. Save them at once if the wait matters
		async Task<Texture> Store( Texture texture, string name, ImageFormat? format = null )
		{
			if ( write is null ) return texture;

			var data = await texture.SaveToVtexAsync( format ?? texture.ImageFormat );
			ct.ThrowIfCancellationRequested();

			var path = Resource.FixPath( write( $"/lightmaps/{name}.vtex_c", data ) );
			g_pResourceSystem.ReloadResource( path );

			var saved = Texture.Load( path );
			if ( !saved.IsValid() || saved.IsError )
				throw new InvalidOperationException( $"Could not load the saved lightmap '{path}'." );

			saved.RefreshNativeDescription();
			return saved;
		}

		var live = lightmap.Textures;
		var textures = new LightmapTextures( await Store( live.Indices, "indices" ), await Store( live.Strengths, "strengths", ImageFormat.BC7 ),
			await Store( live.Irradiance, "irradiance", ImageFormat.BC6H ), await Store( live.Direction, "direction", ImageFormat.BC7 ) );

		var probes = new Texture[lightmap.Probes.Length][];
		for ( int i = 0; i < probes.Length; i++ )
		{
			var (volume, made) = lightmap.Probes[i];
			string[] names = volume is null ? ["fog"] : [$"probes{i}", $"probe_indices{i}", $"probe_strengths{i}"];
			probes[i] = new Texture[made.Length];
			for ( int t = 0; t < made.Length; t++ ) probes[i][t] = await Store( made[t], names[t] );
		}

		ct.ThrowIfCancellationRequested();

		foreach ( var light in Scene.GetAll<Light>() )
		{
			light.BakeLightIndex = Array.IndexOf( lightmap.Lights, light );
			if ( light is not DirectionalLight sun ) continue;

			sun.IncludeStaticShadowCasters = RealtimeSunShadows;
			sun.MixedShadows |= RealtimeSunShadows;
		}

		Textures = textures;

		for ( int i = 0; i < probes.Length; i++ )
		{
			var (volume, made) = (lightmap.Probes[i].Volume, probes[i]);

			if ( volume is null ) (Scene.Get<VolumetricFogController>() ?? new GameObject( Scene, true, "Volumetric Fog" ).Components.Create<VolumetricFogController>()).BakedFogTexture = made[0];
			else (volume.Texture, volume.IndicesTexture, volume.ScalarsTexture) = (made[0], made[1], made[2]);
		}
	}

	/// <summary>
	/// Remove the lightmaps and make every light dynamic again.
	/// </summary>
	public void Clear()
	{
		// One its scene left behind, loading another or leaving Lightmap mode, has nothing there to clear
		if ( Scene.Lightmap != this ) return;

		Scene.Editor?.MarkBakeReferencesChanged();

		// Disabled ones too, or they'd come back with their place in a bake that's gone
		foreach ( var light in Scene.Components.GetAll<Light>( FindMode.EverythingInSelfAndDescendants ) )
			light.BakeLightIndex = -1;

		(Textures, LuxelSize) = (null, 0);

		foreach ( var mesh in Scene.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ) )
			mesh.LightmapOffset = null;

		foreach ( var model in Scene.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			model.LightmapOffset = null;

		foreach ( var volume in Scene.Components.GetAll<LightProbeVolume>( FindMode.EverythingInSelfAndDescendants ) )
			(volume.Texture, volume.IndicesTexture, volume.ScalarsTexture) = (null, null, null);

		Scene.Get<VolumetricFogController>()?.BakedFogTexture = null;
		PreviewEnabled = true;
	}

	/// <summary>
	/// Place every mesh and lightmapped model in one lightmap and trace it, with static models casting shadows, lighting
	/// the scene from it as it traces. Its textures are saved at the end through write, which takes a file's name and
	/// data and returns its path, or kept in memory without it. Throws as the other Bake does.
	/// </summary>
	internal async Task Bake( Func<string, byte[], string> write, CancellationToken ct = default, IProgressSection progress = null )
	{
		if ( running is not null ) throw new InvalidOperationException( "A lightmap bake is already running." );

		var timer = System.Diagnostics.Stopwatch.StartNew();

		using var cancel = running = CancellationTokenSource.CreateLinkedTokenSource( ct, progress?.GetCancel() ?? default );
		using var done = new Utility.DisposeAction( () =>
		{
			BakeProgress = null;
			running = null;
		} );

		progress?.Title = "Baking Lightmap";
		progress?.TotalCount = (int)Quality;
		Report( "Unwrapping", 0 );

		void Report( string stage, int completed )
		{
			BakeProgress = (stage, completed / (float)Quality);
			progress?.Subtitle = stage;
			progress?.Current = completed;
		}

		var meshes = Scene.GetAll<MeshComponent>().Where( x => x.Mesh is not null && !x.HideInGame ).ToArray();
		var models = Scene.GetAll<ModelRenderer>().Where( x => x.IsBaked ).ToArray();
		var lit = models.Where( x => x.CanLightmap ).ToArray();
		if ( meshes.Length + lit.Length == 0 ) return;

		// Read on the main thread, so unwrapping needs nothing native or that an edit changes, and the editor keeps drawing through it
		var meshCorners = meshes.Select( x => x.Mesh.LightmapCorners( x.WorldScale ) ).ToArray();
		var litInfo = lit.Select( x => (x.Model, x.BodyGroups, x.LightmapLod, x.WorldScale) ).ToArray();
		foreach ( var model in litInfo )
			LightmapGeometry.Read( model.Model, model.BodyGroups, model.LightmapLod );

		// Search for the smallest luxels that still fit: jump to where the charts' area fills it, then bisect
		var maxSize = (int)Size;
		var luxelSize = float.MaxValue;
		var tooSmall = 0f;
		LightmapGeometry.Draw[][] geometry = null;
		Vector2Int[] corners = null;
		Vector2Int size = default;

		await System.Threading.Tasks.Task.Run( () =>
		{
			// A mesh's charts are the same whatever size a luxel is
			var meshCharts = new LightmapUnwrap.Charts[meshCorners.Length];
			Parallel.For( 0, meshCharts.Length, i => meshCharts[i] = LightmapUnwrap.Split( meshCorners[i].Corners, meshCorners[i].Faces ) );
			var room = Math.Min( (double)maxSize * maxSize, MaxLuxels );
			var luxel = 8f;
			for ( int attempt = 0; attempt < 16 && luxelSize > MathF.Max( 0.25f, tooSmall * 1.02f ); attempt++ )
			{
				cancel.Token.ThrowIfCancellationRequested();

				var sizes = new Vector2Int[meshCharts.Length + litInfo.Length];

				Parallel.For( 0, meshCharts.Length, i => sizes[i] = LightmapUnwrap.Size( meshCharts[i], luxel ) );
				Parallel.For( 0, litInfo.Length, i => sizes[meshCharts.Length + i] = LightmapGeometry.Size( litInfo[i].Model, litInfo[i].BodyGroups, litInfo[i].LightmapLod, litInfo[i].WorldScale, luxel ) );

				var packed = LightmapUnwrap.ShelfPack( sizes, out var packedSize );
				if ( packedSize.x <= maxSize && packedSize.y <= maxSize && (long)packedSize.x * packedSize.y <= MaxLuxels ) (luxelSize, corners, size) = (luxel, packed, packedSize);
				else tooSmall = luxel;

				var area = sizes.Sum( x => (double)x.x * x.y );
				var estimate = luxel * MathF.Sqrt( (float)(area / room) );
				luxel = MathF.Max( 0.25f, luxelSize == float.MaxValue ? MathF.Max( estimate, tooSmall * 1.25f )
					: tooSmall == 0 ? MathF.Min( estimate, luxelSize * 0.8f )
					: MathF.Sqrt( tooSmall * luxelSize ) );
			}

			// Only the size that fits is built
			if ( luxelSize == float.MaxValue ) return;

			geometry = new LightmapGeometry.Draw[litInfo.Length][];
			Parallel.For( 0, litInfo.Length, i => geometry[i] = LightmapGeometry.Unwrap( litInfo[i].Model, litInfo[i].BodyGroups, litInfo[i].LightmapLod, litInfo[i].WorldScale, luxelSize ) );
		}, cancel.Token );

		// Stopped while that ran, the scene may have moved on
		cancel.Token.ThrowIfCancellationRequested();

		// Whole blocks for the compressed textures
		size = new Vector2Int( (size.x + 3) & ~3, (size.y + 3) & ~3 );

		if ( geometry is null )
			throw new InvalidOperationException( $"The scene doesn't fit a {maxSize}x{maxSize} lightmap at any luxel size." );

		// The scene changes from here: stopped short it's left with no lightmap, and the live textures go once saved
		Lightmap lightmap = null;
		var finished = false;
		using var cleanup = new Utility.DisposeAction( () =>
		{
			if ( !finished ) Clear();
			if ( lightmap is null || finished && write is null ) return;

			var live = lightmap.Textures;
			foreach ( var texture in lightmap.Probes.SelectMany( x => x.Textures ).Concat( [live.Indices, live.Strengths, live.Irradiance, live.Direction] ) )
				texture.Dispose();
		} );

		// The last bake would draw wrong at its new place
		Clear();

		// The sun first, so it keeps the first channel
		var lights = Scene.GetAll<Light>()
			.Where( x => x.SceneObject.IsValid() && (x is DirectionalLight || x.GameObject.IsStatic) )
			.OrderBy( x => x is not DirectionalLight )
			.Take( 255 )
			.ToArray();

		// The surfaces a ray can hit, each the colour light bounces off it with and the light it gives off: its material's,
		// times the renderer's tint. A triangle's id is its place in them
		var bakeSurfaces = new List<BakeSurface>();
		var surfaceIds = new Dictionary<(Material, Color), uint>();

		uint SurfaceId( Material material, Color tint )
		{
			if ( surfaceIds.TryGetValue( (material, tint), out var surfaceId ) ) return surfaceId;

			var (albedo, emission) = MaterialColours( material );
			bakeSurfaces.Add( new BakeSurface( new Vector4( (albedo * (Vector3)tint).Clamp( 0, 1 ), 0 ), new Vector4( emission * (Vector3)tint, 0 ) ) );
			return surfaceIds[(material, tint)] = (uint)bakeSurfaces.Count - 1;
		}

		var vertices = new List<Vector3>();
		var luxels = new List<Luxel>();
		var tangents = new List<Vector4>();
		var sources = new List<int>();
		var nearest = new float[size.x * size.y];
		var slots = new int[size.x * size.y];
		Array.Fill( nearest, float.MaxValue );
		Array.Fill( slots, -1 );
		var toRasterize = new List<(BakeVertex, BakeVertex, BakeVertex)>();
		var bakeTriangles = new List<BakeTriangle>();

		void Add( Material material, Color tint, ModelRenderer.ShadowRenderType shadows, bool lit, BakeVertex[] points, int[] indices )
		{
			uint? surface = null;

			for ( int i = 0; i + 2 < indices.Length; i += 3 )
			{
				BakeVertex a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];

				// Glass lets light through. Wound to face the way its normals do, so a ray can tell a surface's back
				if ( shadows != ModelRenderer.ShadowRenderType.Off && material?.Flags.IsTranslucent != true )
				{
					var flip = Vector3.Dot( Vector3.Cross( b.Position - a.Position, c.Position - a.Position ), a.Normal + b.Normal + c.Normal ) < 0;
					vertices.Add( a.Position );
					vertices.Add( flip ? c.Position : b.Position );
					vertices.Add( flip ? b.Position : c.Position );
					bakeTriangles.Add( new BakeTriangle( surface ??= SurfaceId( material, tint ), lit ? a.Uv : new Vector2( -1 ), flip ? c.Uv : b.Uv, flip ? b.Uv : c.Uv ) );
				}

				if ( lit ) toRasterize.Add( (a, b, c) );
			}
		}

		LuxelSize = luxelSize;

		for ( int m = 0; m < meshes.Length; m++ )
		{
			var mesh = meshes[m];
			var transform = mesh.WorldTransform;
			mesh.LightmapOffset = corners[m];
			mesh.Mesh.SetSmoothingAngle( mesh.SmoothingAngle );

			foreach ( var submesh in mesh.Mesh.Triangulate() )
				Add( submesh.Material, mesh.Color, mesh.RenderType, true, submesh.Vertices.Select( v =>
					new BakeVertex( transform.PointToWorld( v.Position ), transform.Rotation * v.Normal, WorldTangent( transform, v.Tangent ), v.LightmapUv ) ).ToArray(), submesh.Indices );
		}

		foreach ( var model in models )
		{
			// A lightmapped model has its place in the lightmap, the rest only cast shadows into it
			var l = Array.IndexOf( lit, model );
			var draws = l >= 0 ? geometry[l] : LightmapGeometry.Read( model.Model, model.BodyGroups, model.LightmapLod );
			if ( draws is null ) continue;

			var offset = l >= 0 ? (Vector2)corners[meshes.Length + l] : default;
			if ( l >= 0 ) model.LightmapOffset = offset;

			var transform = model.WorldTransform;
			var swap = LightmapGeometry.MaterialSwap( model.Model, model.MaterialGroup );

			foreach ( var draw in draws )
				Add( model.MaterialOverride ?? swap( draw.Material ), model.Tint, model.RenderType, l >= 0, draw.Vertices.Select( ( v, i ) =>
					new BakeVertex( transform.PointToWorld( v.Position ), (transform.Rotation * (v.Normal / transform.Scale)).Normal, WorldTangent( transform, v.Tangent ), (draw.Uvs?[i] ?? default) + offset ) ).ToArray(), draw.Indices );
		}

		// Placing luxels and building the BVH don't touch each other's data, so they run at once
		var rasterized = System.Threading.Tasks.Task.Run( () => { foreach ( var (a, b, c) in toRasterize ) Rasterize( a, b, c, size, nearest, slots, luxels, tangents, sources ); }, cancel.Token );
		using var bvh = await System.Threading.Tasks.Task.Run( () => new Bvh( CollectionsMarshal.AsSpan( vertices ) ), cancel.Token );
		await rasterized;
		cancel.Token.ThrowIfCancellationRequested();

		if ( luxels.Count == 0 ) throw new InvalidOperationException( "Nothing in the scene has a surface to lightmap." );

		// A probe is six luxels in one place, facing along each axis
		var surfaces = luxels.Count;
		// Into the scene's probe volumes, or a new one around what was lightmapped
		var volumes = Scene.GetAll<LightProbeVolume>().ToList();
		if ( volumes.Count == 0 )
		{
			volumes.Add( new GameObject( Scene, true, "Light Probes" ).Components.Create<LightProbeVolume>() );
			FittedProbes = volumes[0].Id;
		}

		// The one a bake made is fitted again each bake, the scene may have grown
		if ( Scene.Directory.FindComponentByGuid( FittedProbes ) is LightProbeVolume fitted )
		{
			fitted.WorldTransform = global::Transform.Zero;
			fitted.Bounds = BBox.FromPoints( luxels.Select( x => x.Position ) ).Grow( ProbeSpacing );
		}

		// And for the fog, the box around every fog volume's corners, which VolumetricFog maps a baked fog texture over
		var grids = volumes.Select( x => (Volume: x, x.Bounds, Place: x.WorldTransform.WithScale( 1 )) ).ToList();
		var fogCorners = Scene.GetAll<VolumetricFogVolume>().SelectMany( x => x.Bounds.Corners.Select( c => x.WorldPosition + x.WorldRotation * c ) ).ToArray();
		if ( BakeFog && fogCorners.Length > 0 )
			grids.Add( (null, BBox.FromPoints( fogCorners ), global::Transform.Zero) );

		// Each grid's volume, none for the fog's, how many probes it is each way and its first luxel
		var probeSets = new (LightProbeVolume Volume, Vector3Int Size, int First)[grids.Count];

		for ( int v = 0; v < grids.Count; v++ )
		{
			var (volume, bounds, place) = grids[v];
			var probeSize = GetProbeGridSize( bounds.Size, volume is null, luxels.Count );
			var count = probeSize.x * probeSize.y * probeSize.z;
			probeSets[v] = (volume, probeSize, luxels.Count);

			for ( int p = 0; p < count; p++ )
			{
				var cell = new Vector3( p % probeSize.x, p / probeSize.x % probeSize.y, p / (probeSize.x * probeSize.y) ) + 0.5f;
				var position = place.PointToWorld( bounds.Mins + bounds.Size * cell / probeSize );

				foreach ( var axis in ProbeAxes )
					luxels.Add( new Luxel { Position = position, Normal = axis } );
			}
		}

		// Whole thread groups of 64, so every thread has a luxel and reaches the shader's group sync
		while ( luxels.Count % 64 != 0 ) luxels.Add( luxels[^1] );
		var placed = luxels.ToArray();

		// Padding takes the luxel inside the face beside it. Lit on the edge itself, its rays slip past the corner
		var froms = Enumerable.Range( 0, surfaces ).Select( i => sources[i] >= 0 && nearest[sources[i]] == 0 ? slots[sources[i]] : i ).ToArray();
		var lightmapped = lit.Where( ( x, i ) => geometry[i] is not null ).ToArray();

		using var luxelBuffer = new GpuBuffer<Luxel>( placed.Length );
		using var lightBuffer = new GpuBuffer( Math.Max( 1, lights.Length ), GpuLightSize );
		using var bakeLightBuffer = new GpuBuffer<BakeLight>( Math.Max( 1, lights.Length ) );
		using var strengthBuffer = new GpuBuffer<Vector4>( placed.Length );
		using var irradianceBuffer = new GpuBuffer<Vector4>( placed.Length );
		using var directionBuffer = new GpuBuffer<Vector4>( placed.Length );
		using var indexBuffer = new GpuBuffer<uint>( placed.Length );
		using var spreadBuffer = new GpuBuffer<Vector4>( placed.Length );

		// A layer of every luxel for each bounce, 16 bytes a luxel. Halve them to 8 if big lightmaps run out of memory
		using var bounceBuffer = new GpuBuffer<Vector4>( placed.Length * Math.Max( 1, Bounces ) );
		using var surfaceBuffer = new GpuBuffer<BakeSurface>( Math.Max( 1, bakeSurfaces.Count ) );
		using var triangleBuffer = new GpuBuffer<BakeTriangle>( Math.Max( 1, bakeTriangles.Count ) );
		using var slotBuffer = new GpuBuffer<int>( slots.Length );
		if ( bakeTriangles.Count > 0 ) triangleBuffer.SetData( CollectionsMarshal.AsSpan( bakeTriangles ) );
		slotBuffer.SetData( slots.AsSpan() );
		using var fromBuffer = new GpuBuffer<int>( surfaces );
		using var tangentBuffer = new GpuBuffer<Vector4>( surfaces );
		fromBuffer.SetData( froms.AsSpan() );
		tangentBuffer.SetData( CollectionsMarshal.AsSpan( tangents ) );

		luxelBuffer.SetData( placed.AsSpan() );
		strengthBuffer.Clear();
		irradianceBuffer.Clear();
		directionBuffer.Clear();
		spreadBuffer.Clear();
		bounceBuffer.Clear();

		if ( bakeSurfaces.Count > 0 )
			surfaceBuffer.SetData( CollectionsMarshal.AsSpan( bakeSurfaces ) );

		// A luxel holds four lights, the sun one of them. Past that two share a channel and only the stronger shows
		var crowded = lights.Length > 0 ? PackLights( lights, lightBuffer, bakeLightBuffer ) : 0;

		var shader = new ComputeShader( "lightmap_bake_cs" );
		var attributes = shader.Attributes;
		bvh.Bind( attributes );
		attributes.Set( "Luxels", luxelBuffer );
		attributes.Set( "Lights", lightBuffer );
		attributes.Set( "BakeLights", bakeLightBuffer );
		attributes.Set( "OutStrengths", strengthBuffer );
		attributes.Set( "OutIrradiance", irradianceBuffer );
		attributes.Set( "OutDirection", directionBuffer );
		attributes.Set( "Surfaces", surfaceBuffer );
		attributes.Set( "OcclusionDistance", AmbientOcclusion );
		attributes.Set( "OutIndices", indexBuffer );
		attributes.Set( "OutSpread", spreadBuffer );
		attributes.Set( "OutBounces", bounceBuffer );
		attributes.Set( "Bounces", Bounces );
		attributes.Set( "LuxelCount", placed.Length );
		attributes.Set( "LightCount", lights.Length );
		attributes.Set( "Triangles", triangleBuffer );
		attributes.Set( "Slots", slotBuffer );
		attributes.Set( "LightmapWidth", size.x );
		attributes.Set( "LightmapHeight", size.y );
		attributes.Set( "Froms", fromBuffer );
		attributes.Set( "Tangents", tangentBuffer );

		// A sky with no cube texture (a procedural one) lights nothing. Render the sky into a cube when one needs baking
		var sky = Scene.GetAll<SkyBox2D>().FirstOrDefault( x => x.SkyMaterial is not null );
		var skyTexture = sky?.SkyTexture;
		var hasSky = skyTexture.IsValid() && !skyTexture.IsError;
		using var noSky = hasSky ? null : Texture.CreateCube( 1, 1 ).WithData( new byte[24] ).Finish();
		var skyRotation = hasSky ? sky.WorldRotation : Rotation.Identity;

		attributes.Set( "SkyTexture", hasSky ? skyTexture : noSky );
		attributes.Set( "SkyTint", hasSky ? new Vector3( sky.Tint.r, sky.Tint.g, sky.Tint.b ) : Vector3.Zero );
		attributes.Set( "SkyForward", skyRotation * Vector3.Forward );
		attributes.Set( "SkyLeft", skyRotation * Vector3.Left );
		attributes.Set( "SkyUp", skyRotation * Vector3.Up );

		// Once, what's the same every pass. A luxel under another surface would bake black, or lit from its far side, and
		// bleed out along the join, so it moves out to that surface's open side. Then where in each luxel it can be lit
		// from, and the lights in its channels
		attributes.SetCombo( "D_STAGE", 2 );
		attributes.Set( "LuxelSize", luxelSize );

		for ( int i = 0; i < placed.Length; i += 1 << 20 )
		{
			attributes.Set( "LuxelOffset", i );
			shader.Dispatch( Math.Min( 1 << 20, placed.Length - i ), 1, 1 );
		}

		// A luxel's lights never change from here, so their textures are made now, as the bytes they're saved as
		var indices = new uint[placed.Length];
		indexBuffer.GetData( indices.AsSpan() );
		var texels = new uint[size.x * size.y];
		Array.Fill( texels, uint.MaxValue );
		for ( int i = 0; i < surfaces; i++ ) texels[(placed[i].Pixel >> 16) * size.x + (placed[i].Pixel & 0xFFFF)] = indices[froms[i]];

		// The rest the bake writes into as it traces. Half floats, the shader can't write a UAV of bytes
		Texture Live( float x, float y, float z, float w )
		{
			var filled = new byte[size.x * size.y * 8];
			MemoryMarshal.Cast<byte, (Half, Half, Half, Half)>( filled.AsSpan() ).Fill( ((Half)x, (Half)y, (Half)z, (Half)w) );
			return Texture.Create( size.x, size.y, ImageFormat.RGBA16161616F ).WithUAVBinding().WithData( filled ).Finish();
		}

		Texture LiveVolume( Vector3Int cells, int depth ) => Texture.CreateVolume( cells.x, cells.y, depth, ImageFormat.RGBA16161616F ).WithUAVBinding().WithData( new byte[cells.x * cells.y * depth * 8] ).Finish();
		Texture[] ProbeTextures( Vector3Int cells, int first ) => [LiveVolume( cells, cells.z * 6 ),
			Texture.CreateVolume( cells.x, cells.y, cells.z, ImageFormat.RGBA8888 ).WithData( MemoryMarshal.AsBytes( Enumerable.Range( 0, cells.x * cells.y * cells.z ).Select( p => indices[first + p * 6] ).ToArray().AsSpan() ).ToArray() ).Finish(),
			LiveVolume( cells, cells.z )];

		// No direction, all of it ambient and open
		lightmap = new Lightmap( new LightmapTextures( Texture.Create( size.x, size.y, ImageFormat.RGBA8888 ).WithData<uint>( texels ).Finish(), Live( 0, 0, 0, 0 ), Live( 0, 0, 0, 0 ), Live( 128 / 255f, 128 / 255f, 1, 1 ) ),
			lights, probeSets.Select( x => (x.Volume, x.Volume is null ? [LiveVolume( x.Size, x.Size.z )] : ProbeTextures( x.Size, x.First )) ).ToArray() );

		attributes.Set( "LiveStrengths", lightmap.Textures.Strengths );
		attributes.Set( "LiveIrradiance", lightmap.Textures.Irradiance );
		attributes.Set( "LiveDirection", lightmap.Textures.Direction );
		await Apply( lightmap, null );

		attributes.SetCombo( "D_STAGE", 0 );

		var frame = new System.Diagnostics.Stopwatch();
		var previewed = System.Diagnostics.Stopwatch.StartNew();

		// The editor's own frame, which a heavy scene can take well past any fixed limit just to draw
		var idle = double.MaxValue;
		for ( int i = 0; i < 4; i++ )
		{
			frame.Restart();
			await System.Threading.Tasks.Task.Delay( 1, cancel.Token );
			idle = Math.Min( idle, frame.Elapsed.TotalMilliseconds );
		}

		// Luxels traced a frame: more while tracing adds under 16ms to the editor's frame, fewer when it adds more
		var budget = 1 << 14;
		int pass = 0, next = 0;

		// What's traced so far into the textures, every luxel each time
		void Resolve()
		{
			attributes.Set( "Pass", pass );
			attributes.Set( "LuxelOffset", next );
			attributes.SetCombo( "D_STAGE", 1 );
			shader.Dispatch( surfaces, 1, 1 );

			for ( int i = 0; i < probeSets.Length; i++ )
			{
				var (volume, cells, first) = probeSets[i];
				var made = lightmap.Probes[i].Textures;
				attributes.SetCombo( "D_STAGE", volume is null ? 4 : 3 );
				attributes.Set( "ProbeFirst", first );
				attributes.Set( "ProbeSize", cells );
				attributes.Set( volume is null ? "LiveFog" : "LiveProbes", made[0] );
				if ( volume is not null ) attributes.Set( "LiveProbeStrengths", made[2] );
				shader.Dispatch( cells.x * cells.y * cells.z, 1, 1 );
			}

			attributes.SetCombo( "D_STAGE", 0 );
		}

		Report( "Tracing", 0 );
		while ( pass < (int)Quality )
		{
			var previousPass = pass;
			for ( var traced = 0; traced < budget && pass < (int)Quality; )
			{
				var count = Math.Min( budget - traced, placed.Length - next );
				attributes.Set( "Pass", pass );
				attributes.Set( "LuxelOffset", next );
				shader.Dispatch( count, 1, 1 );

				traced += count;
				next += count;
				if ( next < placed.Length ) continue;
				next = 0;
				pass++;
			}

			if ( pass != previousPass )
				Report( $"Tracing · {pass:n0} of {(int)Quality:n0} passes", pass );

			// Not Component.Task, which waits on scene time
			frame.Restart();
			await System.Threading.Tasks.Task.Delay( 1, cancel.Token );
			budget = Math.Clamp( (int)(budget * (frame.Elapsed.TotalMilliseconds > idle + 16 ? 0.7f : 1.25f)), 1 << 12, 1 << 22 ) & ~63;

			// Four times a second is live enough
			if ( previewed.ElapsedMilliseconds < 250 ) continue;
			previewed.Restart();
			Resolve();
		}

		if ( Denoise )
		{
			Report( "Denoising", pass );
			var strengths = new Vector4[placed.Length];
			var irradiance = new Vector4[placed.Length];
			var direction = new Vector4[placed.Length];
			var spread = new Vector4[placed.Length];
			strengthBuffer.GetData( strengths.AsSpan() );
			irradianceBuffer.GetData( irradiance.AsSpan() );
			directionBuffer.GetData( direction.AsSpan() );
			spreadBuffer.GetData( spread.AsSpan() );

			await System.Threading.Tasks.Task.Run( () => Blur( size, slots, nearest, placed, surfaces, luxelSize, pass, irradiance, direction, indices, strengths, spread ), cancel.Token );
			cancel.Token.ThrowIfCancellationRequested();

			strengthBuffer.SetData( strengths.AsSpan() );
			irradianceBuffer.SetData( irradiance.AsSpan() );
			directionBuffer.SetData( direction.AsSpan() );
		}

		Resolve();
		Report( "Saving textures", pass );
		await Apply( lightmap, write, cancel.Token );
		finished = true;

		// Saved since it started, the scene would be skipped by the next save. Not an edit, or a compile running it would abort
		Scene.Editor?.MarkBakeReferencesChanged();

		if ( crowded > 0 )
			Log.Warning( $"Lightmap: more than four baked lights reach the same place, the sun counted. Make some of them dynamic, shrink their radius or move them apart." );
		Log.Info( $"Traced a {size.x}x{size.y} lightmap: {surfaces:n0} luxels of {luxelSize:0.##} units, {probeSets.Sum( x => x.Size.x * x.Size.y * x.Size.z ):n0} probes, {meshes.Length} meshes, {lightmapped.Length} of {models.Length} models, {lights.Length} lights, {vertices.Count / 3:n0} triangles, {timer.Elapsed.TotalSeconds:0.0}s" );
	}

	static readonly Vector3[] ProbeAxes = [Vector3.Forward, Vector3.Left, Vector3.Up, Vector3.Backward, Vector3.Right, Vector3.Down];

	internal Vector3Int GetProbeGridSize( Vector3 extent, bool fog, int placedLuxels )
	{
		// Fit whole cells so adjacent probes are at least ProbeSpacing units apart.
		var cells = extent / ProbeSpacing;
		const int maxVolumeDimension = 2048;
		var layers = fog ? 1 : ProbeAxes.Length;
		if ( !cells.IsFinite || cells.x >= maxVolumeDimension + 1 || cells.y >= maxVolumeDimension + 1 || cells.z >= maxVolumeDimension / layers + 1 )
			throw new InvalidOperationException( "The probe grid exceeds the volume texture dimensions. Increase Probe Spacing or reduce the volume bounds." );

		var size = (Vector3Int)Vector3.Max( cells, 1 );
		var count = (long)size.x * size.y * size.z;

		// The resolve dispatch has 64 probes per group. Tracing needs six luxels per probe, including fog.
		if ( count > 65535L * 64 || placedLuxels + count * ProbeAxes.Length > MaxLuxels )
			throw new InvalidOperationException( "The probe grid is too large to bake. Increase Probe Spacing or reduce the volume bounds." );

		return size;
	}

	// The sun's radius in radians at no shadow hardness, about 5 degrees across
	const float MaxSunSize = 0.045f;

	/// <summary>
	/// Upload the lights as the renderer packs them, with the lightmap channel each one writes to. Returns how many
	/// had to share a channel with a light they overlap.
	/// </summary>
	static unsafe int PackLights( Light[] lights, GpuBuffer lightBuffer, GpuBuffer<BakeLight> bakeLightBuffer )
	{
		var packed = new byte[lights.Length * GpuLightSize];
		var bakeLights = new BakeLight[lights.Length];
		var channels = new int[lights.Length];
		var crowded = 0;

		fixed ( byte* data = packed )
		{
			for ( int i = 0; i < lights.Length; i++ )
			{
				// How far into this light's reach each channel's lights come. It takes the first free channel, or shares the
				// one it clashes with least
				// Reach is a sphere, so lights that never light the same surface can still clash. Count the luxels they share to fix that
				var clash = new float[4];
				for ( int j = 0; j < i; j++ )
					clash[channels[j]] = MathF.Max( clash[channels[j]], Overlap( lights[i], lights[j] ) );

				channels[i] = Array.IndexOf( clash, clash.Min() );
				if ( clash[channels[i]] > 0 ) crowded++;

				// Over 4 pi, the phase function a light's fog goes through averages out
				var colour = lights[i].SceneObject.LightColor;
				var fog = lights[i].FogMode == Light.FogInfluence.Disabled ? Vector3.Zero : new Vector3( colour.r, colour.g, colour.b ) * lights[i].FogStrength / (4 * MathF.PI);

				RenderTools.PackSceneLight( lights[i].SceneObject, (IntPtr)(data + i * GpuLightSize), IntPtr.Zero );
				bakeLights[i] = new BakeLight { ToSun = -lights[i].WorldRotation.Forward, Channel = (uint)channels[i], Fog = fog, SunSize = MaxSunSize * (1.0f - Math.Clamp( lights[i].ShadowHardness, 0.0f, 1.0f )), NoShadows = lights[i].Shadows ? 0u : 1u };
			}
		}

		lightBuffer.SetData<byte>( packed );
		bakeLightBuffer.SetData( bakeLights.AsSpan() );
		return crowded;
	}

	// How far two lights' reaches run into each other, 0 if they don't
	static float Overlap( Light a, Light b )
	{
		if ( a is DirectionalLight || b is DirectionalLight ) return float.MaxValue;
		return MathF.Max( 0, a.SceneObject.Radius + b.SceneObject.Radius - a.WorldPosition.Distance( b.WorldPosition ) );
	}

	/// <summary>
	/// A material's colour, the average of its first texture times its tints, and the light its self illumination gives off.
	/// </summary>
	static (Vector3 Albedo, Vector3 Emission) MaterialColours( Material material )
	{
		var colour = new Vector3( 0x80 / 255.0f );
		var emission = Vector3.Zero;
		if ( material is null ) return (colour, emission);

		try
		{
			colour = (Average( material.FirstTexture ) ?? colour) * Tint( material, "g_flTintColor" ) * Tint( material, "g_vColorTint" );

			// Self illum as the shaders tell a light simulator of it, at its mask's average colour. Sample the mask where a ray hits if a small glowing part matters
			if ( Average( Texture.FromNative( material.native.GetTextureAttributeOrDefault( "LightSim_SelfIllumMaskTexture" ) ) ) is { } mask )
				emission = mask * colour * Tint( material, "g_vSelfIllumTint" ) * material.Flags.GetFloat( "LightSim_SelfIllumScale", 1 ) * MathF.Pow( 2, material.GetVector4( "g_flSelfIllumBrightness" ).x );
		}
		catch ( Exception e )
		{
			// It bounces grey rather than stopping the bake
			Log.Warning( $"Lightmap bake couldn't read {material.Name}'s colour: {e.Message}" );
		}

		return (colour, emission);
	}

	// Unset parameters read as zero, which no tint is
	static Vector3 Tint( Material material, string name )
	{
		var tint = material.GetVector4( name );
		return tint.x > 0 || tint.y > 0 || tint.z > 0 ? new Vector3( tint.x, tint.y, tint.z ) : Vector3.One;
	}

	// A texture's colour: the smallest mip that's streamed in, which Mips counts past
	static Vector3? Average( Texture texture )
	{
		if ( !texture.IsValid() || texture.IsError ) return null;

		var smallest = texture.GetPixels( g_pRenderDevice.GetTextureDesc( texture.native ).m_nNumMipLevels - 1 );
		if ( smallest is not { Length: > 0 } ) return null;

		return new Vector3( Linear( smallest.Average( x => x.r ) ), Linear( smallest.Average( x => x.g ) ), Linear( smallest.Average( x => x.b ) ) );

		static float Linear( double srgb ) => MathF.Pow( (float)srgb / 255.0f, 2.2f );
	}

	static Vector4 WorldTangent( Transform transform, Vector4 tangent ) => new( transform.Rotation * (new Vector3( tangent.x, tangent.y, tangent.z ) * transform.Scale), tangent.w );

	static readonly float[] blurKernel = [1, 4, 6, 4, 1];

	/// <summary>
	/// Smooth each luxel's indirect light and soft shadows with pairs of luxels either side of it on the same surface: inside
	/// a face, facing the same way, near its plane and no further off in the world than in the lightmap. A neighbour counts
	/// when it differs by no more than the two luxels' own noise explains, measured from the spread of their samples, and
	/// for less if it's the noisier. So a noisy bake is smoothed a lot, a clean one hardly at all, and edges that are
	/// really there stay. It's done three times, 1, 2 then 4 luxels apart.
	/// </summary>
	static void Blur( Vector2Int size, int[] slots, float[] nearest, Luxel[] luxels, int surfaces, float luxelSize, int samples, Vector4[] irradiance, Vector4[] direction, uint[] indices, Vector4[] strengths, Vector4[] strengthsSquared )
	{
		// How far off each luxel's summed light and shadows could be by chance: a sum of squares less the square of the sum
		var lightNoise = new float[surfaces];
		var shadowNoise = new float[surfaces];

		Parallel.For( 0, surfaces, i =>
		{
			var s = strengthsSquared[i];
			lightNoise[i] = MathF.Max( 0, direction[i].w - Brightness( irradiance[i] ) * Brightness( irradiance[i] ) / samples );
			shadowNoise[i] = MathF.Max( 0, s.x + s.y + s.z + s.w - strengths[i].LengthSquared / samples );
		} );

		var irradianceBefore = new Vector4[irradiance.Length];
		var directionBefore = new Vector4[direction.Length];
		var strengthsBefore = new Vector4[strengths.Length];
		var lightNoiseBefore = new float[surfaces];
		var shadowNoiseBefore = new float[surfaces];

		for ( int step = 1; step <= 4; step *= 2 )
		{
			irradiance.CopyTo( irradianceBefore, 0 );
			direction.CopyTo( directionBefore, 0 );
			strengths.CopyTo( strengthsBefore, 0 );
			lightNoise.CopyTo( lightNoiseBefore, 0 );
			shadowNoise.CopyTo( shadowNoiseBefore, 0 );

			Parallel.For( 0, surfaces, i =>
			{
				var luxel = luxels[i];
				var (x, y) = ((int)(luxel.Pixel & 0xFFFF), (int)(luxel.Pixel >> 16));
				var centre = blurKernel[2] * blurKernel[2];
				var (light, towards, total, lightSpread) = (irradianceBefore[i] * centre, directionBefore[i] * centre, centre, lightNoiseBefore[i] * centre * centre);
				var (shadow, shadowTotal, shadowSpread) = (strengthsBefore[i] * centre, centre, shadowNoiseBefore[i] * centre * centre);
				var brightness = Brightness( irradianceBefore[i] );

				int Near( int nx, int ny )
				{
					if ( nx < 0 || ny < 0 || nx >= size.x || ny >= size.y ) return -1;

					var n = slots[ny * size.x + nx];
					if ( n < 0 || nearest[ny * size.x + nx] > 0 ) return -1;

					var offset = luxels[n].Position - luxel.Position;
					if ( Vector3.Dot( luxels[n].Normal, luxel.Normal ) < 0.9f ) return -1;
					if ( MathF.Abs( Vector3.Dot( offset, luxel.Normal ) ) > luxelSize * 0.5f * step || offset.Length > luxelSize * 4 * step ) return -1;
					return n;
				}

				// In pairs either side of it. With one side only, a chart's edge takes on the light further in and its seam shows
				for ( int k = 13; k < 25; k++ )
				{
					var (dx, dy) = ((k % 5 - 2) * step, (k / 5 - 2) * step);
					var (a, b) = (Near( x + dx, y + dy ), Near( x - dx, y - dy ));
					if ( a < 0 || b < 0 ) continue;

					var weight = blurKernel[k % 5] * blurKernel[k / 5];
					for ( int side = 0; side < 2; side++ )
					{
						var n = side == 0 ? a : b;
						var difference = Brightness( irradianceBefore[n] ) - brightness;
						var w = weight * Trust( difference * difference, lightNoiseBefore[i], lightNoiseBefore[n] );
						light += irradianceBefore[n] * w;
						towards += directionBefore[n] * w;
						lightSpread += lightNoiseBefore[n] * w * w;
						total += w;

						// Only between luxels the same lights reach, each channel is a different one
						if ( indices[n] != indices[i] ) continue;
						w = weight * Trust( (strengthsBefore[n] - strengthsBefore[i]).LengthSquared, shadowNoiseBefore[i], shadowNoiseBefore[n] );
						shadow += strengthsBefore[n] * w;
						shadowSpread += shadowNoiseBefore[n] * w * w;
						shadowTotal += w;
					}
				}

				irradiance[i] = light / total;
				direction[i] = towards / total;
				strengths[i] = shadow / shadowTotal;
				lightNoise[i] = lightSpread / (total * total);
				shadowNoise[i] = shadowSpread / (shadowTotal * shadowTotal);
			} );
		}
	}

	// How many times the noise two luxels could differ by, squared, before they stop counting as the same
	const float NoiseTolerance = 4;

	static float Brightness( Vector4 v ) => v.x * 0.2126f + v.y * 0.7152f + v.z * 0.0722f;

	// How much a neighbour counts: fully when it differs by no more than the two's noise explains, and less if it's the noisier
	static float Trust( float differenceSquared, float noise, float neighbourNoise ) =>
		MathF.Exp( -differenceSquared / (NoiseTolerance * (noise + neighbourNoise) + 1e-12f) ) * MathF.Min( 1, (noise + 1e-12f) / (neighbourNoise + 1e-12f) );

	/// <summary>
	/// Give every luxel a triangle covers, or is near enough to be filtered with, a place on it to be lit from.
	/// </summary>
	static void Rasterize( BakeVertex a, BakeVertex b, BakeVertex c, Vector2Int size, float[] nearest, int[] slots, List<Luxel> luxels, List<Vector4> tangents, List<int> sources )
	{
		var (pa, na, ta, ua) = a;
		var (pb, nb, tb, ub) = b;
		var (pc, nc, tc, uc) = c;
		var area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
		if ( MathF.Abs( area ) < 1e-6f ) return;

		// How far the surface moves for a luxel across and a luxel down
		var du = ((pb - pa) * (uc.y - ua.y) - (pc - pa) * (ub.y - ua.y)) / area;
		var dv = ((pc - pa) * (ub.x - ua.x) - (pb - pa) * (uc.x - ua.x)) / area;

		var mins = Vector2.Min( ua, Vector2.Min( ub, uc ) );
		var maxs = Vector2.Max( ua, Vector2.Max( ub, uc ) );
		var x0 = Math.Max( 0, (int)MathF.Floor( mins.x - Reach ) );
		var y0 = Math.Max( 0, (int)MathF.Floor( mins.y - Reach ) );
		var x1 = Math.Min( size.x - 1, (int)MathF.Floor( maxs.x + Reach ) );
		var y1 = Math.Min( size.y - 1, (int)MathF.Floor( maxs.y + Reach ) );

		for ( int y = y0; y <= y1; y++ )
		{
			for ( int x = x0; x <= x1; x++ )
			{
				var pixel = y * size.x + x;
				if ( nearest[pixel] == 0 ) continue;

				var centre = new Vector2( x + 0.5f, y + 0.5f );
				var weights = ClosestWeights( centre, ua, ub, uc, area, out var distance );
				if ( distance >= Reach || distance >= nearest[pixel] ) continue;

				nearest[pixel] = distance;

				// Padding: the luxel a little way into the triangle from here
				var source = -1;
				if ( distance > 0 )
				{
					var closest = ua * weights.x + ub * weights.y + uc * weights.z;
					var inside = closest + (closest - centre) / distance * 0.75f;
					var sx = (int)MathF.Floor( inside.x );
					var sy = (int)MathF.Floor( inside.y );
					if ( sx >= 0 && sy >= 0 && sx < size.x && sy < size.y ) source = sy * size.x + sx;
				}

				var luxel = new Luxel
				{
					Position = pa * weights.x + pb * weights.y + pc * weights.z,
					Normal = (na * weights.x + nb * weights.y + nc * weights.z).Normal,
					Du = du,
					Dv = dv,
					Pixel = (uint)(x | y << 16),
				};

				var tangent = ta * weights.x + tb * weights.y + tc * weights.z;

				if ( slots[pixel] < 0 )
				{
					slots[pixel] = luxels.Count;
					luxels.Add( luxel );
					tangents.Add( tangent );
					sources.Add( source );
				}
				else
				{
					luxels[slots[pixel]] = luxel;
					tangents[slots[pixel]] = tangent;
					sources[slots[pixel]] = source;
				}
			}
		}
	}

	/// <summary>
	/// Barycentric weights of the point on a triangle nearest to a point, and how far away that is.
	/// </summary>
	static Vector3 ClosestWeights( Vector2 p, Vector2 a, Vector2 b, Vector2 c, float area, out float distance )
	{
		var v = ((p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y)) / area;
		var w = ((b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y)) / area;

		if ( v >= 0 && w >= 0 && v + w <= 1 )
		{
			distance = 0;
			return new Vector3( 1 - v - w, v, w );
		}

		distance = float.MaxValue;
		var weights = Vector3.Zero;
		Edge( p, a, b, new Vector3( 1, 0, 0 ), new Vector3( 0, 1, 0 ), ref distance, ref weights );
		Edge( p, b, c, new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ), ref distance, ref weights );
		Edge( p, c, a, new Vector3( 0, 0, 1 ), new Vector3( 1, 0, 0 ), ref distance, ref weights );
		return weights;

		static void Edge( Vector2 p, Vector2 from, Vector2 to, Vector3 fromWeights, Vector3 toWeights, ref float distance, ref Vector3 weights )
		{
			var along = to - from;
			var t = Math.Clamp( Vector2.Dot( p - from, along ) / along.LengthSquared, 0, 1 );
			var d = Vector2.Distance( p, from + along * t );
			if ( d >= distance ) return;

			distance = d;
			weights = fromWeights * (1 - t) + toWeights * t;
		}
	}
}
