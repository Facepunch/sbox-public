using static Sandbox.ModelRenderer;

namespace Sandbox;

public partial class Terrain
{
	private TerrainStorage _storage;

	[Property]
	public TerrainStorage Storage
	{
		get => _storage;
		set
		{
			if ( _storage == value ) return;

			_storage?.MaterialSettings?.OnChanged -= OnTerrainChanged;
			_storage = value;
			_storage?.MaterialSettings?.OnChanged += OnTerrainChanged;

			Create();
		}
	}

	/// <summary>
	/// Override the terrain rendering with your own material shader.
	/// This needs to be explicitly set up to work with the Terrain Shader API.
	/// </summary>
	private Material _materialOverride;
	[Property]
	public Material MaterialOverride
	{
		get => _materialOverride;
		set
		{
			_materialOverride = value;

			if ( _so.IsValid() )
			{
				_so.SetMaterialOverride( value );
			}
		}
	}

	/// <summary>
	/// Uniform world size of the width and length of the terrain.
	/// </summary>
	[Property, Group( "Size" )]
	public float TerrainSize
	{
		get => Storage is null ? 0.0f : Storage.TerrainSize;
		set
		{
			if ( Storage is null )
				return;

			Storage.TerrainSize = value;

			// Update the collider and the terrain rendering buffer
			Rebuild();
			UpdateTerrainBuffer();
		}
	}

	/// <summary>
	/// World size of the maximum height of the terrain.
	/// </summary>
	[Property, Group( "Size" )]
	public float TerrainHeight
	{
		get => Storage is null ? 0.0f : Storage.TerrainHeight;
		set
		{
			if ( Storage is null )
				return;

			Storage.TerrainHeight = value;

			// Update the collider and the terrain rendering buffer
			Rebuild();
			UpdateTerrainBuffer();
		}
	}

	private int _clipMapLodLevelsProperty = 6;
	private int _clipMapLodExtentTexelsProperty = 256;
	private int _subdivisionFactorProperty = 1;

	[Property, Category( "Clipmap" )]
	public int ClipMapLodLevels
	{
		get => _clipMapLodLevelsProperty;
		set
		{
			if ( _clipMapLodLevelsProperty == value )
				return;

			_clipMapLodLevelsProperty = value;

			// Rebuild clipmap mesh when LOD levels change
			CreateClipmapSceneObject();
		}
	}

	[Property, Category( "Clipmap" )]
	public int ClipMapLodExtentTexels
	{
		get => _clipMapLodExtentTexelsProperty;
		set
		{
			if ( _clipMapLodExtentTexelsProperty == value )
				return;

			_clipMapLodExtentTexelsProperty = value;

			// Rebuild clipmap mesh when extent changes
			CreateClipmapSceneObject();
		}
	}

	[Property, Category( "Clipmap" ), Range( 1, 4 ), Title( "Subdivision Factor" )]
	public int SubdivisionFactor
	{
		get => _subdivisionFactorProperty;
		set
		{
			if ( _subdivisionFactorProperty == value )
				return;

			_subdivisionFactorProperty = value;

			// Rebuild clipmap mesh when subdivision changes
			CreateClipmapSceneObject();
		}
	}

	private int _subdivisionLodCountProperty = 3;

	[Property, Category( "Clipmap" ), Range( 1, 6 ), Title( "Subdivision LOD Count" )]
	public int SubdivisionLodCount
	{
		get => _subdivisionLodCountProperty;
		set
		{
			if ( _subdivisionLodCountProperty == value )
				return;

			_subdivisionLodCountProperty = value;

			// Rebuild clipmap mesh when subdivision LOD count changes
			CreateClipmapSceneObject();
		}
	}

	private ModelRenderer.ShadowRenderType _renderType = ModelRenderer.ShadowRenderType.Off;

	[Title( "Cast Shadows" ), Property, Category( "Lighting" )]
	public ModelRenderer.ShadowRenderType RenderType
	{
		get => _renderType;
		set
		{
			_renderType = value;

			if ( !_so.IsValid() )
				return;

			_so.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
			_so.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		}
	}
}
