using Sandbox.Rendering;
using System.Runtime.InteropServices;
using static Sandbox.ModelRenderer;

namespace Sandbox;

public partial class Terrain
{
	// I think I've made all of these public for the editor... Feels shitty
	public Texture HeightMap { get; private set; }
	public Texture ControlMap { get; private set; }

	// Baked geometric normal map, updated by a compute pass whenever the heightmap changes
	internal Texture NormalMap { get; private set; }
	ComputeShader _normalBakeCs;
	bool _normalBakeDirty;

	private TerrainClipmapSceneObject _so { get; set; }

	// World units between heightmap texels
	float UnitsPerTexel => Storage.TerrainSize / Storage.Resolution;

	/// <summary>
	/// Create buffers needed for terrain rendering, set sane empties, always bound
	/// </summary>
	void CreateBuffers()
	{
		if ( TerrainBuffer != null )
			return;

		TerrainBuffer = new( 1 );
		MaterialsBuffer = new( 64 );

		// GPU allocation can fail (e.g. test runners without a render device)
		if ( !TerrainBuffer.IsValid || !MaterialsBuffer.IsValid )
			return;

		var gpuTerrain = new GPUTerrain()
		{
			Transform = Matrix.Identity,
			TransformInv = Matrix.Identity,
			HeightMapTextureID = 0,
			ControlMapTextureID = 0,
			UnitsPerTexel = 1024,
			HeightScale = 1024,
			HeightBlending = false,
			HeightBlendSharpness = 0
		};

		var gpuMaterials = new GPUTerrainMaterial[64];
		for ( int i = 0; i < 64; i++ )
		{
			gpuMaterials[i] = new GPUTerrainMaterial
			{
				BCRTextureID = 0,
				NHOTextureID = 0,
				UVScale = 1.0f,
				Metalness = 0.0f,
				NormalStrength = 1.0f,
				HeightBlendStrength = 1.0f,
				DisplacementScale = 0.0f,
				Flags = TerrainFlags.None,
			};
		}

		TerrainBuffer.SetData( MemoryMarshal.CreateReadOnlySpan( ref gpuTerrain, 1 ) );
		MaterialsBuffer.SetData( gpuMaterials );
	}

	void CreateClipmapSceneObject()
	{
		if ( !Active || Application.IsHeadless )
			return;

		// These get created once
		CreateBuffers();

		Assert.NotNull( Scene );

		BackupRenderAttributes( _so?.Attributes );
		_so?.Delete();
		_so = null;

		var material = MaterialOverride ?? Material.Load( "materials/core/terrain.vmat" );

		// Extent must be a whole number of blocks per side; snap it down if it isn't.
		int blocksPerSide = Math.Max( 1, ClipMapLodExtentTexels / BlockSize );
		int extentCells = blocksPerSide * BlockSize;

		var meshlets = TerrainClipmap.BuildLayout( ClipMapLodLevels, extentCells, BlockSize );

		_so = new TerrainClipmapSceneObject( Scene.SceneWorld )
		{
			UnitsPerTexel = UnitsPerTexel,
			HeightScale = Storage.TerrainHeight,
		};

		// How many of the finest LOD levels carry the subdivided (displaceable) mesh
		int displacedLevels = SubdivisionFactor > 1 ? Math.Min( SubdivisionLodCount, ClipMapLodLevels ) : 0;
		_so.Build( meshlets, BlockSize, SubdivisionFactor, displacedLevels, material );

		_so.Tags.SetFrom( GameObject.Tags );
		_so.Transform = WorldTransform;
		_so.Component = this;

		_so.Flags.IsOpaque = true;
		_so.Flags.IsTranslucent = false;

		_so.Batchable = false;

		_so.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
		_so.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;

		RestoreRenderAttributes( _so.Attributes );

		// If we have no textures, push a grid texture (SUCKS)
		_so.Attributes.SetCombo( "D_GRID", Storage?.Materials.Count == 0 );

		_so.Attributes.Set( "TerrainHasHoles", _hasHoles );

		_so.Attributes.Set( "Terrain", TerrainBuffer );
		_so.Attributes.Set( "TerrainMaterials", MaterialsBuffer );
		TerrainClipmap.GetMorphBand( extentCells, BlockSize, out float morphStartCells, out float morphEndCells );
		_so.Attributes.Set( "ClipMorphStartCells", morphStartCells );
		_so.Attributes.Set( "ClipMorphEndCells", morphEndCells );

		// Displacement fades to zero by the outer edge of the subdivided region, so the flat far tier never abuts displaced terrain
		float fadeDist = (extentCells / 2.0f) * UnitsPerTexel * (1 << (Math.Max( displacedLevels, 1 ) - 1));
		_so.Attributes.Set( "DisplacementFadeDist", fadeDist );

		// We want these accessible globally too, probably
		Scene.RenderAttributes.Set( "Terrain", TerrainBuffer );
		Scene.RenderAttributes.Set( "TerrainMaterials", MaterialsBuffer );
		Scene.RenderAttributes.Set( "TerrainCount", 1 );
	}

	[StructLayout( LayoutKind.Sequential, Pack = 0 )]
	private struct GPUTerrain
	{
		public Matrix Transform;
		public Matrix TransformInv;

		public int HeightMapTextureID;
		public int ControlMapTextureID;

