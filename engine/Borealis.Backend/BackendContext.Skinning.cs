using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// Skinning on Borealis.Gpu, as native's <c>RenderTools</c> skins: compute skinning
/// (<c>skinning_cs</c>) writes a skinned mesh's instances into the device's vertex cache with the same attributes and
/// instance constants (<c>DispatchModelMeshSkinning</c>), and vertex-cache draws read them back through the vertex ids
/// native binds (<c>BindModelDrawCallVertexIds</c>). One-weight meshes skin in the vertex shader, from the transform
/// buffer's bones, with no work here.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>Instances one dispatch skins: the length of <c>skinning_cs</c>'s <c>Instances_t</c>.</summary>
	const int MaxSkinningInstances = 256;

	/// <summary>Bytes of one instance's constants (<c>InstanceParams_t</c>).</summary>
	const int SkinningInstanceSize = 32;

	/// <summary>The vertex-cache draw being recorded, whose geometry and vertex ids <see cref="Draw"/> binds instead.</summary>
	VertexCacheDraw? cacheDraw;


	/// <summary>The attribute ids a skinning dispatch sets, found once: setting by id hashes no names.</summary>
	static class SkinningIds
	{
		public static readonly int BoneIdxBits = AttributeIds.Of( "BoneIdxBits" );
		public static readonly int BoneIdxOffset = AttributeIds.Of( "BoneIdxOffset" );
		public static readonly int BoneWeightOffset = AttributeIds.Of( "BoneWeightOffset" );
		public static readonly int CompositeMorphTextureAtlas = AttributeIds.Of( "CompositeMorphTextureAtlas" );
		public static readonly int DeformationAnchors = AttributeIds.Of( "DeformationAnchors" );
		public static readonly int DeformationVolumes = AttributeIds.Of( "DeformationVolumes" );
		public static readonly int InputVB = AttributeIds.Of( "InputVB" );
		public static readonly int InstanceCount = AttributeIds.Of( "InstanceCount" );
		public static readonly int Instances_t = AttributeIds.Of( "Instances_t" );
		public static readonly int MorphTextureAtlasWidth = AttributeIds.Of( "MorphTextureAtlasWidth" );
		public static readonly int NormalOffset = AttributeIds.Of( "NormalOffset" );
		public static readonly int OutputVB = AttributeIds.Of( "OutputVB" );
		public static readonly int TangentSpaceOffset = AttributeIds.Of( "TangentSpaceOffset" );
		public static readonly int UncompressedTangentFrame = AttributeIds.Of( "UncompressedTangentFrame" );
		public static readonly int VertexCount = AttributeIds.Of( "VertexCount" );
		public static readonly int VertexSize = AttributeIds.Of( "VertexSize" );
		public static readonly int g_TransformBuffer = AttributeIds.Of( "g_TransformBuffer" );
	}

	ShaderDrawer skinningDrawer;
	ShaderAttributes skinningAttributes;
	byte[] skinningInstances;

	public override bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
		GpuBuffer volumes = null, ReadOnlySpan<int> volumeOffsets = default, ReadOnlySpan<int> volumeCounts = default, GpuBuffer anchors = null, ReadOnlySpan<int> anchorOffsets = default, bool morph = false )
	{
		if ( recording is null || skinningShader is not BackendMaterial { Shader: { } shader } || mesh.Geometry is not ISkinnedGeometry geometry || !geometry.Use() ) return false;
		if ( transforms?.Resource is not BackendBuffer transformBuffer || !device.VertexCacheBuffer.IsValid || slots.Length == 0 ) return false;

		var buffers = geometry.SkinnedBuffers( modelMesh );
		if ( buffers.Length == 0 ) return false;

		var deformed = volumes?.Resource is BackendBuffer && anchors?.Resource is BackendBuffer;
		CloseScope();

		skinningDrawer ??= new ShaderDrawer( device );
		var attributes = skinningAttributes ??= new ShaderAttributes();
		var instances = skinningInstances ??= new byte[MaxSkinningInstances * SkinningInstanceSize];
		var r = recording;

		// One dispatch per vertex buffer per 256 instances, as native batches them (RunVertexCacheUpdateComputeShader)
		foreach ( var vb in buffers )
		{
			if ( vb.VertexCount <= 0 ) continue;

			for ( int first = 0; first < slots.Length; first += MaxSkinningInstances )
			{
				var count = Math.Min( slots.Length - first, MaxSkinningInstances );

				attributes.Clear();
				attributes.SetById( SkinningIds.InstanceCount, count );
				attributes.SetById( SkinningIds.g_TransformBuffer, transformBuffer.Handle );
				attributes.SetById( SkinningIds.OutputVB, device.VertexCacheBuffer );
				attributes.SetById( SkinningIds.VertexCount, vb.VertexCount );
				attributes.SetCombo( "D_MORPH", morph ? 1 : 0 );
				if ( morph )
				{
					attributes.SetById( SkinningIds.CompositeMorphTextureAtlas, device.Morphs.Texture.Handle );
					attributes.SetById( SkinningIds.MorphTextureAtlasWidth, MorphAtlas.Width );
				}

				attributes.SetCombo( "D_DEFORMATION_VOLUME", deformed ? 1 : 0 );
				if ( deformed )
				{
					attributes.SetById( SkinningIds.DeformationVolumes, ((BackendBuffer)volumes.Resource).Handle );
					attributes.SetById( SkinningIds.DeformationAnchors, ((BackendBuffer)anchors.Resource).Handle );
				}

				attributes.SetById( SkinningIds.InputVB, vb.Buffer );
				attributes.SetById( SkinningIds.VertexSize, vb.Stride );
				attributes.SetById( SkinningIds.BoneIdxOffset, vb.BoneIndexOffset );
				attributes.SetById( SkinningIds.BoneIdxBits, vb.BoneIndexBits );
				attributes.SetById( SkinningIds.BoneWeightOffset, vb.BoneWeightOffset );
				attributes.SetById( SkinningIds.NormalOffset, vb.NormalOffset );
				attributes.SetById( SkinningIds.TangentSpaceOffset, vb.TangentOffset );
				attributes.SetById( SkinningIds.UncompressedTangentFrame, vb.CompressedTangentFrame ? 0 : 1 );

				// InstanceParams_t, as native fills it
				instances.AsSpan().Clear();
				var parameters = MemoryMarshal.Cast<byte, uint>( instances.AsSpan() );
				for ( int n = 0; n < count; n++ )
				{
					var at = n * (SkinningInstanceSize / 4);
					parameters[at + 0] = 0;
					parameters[at + 1] = (uint)(vertexCacheOffsets[first + n] + vb.CacheOffset);
					parameters[at + 2] = (uint)(slots[first + n] << 4) | (uint)(blendWeights & 0xF);
					parameters[at + 3] = (uint)vb.CacheOffset;
					if ( deformed )
					{
						parameters[at + 4] = (uint)volumeOffsets[first + n];
						parameters[at + 5] = (uint)volumeCounts[first + n];
						parameters[at + 6] = (uint)anchorOffsets[first + n];
					}
				}

				attributes.SetById( SkinningIds.Instances_t, instances );
				if ( !skinningDrawer.Dispatch( r.List, shader, attributes, vb.VertexCount, count, 1 ) ) return false;
			}
		}

		// The drawer bound its own pipeline and sets
		r.Pipeline = default;
		r.Binder = null;
		return true;
	}

	/// <summary>
	/// Composite the morphs queued this frame into the device's morph atlas, before skinning reads them
	/// (<c>RenderTools.GenerateMorphs</c>).
	/// </summary>
	public override void GenerateMorphs()
	{
		if ( recording is null || !device.Morphs.Pending ) return;
		CloseScope();

		skinningDrawer ??= new ShaderDrawer( device );

		// Placed and made as tall as they need, before any skinning dispatch binds it
		var atlas = device.Morphs.Fit();

		// A list recorded beside others that hasn't used the atlas yet needs it as a target at its start, unless the plan says
		// where the lists before leave it
		var local = recording.Layouts;
		var known = default( (ImageLayout First, ImageLayout Current) );
		var used = local is not null && local.TryGetValue( atlas, out known );
		if ( local is not null && !used && PlannedLayout( atlas, out var planned ) ) (known, used) = ((planned, planned), true);
		var layout = local is null ? atlas.Layout : used ? known.Current : ImageLayout.ColorTarget;
		if ( !device.Morphs.Generate( List, skinningDrawer, ref layout ) ) return;

		if ( local is not null ) local[atlas] = (used ? known.First : ImageLayout.ColorTarget, layout);
		else atlas.Layout = layout;
		recording.Pipeline = default;
		recording.Binder = null;
	}

	/// <summary>Before skinning: vertex shaders from earlier work that read the cache finish before compute writes it.</summary>
	public override void BarrierVertexCacheToWrite()
	{
		if ( recording is null ) return;
		CloseScope();
		List.Barrier( PipelineStage.VertexShader, MemoryAccess.ShaderRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite );
	}

	/// <summary>After skinning: the writes are visible to the vertex shaders that read the cache.</summary>
	public override void BarrierVertexCacheToRead()
	{
		if ( recording is null ) return;
		CloseScope();
		List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.VertexShader, MemoryAccess.ShaderRead );
	}

	/// <summary>
	/// Draw skinned instances from the vertex cache: the draw's own vertices for what the cache doesn't hold (uvs, blend
	/// weights), the instance ids, then the vertex ids the cache is read by, through material objects or by reflection as
	/// <see cref="DrawModel"/> draws.
	/// </summary>
	public override bool DrawVertexCache( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( mesh.Geometry is not ISkinnedGeometry geometry || !geometry.Use() || geometry.VertexCacheFor( draw.Mesh, draw.DrawCall ) is not { } from ) return false;

		device.AskTextures( material, textureSize );
		cacheDraw = from;
		try
		{
			return Draw( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, true, default );
		}
		finally
		{
			cacheDraw = null;
		}
	}

	/// <summary>A draw's geometry: its vertex-cache form while one is recorded.</summary>
	DrawGeometry GeometryFor( IBackendGeometry source, in RenderMesh.Draw draw ) => cacheDraw is { } cache ? cache.Geometry : source.For( draw.Mesh, draw.DrawCall );

	/// <summary>Streams 2 and 3 of a vertex-cache draw: vertex ids from its first vertex and from its place in the cache.</summary>
	void BindVertexIds( CommandList list )
	{
		if ( cacheDraw is not { } cache ) return;
		list.SetVertexBuffer( 2, device.VertexIds, cache.FirstVertexId * 4L );
		list.SetVertexBuffer( 3, device.VertexIds, cache.FirstCacheVertex * 4L );
		list.SetVertexBuffer( 4, device.Defaults.VertexDefaults );
	}
}
