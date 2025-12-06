using NativeEngine;

namespace Sandbox;

/// <summary>
/// A model scene object that can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public partial class SceneObject : IHandle
{
	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal CSceneObject native;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr ) => OnNativeInit( ptr );
	void IHandle.HandleDestroy() => OnNativeDestroy();
	bool IHandle.HandleValid() => !native.IsNull;

	#endregion

	RenderAttributes _attributes;
	public RenderAttributes Attributes
	{
		get
		{
			if ( native.IsNull ) return null;
			if ( _attributes == null )
			{
				_attributes = new RenderAttributes( native.GetAttributesPtrForModify() );
			}
			return _attributes;
		}
	}

	/// <summary>
	/// The scene world this object belongs to.
	/// </summary>
	public SceneWorld World { get; internal set; }

	internal SceneObject()
	{
		Tags = new Internal.SceneObjectTags( this );
		Flags = new SceneObjectFlagAccessor( this );
	}

	internal SceneObject( HandleCreationData _ ) : this()
	{
	}

	public SceneObject( SceneWorld sceneWorld, Model model ) : this( sceneWorld, model, Transform.Zero )
	{
	}

	public SceneObject( SceneWorld sceneWorld, Model model, Transform transform ) : this()
	{
		Assert.IsValid( sceneWorld );

		if ( !model.HasRenderMeshes() ) model = Model.Error;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			var flags = Rendering.SceneObjectFlags.CastShadows | Rendering.SceneObjectFlags.IsLoaded;
			var typeFlags = ESceneObjectTypeFlags.NONE;
			MeshSystem.CreateSceneObject( model.native, transform, null, flags, typeFlags, sceneWorld, 0x1 );
			Transform = transform;
		}
	}

	public SceneObject( SceneWorld sceneWorld, string modelName, Transform transform ) : this( sceneWorld, Model.Load( modelName ), transform )
	{
	}

	public SceneObject( SceneWorld sceneWorld, string modelName ) : this( sceneWorld, modelName, Transform.Zero )
	{
	}

	/// <summary>
	/// Delete this scene object. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( native.IsValid )
		{
			CSceneSystem.DeleteSceneObjectAtFrameEnd( this );
			native = IntPtr.Zero; // avoid double delete etc
		}
	}

	internal virtual void OnNativeInit( CSceneObject ptr )
	{
		native = ptr;

		World = native.GetWorld();
		Assert.NotNull( World );

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneObjects.Add( this );
		}

		//Log.Info( $"Created SceneObject: {GetType().Name}" );
	}

	internal virtual void OnNativeDestroy()
	{
		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneObjects.Remove( this );
		}

		native = IntPtr.Zero;

		if ( _attributes != null )
		{
			_attributes.Set( default );
			_attributes = null;
		}

		//Log.Info( $"Destroyed SceneObject: {GetType().Name}" );
	}

	Transform _transform;

	/// <summary>
	/// Transform of this scene object, relative to its <see cref="Parent"/>, or <see cref="SceneWorld"/> if parent is not set.
	/// </summary>
	public Transform Transform
	{
		get => _transform;
		set
		{
			if ( _transform == value )
				return;

			_transform = value;
			native.SetTransform( value );
			OnTransformChanged( value );
		}
	}

	/// <summary>
	/// Rotation of this scene object, relative to its <see cref="Parent"/>, or <see cref="SceneWorld"/> if parent is not set.
	/// </summary>
	public Rotation Rotation
	{
		get => Transform.Rotation;
		set
		{
			if ( Rotation == value )
				return;

			Transform = Transform.WithRotation( value );
		}
	}

	/// <summary>
	/// Position of this scene object, relative to its <see cref="Parent"/>, or <see cref="SceneWorld"/> if parent is not set.
	/// </summary>
	[Property( "origin" )]
	public Vector3 Position
	{
		get => Transform.Position;
		set
		{
			if ( Position == value )
				return;

			Transform = Transform.WithPosition( value );
		}
	}

	// We could do better here, sometimes sceneobjects are set to infinite bounds by design
	private BBox GetSafeBounds()
	{
		var bounds = native.GetBounds();
		var volume = bounds.Volume;

		if ( !float.IsNaN( volume ) && !float.IsInfinity( volume ) )
			return bounds;

		return BBox.FromPositionAndSize( Position );
	}

	/// <summary>
	/// Set or get the axis aligned bounding box for this object.
	/// </summary>
	public BBox Bounds
	{
		get => GetSafeBounds();
		set => native.SetBounds( value );
	}

	/// <summary>
	/// Set the axis aligned bounding box by transforming by this objects transform.
	/// </summary>
	public BBox LocalBounds
	{
		[Obsolete( "LocalBounds.get is incorrect and should not be used. It does not reverse the full transform." )]
		get => Bounds.Translate( -Position );
		set => Bounds = value.Transform( Transform );
	}

	/// <summary>
	/// Whether this scene object should render or not.
	/// </summary>
	public bool RenderingEnabled
	{
		get => native.IsRenderingEnabled();
		set => native.SetRenderingEnabled( value );
	}

	/// <summary>
	/// Color tint of this scene object.
	/// </summary>
	public Color ColorTint
	{
		get => native.GetTintRGBA();
		set => native.SetTintRGBA( value );
	}

	/// <summary>
	/// Clipping plane for this scene object. Requires <see cref="ClipPlaneEnabled"/> to be <c>true</c>.
	/// </summary>
	[Obsolete]
	public Plane ClipPlane;

	/// <summary>
	/// Whether or not to use the clipping plane defined in <see cref="ClipPlane"/>.
	/// </summary>
	[Obsolete]
	public bool ClipPlaneEnabled;

	/// <summary>
	/// Movement parent of this scene object, if any.
	/// </summary>
	public SceneObject Parent => native.GetParent();

	/// <summary>
	/// Add a named child scene object to this one. The child scene object will have its parent set.
	/// </summary>
	/// <remarks>
	/// The name can be used to look up children by name, but it is not bound. (SceneObject_FindChild)
	/// </remarks>
	public void AddChild( string name, SceneObject child )
	{
		if ( !child.IsValid() )
			return;

		native.AddChildObject( name, child, 0x02 );
	}

	/// <summary>
	/// Unlink given scene object as a child from this one. The child scene object will have its parent set to null. It will not be deleted.
	/// </summary>
	public void RemoveChild( SceneObject child )
	{
		if ( !child.IsValid() )
			return;

		native.RemoveChild( child );
	}

	/// <summary>
	/// The model this scene object will render.
	/// </summary>
	public Model Model
	{
		get
		{
			return Model.FromNative( native.GetModelHandle() );
		}

		set
		{
			if ( value == Model )
				return;

			var model = value;

			if ( !model.HasRenderMeshes() ) model = Model.Error;

			MeshSystem.ChangeModel( this, model.native );

			OnModelChanged();
		}
	}

	internal virtual void OnModelChanged()
	{
		_materialOverride = null;
	}

	/// <summary>
	/// State of all bodygroups of this object's model. You might be looking for <see cref="SceneModel.SetBodyGroup"/>.
	/// </summary>
	public ulong MeshGroupMask
	{
		get => native.GetCurrentMeshGroupMask();
		set => native.ResetMeshGroups( value );
	}

	/// <summary>
	/// Override current LOD level, -1 to disable.
	/// </summary>
	internal int LodOverride
	{
		set => native.SetLOD( value );
	}

	Material _materialOverride;

	/// <summary>
	/// Override all materials on this object's <see cref="Model"/>.
	/// </summary>
	public void SetMaterialOverride( Material material )
	{
		if ( _materialOverride == material )
			return;

		_materialOverride = material;

		if ( material != null && material.native.IsValid )
		{
			native.SetMaterialOverrideForMeshInstances( material.native );
			return;
		}

		native.SetMaterialOverrideForMeshInstances( default );
	}

	/// <summary>
	/// Clear all material replacements.
	/// </summary>
	public void ClearMaterialOverride()
	{
		native.ClearMaterialOverrideList();
		_materialOverride = default;
	}

	/// <summary>
	/// Replaces all materials of the model that have the given <b>User Material Attribute</b> set to <b>"1"</b>, with given material.
	///
	/// <para>The system checks both the models' default material group materials and the materials of the active material group.</para>
	/// </summary>
	/// <param name="material">Material to replace with.</param>
	/// <param name="attributeName">Name of the <b>User Material Attribute</b> to test on each material of the model. They are set in the Material Editor's <b>Attributes</b> tab.</param>
	/// <param name="attributeValue">Value of the attribute to test for.</param>
	public void SetMaterialOverride( Material material, string attributeName, int attributeValue = 1 )
	{
		native.SetMaterialOverride( material?.native ?? IntPtr.Zero, attributeName, attributeValue );
	}

	/// <summary>
	/// Set material group to replace materials of the model as set up in ModelDoc.
	/// </summary>
	public void SetMaterialGroup( string name )
	{
		native.SetMaterialGroup( name );
	}

	internal virtual void OnTransformChanged( in Transform tx )
	{

	}

	/// <summary>
	/// This object is not batchable by material for some reason ( example: has dynamic attributes that affect rendering )
	/// </summary>
	public bool Batchable
	{
		get => !native.IsNotBatchable();
		set => native.SetBatchable( value );
	}

	Component _component;

	/// <summary>
	/// For storing and retrieving the GameObject this SceneObject belongs to
	/// </summary>
	internal GameObject GameObject { get; set; }

	/// <summary>
	/// The component that created this object
	/// </summary>
	internal Component Component
	{
		get => _component;
		set
		{
			_component = value;
			GameObject = _component?.GameObject;
		}
	}

	[Obsolete( "Use Component property" )]
	public void SetComponentSource( Component c )
	{
		Component = c;
	}

	[Obsolete( "Use GameObject property" )]
	public GameObject GetGameObject() => GameObject;

	/// <summary>
	/// Updates flags like transparent/opaque based on object's material, this is usually called automatically.
	/// But some procedural workflows (mesh editor) may want to call this manually.
	/// </summary>
	internal void UpdateFlagsBasedOnMaterial()
	{
		native.UpdateFlagsBasedOnMaterial();
	}

	/// <summary>
	/// Access to various advanced scene object flags.
	/// </summary>
	public SceneObjectFlagAccessor Flags { get; internal set; }

	public class SceneObjectFlagAccessor
	{
		SceneObject Object;

		internal SceneObjectFlagAccessor( SceneObject obj )
		{
			Object = obj;
		}

		internal bool HasFlag( Rendering.SceneObjectFlags f )
		{
			return Object.native.HasFlags( f );
		}

		internal void SetFlag( Rendering.SceneObjectFlags f, bool val )
		{
			Object.native.ChangeFlags( val ? f : Rendering.SceneObjectFlags.None, f );
		}


		/// <summary>
		/// Whether this scene object should cast shadows.
		/// </summary>
		public bool CastShadows
		{
			get => HasFlag( Rendering.SceneObjectFlags.CastShadows );
			set => SetFlag( Rendering.SceneObjectFlags.CastShadows, value );
		}

		public bool IsOpaque
		{
			get => HasFlag( Rendering.SceneObjectFlags.IsOpaque );
			set => SetFlag( Rendering.SceneObjectFlags.IsOpaque, value );
		}

		public bool IsTranslucent
		{
			get => HasFlag( Rendering.SceneObjectFlags.IsTranslucent );
			set => SetFlag( Rendering.SceneObjectFlags.IsTranslucent, value );
		}

		[Obsolete( "SceneObject.IsDecal is obsolete" )]
		public bool IsDecal
		{
			get => HasFlag( Rendering.SceneObjectFlags.IsDecal );
			set => SetFlag( Rendering.SceneObjectFlags.IsDecal, value );
		}

		public bool OverlayLayer
		{
			get => HasFlag( Rendering.SceneObjectFlags.GameOverlayLayer );
			set => SetFlag( Rendering.SceneObjectFlags.GameOverlayLayer, value );
		}

		/// <summary>
		/// Don't render in the opaque/translucent game passes. This is useful when you
		/// want to only render in the Bloom layer, rather than additionally to it.
		/// </summary>
		public bool ExcludeGameLayer
		{
			get => HasFlag( Rendering.SceneObjectFlags.ExcludeGameLayer );
			set => SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, value );
		}

		public bool ViewModelLayer
		{
			get => HasFlag( Rendering.SceneObjectFlags.ViewModelLayer );
			set => SetFlag( Rendering.SceneObjectFlags.ViewModelLayer, value );
		}

		public bool SkyBoxLayer
		{
			get => HasFlag( Rendering.SceneObjectFlags.Skybox3DLayer );
			set => SetFlag( Rendering.SceneObjectFlags.Skybox3DLayer, value );
		}

		public bool NeedsLightProbe
		{
			get => HasFlag( Rendering.SceneObjectFlags.NeedsLightProbe );
			set => SetFlag( Rendering.SceneObjectFlags.NeedsLightProbe, value );
		}

		/// <summary>
		/// True if this object needs cubemap information
		/// </summary>
		public bool NeedsEnvironmentMap
		{
			get => HasFlag( Rendering.SceneObjectFlags.EnvironmentMapped );
			set => SetFlag( Rendering.SceneObjectFlags.EnvironmentMapped, value );
		}

		/// <summary>
		/// Automatically sets the "FrameBufferCopyTexture" attribute within the material.
		/// This does the same thing as Render.CopyFrameBuffer(); except automatically if
		/// the pass allows for it.
		/// </summary>
		public bool WantsFrameBufferCopy
		{
			get => HasFlag( Rendering.SceneObjectFlags.WantsFrameBufferCopyTexture );
			set => SetFlag( Rendering.SceneObjectFlags.WantsFrameBufferCopyTexture, value );
		}

		/// <summary>
		/// Draw this in cubemaps
		/// </summary>
		public bool IncludeInCubemap
		{
			get => !HasFlag( Rendering.SceneObjectFlags.HideInCubemaps );
			set => SetFlag( Rendering.SceneObjectFlags.HideInCubemaps, !value );
		}

		internal bool WantsExecuteBefore
		{
			get => HasFlag( Rendering.SceneObjectFlags.ExecuteBefore );
			set => SetFlag( Rendering.SceneObjectFlags.ExecuteBefore, value );
		}

		internal bool WantsExecuteAfter
		{
			get => HasFlag( Rendering.SceneObjectFlags.ExecuteAfter );
			set => SetFlag( Rendering.SceneObjectFlags.ExecuteAfter, value );
		}

		public bool WantsPrePass
		{
			get => HasFlag( Rendering.SceneObjectFlags.NoZPrepass ) == false;
			set => SetFlag( Rendering.SceneObjectFlags.NoZPrepass, !value );
		}
	}

	public sealed override int GetHashCode() => base.GetHashCode();
	public sealed override bool Equals( object obj ) => base.Equals( obj );
}
