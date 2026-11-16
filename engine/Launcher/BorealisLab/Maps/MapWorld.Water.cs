using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Resources;
using Borealis.Source2.Shaders;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// The map's water bodies, as the game makes them: a <c>WaterBody</c> component on a game object of the map's scene
/// (<c>maps/name/world.scene</c>) or of a prefab it instances, drawn as the component draws it. Its surface is the
/// component's clipmap (<c>WaterBody.SelectClipmap</c>, <c>BuildClipmap</c> and <c>BuildUnderside</c>) chosen for the view,
/// with the material's water shader and the attributes the component sets: the surface frame, waves, the clipmap, the sky
/// it reflects. Its refraction is a view of the world without the water, through the material's water fog, rendered
/// before the frame while the water is in view (<c>WaterBody.AddRefractionCapture</c>). Prefab instance patches,
/// caustics, overlays and the underwater effects aren't applied.
/// </summary>
internal sealed partial class MapWorld
{
	readonly List<MapWater> waters = new();

	/// <summary>Water bodies the map's scene places.</summary>
	public int WaterBodies => waters.Count;

	/// <summary>
	/// Every enabled <c>WaterBody</c> under the map's scene's game objects, prefab instances opened, each at its game
	/// object's world transform.
	/// </summary>
	void LoadWater( ContentFiles content, string map )
	{
		if ( SceneJson( content, $"maps/{map}/world.scene" ) is not { } scene ) return;
		if ( !scene.RootElement.TryGetProperty( "GameObjects", out var objects ) || objects.ValueKind != JsonValueKind.Array ) return;

		foreach ( var o in objects.EnumerateArray() ) AddWater( content, o, Transform.Zero, 0 );
		if ( waters.Count > 0 ) EntityClasses["WaterBody"] = waters.Count;
	}

	/// <summary>A compiled scene or prefab's JSON (its <c>DATA</c> block), or null.</summary>
	static JsonDocument SceneJson( ContentFiles content, string path )
	{
		if ( content.TryRead( path ) is not { } bytes ) return null;
		var text = Encoding.UTF8.GetString( ResourceFile.Read( bytes ).Block( "DATA" ) ).TrimEnd( '\0' );
		return text.Length == 0 ? null : JsonDocument.Parse( text );
	}

	void AddWater( ContentFiles content, JsonElement o, Transform parent, int depth )
	{
		if ( depth > 32 ) return;

		// A prefab instance is its prefab's root, placed where the instance says
		if ( o.TryGetProperty( "__Prefab", out var prefab ) && prefab.TryGetProperty( "Path", out var prefabPath ) && prefabPath.GetString() is { Length: > 0 } path )
		{
			using var file = SceneJson( content, path );
			if ( file is null || !file.RootElement.TryGetProperty( "RootObject", out var root ) )
			{
				Problems.Add( $"prefab {path} isn't readable" );
				return;
			}

			if ( !Bool( o, "Enabled", Bool( root, "Enabled", true ) ) ) return;
			var placed = parent.ToWorld( LocalTransform( o.TryGetProperty( "Position", out _ ) ? o : root ) );
			AddChildren( content, root, placed, depth );
			return;
		}

		if ( !Bool( o, "Enabled", true ) ) return;
		AddChildren( content, o, parent.ToWorld( LocalTransform( o ) ), depth );
	}

	void AddChildren( ContentFiles content, JsonElement o, Transform world, int depth )
	{
		if ( o.TryGetProperty( "Components", out var components ) && components.ValueKind == JsonValueKind.Array )
		{
			foreach ( var c in components.EnumerateArray() )
			{
				if ( Text( c, "__type" ) != "Sandbox.WaterBody" || !Bool( c, "__enabled", true ) ) continue;
				var water = new MapWater( this, c, world );
				if ( water.Object is null ) continue;
				World.Add( water.Object );
				waters.Add( water );
			}
		}

		if ( o.TryGetProperty( "Children", out var children ) && children.ValueKind == JsonValueKind.Array )
		{
			foreach ( var child in children.EnumerateArray() ) AddWater( content, child, world, depth + 1 );
		}
	}

	static Transform LocalTransform( JsonElement o )
	{
		var position = Numbers( o, "Position" ) is [var px, var py, var pz] ? new Vector3( px, py, pz ) : Vector3.Zero;
		var rotation = Numbers( o, "Rotation" ) is [var x, var y, var z, var w] ? new Rotation( x, y, z, w ) : Rotation.Identity;
		var scale = Numbers( o, "Scale" ) is [var sx, var sy, var sz] ? new Vector3( sx, sy, sz ) : Vector3.One;
		return new Transform( position, rotation, scale );
	}

