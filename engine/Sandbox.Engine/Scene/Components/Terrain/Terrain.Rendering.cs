using ExCSS;
using System.Runtime.InteropServices;
using static Sandbox.ModelRenderer;

namespace Sandbox;

public partial class Terrain
{
	// I think I've made all of these public for the editor... Feels shitty
	public Texture HeightMap { get; private set; }
	public Texture ControlMap { get; private set; }

	Model _clipmapModel;
	int _clipMapLodLevels;
	int _clipMapLodExtentTexels;
	int _subdivisionFactor;
	int _subdivisionLodCount;

	private SceneObject _so { get; set; }

	/// <summary>
	/// Create buffers needed for terrain rendering, set sane empties, always bound
	/// </summary>
	void CreateBuffers()
	{
		TerrainBuffer ??= new( 1 );
		MaterialsBuffer ??= new( 64 );

		var gpuTerrain = new GPUTerrain()
		{
			Transform = Matrix.Identity,
			TransformInv = Matrix.Identity,
			HeightMapTextureID = 0,
			ControlMapTextureID = 0,
			Resolution = 1024,
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

		TerrainBuffer.SetData( new List<GPUTerrain>() { gpuTerrain } );
		MaterialsBuffer.SetData( gpuMaterials );
	}

	void CreateClipmapSceneObject()
	{
		if ( !Active || Application.IsHeadless )
			return;

		// These get created once
		CreateBuffers();

		Assert.NotNull( Scene );

		_so?.Delete();
		_so = null;

		var material = MaterialOverride ?? Material.Load( "materials/core/terrain.vmat" );

		var clipmapMesh = TerrainClipmap.GenerateMesh_DiamondSquare( ClipMapLodLevels, ClipMapLodExtentTexels, material, SubdivisionFactor, SubdivisionLodCount );
		_clipmapModel = Model.Builder.AddMesh( clipmapMesh ).Create();

		_so = new SceneObject( Scene.SceneWorld, _clipmapModel, WorldTransform );
		_so.Tags.SetFrom( GameObject.Tags );
		_so.Transform = WorldTransform;
		_so.Component = this;

		_so.Flags.IsOpaque = true;
		_so.Flags.IsTranslucent = false;

		_so.Batchable = false;

		_so.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
		_so.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;

		// If we have no textures, push a grid texture (SUCKS)
		_so.Attributes.SetCombo( "D_GRID", Storage?.Materials.Count == 0 );

		_so.Attributes.Set( "Terrain", TerrainBuffer );
		_so.Attributes.Set( "TerrainMaterials", MaterialsBuffer );

		// We want these accessible globally too, probably
		Scene.RenderAttributes.Set( "Terrain", TerrainBuffer );
		Scene.RenderAttributes.Set( "TerrainMaterials", MaterialsBuffer );

		_clipMapLodLevels = ClipMapLodLevels;
		_clipMapLodExtentTexels = ClipMapLodExtentTexels;
		_subdivisionFactor = SubdivisionFactor;
		_subdivisionLodCount = SubdivisionLodCount;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 0 )]
	private struct GPUTerrain
	{
		public Matrix Transform;
		public Matrix TransformInv;

		public int HeightMapTextureID;
		public int ControlMapTextureID;

		public float Resolution;
		public float HeightScale;

		public bool HeightBlending;
		public float HeightBlendSharpness;
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

		var transform = Matrix.FromTransform( WorldTransform );

		var gpuTerrain = new GPUTerrain()
		{
			Transform = transform,
			TransformInv = transform.Inverted,
			HeightMapTextureID = HeightMap?.Index ?? 0,
			ControlMapTextureID = ControlMap?.Index ?? 0,
			Resolution = Storage.TerrainSize / Storage.Resolution,
			HeightScale = Storage.TerrainHeight,
			HeightBlending = Storage.MaterialSettings.HeightBlendEnabled,
			HeightBlendSharpness = Storage.MaterialSettings.HeightBlendSharpness
		};

		// Upload to the GPU buffer
		TerrainBuffer.SetData( new List<GPUTerrain>() { gpuTerrain } );
	}

	/// <summary>
	/// Upload the Terrain buffer, this should be called when materials are added, removed or modified.
	/// </summary>
	public unsafe void UpdateMaterialsBuffer()
	{
		// No GPU, no GPU buffer..
		if ( Application.IsHeadless )
			return;

		if ( Storage is null )
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
