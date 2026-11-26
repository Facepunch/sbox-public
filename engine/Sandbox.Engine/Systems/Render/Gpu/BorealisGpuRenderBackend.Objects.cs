using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Models;
using Borealis.Source2.Shaders;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackendVertexField = Borealis.Backend.VertexField;
using BackendVertexLayout = Borealis.Backend.VertexLayout;

namespace Sandbox;

internal sealed partial class BorealisGpuRenderBackend
{
	/// <summary>
	/// A mirrored mesh object drawn again in the block being recorded, as <c>RenderTools::DrawSceneObject</c> draws a scene
	/// object for <c>Graphics.Render</c> (a highlight's outline): its draws at the LOD it last drew, each with
	/// <paramref name="material"/> (null for the draw's own), the caller's <paramref name="attributes"/> (over the block's)
	/// under the object's own (<paramref name="objectAttributes"/>, its managed copy). A rigid object draws at <paramref name="transform"/> tinted
	/// <paramref name="color"/>; a skinned one skinned this frame reads its slots in <paramref name="frameTransforms"/>, the
	/// frame's transforms, and its vertex-cache meshes this frame's vertex cache.
	/// </summary>
	/// <remarks>
	/// Each draw reads its triangles' vertices one after another (<see cref="Unrolled"/>), so a module that expands
	/// triangles (<c>ObjectHighlight</c>'s outline) draws a model as it draws CPU vertices.
	/// </remarks>
	internal void DrawObject( MeshObject obj, TransformBuffer frameTransforms, in Transform transform, Color color, Material material, RenderAttributes attributes, RenderAttributes objectAttributes )
	{
		var recording = Recording;
		if ( obj.Mesh is not { Geometry: BackendModel model } mesh || mesh.LodCount == 0 ) return;

		var lod = Math.Clamp( obj.LodOverride >= 0 ? obj.LodOverride : obj.DrawnLod, 0, mesh.LodCount - 1 );
		var draws = mesh.DrawsForLod( lod );
		if ( draws.Length == 0 ) return;

		// A skinned object posed this frame reads its slots in the frame's transforms; anything else an entry of its own
		var skinned = obj.IsSkinned && obj.SkinGeneration == frameTransforms.Generation && frameTransforms.Current?.Resource is BackendBuffer;
		BufferHandle transforms;
		uint rigidSlot = 0;
		if ( skinned )
		{
			transforms = ((BackendBuffer)frameTransforms.Current.Resource).Handle;
		}
		else
		{
			// In the list's upload memory, which the draws bind whole: its slot is its offset in entries
			var size = Unsafe.SizeOf<TransformBuffer.Entry>();
			var entry = recording.AllocateUpload( size, size );
			MemoryMarshal.Write( entry.Data, TransformBuffer.MakeEntry( RenderWorld.ToMatrix( transform ), color ) );
			transforms = entry.Buffer;
			rigidSlot = (uint)(entry.Offset / size);
		}

		var ids = recording.AllocateUpload( draws.Length * sizeof( uint ), 16 );
		var idValues = MemoryMarshal.Cast<byte, uint>( ids.Data );
		for ( int d = 0; d < draws.Length; d++ )
			idValues[d] = skinned ? (uint)Math.Max( obj.SkinSlots.Length > draws[d].Mesh ? obj.SkinSlots[draws[d].Mesh] : 0, 0 ) : rigidSlot;

		var unrolled = UnrolledOf( model );
		var (overrideShader, overrideSurface) = material is null ? default : PrepareDraw( material );
		var own = objectAttributes?.Managed ?? objectAttributes?.ShadowSet?.Managed;
		var previousMaterial = drawer.Material;

		try
		{
			for ( int d = 0; d < draws.Length; d++ )
			{
				var draw = draws[d];
				if ( draw.IndexCount < 3 ) continue;

				// The override's shader with its file's values, else the draw's own material; a surface where the material is one
				ShaderFile shader = null;
				SurfaceMaterial surface = null;
				if ( overrideShader is not null )
				{
					shader = overrideShader;
				}
				else if ( overrideSurface is not null )
				{
					surface = overrideSurface;
				}
				else if ( (obj.MaterialOverride ?? draw.Material) is BackendMaterial drawMaterial && drawMaterial.Shader is not null )
				{
					shader = drawMaterial.Shader;
					drawer.Material = drawMaterial;
				}
				else if ( (obj.MaterialOverride ?? draw.Material) is SurfaceMaterial drawSurface )
				{
					surface = drawSurface;
				}
				else continue;

				var vertexCache = skinned && mesh.SkinFor( draw.Mesh ) is { } skin && (skin.UsesVertexCache || obj.IsDeformed || (obj.MorphSource is not null && skin.Morphs));
				if ( unrolled.For( this, model, draw, vertexCache ) is not { } geometry ) continue;

				var lowered = Lower( attributes ?? blockAttributes );
				if ( own is not null ) Lower( own );
				lowered.Set( "g_TransformBuffer", transforms );
				lowered.Set( UncompressedTangentFrame, geometry.UncompressedTangentFrame ? 1.0f : 0.0f );
				if ( obj.IsSkinned ) lowered.SetCombo( "D_CS_VERTEX_ANIMATION", vertexCache ? 1 : 0 );

				drawer.InstanceIds = (ids.Buffer, ids.Offset + d * sizeof( uint ));
				if ( surface is not null ) context.DrawSurface( surface, lowered, new GeometryDraw { Vertices = geometry.Vertices, Layout = geometry.Layout, Count = geometry.Count, Topology = PrimitiveTopology.TriangleList } );
				else context.DrawBuffer( shader, lowered, geometry.Vertices, 0, geometry.Layout, geometry.Count, PrimitiveTopology.TriangleList );
			}
		}
		finally
		{
			drawer.InstanceIds = default;
			drawer.Material = previousMaterial;
		}
	}

