using NativeEngine;

namespace Sandbox;

[Expose]
public sealed class SceneCubemap : SceneLight
{
	public enum ProjectionMode
	{
		[Icon( "panorama_photosphere" )]
		Sphere = 0,

		[Icon( "check_box_outline_blank" )]
		Box = 1,
	}

	private CEnvMapSceneObject envMap;

	internal SceneCubemap( HandleCreationData d ) : base( d )
	{
	}

	public SceneCubemap( SceneWorld sceneWorld ) : this( sceneWorld, default, BBox.FromPositionAndSize( 0, 1000 ) )
	{
	}

	public SceneCubemap( SceneWorld sceneWorld, Texture texture, BBox bounds ) : this( sceneWorld, texture, bounds, Transform.Zero, Color.White, 0.25f, 1 )
	{
	}

	internal SceneCubemap( SceneWorld sceneWorld, Texture texture, BBox bounds, Transform transform, Color tint, float feathering, int projectionMode = 0 ) : base()
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateEnvMap( sceneWorld, projectionMode );
		}

		envMap.m_vColor = tint;
		envMap.m_flFeathering = feathering;
		envMap.m_vBoxProjectMins = bounds.Mins;
		envMap.m_vBoxProjectMaxs = bounds.Maxs;

		Transform = transform;
		envMap.CalculateBounds();

		if ( texture != null && texture.native.IsValid )
		{
			envMap.m_hEnvMapTexture = texture.native;
			envMap.CalculateRadianceSH();
			envMap.CalculateNormalizationSH();
			CSceneSystem.MarkEnvironmentMapObjectUpdated( this );
		}
	}

	public int Priority
	{
		get => envMap.m_nRenderPriority;
		set
		{
			if ( Priority == value )
				return;

			envMap.m_nRenderPriority = value;
			RenderDirty();
		}
	}

	public ProjectionMode Projection
	{
		get => envMap.m_nProjectionMode;
		set
		{
			if ( Projection == value )
				return;

			envMap.m_nProjectionMode = value;
			RenderDirty();
		}
	}

	public Color TintColor
	{
		get => envMap.m_vColor;
		set
		{
			if ( TintColor == value )
				return;

			envMap.m_vColor = value;
			RenderDirty();
		}
	}

	public float Feathering
	{
		get => envMap.m_flFeathering;
		set
		{
			if ( Feathering == value )
				return;

			envMap.m_flFeathering = value;
			RenderDirty();
		}
	}
	public BBox ProjectionBounds
	{
		get => new BBox( envMap.m_vBoxProjectMins, envMap.m_vBoxProjectMaxs );
		set
		{
			if ( ProjectionBounds == value )
				return;

			envMap.m_vBoxProjectMins = value.Mins;
			envMap.m_vBoxProjectMaxs = value.Maxs;

			RenderDirty();
		}
	}


	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );
		RenderDirty();
	}

	public Texture Texture
	{
		get => Texture.FromNative( envMap.m_hEnvMapTexture );
		set
		{
			if ( Texture == value )
				return;

			envMap.m_hEnvMapTexture = value?.native ?? default;
			RenderDirty();
		}
	}

	/// <summary>
	/// Marks the cubemap as dirty, to be re-rendered on the next render.
	/// </summary>
	public void RenderDirty()
	{
		envMap.CalculateBounds();
		envMap.CalculateRadianceSH();
		envMap.CalculateNormalizationSH();
		CSceneSystem.MarkEnvironmentMapObjectUpdated( this );
		RequiresUpdate = true;
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		envMap = (CEnvMapSceneObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		envMap = default;
		base.OnNativeDestroy();
	}

	internal bool RequiresUpdate = true;
}
