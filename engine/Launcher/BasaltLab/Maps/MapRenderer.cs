using Basalt.Gpu;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GpuDevice = Basalt.Gpu.Device;
using Matrix4x4 = System.Numerics.Matrix4x4;
using TextureBarrier = Basalt.Gpu.TextureBarrier;
using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;

namespace Sandbox.BasaltLab.Maps;

using CullMode = Basalt.Gpu.CullMode;
using VertexAttribute = Basalt.Gpu.VertexAttribute;

/// <summary>How a frame's draws are issued to Basalt.Gpu.</summary>
internal enum IssueMode
{
	/// <summary>Each draw uploads its constants and writes and binds a descriptor set of its own, and binds its buffers.</summary>
	PerDraw,

	/// <summary>Each material keeps a persistent set; objects are in one buffer, read by the instance index.</summary>
	MaterialObjects,

	/// <summary>Objects and materials in buffers, textures through the bindless set: one multi-draw indirect per pipeline.</summary>
	Indirect,
}

/// <summary>What one pass drew and bound.</summary>
internal struct PassStats
{
	public int Draws, Triangles, Materials, Pipelines, SetBinds, IndirectCalls;

	public void Add( in PassStats o )
	{
		Draws += o.Draws;
		Triangles += o.Triangles;
		Materials += o.Materials;
		Pipelines += o.Pipelines;
		SetBinds += o.SetBinds;
		IndirectCalls += o.IndirectCalls;
	}
}

/// <summary>A frame's CPU time by part, in milliseconds, and what it drew.</summary>
internal struct FrameTimes
{
	public double Wait, Prepare, Record, Submit, Total;

	/// <summary>GPU time of a frame three frames back, from timestamps: shadows, prepass, forward and translucent, the copy to the window, and all of it; NaN until known.</summary>
	public double GpuShadows, GpuPrepass, GpuForward, GpuCopy, Gpu;
}

/// <summary>A camera.</summary>
internal readonly record struct MapView( string Name, V3 Position, V3 LookAt, float FieldOfView = 80, float ZNear = 5, float ZFar = 30000 );

/// <summary>
/// Draws a <see cref="MapContent"/> on Basalt.Gpu with its own simple shader (<c>Shaders/map.hlsl</c>): sun shadow
/// cascades (depth only), a depth prepass, forward opaque and alpha tested, then translucent, into a multisampled HDR
/// target that's resolved and copied into the window. Everything is frustum culled and size culled per object or
/// aggregate draw, opaque draws are sorted by pipeline then material, translucent ones back to front.
/// </summary>
internal sealed unsafe class MapRenderer : IDisposable
{
	public const int MaxCascades = 3;

	readonly GpuDevice gpu;
	readonly MapContent map;

	public int Width { get; }
	public int Height { get; }
	public int Samples { get; }

	/// <summary>Sun shadow cascades drawn, 0 for none.</summary>
	public int Cascades { get; set; } = MaxCascades;

	public int CascadeSize { get; }
	public IssueMode Mode { get; set; } = IssueMode.MaterialObjects;

	/// <summary>With material objects, draw each run of draws sharing a pipeline and material as one multi-draw indirect.</summary>
	public bool MergeRuns { get; set; }

	/// <summary>Record shadows, the prepass and forward on worker threads into lists of their own.</summary>
	public bool Parallel { get; set; }

	/// <summary>Towards the sun: low in the north-east, so shadows fall away from both of SceneLab's Construct cameras.</summary>
	public V3 SunDirection { get; set; } = V3.Normalize( new V3( 0.55f, 0.45f, 0.5f ) );

	readonly TextureHandle color, depth, resolved, shadow;
	readonly SamplerHandle sampler, shadowSampler;
	readonly ShaderHandle[] vertexShaders = new ShaderHandle[3], pixelShaders = new ShaderHandle[3], depthShaders = new ShaderHandle[3];
	readonly BindingLayoutHandle[] set0 = new BindingLayoutHandle[3], set1 = new BindingLayoutHandle[3];

	// Pipelines by mode, then by kind
	readonly PipelineHandle[][] prepass = new PipelineHandle[3][], forward = new PipelineHandle[3][], shadows = new PipelineHandle[3][];
	readonly PipelineHandle[] translucent = new PipelineHandle[3];

	readonly BufferHandle materialTable, materialConstants;
	readonly int materialStride;

	public TextureHandle Resolved => resolved;