	readonly ConditionalWeakTable<Material, System.Runtime.CompilerServices.StrongBox<bool>> stencilMaterials = new();

	/// <summary>
	/// Whether a draw with <paramref name="material"/> through the engine's graphics context (its shader's first mode, its
	/// file's features) needs a stencil in the depth it draws into (<see cref="ShaderPipelineState.NeedsStencil"/>): a dynamic
	/// object's, known before the frame records. Read once per material.
	/// </summary>
	internal bool NeedsStencil( Material material )
	{
		if ( material is null ) return false;
		if ( stencilMaterials.TryGetValue( material, out var known ) ) return known.Value;

		var needs = false;
		try
		{
			if ( ShaderOf( material ) is { Modes.Count: > 0 } shader )
			{
				var file = MaterialValues ? MaterialFile( material ) : null;
				var features = file is not null && ReferenceEquals( file.Shader, shader ) ? file.Features : [];
				needs = services.Shaders.Variant( shader, shader.Modes[0].Name, features )?.State.NeedsStencil == true;
			}
		}
		catch ( System.IO.InvalidDataException )
		{
			// A combo that wasn't compiled doesn't draw
		}

		stencilMaterials.AddOrUpdate( material, new System.Runtime.CompilerServices.StrongBox<bool>( needs ) );
		return needs;
	}

	/// <summary>
	/// A model's draws with their triangles' vertices one after another, in the model's own vertex layout: what a module that
	/// expands triangles reads (each triangle's corners as one instance's streams), made the first time a draw is drawn
	/// again and freed after the model is. A vertex-cache draw's vertices also hold their vertex ids (<c>TEXCOORD14</c>,
	/// <c>TEXCOORD15</c>) as the cache's streams give them, the cache's place again as <c>TEXCOORD3</c> (which a skinned mesh
	/// never has), so an expanding stage's other corners read theirs four and eight up.
	/// </summary>
	sealed class Unrolled
	{
		public readonly Dictionary<(int Mesh, int Draw, int First, bool Cache), UnrolledDraw?> Draws = new();
		public System.Collections.Concurrent.ConcurrentQueue<BufferHandle> FreeQueue;

		public UnrolledDraw? For( BorealisGpuRenderBackend backend, BackendModel model, in RenderMesh.Draw draw, bool vertexCache )
		{
			var key = (draw.Mesh, draw.DrawCall, draw.StartIndex, vertexCache);
			if ( Draws.TryGetValue( key, out var made ) ) return made;
			try
			{
				made = backend.Unroll( model, draw, vertexCache );
			}
			catch ( Exception e ) when ( e is System.IO.IOException or System.IO.InvalidDataException or ArgumentOutOfRangeException )
			{
				Log.Warning( $"Borealis.Gpu: {model.File.Name} can't be unrolled: {e.Message}" );
				made = null;
			}

			Draws[key] = made;
			return made;
		}

		~Unrolled()
		{
			foreach ( var draw in Draws.Values )
			{
				if ( draw is { } d ) FreeQueue.Enqueue( d.Vertices );
			}
		}
	}

	readonly record struct UnrolledDraw( BufferHandle Vertices, VertexStreamLayout Layout, int Count, bool UncompressedTangentFrame );

	readonly ConditionalWeakTable<BackendModel, Unrolled> unrolledModels = new();
	readonly Dictionary<(BackendVertexLayout, bool), VertexStreamLayout> unrolledLayouts = new();