		public float UnitsPerTexel;
		public float HeightScale;

		public bool HeightBlending;
		public float HeightBlendSharpness;
		public int SamplerIndex;
		public int NormalMapTextureID;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct GPUTerrainMaterial
	{
		public int BCRTextureID;
		public int NHOTextureID;
		public float UVScale;
		public TerrainFlags Flags;
		public float Metalness;
		public float HeightBlendStrength;
		public float NormalStrength;
		public float DisplacementScale;
	}

	GpuBuffer<GPUTerrain> TerrainBuffer;
	GpuBuffer<GPUTerrainMaterial> MaterialsBuffer;

	/// <summary>
	/// Upload the Terrain buffer, this should be called when some base settings change.
	/// </summary>
	private void UpdateTerrainBuffer()
	{
		// No GPU, no GPU buffer..
		if ( Application.IsHeadless )
			return;

		if ( Storage is null )
			return;

		// Buffer not created yet, or its GPU allocation failed
		if ( TerrainBuffer is not { IsValid: true } )
			return;

		var transform = Matrix.FromTransform( WorldTransform );

		var gpuTerrain = new GPUTerrain()
		{
			Transform = transform,
			TransformInv = transform.Inverted,
			HeightMapTextureID = HeightMap?.Index ?? 0,
			ControlMapTextureID = ControlMap?.Index ?? 0,
			UnitsPerTexel = UnitsPerTexel,
			HeightScale = Storage.TerrainHeight,
			HeightBlending = Storage.MaterialSettings.HeightBlendEnabled,
			HeightBlendSharpness = Storage.MaterialSettings.HeightBlendSharpness,
			SamplerIndex = SamplerState.GetBindlessIndex( Storage.MaterialSettings.Sampler ),
			NormalMapTextureID = NormalMap?.Index ?? 0,
		};

		// Upload to the GPU buffer
		TerrainBuffer.SetData( MemoryMarshal.CreateReadOnlySpan( ref gpuTerrain, 1 ) );

		if ( _so.IsValid() )
		{
			_so.UnitsPerTexel = UnitsPerTexel;
			_so.HeightScale = Storage.TerrainHeight;
		}
	}

	/// <summary>
	/// Rebuild the baked normal map after height edits.
	/// </summary>
	void RebakeNormalMap()
	{
		if ( Application.IsHeadless || Storage is null )
			return;

		// Persistent UAV target, reused across bakes so the bindless index stays stable
		int res = Storage.Resolution;
		if ( NormalMap is null || NormalMap.Width != res )
		{
			NormalMap?.Dispose();

			NormalMap = Texture.Create( res, res, ImageFormat.RG1616 )
				.WithUAVBinding()
				.WithName( "terrain_normalmap" )
				.Finish();
		}

		// OnPreRender records the bake and the scene object runs it on the render thread.
		// The dirty flag coalesces rapid edits into one bake per frame.
		_normalBakeCs ??= new ComputeShader( "terrain/cs_terrain_normal" );
		_normalBakeDirty = true;

		// Resync the buffer's texture index (only changes if the texture was recreated)
		UpdateTerrainBuffer();
	}

	CommandList RecordNormalBake()
	{
		var cmd = new CommandList( "TerrainNormalBake" );
		cmd.Attributes.Set( "Heightmap", HeightMap );
		cmd.Attributes.Set( "OutputMap", NormalMap );
		cmd.Attributes.Set( "HeightScale", Storage.TerrainHeight );
		cmd.Attributes.Set( "UnitsPerTexel", UnitsPerTexel );

		cmd.DispatchCompute( _normalBakeCs, Storage.Resolution, Storage.Resolution, 1 );

		// Make the compute writes visible to the terrain shader sampling the texture
		cmd.UavBarrier( NormalMap );

		return cmd;
	}

	/// <summary>
	/// Upload the Materials buffer, this should be called when materials are added, removed or modified.
	/// </summary>
	public unsafe void UpdateMaterialsBuffer()
	{
		// No GPU, no GPU buffer..
		if ( Application.IsHeadless )
			return;

		if ( Storage is null )
			return;

		// Buffer not created yet, or its GPU allocation failed
		if ( MaterialsBuffer is not { IsValid: true } )
			return;

		var gpuMaterials = new GPUTerrainMaterial[64];

		for ( int i = 0; i < 64; i++ )
		{
			var layer = Storage.Materials.ElementAtOrDefault( i );

			gpuMaterials[i] = new GPUTerrainMaterial
			{
				BCRTextureID = layer?.BCRTexture?.Index ?? 0,
				NHOTextureID = layer?.NHOTexture?.Index ?? 0,
				UVScale = 1.0f / (layer?.UVScale ?? 1.0f),
				Metalness = layer?.Metalness ?? 0.0f,
				NormalStrength = 1.0f / (layer?.NormalStrength ?? 1.0f),
				HeightBlendStrength = layer?.HeightBlendStrength ?? 1.0f,
				DisplacementScale = layer?.DisplacementScale ?? 0.0f,
				Flags = layer?.Flags ?? TerrainFlags.None,
			};
		}

		MaterialsBuffer.SetData( gpuMaterials );

		// If we have no textures, push a grid texture (SUCKS)
		_so.Attributes.SetCombo( "D_GRID", Storage.Materials.Count == 0 );
	}
}