	public MapRenderer( GpuDevice gpu, MapContent map, int width, int height, int samples, int cascadeSize )
	{
		this.gpu = gpu;
		this.map = map;
		Width = width;
		Height = height;
		Samples = samples;
		CascadeSize = cascadeSize;

		color = gpu.CreateTexture( TextureInfo.RenderTarget( width, height, Format.RGBA16Float, samples, "Map colour" ) );
		depth = gpu.CreateTexture( TextureInfo.RenderTarget( width, height, Format.D32Float, samples, "Map depth" ) );
		resolved = gpu.CreateTexture( TextureInfo.RenderTarget( width, height, Format.RGBA16Float, 1, "Map resolved" ) );
		shadow = gpu.CreateTexture( new TextureInfo
		{
			Width = cascadeSize,
			Height = cascadeSize,
			Depth = 1,
			Layers = MaxCascades,
			Mips = 1,
			Samples = 1,
			Dimension = TextureDimension.Texture2DArray,
			Format = Format.D32Float,
			Usage = TextureUsage.DepthTarget | TextureUsage.Sampled,
			Name = "Map sun shadows",
		} );

		sampler = gpu.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap, 8 ) );
		var compare = SamplerInfo.Linear( AddressMode.Clamp );
		compare.Compare = CompareFunction.LessEqual;
		compare.MipFilter = Filter.Nearest;
		shadowSampler = gpu.CreateSampler( compare );

		// Material data: a table for the bindless variant, and constants per material for the material objects
		var materials = new MaterialData[map.Materials.Count];
		for ( int i = 0; i < materials.Length; i++ ) materials[i] = MaterialData.Of( gpu, map.Materials[i], gpu.SamplerIndex( sampler ) );
		materialTable = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( materials.Length, 1 ) * sizeof( MaterialData ), Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Map materials" } );
		gpu.WriteBuffer<MaterialData>( materialTable, materials );

		materialStride = Align( sizeof( MaterialData ), gpu.Capabilities.UniformAlignment );
		var constants = new byte[Math.Max( materials.Length, 1 ) * materialStride];
		for ( int i = 0; i < materials.Length; i++ ) MemoryMarshal.Write( constants.AsSpan( i * materialStride ), in materials[i] );
		materialConstants = gpu.CreateBuffer( new BufferInfo { Size = constants.Length, Usage = BufferUsage.Uniform | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Map material constants" } );
		gpu.WriteBuffer<byte>( materialConstants, constants );

		for ( int mode = 0; mode < 3; mode++ ) CreatePipelines( mode );
		timestamps = gpu.CreateTimestamps( Stamps * Slots );

		// Material objects: one persistent set each
		Span<Binding> bindings = stackalloc Binding[3];
		foreach ( var material in map.Materials )
		{
			bindings[0] = Binding.Uniform( 0, materialConstants, material.Index * materialStride, sizeof( MaterialData ) );
			bindings[1] = Binding.Sampled( 1, material.Texture, srgb: material.Textured );
			bindings[2] = Binding.SamplerState( 2, sampler );
			material.Set = gpu.CreateBindingSet( set1[(int)IssueMode.MaterialObjects], bindings );
		}
	}

	static int Align( int size, int alignment ) => (size + alignment - 1) / alignment * alignment;

	static byte[] Spirv( string name )
	{
		using var stream = typeof( MapRenderer ).Assembly.GetManifestResourceStream( name ) ?? throw new InvalidOperationException( $"{name} isn't embedded" );
		var data = new byte[stream.Length];
		stream.ReadExactly( data );
		return data;
	}

	void CreatePipelines( int mode )
	{
		vertexShaders[mode] = gpu.CreateShader( Spirv( $"map{mode}.vs.spv" ), $"map{mode} vs" );
		pixelShaders[mode] = gpu.CreateShader( Spirv( $"map{mode}.ps.spv" ), $"map{mode} ps" );
		depthShaders[mode] = gpu.CreateShader( Spirv( $"map{mode}.depth.spv" ), $"map{mode} depth" );

		var all = ShaderStages.Vertex | ShaderStages.Fragment;
		set0[mode] = gpu.CreateBindingLayout( mode switch
		{
			0 => [new BindingSlot( 0, BindingType.UniformBuffer, all )],
			1 => [new BindingSlot( 0, BindingType.UniformBuffer, all ), new BindingSlot( 1, BindingType.StorageBuffer, all )],
			_ => [new BindingSlot( 0, BindingType.UniformBuffer, all ), new BindingSlot( 1, BindingType.StorageBuffer, all ), new BindingSlot( 2, BindingType.StorageBuffer, all )],
		} );

		set1[mode] = mode == 2 ? default : gpu.CreateBindingLayout( [new BindingSlot( 0, BindingType.UniformBuffer, all ), new BindingSlot( 1, BindingType.SampledTexture, all ), new BindingSlot( 2, BindingType.Sampler, all )] );

		var sets = mode == 2 ? new[] { set0[mode] } : new[] { set0[mode], set1[mode] };
		VertexBufferLayout[] buffers = [new VertexBufferLayout( sizeof( MapVertex ) )];
		VertexAttribute[] attributes = [new VertexAttribute( 0, 0, VertexFormat.Float3, 0 ), new VertexAttribute( 1, 0, VertexFormat.Byte4Norm, 12 ), new VertexAttribute( 2, 0, VertexFormat.Float2, 16 )];
		SpecializationConstant[] alpha = [new SpecializationConstant( 0, 1 )];

		PipelineHandle Make( string name, bool pixel, bool alphaTest, bool lit, CullMode cull, int samples, Format[] colors, bool depthWrite, CompareFunction compare, bool bias, BlendState blend = default )
		{
			return gpu.CreateGraphicsPipeline( new GraphicsPipelineInfo
			{
				VertexShader = vertexShaders[mode],
				VertexEntry = "MainVs",
				FragmentShader = pixel ? (lit ? pixelShaders[mode] : depthShaders[mode]) : default,
				FragmentEntry = lit ? "MainPs" : "MainDepthPs",
				FragmentConstants = alphaTest ? alpha : null,
				Sets = sets,
				VertexBuffers = buffers,
				Attributes = attributes,
				Topology = PrimitiveTopology.TriangleList,
				CullMode = cull,
				FrontFace = FrontFace.CounterClockwise,
				DepthClamp = bias,
				DepthBias = bias,
				DepthTest = true,
				DepthWrite = depthWrite,
				DepthCompare = compare,
				Blend = [blend],
				ColorFormats = colors,
				DepthFormat = Format.D32Float,
				Samples = samples,
				Name = $"map{mode} {name}",
			} );
		}

		Format[] hdr = [Format.RGBA16Float];
		prepass[mode] = new PipelineHandle[4];
		forward[mode] = new PipelineHandle[4];
		shadows[mode] = new PipelineHandle[2];
		for ( int kind = 0; kind < 4; kind++ )
		{
			var alphaTest = (kind & 1) != 0;
			var cull = (kind & 2) != 0 ? CullMode.None : CullMode.Back;
			prepass[mode][kind] = Make( $"prepass {kind}", alphaTest, alphaTest, false, cull, Samples, [], true, CompareFunction.GreaterEqual, false );
			forward[mode][kind] = Make( $"forward {kind}", true, alphaTest, true, cull, Samples, hdr, false, CompareFunction.GreaterEqual, false );
		}

		shadows[mode][0] = Make( "shadow", false, false, false, CullMode.None, 1, [], true, CompareFunction.LessEqual, true );
		shadows[mode][1] = Make( "shadow alpha", true, true, false, CullMode.None, 1, [], true, CompareFunction.LessEqual, true );
		translucent[mode] = Make( "translucent", true, false, true, CullMode.None, Samples, hdr, false, CompareFunction.GreaterEqual, false, BlendState.AlphaBlend );
	}

	//
	// Data the shader reads
	//

	[StructLayout( LayoutKind.Sequential )]
	struct MaterialData
	{
		public V4 Color;
		public uint Texture;
		public uint Sampler;
		public float AlphaReference;
		public uint Pad;

		public static MaterialData Of( GpuDevice gpu, MapMaterial m, int sampler ) => new()
		{
			Color = m.Color,
			Texture = (uint)(gpu.BindlessIndex( m.Texture ) + (m.Textured ? 1 : 0)),
			Sampler = (uint)sampler,
			AlphaReference = m.Kind == SurfaceKind.AlphaTest ? m.AlphaReference : -1,
		};
	}

	[StructLayout( LayoutKind.Sequential )]
	struct InstanceData
	{
		public V4 Row0, Row1, Row2, Tint;
		public uint Material;
		public uint Pad0, Pad1, Pad2;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct DrawData
	{
		public InstanceData Object;
		public MaterialData Surface;
	}

	[InlineArray( 4 )]
	struct Matrices
	{
		Matrix4x4 element;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct ViewData
	{
		public Matrix4x4 WorldToProjection;
		public Matrices WorldToShadow;
		public V4 CameraPosition, CameraForward, SunDirection, SunColor, SkyColor, GroundColor, CascadeEnds, NormalOffsets;
		public uint ShadowMap, ShadowSampler, CascadeCount;
		public float ShadowTexel;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct IndirectCommand
	{
		public uint IndexCount, InstanceCount, FirstIndex;
		public int VertexOffset;
		public uint FirstInstance;
	}

	//
	// Culling
	//

	/// <summary>One draw that passed culling: its sort key, object and range.</summary>
	struct Visible
	{
		public ulong Key;
		public int Object;
		public MapDraw Draw;
		public int Kind;
		public int Instance;
	}

	sealed class VisibleList
	{
		public Visible[] Items = new Visible[4096];
		public ulong[] Keys = new ulong[4096];
		public int Count;

		public void Add( in Visible v )
		{
			if ( Count == Items.Length )
			{
				Array.Resize( ref Items, Count * 2 );
				Array.Resize( ref Keys, Count * 2 );
			}

			Items[Count] = v;
			Keys[Count++] = v.Key;
		}

		public void Sort() => Array.Sort( Keys, Items, 0, Count );
	}

	readonly VisibleList opaque = new(), blended = new();
	readonly VisibleList[] cascadeLists = [new(), new(), new()];

	/// <summary>Per pass: shadows (one per cascade), prepass, forward, translucent.</summary>
	public PassStats[] Passes { get; } = new PassStats[MaxCascades + 3];

	public static readonly string[] PassNames = ["Shadow 0", "Shadow 1", "Shadow 2", "Prepass", "Forward", "Translucent"];

	/// <summary>Objects tested and passed in the main view.</summary>
	public int ObjectsVisible { get; private set; }

	ViewData mainView;
	readonly ViewData[] cascadeViews = new ViewData[MaxCascades];
	int[] lodOf = [];

	static V4 Plane( V4 v ) => v / new V3( v.X, v.Y, v.Z ).Length();

	void Prepare( MapView view )
	{
		var forwardDir = V3.Normalize( view.LookAt - view.Position );
		var right = V3.Normalize( V3.Cross( forwardDir, V3.UnitZ ) );
		var up = V3.Cross( right, forwardDir );
		var tanX = MathF.Tan( view.FieldOfView * MathF.PI / 360 );
		var tanY = tanX * Height / Width;

		var viewMatrix = new Matrix4x4(
			right.X, up.X, forwardDir.X, 0,
			right.Y, up.Y, forwardDir.Y, 0,
			right.Z, up.Z, forwardDir.Z, 0,
			-V3.Dot( right, view.Position ), -V3.Dot( up, view.Position ), -V3.Dot( forwardDir, view.Position ), 1 );

		// Reverse Z: depth 1 at the near plane, 0 at the far one
		var n = view.ZNear;
		var f = view.ZFar;
		var projection = new Matrix4x4(
			1 / tanX, 0, 0, 0,
			0, 1 / tanY, 0, 0,
			0, 0, -n / (f - n), 1,
			0, 0, n * f / (f - n), 0 );

		var viewProjection = viewMatrix * projection;
		var m = viewProjection;
		var c0 = new V4( m.M11, m.M21, m.M31, m.M41 );
		var c1 = new V4( m.M12, m.M22, m.M32, m.M42 );
		var c2 = new V4( m.M13, m.M23, m.M33, m.M43 );
		var c3 = new V4( m.M14, m.M24, m.M34, m.M44 );
		Span<V4> planes = [Plane( c3 + c0 ), Plane( c3 - c0 ), Plane( c3 + c1 ), Plane( c3 - c1 ), Plane( c3 - c2 ), Plane( c2 )];

		mainView = new ViewData
		{
			WorldToProjection = viewProjection,
			CameraPosition = new V4( view.Position, 1 ),
			CameraForward = new V4( forwardDir, 0 ),
			SunDirection = new V4( SunDirection, 0 ),
			SunColor = new V4( 2.2f, 2.0f, 1.8f, 1 ),
			SkyColor = new V4( 0.32f, 0.38f, 0.48f, 1 ),
			GroundColor = new V4( 0.16f, 0.14f, 0.12f, 1 ),
			ShadowMap = (uint)gpu.BindlessIndex( shadow ),
			ShadowSampler = (uint)gpu.SamplerIndex( shadowSampler ),
			CascadeCount = (uint)Cascades,
			ShadowTexel = 1.0f / CascadeSize,
		};

		var objects = map.Objects;
		if ( lodOf.Length != objects.Count ) lodOf = new int[objects.Count];

		// Size culling as Basalt's (RenderView.SizeCullThreshold, 0.25% of the screen)
		var threshold = 0.0025f * tanX;
		var thresholdSquared = threshold * threshold;
		var pixelsPerUnit = Width / (2 * tanX);

		opaque.Count = 0;
		blended.Count = 0;
		var visible = 0;
		for ( int i = 0; i < objects.Count; i++ )
		{
			var o = objects[i];
			var c = o.Center;
			var e = o.Extents;
			var toCamera = c - view.Position;
			var distanceSquared = toCamera.LengthSquared();
			var distance = MathF.Sqrt( distanceSquared );

			lodOf[i] = Lod( o, distance, pixelsPerUnit );

			if ( o.FadeEnd > 0 && distance > o.FadeEnd ) { lodOf[i] = -1; continue; }
			var radiusSquared = e.LengthSquared();
			if ( distanceSquared > radiusSquared && radiusSquared < thresholdSquared * distanceSquared ) continue;
			if ( !InFrustum( planes, c, e ) ) continue;

			visible++;
			foreach ( var draw in o.Lods[lodOf[i]] )
			{
				if ( draw.IndexCount == 0 ) continue;
				var material = map.Materials[draw.Material];
				if ( material.Kind == SurfaceKind.Translucent )
				{
					// Back to front
					var key = (ulong)(uint.MaxValue - (uint)Math.Min( distance * 16, uint.MaxValue - 1 )) << 32 | (uint)i;
					blended.Add( new Visible { Key = key, Object = i, Draw = draw, Kind = 0 } );
				}
				else
				{
					var kind = (material.Kind == SurfaceKind.AlphaTest ? 1 : 0) | (material.TwoSided ? 2 : 0);
					opaque.Add( new Visible { Key = (ulong)kind << 56 | (ulong)(uint)draw.Material << 24 | (uint)(i & 0xFFFFFF), Object = i, Draw = draw, Kind = kind } );
				}
			}
		}

		ObjectsVisible = visible;
		opaque.Sort();
		blended.Sort();

		PrepareCascades( view, forwardDir, right, up, tanX, tanY );
	}

	int Lod( MapObject o, float distance, float pixelsPerUnit )
	{
		var lods = o.Lods.Length;
		if ( lods <= 1 ) return 0;
		if ( o.LodOverride >= 0 ) return Math.Min( o.LodOverride, lods - 1 );

		var pixels = distance > 0 ? pixelsPerUnit / distance : float.MaxValue;
		var metric = pixels > 0 ? 50.0f / pixels : 0;
		var switches = o.Model.SwitchDistances;
		var lod = Math.Max( switches.Length - 1, 0 );
		for ( ; lod > 0; lod-- )
		{
			var d = switches[lod] * o.Scale;
			if ( d > 0 && d < metric ) break;
		}

		return Math.Min( lod, lods - 1 );
	}

	static bool InFrustum( ReadOnlySpan<V4> planes, V3 c, V3 e )
	{
		foreach ( var p in planes )
		{
			var distance = p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W;
			var radius = MathF.Abs( p.X ) * e.X + MathF.Abs( p.Y ) * e.Y + MathF.Abs( p.Z ) * e.Z;
			if ( distance + radius < 0 ) return false;
		}

		return true;
	}

	void PrepareCascades( MapView view, V3 forwardDir, V3 right, V3 up, float tanX, float tanY )
	{
		for ( int c = 0; c < MaxCascades; c++ ) cascadeLists[c].Count = 0;
		if ( Cascades == 0 ) return;

		// Practical splits out to 15000, Basalt's cascade distance
		const float Far = 15000, Lambda = 0.75f;
		var near = view.ZNear;
		Span<float> splits = stackalloc float[MaxCascades + 1];
		splits[0] = near;
		for ( int i = 1; i <= Cascades; i++ )
		{
			var t = (float)i / Cascades;
			splits[i] = Lambda * near * MathF.Pow( Far / near, t ) + (1 - Lambda) * (near + (Far - near) * t );
		}

		var light = -SunDirection;
		var lx = V3.Normalize( V3.Cross( light, MathF.Abs( light.Z ) > 0.99f ? V3.UnitX : V3.UnitZ ) );
		var ly = V3.Cross( lx, light );

		var ends = new V4( float.MaxValue );
		var offsets = V4.Zero;
		for ( int c = 0; c < Cascades; c++ )
		{
			// The slice's bounding sphere
			var a = splits[c];
			var b = splits[c + 1];
			var centre = view.Position + forwardDir * ((a + b) * 0.5f);
			var radius = 0.0f;
			foreach ( var d in (ReadOnlySpan<float>)[a, b] )
			{
				foreach ( var (sx, sy) in (ReadOnlySpan<(float, float)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)] )
				{
					var corner = view.Position + forwardDir * d + right * (sx * d * tanX) + up * (sy * d * tanY);
					radius = MathF.Max( radius, V3.Distance( corner, centre ) );
				}
			}

			// Depth over the whole map along the light, so every caster is in range
			float zmin = float.MaxValue, zmax = float.MinValue;
			for ( int k = 0; k < 8; k++ )
			{
				var corner = new V3( (k & 1) != 0 ? map.Maxs.X : map.Mins.X, (k & 2) != 0 ? map.Maxs.Y : map.Mins.Y, (k & 4) != 0 ? map.Maxs.Z : map.Mins.Z );
				var z = V3.Dot( corner - centre, light );
				zmin = MathF.Min( zmin, z );
				zmax = MathF.Max( zmax, z );
			}

			zmax = MathF.Min( zmax, radius );
			var depthRange = MathF.Max( zmax - zmin, 1 );
			var toLight = new Matrix4x4(
				lx.X / radius, ly.X / radius, light.X / depthRange, 0,
				lx.Y / radius, ly.Y / radius, light.Y / depthRange, 0,
				lx.Z / radius, ly.Z / radius, light.Z / depthRange, 0,
				-V3.Dot( lx, centre ) / radius, -V3.Dot( ly, centre ) / radius, (-V3.Dot( light, centre ) - zmin) / depthRange, 1 );

			var toTexture = new Matrix4x4( 0.5f, 0, 0, 0, 0, -0.5f, 0, 0, 0, 0, 1, 0, 0.5f, 0.5f, 0, 1 );
			mainView.WorldToShadow[c] = toLight * toTexture;
			cascadeViews[c] = mainView with { WorldToProjection = toLight };
			if ( c < 3 ) ends[c] = b;
			offsets[c] = 2 * radius / CascadeSize * 1.5f;

			// Casters: overlapping the cascade's square across the light
			var list = cascadeLists[c];
			var objects = map.Objects;
			for ( int i = 0; i < objects.Count; i++ )
			{
				var lod = lodOf[i];
				if ( lod < 0 ) continue;
				var o = objects[i];
				var rel = o.Center - centre;
				var e = o.Extents;
				var x = V3.Dot( rel, lx );
				var ex = MathF.Abs( lx.X ) * e.X + MathF.Abs( lx.Y ) * e.Y + MathF.Abs( lx.Z ) * e.Z;
				if ( MathF.Abs( x ) - ex > radius ) continue;
				var y = V3.Dot( rel, ly );
				var ey = MathF.Abs( ly.X ) * e.X + MathF.Abs( ly.Y ) * e.Y + MathF.Abs( ly.Z ) * e.Z;
				if ( MathF.Abs( y ) - ey > radius ) continue;

				// Too small to matter at this cascade's texel size
				if ( e.LengthSquared() < offsets[c] * offsets[c] * 0.25f ) continue;

				foreach ( var draw in o.Lods[lod] )
				{
					if ( draw.IndexCount == 0 ) continue;
					var material = map.Materials[draw.Material];
					if ( material.Kind == SurfaceKind.Translucent ) continue;
					var kind = material.Kind == SurfaceKind.AlphaTest ? 1 : 0;
					var key = (ulong)kind << 56 | (kind == 1 ? (ulong)(uint)draw.Material << 24 : 0) | (uint)(i & 0xFFFFFF);
					list.Add( new Visible { Key = key, Object = i, Draw = draw, Kind = kind } );
				}
			}

			list.Sort();
		}

		mainView.CascadeEnds = ends;
		mainView.NormalOffsets = offsets;
		for ( int c = 0; c < Cascades; c++ )
		{
			cascadeViews[c].CascadeEnds = ends;
			cascadeViews[c].NormalOffsets = offsets;
		}
	}

	//
	// Recording
	//

	/// <summary>Instance data and indirect commands for the frame, in one upload allocation.</summary>
	(BufferHandle Buffer, long Offset, long Size) instances;
	(BufferHandle Buffer, long Offset) commands;

	void WriteInstances( CommandList list )
	{
		var lists = new List<VisibleList> { opaque, blended };
		for ( int c = 0; c < Cascades; c++ ) lists.Add( cascadeLists[c] );
		var total = lists.Sum( x => x.Count );

		var size = Math.Max( total, 1 ) * sizeof( InstanceData );
		var upload = list.AllocateUpload( size, gpu.Capabilities.StorageAlignment );
		var data = MemoryMarshal.Cast<byte, InstanceData>( upload.Data );
		instances = (upload.Buffer, upload.Offset, size);

		IndirectCommand[] none = [];
		var writeCommands = Mode == IssueMode.Indirect || (Mode == IssueMode.MaterialObjects && MergeRuns);
		var commandUpload = writeCommands ? list.AllocateUpload( Math.Max( total, 1 ) * sizeof( IndirectCommand ), 16 ) : default;
		var indirect = writeCommands ? MemoryMarshal.Cast<byte, IndirectCommand>( commandUpload.Data ) : none;
		commands = (commandUpload.Buffer, commandUpload.Offset);

		var next = 0;
		var objects = map.Objects;
		foreach ( var drawList in lists )
		{
			for ( int i = 0; i < drawList.Count; i++ )
			{
				ref var v = ref drawList.Items[i];
				var o = objects[v.Object];
				v.Instance = next;
				data[next] = new InstanceData { Row0 = o.Row0, Row1 = o.Row1, Row2 = o.Row2, Tint = o.Tint * v.Draw.Tint, Material = (uint)v.Draw.Material };
				if ( indirect.Length > 0 ) indirect[next] = new IndirectCommand { IndexCount = (uint)v.Draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)v.Draw.FirstIndex, VertexOffset = v.Draw.VertexOffset, FirstInstance = (uint)next };
				next++;
			}
		}
	}

	void BindView( CommandList list, in ViewData view )
	{
		var mode = (int)Mode;
		var constants = list.Upload( view );
		Span<Binding> bindings = stackalloc Binding[3];
		bindings[0] = Binding.Uniform( 0, constants.Buffer, constants.Offset, sizeof( ViewData ) );
		bindings[1] = Binding.Storage( 1, instances.Buffer, instances.Offset, instances.Size );
		bindings[2] = Binding.Storage( 2, materialTable );
		list.SetBindings( 0, set0[mode], bindings[..(mode + 1)] );
	}

	/// <summary>Draw a sorted list with one of the pipelines in <paramref name="pipelines"/> (by each draw's kind).</summary>
	void DrawList( CommandList list, VisibleList draws, PipelineHandle[] pipelines, bool depthOnly, ref PassStats stats )
	{
		var mode = Mode;
		var current = -1;
		var lastMaterial = -1;
		var materialCount = 0;
		var objects = map.Objects;
		Span<Binding> bindings = stackalloc Binding[3];

		if ( mode != IssueMode.PerDraw )
		{
			list.SetVertexBuffer( 0, map.Vertices );
			list.SetIndexBuffer( map.Indices, IndexFormat.UInt32 );
		}

		var i = 0;
		while ( i < draws.Count )
		{
			ref var v = ref draws.Items[i];
			var pipelineIndex = Math.Min( v.Kind, pipelines.Length - 1 );
			if ( pipelineIndex != current )
			{
				list.SetPipeline( pipelines[pipelineIndex] );
				current = pipelineIndex;
				stats.Pipelines++;
				lastMaterial = -1;
			}

			if ( mode == IssueMode.Indirect )
			{
				// Every draw of this pipeline in one call: the list is sorted by kind, so they're contiguous
				var start = i;
				while ( i < draws.Count && Math.Min( draws.Items[i].Kind, pipelines.Length - 1 ) == pipelineIndex )
				{
					stats.Triangles += draws.Items[i].Draw.IndexCount / 3;
					if ( draws.Items[i].Draw.Material != lastMaterial ) { lastMaterial = draws.Items[i].Draw.Material; materialCount++; }
					i++;
				}

				list.DrawIndexedIndirect( commands.Buffer, commands.Offset + (long)draws.Items[start].Instance * sizeof( IndirectCommand ), i - start, sizeof( IndirectCommand ) );
				stats.IndirectCalls++;
				stats.Draws += i - start;
				continue;
			}

			var needsMaterial = !depthOnly || (v.Kind & 1) != 0;
			if ( v.Draw.Material != lastMaterial )
			{
				lastMaterial = v.Draw.Material;
				materialCount++;
				if ( mode == IssueMode.MaterialObjects && needsMaterial )
				{
					list.SetBindingSet( 1, map.Materials[v.Draw.Material].Set );
					stats.SetBinds++;
				}
			}

			if ( mode == IssueMode.MaterialObjects && MergeRuns )
			{
				// A run of draws with this pipeline and material in one multi-draw, as Basalt merges an aggregate's draws
				var end = i + 1;
				while ( end < draws.Count && (!needsMaterial || draws.Items[end].Draw.Material == v.Draw.Material) && Math.Min( draws.Items[end].Kind, pipelines.Length - 1 ) == pipelineIndex ) end++;
				if ( end - i > 1 )
				{
					for ( int k = i; k < end; k++ ) stats.Triangles += draws.Items[k].Draw.IndexCount / 3;
					list.DrawIndexedIndirect( commands.Buffer, commands.Offset + (long)v.Instance * sizeof( IndirectCommand ), end - i, sizeof( IndirectCommand ) );
					stats.IndirectCalls++;
					stats.Draws += end - i;
					i = end;
					continue;
				}
			}

			if ( mode == IssueMode.PerDraw )
			{
				var o = objects[v.Object];
				var material = map.Materials[v.Draw.Material];
				var draw = new DrawData
				{
					Object = new InstanceData { Row0 = o.Row0, Row1 = o.Row1, Row2 = o.Row2, Tint = o.Tint * v.Draw.Tint, Material = (uint)v.Draw.Material },
					Surface = MaterialData.Of( gpu, material, 0 ),
				};

				var constants = list.Upload( draw );
				bindings[0] = Binding.Uniform( 0, constants.Buffer, constants.Offset, sizeof( DrawData ) );
				bindings[1] = Binding.Sampled( 1, material.Texture, srgb: material.Textured );
				bindings[2] = Binding.SamplerState( 2, sampler );
				list.SetBindings( 1, set1[0], bindings );
				stats.SetBinds++;

				list.SetVertexBuffer( 0, map.Vertices );
				list.SetIndexBuffer( map.Indices, IndexFormat.UInt32 );
				list.DrawIndexed( v.Draw.IndexCount, 1, v.Draw.FirstIndex, v.Draw.VertexOffset, 0 );
			}
			else
			{
				list.DrawIndexed( v.Draw.IndexCount, 1, v.Draw.FirstIndex, v.Draw.VertexOffset, v.Instance );
			}

			stats.Draws++;
			stats.Triangles += v.Draw.IndexCount / 3;
			i++;
		}

		stats.Materials += materialCount;
	}

	void RecordShadow( CommandList list, int cascade )
	{
		if ( cascade == 0 )
			list.Barrier( TextureBarrier.Transition( shadow, ImageLayout.Undefined, ImageLayout.DepthTarget, PipelineStage.FragmentShader, MemoryAccess.ShaderRead, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetRead | MemoryAccess.DepthTargetWrite ) );

		list.BeginLabel( PassNames[cascade] );
		var target = DepthTarget.Of( shadow, 1.0f );
		target.Layer = cascade;
		list.BeginRendering( [], target );
		BindView( list, cascadeViews[cascade] );
		list.SetDepthBias( 2, 2.5f );
		var stats = default( PassStats );
		DrawList( list, cascadeLists[cascade], shadows[(int)Mode], true, ref stats );
		Passes[cascade] = stats;
		list.EndRendering();
		list.EndLabel();
	}

	void RecordPrepass( CommandList list )
	{
		list.WriteTimestamp( timestamps, slot * Stamps + 1 );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.DepthTarget, PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetRead | MemoryAccess.DepthTargetWrite ) );
		list.BeginLabel( "Prepass" );
		list.BeginRendering( [], DepthTarget.Of( depth, 0.0f ) );
		BindView( list, mainView );
		var stats = default( PassStats );
		DrawList( list, opaque, prepass[(int)Mode], true, ref stats );
		Passes[MaxCascades] = stats;
		list.EndRendering();
		list.EndLabel();
	}

	void RecordForward( CommandList list, TextureHandle image )
	{
		list.WriteTimestamp( timestamps, slot * Stamps + 2 );
		list.Barrier( TextureBarrier.Transition( shadow, Cascades > 0 ? ImageLayout.DepthTarget : ImageLayout.Undefined, ImageLayout.ShaderRead, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite, PipelineStage.FragmentShader, MemoryAccess.ShaderRead ) );
		list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetRead | MemoryAccess.ColorTargetWrite ) );
		list.Barrier( TextureBarrier.Transition( resolved, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.Transfer, MemoryAccess.TransferRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );

		list.BeginLabel( "Forward" );
		var target = ColorTarget.Of( color, new V4( 0.45f, 0.55f, 0.7f, 1 ) );
		target.Store = StoreOp.DontCare;
		target.ResolveTarget = resolved;
		var depthTarget = DepthTarget.Of( depth );
		depthTarget.Store = StoreOp.DontCare;
		list.BeginRendering( [target], depthTarget );
		BindView( list, mainView );

		var stats = default( PassStats );
		DrawList( list, opaque, forward[(int)Mode], false, ref stats );
		Passes[MaxCascades + 1] = stats;

		var blend = default( PassStats );
		DrawList( list, blended, [translucent[(int)Mode]], false, ref blend );
		Passes[MaxCascades + 2] = blend;
		list.EndRendering();
		list.EndLabel();
		list.WriteTimestamp( timestamps, slot * Stamps + 3 );

		// Into the window
		list.Barrier( TextureBarrier.Transition( resolved, ImageLayout.ColorTarget, ImageLayout.CopySource, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		if ( image.IsValid ) Blit( list, image );
		list.WriteTimestamp( timestamps, slot * Stamps + 4 );
	}

	void Blit( CommandList list, TextureHandle image )
	{
		list.Barrier( TextureBarrier.Transition( image, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		var info = gpu.InfoOf( image );
		list.Blit( resolved, (0, 0, Width, Height), image, (0, 0, info.Width, info.Height), Filter.Linear );
		list.Barrier( TextureBarrier.Transition( image, ImageLayout.CopyDestination, ImageLayout.Present, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.BottomOfPipe, MemoryAccess.None ) );
	}

	readonly Stopwatch clock = Stopwatch.StartNew();

	// Timestamps: per frame the start, after shadows, after the prepass, after forward and translucent, and the end, in four slots
	const int Stamps = 5, Slots = 4;
	readonly TimestampQueries timestamps;
	int slot;
	long frame;
	readonly ulong[] ticks = new ulong[Stamps];

	FrameTimes ReadGpuTimes( FrameTimes times )
	{
		times.GpuShadows = times.GpuPrepass = times.GpuForward = times.GpuCopy = times.Gpu = double.NaN;
		var old = (int)((frame + 1) % Slots);
		if ( frame < Slots || !timestamps.TryRead( old * Stamps, ticks ) ) return times;

		var ms = gpu.Capabilities.TimestampPeriod / 1e6;
		times.GpuShadows = (ticks[1] - ticks[0]) * ms;
		times.GpuPrepass = (ticks[2] - ticks[1]) * ms;
		times.GpuForward = (ticks[3] - ticks[2]) * ms;
		times.GpuCopy = (ticks[4] - ticks[3]) * ms;
		times.Gpu = (ticks[4] - ticks[0]) * ms;
		return times;
	}

	/// <summary>
	/// Render a frame from <paramref name="view"/> into <paramref name="swapChain"/> (or only into <see cref="Resolved"/>
	/// when it's null), and say how long each part took on the CPU.
	/// </summary>
	public FrameTimes Render( MapView view, SwapChain swapChain )
	{
		var t0 = clock.Elapsed.TotalMilliseconds;
		gpu.BeginFrame();
		var image = default( TextureHandle );
		if ( swapChain is not null && !swapChain.Acquire( out image ) ) image = default;
		var t1 = clock.Elapsed.TotalMilliseconds;

		Array.Clear( Passes );
		var main = gpu.BeginCommandList( QueueType.Graphics, "Map frame" );
		slot = (int)(frame % Slots);
		main.ResetTimestamps( timestamps, slot * Stamps, Stamps );
		main.WriteTimestamp( timestamps, slot * Stamps );
		Prepare( view );
		WriteInstances( main );
		var t2 = clock.Elapsed.TotalMilliseconds;

		CommandList[] lists;
		if ( Parallel )
		{
			// Shadows, the prepass and forward each record into a list of their own, submitted in that order
			lists = new CommandList[Cascades + 2];
			lists[0] = main;
			for ( int i = 1; i < lists.Length; i++ ) lists[i] = gpu.BeginCommandList( QueueType.Graphics, "Map pass" );
			var cascades = Cascades;
			System.Threading.Tasks.Parallel.For( 0, lists.Length, i =>
			{
				if ( i < cascades ) RecordShadow( lists[i], i );
				else if ( i == cascades ) RecordPrepass( lists[i] );
				else RecordForward( lists[i], image );
			} );
		}
		else
		{
			for ( int c = 0; c < Cascades; c++ ) RecordShadow( main, c );
			RecordPrepass( main );
			RecordForward( main, image );
			lists = [main];
		}

		var t3 = clock.Elapsed.TotalMilliseconds;
		for ( int i = 0; i < lists.Length; i++ ) gpu.Submit( lists[i], swapChain: i == lists.Length - 1 && image.IsValid ? swapChain : null );
		if ( image.IsValid ) swapChain.Present();
		var t4 = clock.Elapsed.TotalMilliseconds;

		var times = ReadGpuTimes( new FrameTimes { Wait = t1 - t0, Prepare = t2 - t1, Record = t3 - t2, Submit = t4 - t3, Total = t4 - t0 } );
		frame++;
		return times;
	}

	public void Dispose()
	{
		gpu.WaitIdle();
		timestamps.Dispose();
		foreach ( var t in (TextureHandle[])[color, depth, resolved, shadow] ) gpu.Destroy( t );
		gpu.Destroy( sampler );
		gpu.Destroy( shadowSampler );
		gpu.Destroy( materialTable );
		gpu.Destroy( materialConstants );
		foreach ( var group in new[] { prepass, forward, shadows } )
		{
			foreach ( var pipelines in group )
			{
				foreach ( var p in pipelines ?? [] ) gpu.Destroy( p );
			}
		}

		foreach ( var p in translucent ) gpu.Destroy( p );
	}
}