	static string Text( JsonElement o, string name ) => o.TryGetProperty( name, out var v ) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	static bool Bool( JsonElement o, string name, bool fallback ) => o.TryGetProperty( name, out var v ) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

	static float Float( JsonElement o, string name, float fallback ) => o.TryGetProperty( name, out var v ) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

	/// <summary>A serialized vector or rotation, "x,y,z(,w)".</summary>
	static float[] Numbers( JsonElement o, string name ) => Text( o, name ) is { } text
		? text.Split( ',' ).Select( x => float.Parse( x, CultureInfo.InvariantCulture ) ).ToArray()
		: null;

	/// <summary>
	/// Before each frame: every water body's surface and attributes for the view, then the refraction of each in view.
	/// </summary>
	void UpdateWater( RenderView view )
	{
		if ( waters.Count == 0 ) return;

		// Which bodies the view sees, from its frustum
		var probe = waterCullView ??= new RenderView { Name = "Water culling" };
		probe.Position = view.Position;
		probe.Rotation = view.Rotation;
		probe.FieldOfView = view.FieldOfView;
		probe.ZNear = view.ZNear;
		probe.ZFar = view.ZFar;
		probe.Viewport = view.Viewport;
		probe.Update();

		var now = view.Time ?? (float)waterClock.Elapsed.TotalSeconds;
		foreach ( var water in waters ) water.Update( view.Position, now, probe.Frustum );
		foreach ( var water in waters )
		{
			if ( water.InView ) water.RenderRefraction( view, waterRefractionSystem ??= new RenderSystem(), System );
		}
	}

	RenderView waterCullView;
	RenderSystem waterRefractionSystem;
	readonly global::System.Diagnostics.Stopwatch waterClock = global::System.Diagnostics.Stopwatch.StartNew();

	void DisposeWater()
	{
		foreach ( var water in waters ) water.Free();
		waterRefractionSystem?.Dispose();
	}

	/// <summary>One water body: its surface object, its settings from the component and its refraction target.</summary>
	sealed class MapWater
	{
		/// <summary><c>WaterBody.WaveRatios</c> and <c>WaveAngles</c>: the four wave bands against the longest.</summary>
		static readonly float[] WaveRatios = [1f, 0.62f, 0.37f, 0.21f];
		static readonly float[] WaveAngles = [0f, 31f, -24f, 57f];
		static readonly StringToken[] WaveNames = [new( "Wave0" ), new( "Wave1" ), new( "Wave2" ), new( "Wave3" )];

		/// <summary><c>WaterBody.SurfaceTag</c>: the refraction view leaves the surface out.</summary>
		static readonly uint SurfaceTag = new StringToken( WaterBody.SurfaceTag ).Value;

		readonly MapWorld map;
		readonly BackendMaterial material;
		readonly Vector3 origin;
		readonly Rotation rotation;
		readonly Vector2 half;
		readonly bool ocean, waves;
		readonly float waveHeight, waveLength, waveWidth, waveSteepness, waveSpeed, detailSpeed;
		readonly double waveTimeOffset, detailTimeOffset;
		readonly Vector4[] bands = new Vector4[4];
		readonly bool reflectSky;
		readonly Dictionary<WaterBody.ClipmapLayout, RenderMesh> meshes = new();
		readonly float opacity;
		readonly FogSettings? fog;
		WaterBody.ClipmapLayout layout;
		ViewTarget refraction;

		public MeshObject Object { get; }
		public bool InView { get; private set; }

