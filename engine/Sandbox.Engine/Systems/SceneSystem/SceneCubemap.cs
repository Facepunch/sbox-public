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

	// The settings, once managed code owns them: a cubemap it made, or one native code made (a tool's) that it has changed
	// since
	bool managed;
	int priority;
	ProjectionMode projection;
	Color tint;
	float feathering;
	BBox projectionBounds;
	Texture texture;

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

		MakeManagedLight( sceneWorld, NewDescription( TypeEnvironmentProbe ), Rendering.SceneObjectFlags.IsEnvMap | Rendering.SceneObjectFlags.IsLight | Rendering.SceneObjectFlags.IsIndirectLight );

		// What native's CreateEnvMap gave a new one: the projection asked for, no texture
		managed = true;
		projection = (ProjectionMode)projectionMode;
		this.tint = tint;
		this.feathering = feathering;
		projectionBounds = bounds;
		if ( texture != null && texture.native.IsValid ) this.texture = texture;

		Transform = transform;
		Calculate();

		if ( this.texture is not null ) NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	public int Priority
	{
		get => managed ? priority : envMap.m_nRenderPriority;
		set
		{
			if ( Priority == value )
				return;

			TakeOver();
			priority = value;
			RenderDirty();
		}
	}

	public ProjectionMode Projection
	{
		get => managed ? projection : envMap.m_nProjectionMode;
		set
		{
			if ( Projection == value )
				return;

			TakeOver();
			projection = value;
			RenderDirty();
		}
	}

	public Color TintColor
	{
		get => managed ? tint : envMap.m_vColor;
		set
		{
			if ( TintColor == value )
				return;

			TakeOver();
			tint = value;
			RenderDirty();
		}
	}

	public float Feathering
	{
		get => managed ? feathering : envMap.m_flFeathering;
		set
		{
			if ( Feathering == value )
				return;

			TakeOver();
			feathering = value;
			RenderDirty();
		}
	}
	public BBox ProjectionBounds
	{
		get => managed ? projectionBounds : new BBox( envMap.m_vBoxProjectMins, envMap.m_vBoxProjectMaxs );
		set
		{
			if ( ProjectionBounds == value )
				return;

			TakeOver();
			projectionBounds = value;
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
		get => managed ? texture : NativeTexture();
		set
		{
			if ( Texture == value )
				return;

			TakeOver();
			texture = value;
			RenderDirty();
		}
	}

	// A native-made cubemap's texture, by resource name (Texture.FromNativeResource), kept while native's stays the same
	Texture nativeTexture;
	IntPtr nativeTextureBinding;

	Texture NativeTexture()
	{
		if ( envMap.IsNull ) return null;

		var native = envMap.m_hEnvMapTexture;
		if ( native.IsNull ) return null;

		var binding = native.GetBindingPtr();
		if ( binding == nativeTextureBinding && nativeTexture is not null )
		{
			native.DestroyStrongHandle();
			return nativeTexture;
		}

		nativeTextureBinding = binding;
		return nativeTexture = Texture.FromNativeResource( native );
	}

	/// <summary>
	/// Marks the cubemap as dirty, to be re-rendered on the next render.
	/// </summary>
	public void RenderDirty()
	{
		WriteNative();
		Calculate();
		RequiresUpdate = true;

		// Renderers mirroring the scene read the texture and settings again (a probe's texture made anew)
		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	/// <summary>
	/// Managed code owns the settings from here: take what the native cubemap has (what native code set) once.
	/// </summary>
	void TakeOver()
	{
		if ( managed || envMap.IsNull ) return;
		priority = envMap.m_nRenderPriority;
		projection = envMap.m_nProjectionMode;
		tint = envMap.m_vColor;
		feathering = envMap.m_flFeathering;
		projectionBounds = new BBox( envMap.m_vBoxProjectMins, envMap.m_vBoxProjectMaxs );
		texture = Texture.FromNative( envMap.m_hEnvMapTexture );
		managed = true;
	}

	/// <summary>
	/// A native cubemap's projection box, which its bounds come from (<see cref="Calculate"/>): bounds are still the native
	/// object's.
	/// </summary>
	void WriteNative()
	{
		if ( !managed || envMap.IsNull ) return;
		envMap.m_vBoxProjectMins = projectionBounds.Mins;
		envMap.m_vBoxProjectMaxs = projectionBounds.Maxs;
	}

	/// <summary>
	/// The bounds from the projection box, as <c>CEnvMapSceneObject::CalculateBounds</c> works them out: here, or by a native
	/// cubemap.
	/// </summary>
	void Calculate()
	{
		if ( envMap.IsNull )
		{
			if ( managed ) KeepBounds( TransformBounds( Transform, projectionBounds ) );
			return;
		}

		envMap.CalculateBounds();
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

	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Borealis.EnvMapObject( null, default, Transform.Zero );

	/// <summary>
	/// Its placement, cube, box and blending, as native's binner packs a <c>CEnvMapSceneObject</c>.
	/// </summary>
	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		base.WriteRender( obj, change, render );
		if ( obj is not Borealis.EnvMapObject envMap ) return;

		if ( (change & Rendering.SceneObjectChange.Transform) != 0 )
			envMap.Transform = Transform;

		if ( (change & Rendering.SceneObjectChange.Settings) != 0 )
		{
			envMap.Cubemap = render.ProbeCube( this );
			envMap.ProjectionBounds = ProjectionBounds;
			envMap.Tint = TintColor;
			envMap.Feathering = Feathering;
			envMap.Priority = Priority;
		}
	}
}