	Unrolled UnrolledOf( BackendModel model )
	{
		if ( unrolledModels.TryGetValue( model, out var unrolled ) ) return unrolled;
		unrolled = new Unrolled { FreeQueue = freedModelBuffers };
		unrolledModels.Add( model, unrolled );
		return unrolled;
	}

	UnrolledDraw? Unroll( BackendModel model, in RenderMesh.Draw draw, bool vertexCache )
	{
		var file = model.File;
		if ( draw.Mesh >= file.Meshes.Count ) return null;
		var mesh = file.Meshes[draw.Mesh];
		if ( draw.DrawCall >= mesh.DrawCalls.Count ) return null;

		var call = mesh.DrawCalls[draw.DrawCall];
		var geometry = ((IBackendGeometry)model).For( draw.Mesh, draw.DrawCall );
		VertexCacheDraw? cache = vertexCache ? ((ISkinnedGeometry)model).VertexCacheFor( draw.Mesh, draw.DrawCall ) : null;
		if ( vertexCache && cache is null ) return null;

		var (vertexBuffer, vertexOffset) = call.VertexBuffers.Count > 0 ? call.VertexBuffers[0] : (0, 0);
		var (indexBuffer, indexOffset) = call.IndexBuffer;
		if ( vertexBuffer >= mesh.VertexBuffers.Count || indexBuffer >= mesh.IndexBuffers.Count ) return null;

		var source = mesh.VertexBuffers[vertexBuffer];
		var indices = mesh.IndexBuffers[indexBuffer];
		var count = draw.IndexCount / 3 * 3;
		var firstIndex = indexOffset / indices.Size + draw.StartIndex;
		if ( count == 0 || firstIndex + count > indices.Count ) return null;

		var stride = source.Stride;
		var outStride = stride + (vertexCache ? 8 : 0);

		// Padded by a triangle, as CPU vertices are: an expanding draw's per-instance streams step a triangle at a time
		var data = new byte[(count + 3) * outStride];
		var sourceData = source.Data;
		var indexData = indices.Data;
		for ( int k = 0; k < count; k++ )
		{
			var index = indices.Size == 2 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( indexData.AsSpan( (firstIndex + k) * 2 ) ) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( indexData.AsSpan( (firstIndex + k) * 4 ) );
			var vertex = call.BaseVertex + (int)index;
			var at = vertexOffset + (long)vertex * stride;
			if ( vertex < 0 || at + stride > sourceData.Length ) return null;
			sourceData.AsSpan( (int)at, stride ).CopyTo( data.AsSpan( k * outStride ) );

			if ( cache is { } c )
			{
				BitConverter.TryWriteBytes( data.AsSpan( k * outStride + stride ), (float)(c.FirstVertexId + vertex) );
				BitConverter.TryWriteBytes( data.AsSpan( k * outStride + stride + 4 ), (float)(c.FirstCacheVertex + vertex) );
			}
		}

		// The older 8:8:8:8 normals are repacked as the model's own buffer was
		if ( source.LegacyTangentFrame && source.Find( "NORMAL" ) is { } normal ) TangentFrame.RepackLegacy( data, count, outStride, normal.Offset );

		var buffer = gpu.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Vertex | BufferUsage.CopyDestination, Name = $"{file.Name} {mesh.Name} unrolled {draw.DrawCall}" } );
		gpu.WriteBuffer( buffer, data );
		return new UnrolledDraw( buffer, UnrolledLayout( geometry.Layout, vertexCache ), count, geometry.UncompressedTangentFrame );
	}

	VertexStreamLayout UnrolledLayout( BackendVertexLayout layout, bool vertexCache )
	{
		if ( unrolledLayouts.TryGetValue( (layout, vertexCache), out var made ) ) return made;

		var fields = new List<BackendVertexField>();
		foreach ( var field in layout.Fields ) fields.Add( new BackendVertexField( field.Semantic, field.Index, field.Format, field.Offset ) );
		if ( vertexCache )
		{
			fields.Add( new BackendVertexField( VertexSemantic.TexCoord, 14, VertexFormat.Float, layout.Stride ) );
			fields.Add( new BackendVertexField( VertexSemantic.TexCoord, 15, VertexFormat.Float, layout.Stride + 4 ) );
			fields.Add( new BackendVertexField( VertexSemantic.TexCoord, 3, VertexFormat.Float, layout.Stride + 4 ) );
		}

		made = new VertexStreamLayout( layout.Stride + (vertexCache ? 8 : 0), [.. fields] );
		unrolledLayouts[(layout, vertexCache)] = made;
		return made;
	}
}