		public MapWater( MapWorld map, JsonElement component, Transform world )
		{
			this.map = map;
			var path = Text( component.TryGetProperty( "Material", out var m ) && m.ValueKind == JsonValueKind.Object ? m : default, "Path" );
			material = Load( path ) ?? Load( "materials/water/water.vmat" );
			if ( material is null )
			{
				map.Problems.Add( $"water material {path} isn't readable" );
				return;
			}

			origin = world.Position;
			rotation = world.Rotation;
			ocean = Bool( component, "Ocean", false );
			var size = Numbers( component, "Size" ) is [var sx, var sy] ? new Vector2( sx, sy ) : new Vector2( 2048 );
			half = new Vector2( MathF.Max( 0.05f, MathF.Abs( size.x * world.Scale.x ) * 0.5f ), MathF.Max( 0.05f, MathF.Abs( size.y * world.Scale.y ) * 0.5f ) );

			// WaterBody's wave settings, clamped as its properties clamp them
			waveHeight = Math.Clamp( Float( component, "WaveHeight", 16 ), 0, 256 );
			waves = Bool( component, "Waves", false ) && waveHeight > 0;
			waveLength = Math.Clamp( Float( component, "WaveLength", 600 ), 4, 8192 );
			waveWidth = Math.Clamp( Float( component, "WaveWidth", 600 ), 4, 8192 );
			var direction = Float( component, "WaveDirection", 0 ) % 360;
			waveSteepness = Math.Clamp( Float( component, "WaveSteepness", 0.5f ), 0, 1 );
			waveSpeed = Math.Clamp( Float( component, "WaveSpeed", 1 ), 0, 4 );
			waveTimeOffset = Float( component, "WaveTimeOffset", 0 );
			detailTimeOffset = Float( component, "DetailTimeOffset", 0 );
			for ( int i = 0; i < bands.Length; i++ ) bands[i] = WaterWaveMath.BuildBand( waveLength, waveWidth, direction, WaveRatios[i], WaveAngles[i] );

			detailSpeed = Math.Clamp( Value( "DetailSpeed", 0 ), 0, 10 );
			opacity = Math.Clamp( Value( "Opacity", 0 ), 0, 1 );
			fog = RefractionFog();
			reflectSky = (Text( component, "ReflectionEnvironment" ) ?? "Sky") == "Sky";

			// Placed as WaterBody places its surface; the clipmap is chosen for the first view
			layout = WaterBody.SelectClipmap( WaterBody.DetailLevel, waves, 1024, MathF.Max( half.x, half.y ), Math.Max( half.x, half.y ) * 2, default );
			Object = new MeshObject( Mesh( layout ), new Transform( origin, rotation ) )
			{
				CastShadows = false,
				Tags = [SurfaceTag],
				Attributes = map.Backend.CreateParameters(),
			};
			Update( origin + rotation.Up * 1024, 0, default );
		}

		BackendMaterial Load( string path )
			=> !string.IsNullOrEmpty( path ) && map.Backend.LoadMaterial( path ) is BackendMaterial m
				&& m.File?.Shader?.Replace( '\\', '/' ).StartsWith( "shaders/water.shader", StringComparison.OrdinalIgnoreCase ) == true ? m : null;

		/// <summary>A float or int of the material, or the fallback.</summary>
		float Value( string name, float fallback )
		{
			var file = material.File;
			if ( file.Floats.TryGetValue( name, out var f ) && float.IsFinite( f ) ) return f;
			return file.Ints.TryGetValue( name, out var i ) ? i : fallback;
		}

		Vector4 Vector( string name ) => material.File.Vectors.TryGetValue( name, out var v ) && v.Length >= 4 ? new Vector4( v[0], v[1], v[2], v[3] ) : default;

		/// <summary><c>WaterBody.RefractionFog</c>: the material's through-surface fog, which the refraction view sees.</summary>
		FogSettings? RefractionFog()
		{
			var depth = Vector( "WaterFogDepthRange" );
			float end = float.IsFinite( depth.y ) ? MathF.Max( depth.y, 0 ) : 0;
			float start = float.IsFinite( depth.x ) ? Math.Clamp( depth.x, 0, end ) : 0;
			float attenuation = Value( "WaterFogDistance", 0 ) != 0 ? Math.Clamp( Value( "WaterFogAttenuationDistance", 0 ), 0, 4096 ) : 0;
			if ( end <= 0 && attenuation <= 0 ) return null;

			var c = Vector( "WaterFogColor" );
			var color = float.IsFinite( c.x + c.y + c.z ) ? new Color( c.x, c.y, c.z, 1 ) : Color.Black;
			return new FogSettings
			{
				Mode = FogMode.Exponential,
				Color = color,
				DistanceEnabled = attenuation > 0,
				AttenuationDistance = attenuation,
				SunLightingEnabled = false,
				HeightEnabled = end > 0,
				BaseHeight = origin.z - end,
				HeightFalloff = end - start,
				HeightExponent = Math.Clamp( Value( "WaterFogDepthCurve", 0.5f ), 0.1f, 4 ),
				HeightColor = color,
			};
		}

