using Borealis.Gpu;
using Borealis.Source2.Models;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The frame's composite morph atlas, as native's scene system keeps it (<c>m_CMTAtlasDesc</c>): RGBA16F, 2048 across, the
/// left half each morphing mesh's position deltas and the right half its normal deltas and wrinkle, one texel per vertex in
/// a rectangle of its own. Native's is 1024 down; this one is as tall as the rectangles placed in it need (<see cref="Fit"/>),
/// from <see cref="MinHeight"/> to that, and is freed once nothing has morphed for <see cref="IdleFrames"/>.
/// Meshes are queued while Borealis prepares a frame (<see cref="BackendMorphs"/>), their rectangles
/// placed when the frame's morphs are generated (<see cref="BackendContext.GenerateMorphs"/>), and each skinning dispatch
/// reads its mesh's rectangle from the transform buffer's morph entry (<see cref="Subrect"/>).
/// </summary>
internal sealed class MorphAtlas( BackendDevice device )
{
	/// <summary>Texels across each half (native's <c>CMT_ATLAS_LOGICAL_WIDTH</c>).</summary>
	public const int LogicalWidth = 1024;

	/// <summary>Texels across the whole atlas: positions, then normals and wrinkles.</summary>
	public const int Width = LogicalWidth * 2;

	/// <summary>Texels down the atlas at most (native's).</summary>
	public const int MaxHeight = 1024;

	/// <summary>Texels down the atlas at least: a citizen's head and its clothes fit.</summary>
	public const int MinHeight = 64;

	/// <summary>
	/// Generations after which an atlas taller than the last ones needed is made again as tall as the tallest of them, and
	/// device frames without a generation after which it's freed.
	/// </summary>
	public const int ShrinkGenerations = 600, IdleFrames = 600;

	/// <summary>The gutter between rectangles (native's <c>MORPH_GUTTER_SIZE</c>).</summary>
	const int Gutter = 1;

	/// <summary>A mesh's morphs queued for this frame, and once placed, its rectangle in the atlas.</summary>
	sealed class Queued
	{
		public MorphSet Set;
		public BackendTexture Source;
		public float[] Weights, Delayed;
		public int X, Y, W, H;
	}

	readonly List<Queued> queued = new();

	// Entries of frames already generated, reused for later ones
	readonly List<Queued> spareQueued = new();

	static float[] Copy( float[] into, ReadOnlySpan<float> values )
	{
		if ( into is null || into.Length != values.Length ) into = values.ToArray();
		else values.CopyTo( into );
		return into;
	}
	bool generated;
	BackendTexture texture;

	// The tallest atlas the generations since the last resize needed, how many there were, and the frame of the last
	int tallestNeeded, generations;
	uint lastGenerated;

	/// <summary>The atlas, made the first time anything morphs (<see cref="Fit"/>).</summary>
	public BackendTexture Texture => texture ??= Make( MinHeight );

	/// <summary>Texels down the atlas now.</summary>
	int Height => texture?.Height ?? MinHeight;

	BackendTexture Make( int height ) => (BackendTexture)device.CreateTexture( new TextureDesc { Width = Width, Height = height, Depth = 1, Mips = 1, Samples = 1, Format = TextureFormat.RGBA16161616F, RenderTarget = true }, "Borealis composite morph atlas" );

