using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Bridge;

/// <summary>
/// Implements mid-frame <c>Graphics.Render( SceneObject )</c> from mirrored draw state (<c>RenderTools::DrawSceneObject</c>).
/// Rigid overrides upload separate transforms; skinned objects reuse this frame's bones and vertex cache. Main-thread-only.
/// </summary>
internal sealed class SceneObjectDrawer : IDisposable
{
	static readonly StringToken TransformBufferName = new( "g_TransformBuffer" );
	static readonly StringToken CsVertexAnimation = new( "D_CS_VERTEX_ANIMATION" );
	static readonly StringToken BaseOpacity = new( "BaseOpacity" );
	static readonly StringToken HasBaseOpacity = new( "HasBaseOpacity" );

	readonly RenderSystem system;

	/// <summary>
	/// The mirror of the world being drawn - the camera's this frame.
	/// </summary>
	public SceneMirror Mirror { get; set; }

	GpuBuffer<TransformBuffer.Entry> entries;
	GpuBuffer<uint> ids;
	int entryCount, idCount, generation = -1;

	// Retain outgrown buffers until next frame for earlier recorded draws.
	readonly List<GpuBuffer> outgrown = new();

	readonly RenderAttributes drawAttributes = new();
	readonly uint[] idScratch = new uint[64];

	/// <summary>
	/// What the frame's managed view calls (<see cref="Graphics.ManagedView.RenderSceneObject"/>).
	/// </summary>
	public Action<SceneObject, Transform, Color, Material, RenderAttributes> Render { get; }

	public SceneObjectDrawer( RenderSystem system )
	{
		this.system = system;
		Render = Draw;
	}

	void Draw( SceneObject sceneObject, Transform transform, Color color, Material material, RenderAttributes attributes )
	{
		// Require this thread's active Graphics recording context.
		if ( RenderContext.Recording is not { } context || context.Native != Graphics.Context ) return;
		if ( Mirror?.Find( sceneObject ) is not MeshObject obj || obj.Mesh is not { } mesh || mesh.Model is null ) return;

		var transforms = system.Transforms;
		if ( transforms.Generation != generation ) BeginFrame( transforms.Generation );

		var lod = Math.Clamp( obj.LodOverride >= 0 ? obj.LodOverride : obj.DrawnLod, 0, mesh.LodCount - 1 );
		var draws = mesh.DrawsForLod( lod );
		if ( draws.Length == 0 ) return;

		// Reuse current skinning; otherwise upload an override transform.
		var skinned = obj.IsSkinned && obj.SkinGeneration == transforms.Generation;
		if ( !skinned && !WriteEntry( context, RenderWorld.ToMatrix( transform ), color ) ) return;

		// Each draw's instance id: the entry it reads
		if ( draws.Length > idScratch.Length ) return;
		for ( int d = 0; d < draws.Length; d++ )
			idScratch[d] = skinned ? (uint)Math.Max( obj.SkinSlots[draws[d].Mesh], 0 ) : (uint)(entryCount - 1);

		var firstId = idCount;
		if ( !WriteIds( context, idScratch.AsSpan( 0, draws.Length ) ) ) return;

		// External drawing may have changed bindings.
		context.Invalidate();

		var probe = obj.ProbeChoice >= 0 && obj.ProbeChoice < obj.World?.LightProbeVolumes.Count ? obj.World.LightProbeVolumes[obj.ProbeChoice].Attributes : null;
		var mode = Graphics.DrawShaderMode;
		var frameIds = context.InstanceIds;
		context.InstanceIds = ids;

		try
		{
			for ( int d = 0; d < draws.Length; d++ )
			{
				var draw = draws[d];
				var drawMaterial = material ?? obj.MaterialOverride ?? draw.Material;
				if ( drawMaterial is null ) continue;

				// Object attributes override caller attributes; rigid transforms use this drawer's buffer.
				drawAttributes.Clear();
				attributes?.MergeTo( drawAttributes );
				obj.Attributes?.MergeTo( drawAttributes );
				if ( !skinned ) drawAttributes.Set( TransformBufferName, entries );

				// Preserve source opacity for override shaders (RenderTools::DrawSceneObject).
				if ( material is not null )
				{
					var opacity = draw.Material?.GetTexture( "LightSim_Opacity_A" );
					if ( opacity is not null ) drawAttributes.Set( BaseOpacity, opacity );
					drawAttributes.Set( HasBaseOpacity, opacity is not null );
				}

				context.SetObjectAttributes( drawAttributes, draw.Lightmapped ? obj.LightmapAttributes : probe );

				var vertexCache = skinned && mesh.SkinFor( draw.Mesh ) is { } skin && (skin.UsesVertexCache || obj.IsDeformed || (obj.MorphSource is not null && skin.Morphs));
				if ( obj.IsSkinned ) context.SetCombo( CsVertexAnimation, vertexCache ? 1 : 0 );

				if ( vertexCache )
				{
					if ( !context.BindGeometry( mesh, draw ) ) continue;
					if ( !context.SetRenderState( drawMaterial, mode, mesh, draw, vertexCache: true ) ) continue;
					context.BindInstances( 1, firstId + d );
					context.BindVertexCacheIds( mesh, draw );
					context.DrawIndexedInstanced( draw, 1 );
				}
				else
				{
					context.DrawModel( drawMaterial, mode, mesh, draw, firstId + d, 1 );
				}
			}
		}
		finally
		{
			if ( obj.IsSkinned ) context.SetCombo( CsVertexAnimation, 0 );
			context.InstanceIds = frameIds;
			context.SetObjectAttributes( null );

			// Subsequent engine draws bind their own state.
			context.Invalidate();
		}
	}

	void BeginFrame( int frame )
	{
		generation = frame;
		entryCount = 0;
		idCount = 0;

		foreach ( var buffer in outgrown ) buffer.Dispose();
		outgrown.Clear();
	}

	bool WriteEntry( RenderContext context, in Matrix m, Color color )
	{
		Grow( ref entries, entryCount + 1, "SceneRenderer drawn objects' transforms", GpuBuffer.UsageFlags.Structured );

		var entry = TransformBuffer.MakeEntry( m, color );
		context.UploadBuffer( entries, MemoryMarshal.CreateReadOnlySpan( ref entry, 1 ), entryCount * Marshal.SizeOf<TransformBuffer.Entry>() );
		entryCount++;
		return true;
	}

	bool WriteIds( RenderContext context, ReadOnlySpan<uint> values )
	{
		Grow( ref ids, idCount + values.Length, "SceneRenderer drawn objects' instance ids", GpuBuffer.UsageFlags.Vertex );
		context.UploadBuffer( ids, values, idCount * sizeof( uint ) );
		idCount += values.Length;
		return true;
	}

	void Grow<T>( ref GpuBuffer<T> buffer, int needed, string name, GpuBuffer.UsageFlags usage ) where T : unmanaged
	{
		if ( buffer is not null && buffer.ElementCount >= needed ) return;

		// Earlier draws still reference the old buffer.
		if ( buffer is not null ) outgrown.Add( buffer );
		var size = Math.Max( 64, buffer is null ? needed : Math.Max( needed, buffer.ElementCount * 2 ) );
		buffer = new GpuBuffer<T>( size, usage, name );
	}

	public void Dispose()
	{
		entries?.Dispose();
		ids?.Dispose();
		foreach ( var buffer in outgrown ) buffer.Dispose();
		outgrown.Clear();
		drawAttributes.Clear();
	}
}