		/// <summary>The surface for a clipmap: both sides in one buffer, the underside flipped (<c>WaterBody.RebuildMesh</c>).</summary>
		RenderMesh Mesh( WaterBody.ClipmapLayout clipmap )
		{
			if ( meshes.TryGetValue( clipmap, out var known ) ) return known;

			var (top, topIndices) = WaterBody.BuildClipmap( clipmap, half );
			var (under, underIndices) = WaterBody.BuildUnderside( top, topIndices );
			var vertices = new Vertex[top.Length * 2];
			top.CopyTo( vertices, 0 );
			under.CopyTo( vertices, top.Length );
			var indices = new uint[topIndices.Length * 2];
			for ( int i = 0; i < topIndices.Length; i++ )
			{
				indices[i] = (uint)topIndices[i];
				indices[topIndices.Length + i] = (uint)(underIndices[i] + top.Length);
			}

			float rise = MathF.Max( 0.1f, waves ? waveHeight : 0 );
			float horizontal = waves ? MathF.Max( 4, MathF.Max( waveLength, waveWidth ) ) * waveSteepness / MathF.Tau : 0;
			var bounds = new BBox( new Vector3( -half.x - horizontal, -half.y - horizontal, -rise ), new Vector3( half.x + horizontal, half.y + horizontal, rise ) );
			return meshes[clipmap] = BackendMesh.Create<Vertex>( map.Backend, vertices, VertexLayout, indices, bounds, material, "Water surface" );
		}

		/// <summary><see cref="Vertex"/> as the water shader reads it: position, normal, the cell in TEXCOORD0, the level and side in TEXCOORD1.</summary>
		static readonly global::Borealis.Backend.VertexLayout VertexLayout = new( Marshal.SizeOf<Vertex>(),
		[
			new( VertexSemantic.Position, 0, VertexFormat.Float3, (int)Marshal.OffsetOf<Vertex>( nameof( Vertex.Position ) ) ),
			new( VertexSemantic.Normal, 0, VertexFormat.Float3, (int)Marshal.OffsetOf<Vertex>( nameof( Vertex.Normal ) ) ),
			new( VertexSemantic.TexCoord, 0, VertexFormat.Float4, (int)Marshal.OffsetOf<Vertex>( nameof( Vertex.TexCoord0 ) ) ),
			new( VertexSemantic.TexCoord, 1, VertexFormat.Float4, (int)Marshal.OffsetOf<Vertex>( nameof( Vertex.TexCoord1 ) ) ),
			new( VertexSemantic.Tangent, 0, VertexFormat.Float4, (int)Marshal.OffsetOf<Vertex>( nameof( Vertex.Tangent ) ) ),
		] );

		Vector3 ToSurface( Vector3 world ) => rotation.Inverse * (world - origin);

		/// <summary>
		/// The surface and attributes for a camera at <paramref name="eye"/> at time <paramref name="now"/>
		/// (<c>WaterBody.UpdateSurface</c>), and whether <paramref name="frustum"/> sees it.
		/// </summary>
		public void Update( Vector3 eye, float now, in global::Borealis.Culling.ViewFrustum frustum )
		{
			// WaterBody.GetClipmap: the base cell follows the camera's distance to the nearest water
			var local = ToSurface( eye );
			float nearest = ocean ? MathF.Abs( local.z )
				: new Vector3( MathF.Max( 0, MathF.Abs( local.x ) - half.x ), MathF.Max( 0, MathF.Abs( local.y ) - half.y ), local.z ).Length;
			float reach = ocean ? 30000 : MathF.Max( MathF.Abs( local.x ) + half.x, MathF.Abs( local.y ) + half.y );
			float size = ocean ? float.PositiveInfinity : MathF.Max( half.x, half.y ) * 2;
			var next = WaterBody.SelectClipmap( WaterBody.DetailLevel, waves, nearest, reach, size, layout );
			if ( next != layout || Object.Mesh is null )
			{
				layout = next;
				Object.Mesh = Mesh( layout );
			}

			var a = Object.Attributes;
			a.Set( "Origin", origin );
			a.Set( "HalfSize", half );
			a.Set( "WaterOcean", ocean );
			a.Set( "DetailTime", (float)(now * detailSpeed + detailTimeOffset) );
			a.Set( "GridCamera", eye );
			a.Set( "ClipmapLevels", layout.Levels );
			a.Set( "ClipmapCells", layout.Cells );
			a.Set( "ClipmapBaseCell", layout.BaseCell );
			a.Set( "WaveTime", (float)(now * waveSpeed + waveTimeOffset) );
			a.Set( "WaveHeight", waves ? waveHeight : 0 );
			a.Set( "WaveSteepness", waveSteepness );
			for ( int i = 0; i < bands.Length; i++ ) a.Set( WaveNames[i], bands[i] );
			a.Set( "AxisX", rotation.Forward );
			a.Set( "AxisY", rotation.Left );
			a.Set( "SurfaceNormal", rotation.Up );
			SetReflection( a );
			a.Set( "ReflectionWaterHalfSize", half );
			a.Set( "ReflectionWaterInfinite", ocean );
			a.Set( "WaterOverlayCount", 0 );

			// The view sees the water when its bounds, waves included, are in the frustum
			var bounds = Object.Mesh.Bounds;
			var center = origin + rotation * bounds.Center;
			var extents = (bounds.Size * 0.5f).Abs();
			var rotated = new Vector3(
				MathF.Abs( rotation.Forward.x ) * extents.x + MathF.Abs( rotation.Left.x ) * extents.y + MathF.Abs( rotation.Up.x ) * extents.z,
				MathF.Abs( rotation.Forward.y ) * extents.x + MathF.Abs( rotation.Left.y ) * extents.y + MathF.Abs( rotation.Up.y ) * extents.z,
				MathF.Abs( rotation.Forward.z ) * extents.x + MathF.Abs( rotation.Left.z ) * extents.y + MathF.Abs( rotation.Up.z ) * extents.z );
			InView = frustum.Intersects( center, rotated );
		}

