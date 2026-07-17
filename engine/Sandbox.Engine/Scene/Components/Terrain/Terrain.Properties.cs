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
				CreateClipmapSceneObject();
			}
		}
	}

	RenderAttributes _attributes;

	/// <summary>
	/// Attributes that are applied to the terrain based on the current material and shader.
	/// If the terrain is disabled, the changes are deferred until it is enabled again.
	/// Attributes are not saved to disk, and are not cloned when copying the terrain.
	/// </summary>
	public RenderAttributes Attributes
	{
		get
		{
			if ( _so.IsValid() )
			{
				return _so.Attributes;
			}
			_attributes ??= new RenderAttributes();
			return _attributes;
		}
	}

	/// <summary>
	/// Backup the specified RenderAttributes so we can restore them later with <see cref="RestoreRenderAttributes(RenderAttributes)"/>
	/// </summary>
	void BackupRenderAttributes( RenderAttributes attributes )
	{
		if ( attributes is null || !_so.IsValid() )
			return;

		_attributes ??= new RenderAttributes();
		attributes.MergeTo( _attributes );
	}

	/// <summary>
	/// Restore any attributes that were previously backed up with <see cref="BackupRenderAttributes(RenderAttributes)"/>
	/// </summary>
	void RestoreRenderAttributes( RenderAttributes attributes )
	{
		if ( _attributes is not null )
		{
			_attributes.MergeTo( attributes );
		}

		_attributes = null;
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

	// Clamp before comparing, so out-of-range assignments compare against the stored value and don't
	// rebuild the clipmap twice.
	void SetClipmapProperty( ref int field, int value, int min, int max )
	{
		value = value.Clamp( min, max );
		if ( field == value )
			return;

		field = value;
		CreateClipmapSceneObject();
	}

	[Property, Category( "Clipmap" ), Range( 1, 8 )]
	public int ClipMapLodLevels
	{
		get => _clipMapLodLevelsProperty;
		set => SetClipmapProperty( ref _clipMapLodLevelsProperty, value, 1, 8 );
	}

	[Property, Category( "Clipmap" ), Range( 16, 2048 )]
	public int ClipMapLodExtentTexels
	{
		get => _clipMapLodExtentTexelsProperty;
		set => SetClipmapProperty( ref _clipMapLodExtentTexelsProperty, value, 16, 2048 );
	}

	/// <summary>
	/// How many times to subdivide each block mesh (1 = one vertex per heightmap texel). Higher adds displacement
	/// detail at the cost of more vertices.
	/// </summary>
	[Property, Category( "Clipmap" ), Range( 1, 4 ), Title( "Subdivision Factor" )]
	public int SubdivisionFactor
	{
		get => _subdivisionFactorProperty;
		set => SetClipmapProperty( ref _subdivisionFactorProperty, value, 1, 4 );
	}

	/// <summary>
	/// Size (in finest-level texels) of one instanced meshlet block.
	/// </summary>
	internal const int BlockSize = 4;

	/// <summary>
	/// How many of the finest LOD levels get the subdivided mesh when SubdivisionFactor > 1.
	/// </summary>
	internal const int SubdivisionLodCount = 4;

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