	/// <summary>
	/// Before the queued meshes are generated, on the frame's setup context and before any skinning dispatch binds the atlas:
	/// place them and make the atlas as tall as they need (a power of two, from <see cref="MinHeight"/> up to
	/// <see cref="MaxHeight"/>): taller at once when they don't fit, shorter once <see cref="ShrinkGenerations"/> generations
	/// have needed less. The atlas.
	/// </summary>
	public BackendTexture Fit()
	{
		lock ( queued )
		{
			if ( generated || queued.Count == 0 ) return Texture;

			var needed = (int)Math.Min( System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)Math.Max( Place( MaxHeight ), MinHeight ) ), MaxHeight );
			tallestNeeded = Math.Max( tallestNeeded, needed );
			generations++;
			lastGenerated = device.GpuDrivenFrame;
			if ( texture is null || texture.Height < needed || generations >= ShrinkGenerations && tallestNeeded < texture.Height ) Resize( tallestNeeded );
			else if ( generations >= ShrinkGenerations ) (tallestNeeded, generations) = (needed, 0);
			return texture;
		}
	}

	/// <summary>Remade this tall: the old one is destroyed once the GPU is done with it.</summary>
	void Resize( int height )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		texture?.Dispose();
		texture = Make( height );
		(tallestNeeded, generations) = (0, 0);
		Resized++;
		LongestResize = Math.Max( LongestResize, System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	/// <summary>Times the atlas was made at another height, and the longest that took in milliseconds, for tests and reports.</summary>
	public int Resized { get; private set; }
	public double LongestResize { get; private set; }

	/// <summary>Device frames since anything morphed, for reports.</summary>
	public uint IdleFor => device.GpuDrivenFrame - lastGenerated;

	/// <summary>At the start of a device frame: the atlas freed once nothing has morphed for <see cref="IdleFrames"/>.</summary>
	public void BeginFrame()
	{
		if ( texture is null || device.GpuDrivenFrame - lastGenerated < IdleFrames ) return;
		lock ( queued )
		{
			texture.Dispose();
			texture = null;
			(tallestNeeded, generations) = (0, 0);
			Freed++;
		}
	}

	/// <summary>Times the atlas was freed with nothing morphing, for tests.</summary>
	public int Freed { get; private set; }

	/// <summary>Texels down the atlas, 0 while there's none, for reports.</summary>
	public int MadeHeight => texture?.Height ?? 0;

	/// <summary>Meshes queued and not yet generated.</summary>
	public bool Pending
	{
		get
		{
			lock ( queued ) return !generated && queued.Count > 0;
		}
	}

	/// <summary>
	/// Queue a mesh's morphs with their flex descs' weights and delayed weights; the handle <see cref="Subrect"/> takes once
	/// they're generated. A frame's first queue after the last generation starts a new list.
	/// </summary>
	public int Queue( MorphSet set, BackendTexture source, ReadOnlySpan<float> weights, ReadOnlySpan<float> delayed )
	{
		lock ( queued )
		{
			if ( generated )
			{
				// Last frame's entries and their arrays are reused for this frame's
				spareQueued.AddRange( queued );
				queued.Clear();
				generated = false;
			}

			Queued q;
			if ( spareQueued.Count > 0 )
			{
				q = spareQueued[^1];
				spareQueued.RemoveAt( spareQueued.Count - 1 );
			}
			else q = new Queued();

			(q.Set, q.Source, q.X, q.Y, q.W, q.H) = (set, source, 0, 0, 0, 0);
			q.Weights = Copy( q.Weights, weights );
			q.Delayed = Copy( q.Delayed, delayed );
			queued.Add( q );
			return queued.Count - 1;
		}
	}

	/// <summary>
	/// Where a generated mesh's morphs are: u offset, u range, v offset, v range in texels, as native's
	/// <c>SetTransformTextureMorphSubrect</c> stores them; a 1 by 1 black texel for a handle that isn't one.
	/// </summary>
	public Vector4 Subrect( int handle )
	{
		lock ( queued )
		{
			if ( (uint)handle >= (uint)queued.Count ) return new Vector4( 0, 1, 0, 1 );
			var q = queued[handle];
			return new Vector4( q.X, q.W, q.Y, q.H );
		}
	}

	/// <summary>
	/// Place every queued mesh's rectangle, a row at a time below a black texel at the corner, as native's packing does
	/// (<c>PackChartsIntoAtlas</c>; the places differ, the result doesn't), in an atlas <paramref name="height"/> texels down.
	/// One that doesn't fit gets the black texel. Returns the texels down the placed rectangles reach.
	/// </summary>
	int Place( int height )
	{
		int x = Gutter, y = Gutter, row = 0;
		foreach ( var q in queued )
		{
			var w = q.Set.Width + Gutter;
			var h = q.Set.Height + Gutter;
			if ( x + w > LogicalWidth )
			{
				x = Gutter;
				y += row;
				row = 0;
			}

			if ( x + w > LogicalWidth || y + h > height )
			{
				(q.X, q.Y, q.W, q.H) = (0, 0, 1, 1);
				continue;
			}

			(q.X, q.Y, q.W, q.H) = (x, y, q.Set.Width, q.Set.Height);
			x += w;
			row = Math.Max( row, h );
		}

		return y + row;
	}

	/// <summary>One composite vertex (<c>MorphNormalWrinkleVertex_t</c>): both passes read it, the first only part of it.</summary>
	[StructLayout( LayoutKind.Sequential )]
	struct Vertex
	{
		public System.Numerics.Vector4 PositionWeights;
		public System.Numerics.Vector4 TexCoords;
		public System.Numerics.Vector4 OffsetsPositionSpeed;
		public System.Numerics.Vector4 RangesPositionSpeed;
		public uint AtlasIndex;
		public System.Numerics.Vector4 OffsetsNormalWrinkle;
		public System.Numerics.Vector4 RangesNormalWrinkle;
	}

	internal static readonly VertexStreamLayout Layout = new( Marshal.SizeOf<Vertex>(),
		new VertexField( Source2.Shaders.VertexSemantic.Position, 0, VertexFormat.Float4, 0 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 0, VertexFormat.Float4, 16 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 1, VertexFormat.Float4, 32 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 2, VertexFormat.Float4, 48 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 3, VertexFormat.UInt, 64 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 4, VertexFormat.Float4, 68 ),
		new VertexField( Source2.Shaders.VertexSemantic.TexCoord, 5, VertexFormat.Float4, 84 ) );

	/// <summary>
	/// Composite the queued meshes' morphs into the atlas on <paramref name="list"/>, outside any rendering scope, with
	/// the atlas in <paramref name="layout"/>: clear it, then draw every weighted rectangle additively, positions then
	/// normals and wrinkles (<c>CSceneSystem::GenerateCompositeMorphTextureAtlas</c>). Leaves it readable. False when
	/// nothing was queued.
	/// </summary>
	public bool Generate( CommandList list, ShaderDrawer drawer, ref ImageLayout layout )
	{
		lock ( queued )
		{
			if ( generated || queued.Count == 0 ) return false;
			generated = true;
			Place( Height );

			// Six vertices a rectangle (two triangles, as native's quads index them), every rectangle of every weighted morph;
			// the list is kept from frame to frame, as faces morph every frame
			vertices.Clear();
			var positions = Build( vertices, normals: false );
			var normalsStart = vertices.Count;
			Build( vertices, normals: true );

			var atlas = Texture;
			list.Barrier( TextureBarrier.Transition( atlas.Handle, layout, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite | MemoryAccess.ColorTargetRead ) );
			list.BeginRendering( [new ColorTarget { Texture = atlas.Handle, Load = LoadOp.Clear, Store = StoreOp.Store, ClearColor = default }], default );

			if ( vertices.Count > 0 && device.MaterialFromShader( Shader ) is BackendMaterial { Shader: { } shader } )
			{
				var bytes = MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( vertices ) );
				var upload = list.AllocateUpload( bytes.Length, 16 );
				bytes.CopyTo( upload.Data );

				var attributes = compositeAttributes ??= new ShaderAttributes();
				var viewport = new System.Numerics.Vector4( 0, 0, Width, Height );
				attributes.SetCombo( "D_COMPOSITETYPE", 0 );
				drawer.Draw( list, shader, attributes, Format.RGBA16Float, 1, upload.Buffer, upload.Offset, Layout, positions, PrimitiveTopology.TriangleList, viewport );
				attributes.SetCombo( "D_COMPOSITETYPE", 1 );
				drawer.Draw( list, shader, attributes, Format.RGBA16Float, 1, upload.Buffer, upload.Offset + normalsStart * Marshal.SizeOf<Vertex>(), Layout, vertices.Count - normalsStart, PrimitiveTopology.TriangleList, viewport );
			}

			list.EndRendering();
			list.Barrier( TextureBarrier.Transition( atlas.Handle, ImageLayout.ColorTarget, ImageLayout.ShaderRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
			layout = ImageLayout.ShaderRead;
			return true;
		}
	}

	// The composite vertices of the last generation (under the queue's lock), and the attributes it draws with
	readonly List<Vertex> vertices = new();
	ShaderAttributes compositeAttributes;

	/// <summary>The composite shader (<c>SCENESYSTEMMAT_MORPH_MATERIAL</c>).</summary>
	internal const string Shader = "shaders/morph_composite.shader";

	/// <summary>
	/// Every weighted morph's rectangles for one pass, as native lays them out: from the morph set's texels into the
	/// mesh's rectangle (the right half for normals), in clip space with y up. Returns the vertices added.
	/// </summary>
	int Build( List<Vertex> vertices, bool normals )
	{
		var first = vertices.Count;
		foreach ( var q in queued )
		{
			var (set, source) = (q.Set, q.Source);
			if ( normals ) (q.Set, q.Source) = (null, null);
			if ( source is not { IsValid: true } || q.W <= 1 && q.H <= 1 && q.X == 0 ) continue;
			var atlasIndex = (uint)source.BindlessIndex;
			var toDstX = (float)q.W / set.Width;
			var toDstY = (float)q.H / set.Height;
			var uToX = source.Width * toDstX;
			var vToY = source.Height * toDstY;

			for ( int desc = 0; desc < q.Weights.Length; desc++ )
			{
				if ( MathF.Abs( q.Weights[desc] ) <= 1e-5f && MathF.Abs( q.Delayed[desc] ) <= 1e-5f ) continue;
				if ( set.MorphFor( desc ) is not { } morph ) continue;

				foreach ( var rect in morph.Rects )
				{
					if ( rect.Bundles.Length < 2 ) continue;
					var position = rect.Bundles[0];
					var normal = rect.Bundles[1];

					var left = q.X + rect.XLeftDst * toDstX + (normals ? LogicalWidth : 0);
					var top = q.Y + rect.YTopDst * toDstY;
					var right = left + rect.UWidthSrc * uToX;
					var bottom = top + rect.VHeightSrc * vToY;
					var x0 = (left / Width - 0.5f) * 2;
					var x1 = (right / Width - 0.5f) * 2;
					var y0 = (0.5f - top / Height) * 2;
					var y1 = (0.5f - bottom / Height) * 2;

					Vertex Corner( float x, float y, bool r, bool b ) => new()
					{
						PositionWeights = new( x, y, q.Weights[desc], q.Delayed[desc] ),
						TexCoords = new( position.ULeftSrc + (r ? rect.UWidthSrc : 0), position.VTopSrc + (b ? rect.VHeightSrc : 0), normal.ULeftSrc + (r ? rect.UWidthSrc : 0), normal.VTopSrc + (b ? rect.VHeightSrc : 0) ),
						OffsetsPositionSpeed = Four( position.Offsets ),
						RangesPositionSpeed = Four( position.Ranges ),
						AtlasIndex = atlasIndex,
						OffsetsNormalWrinkle = Four( normal.Offsets ),
						RangesNormalWrinkle = Four( normal.Ranges ),
					};

					// Native's quad 0, 1, 2 and 2, 0, 3 (top left, top right, bottom right, bottom left)
					var c0 = Corner( x0, y0, false, false );
					var c1 = Corner( x1, y0, true, false );
					var c2 = Corner( x1, y1, true, true );
					var c3 = Corner( x0, y1, false, true );
					vertices.Add( c0 );
					vertices.Add( c1 );
					vertices.Add( c2 );
					vertices.Add( c2 );
					vertices.Add( c0 );
					vertices.Add( c3 );
				}
			}
		}

		return vertices.Count - first;
	}

	static System.Numerics.Vector4 Four( float[] values ) => new( At( values, 0 ), At( values, 1 ), At( values, 2 ), At( values, 3 ) );

	static float At( float[] values, int i ) => i < values.Length ? values[i] : 0;
}

/// <summary>
/// A model's morphs on a Borealis.Gpu device (<see cref="MeshObject.MorphSource"/>): flex controller values by name,
/// evaluated each frame through each mesh's flex rules (<see cref="MorphSet.RunFlexRules"/>), with delayed weights
/// easing towards them as native's <c>SetupFlexWeights</c> eases them, and queued in the device's composite morph atlas.
/// Controllers the mesh's morph set doesn't name are ignored; controllers it names that aren't set are 0.
/// </summary>
public sealed class BackendMorphs( BackendDevice device ) : IMorphSource
{
	readonly Dictionary<string, float> values = new( StringComparer.OrdinalIgnoreCase );
	[ThreadStatic] static float[] scratchWeights;

	/// <summary>Set a flex controller by name.</summary>
	public void Set( string controller, float value ) => values[controller] = value;

	/// <summary>Set every controller back to 0.</summary>
	public void Clear() => values.Clear();

	/// <inheritdoc/>
	public int Queue( GpuMesh mesh, int modelMesh )
	{
		if ( values.Count == 0 || mesh is not BackendModel { Freed: false } model || (uint)modelMesh >= (uint)model.File.Meshes.Count ) return -1;
		var source = model.File.Meshes[modelMesh];
		if ( source.Morphs is not { } set ) return -1;

		Span<float> controllers = stackalloc float[set.Controllers.Count];
		for ( int i = 0; i < controllers.Length; i++ ) controllers[i] = values.GetValueOrDefault( set.Controllers[i].Name );

		// Scratch: the atlas's queue copies them
		if ( scratchWeights is null || scratchWeights.Length < set.FlexDescs.Count ) scratchWeights = new float[Math.Max( set.FlexDescs.Count, 64 )];
		var weights = scratchWeights.AsSpan( 0, set.FlexDescs.Count );
		weights.Clear();
		set.RunFlexRules( controllers, weights );

		// Delayed weights ease towards the weights (ExponentialDecay( 0.8, 0.033, dt )), from the weights the first time
		var now = device.RenderTime;
		var delayed = model.DelayedMorphs( this );
		if ( !delayed.TryGetValue( modelMesh, out var last ) || last.Delayed.Length != weights.Length )
		{
			last = (weights.ToArray(), now);
		}
		else
		{
			var decay = MathF.Exp( MathF.Log( 0.8f ) / 0.033f * (now - last.Time) );
			for ( int i = 0; i < weights.Length; i++ ) last.Delayed[i] = last.Delayed[i] * decay + weights[i] * (1 - decay);
			last.Time = now;
		}

		delayed[modelMesh] = last;

		var any = false;
		for ( int i = 0; i < weights.Length && !any; i++ ) any = (MathF.Abs( weights[i] ) > 1e-5f || MathF.Abs( last.Delayed[i] ) > 1e-5f) && set.MorphFor( i ) is not null;
		return any ? device.Morphs.Queue( set, model.MorphTexture( set.TextureAtlas ?? source.MorphTexture ), weights, last.Delayed ) : -1;
	}
}