		/// <summary>
		/// <c>WaterBody.SetReflectionAttributes</c>: screen-space reflections (planar isn't drawn here) and the scene's sky as
		/// the environment they fall back to.
		/// </summary>
		void SetReflection( ShaderParameters a )
		{
			var lighting = map.World.Lighting;
			var sky = reflectSky ? lighting.SkyMaterial as BackendMaterial : null;
			var texture = sky is not null && sky.Textures.TryGetValue( "g_tSkyTexture", out var t ) ? t : null;
			var hasEnvironment = texture is { IsCube: true };
			a.Set( "HasEnvironment", hasEnvironment );
			if ( hasEnvironment ) a.Set( "EnvironmentTextureIndex", texture.BindlessIndex );
			a.Set( "EnvironmentTextureFormat", sky?.File?.Ints.TryGetValue( "F_TEXTURE_FORMAT2", out var format ) == true ? format : 0 );
			a.Set( "ReflectionMode", 2 );
			var tint = hasEnvironment ? lighting.SkyTint : Color.White;
			var axes = hasEnvironment ? lighting.SkyRotation : Rotation.Identity;
			a.Set( "EnvironmentTint", new Vector3( tint.r, tint.g, tint.b ) );
			a.Set( "EnvironmentAxisX", axes.Forward );
			a.Set( "EnvironmentAxisY", axes.Left );
			a.Set( "EnvironmentAxisZ", axes.Up );
		}

		/// <summary>
		/// <c>WaterBody.AddRefractionCapture</c> from above: the world without the water from the view's camera, through the
		/// material's water fog (cleared to its colour), into a full size float target the surface samples. A material
		/// that covers what's behind it (opacity 1) needs none.
		/// </summary>
		public void RenderRefraction( RenderView view, RenderSystem system, RenderSystem main )
		{
			var a = Object.Attributes;
			if ( opacity >= 1 || view.Viewport.Width < 1 || view.Viewport.Height < 1 )
			{
				a.Set( "HasRefractionTexture", false );
				return;
			}

			var size = new Vector2Int( (int)view.Viewport.Width, (int)view.Viewport.Height );
			if ( refraction is null || refraction.Size != size )
			{
				refraction?.Dispose();
				refraction = ViewTarget.Create( size, 1, TextureFormat.RGBA16161616F, system.DepthFormat, "Water refraction" );
			}

			var capture = refractionView ??= new RenderView { Name = "Water refraction", ExcludeTags = [SurfaceTag] };
			capture.Position = view.Position;
			capture.Rotation = view.Rotation;
			capture.FieldOfView = view.FieldOfView;
			capture.ZNear = view.ZNear;
			capture.ZFar = view.ZFar;
			capture.Time = view.Time;
			capture.ToolsVisMode = view.ToolsVisMode;
			capture.Viewport = new Rect( 0, 0, size.x, size.y );
			capture.Fog = fog;
			capture.ClearColor = fog?.Color ?? view.ClearColor;

			system.ParallelRecording = main.ParallelRecording;
			system.DepthPrepass = main.DepthPrepass;
			system.Render( map.World, capture, refraction, null );
			map.Backend.MakeReadable( refraction.Resolved );

			a.Set( "RefractionTexture", refraction.Resolved );
			a.Set( "RefractionViewport", new Vector4( 0, 0, 1, 1 ) );
			a.Set( "HasRefractionTexture", true );
			a.Set( "RefractionHasSceneFog", fog is null );
		}

		RenderView refractionView;

		/// <summary>Free the refraction target.</summary>
		public void Free() => refraction?.Dispose();
	}
}
