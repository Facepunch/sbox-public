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
	bool IHandle.HandleValid() => _alive || DrawnAfterDelete;

	#endregion

	/// <summary>
	/// The hand-over (<see cref="Rendering.RenderSync.Epoch"/>) of the frame that may still be drawing it when it was deleted on
	/// the main thread, else -1. That frame was handed over with it: on the render thread it's still valid for that frame's
	/// drawing, and its world keeps it until the next hand-over (<see cref="SceneWorld.DeletePendingObjects"/>).
	/// </summary>
	internal long DeletedEpoch = -1;

	bool DrawnAfterDelete => DeletedEpoch >= 0 && !_destroyed && DeletedEpoch == Rendering.RenderSync.Epoch && Rendering.RenderSync.OnRenderThread;

	RenderAttributes _attributes;
	public RenderAttributes Attributes
	{
		get
		{
			if ( _attributes is not null && (_alive || DrawnAfterDelete) ) return _attributes;
			if ( !_alive ) return null;

			// Managed only: Borealis reads them, and nothing native does
			_attributes = RenderAttributes.CreateManaged();

			NotifyChanged( Rendering.SceneObjectChange.Attributes );
			return _attributes;
		}
	}

	/// <summary>
	/// <see cref="Attributes"/> if anything has asked for them, without making them - for a renderer that chains them
	/// in (Borealis).
	/// </summary>
	internal RenderAttributes CreatedAttributes => _attributes;

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

		var flags = Rendering.SceneObjectFlags.CastShadows | Rendering.SceneObjectFlags.IsLoaded;
		MakeManaged( sceneWorld, model );
		_keptModel = model;
		StartResolvingDraws( model );
		FlagsAtCreation( model, flags );
		Tags.RemoveAll();
		Transform = transform;
		KeepRigidBounds();
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
		if ( _alive )
		{
			// Children go with it, and it leaves its parent, as native's CSceneSystem::DeleteSceneObject does
			if ( _children is not null )
			{
				foreach ( var child in _children.ToArray() )
				{
					child._parent = null;
					child.Delete();
				}

				_children = null;
			}

			_parent?.RemoveChild( this );

			World?.Render?.Deleted( this );
			if ( HasNativeObject ) CSceneSystem.DeleteSceneObjectAtFrameEnd( this );
			native = IntPtr.Zero; // avoid double delete etc

			// A frame handed over may still draw it
			if ( Rendering.RenderSync.Overlapping && !Rendering.RenderSync.OnRenderThread ) DeletedEpoch = Rendering.RenderSync.Epoch;
			_alive = false;

			// Gone from its world when the world's deletes run (SceneWorld.DeletePendingObjects, or the frame's end)
			World?.QueueDelete( this );
		}
	}

	bool _destroyed;

	internal virtual void OnNativeInit( CSceneObject ptr )
	{
		native = ptr;
		_alive = true;

		World = native.GetWorld();
		Assert.NotNull( World );

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneObjects.Add( this );
		}

		// Made in its renderer once its constructor is done
		World.Render?.Made( this );

		//Log.Info( $"Created SceneObject: {GetType().Name}" );
	}

	internal virtual void OnNativeDestroy()
	{
		native = IntPtr.Zero;

		// Native deleting an object managed code didn't (its world went, or native code deleted an object it made)
		Destroyed();
	}

	/// <summary>
	/// Out of its world and its renderer, once: when its world's queued deletes run after <see cref="Delete"/>, or when native
	/// destroys it first.
	/// </summary>
	internal virtual void Destroyed()
	{
		if ( _destroyed ) return;
		_destroyed = true;
		_alive = false;

		World.Render?.Destroyed( this );

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneObjects.Remove( this );
		}

		if ( _attributes != null )
		{
			// What a renderer copied stops matching anything (Borealis shares sets of equal values between objects)
			_attributes.ManagedValues?.Clear();
			if ( !_attributes.IsManaged ) _attributes.Set( default );
			_attributes = null;
		}

		//Log.Info( $"Destroyed SceneObject: {GetType().Name}" );
	}

	Transform _transform;
	bool _transformSet;

	/// <summary>
	/// Incremented whenever <see cref="Transform"/> actually changes. Cheap way for
	/// consumers (e.g. the shadow cache) to detect movement without storing a transform.
	/// </summary>
	internal int TransformVersion = 1;

	/// <summary>
	/// What it draws as in its world's render world on Borealis, its own: made when its world is first drawn, null before
	/// that, and for an object drawn another way (a sun, a sky, a probe volume) or not at all.
	/// </summary>
	internal Borealis.RenderObject RenderObject { get; set; }

	/// <summary>
	/// Write a change into its own render object (<see cref="OwnsRenderObject"/>) or its world's lighting, when its world is
	/// drawn; one that can't be written now (made on another thread or during a render, or before its render object is made)
	/// is written when its world's renderer next prepares a frame.
	/// </summary>
	internal void NotifyChanged( Rendering.SceneObjectChange change )
	{
		if ( World?.Render is not { } render || WroteRender( change, render ) ) return;
		render.Later( this, change );
	}

	/// <summary>
	/// Transform of this scene object, relative to its <see cref="Parent"/>, or <see cref="SceneWorld"/> if parent is not set.
	/// </summary>
	public Transform Transform
	{
		// Until it's set from here, native's: an object native made - a map's world geometry - is placed by native, and
		// read as default(Transform), zero scale and all
		get => _transformSet || native.IsNull ? _transform : native.GetCTransformUnaligned();
		set
		{
			if ( _transformSet && _transform == value )
				return;

			var previous = _children is null ? default : Transform;
			_transformSet = true;
			_transform = value;
			TransformVersion++;
			if ( WritesNativeObject( "SetTransform" ) ) native.SetTransform( value );
			UpdateRigidBounds();
			NotifyChanged( Rendering.SceneObjectChange.Transform );
			OnTransformChanged( value );
			MoveChildren( previous, value );
		}
	}

	/// <summary>
	/// Children added with <see cref="AddChild"/>, which move with this one. Kept here only.
	/// </summary>
	List<SceneObject> _children;
	SceneObject _parent;

	/// <summary>
	/// Move the children with this one, as native's <c>SceneObject_MirrorTransformToChildSceneObjectsRelative</c> moves them
	/// (<c>CHILD_SCENEOBJECT_INHERIT_TRANSFORM</c>): each one's transform taken out of this one's old one (<c>InverseTR</c>, an
	/// inverse for a uniform scale) and put into the new one. Each child's setter moves its own children.
	/// </summary>
	void MoveChildren( in Transform previous, in Transform now )
	{
		if ( _children is null || _children.Count == 0 ) return;

		// A zero scale has no inverse: native's leaves the scale out then, as this does by not moving them
		if ( !System.Numerics.Matrix4x4.Invert( Matrix.FromTransform( previous ), out var inverse ) ) return;
		var move = inverse * (System.Numerics.Matrix4x4)Matrix.FromTransform( now );

		foreach ( var child in _children.ToArray() )
		{
			if ( !child.IsValid() ) continue;
			child.Transform = ((Matrix)((System.Numerics.Matrix4x4)Matrix.FromTransform( child.Transform ) * move)).ExtractTransform();
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
		var bounds = WorldBounds;
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
		set
		{
			if ( WritesNativeObject( "SetBounds" ) ) native.SetBounds( value );
			var kept = _bounds;
			SetKeptBounds( value );

			// Copies set the box they already have every frame; the render object holds it, and follows a moved or reposed object itself
			if ( _boundsKind != BoundsKind.Native && _bounds == kept ) return;

			NotifyChanged( Rendering.SceneObjectChange.Bounds );
		}
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

	// Kept here once read from native (what native code made the object with) or set; an object native code made still gets
	// its visibility, which its native code reads
	bool? _renderingEnabled;
	Color? _tint;

	/// <summary>
	/// Read visibility and tint from native again next time: native code changes the objects it made behind managed
	/// code's back (a tool hiding a helper, tinting a selection, switching its worklight off).
	/// </summary>
	internal void ForgetNativeState()
	{
		_renderingEnabled = null;
		_tint = null;
	}

	/// <summary>
	/// Whether this scene object should render or not.
	/// </summary>
	public bool RenderingEnabled
	{
		get => _renderingEnabled ??= !HasNativeObject || native.IsRenderingEnabled();
		set
		{
			var writesNative = WritesNativeObject( "SetRenderingEnabled" );
			// Components set it every frame: an unchanged value would rewrite the render object each time
			if ( _renderingEnabled == value ) return;
			if ( writesNative ) native.SetRenderingEnabled( value );
			_renderingEnabled = value;
			NotifyChanged( Rendering.SceneObjectChange.Visibility );
		}
	}

	/// <summary>
	/// Color tint of this scene object.
	/// </summary>
	public Color ColorTint
	{
		get => _tint ??= HasNativeObject ? native.GetTintRGBA() : Color.White;
		set
		{
			WritesNativeObject( "SetTintRGBA" );
			var tint = NativeTint( value );

			// Copies set the tint they already have every frame, and each write goes through the render object
			if ( _tint is { } held && held == tint ) return;

			_tint = tint;
			NotifyChanged( Rendering.SceneObjectChange.Tint );
		}
	}

	/// <summary>
	/// A tint as native's <c>CSceneObject::SetTintRGBA</c> keeps it and <c>GetTintRGBA</c> gives it back: colour channels as
	/// bytes (the value times 255, cut to a byte as native's conversion cuts it, read back times 1/255 as native's build
	/// computes the division), alpha clamped to 0 to 1.
	/// </summary>
	internal static Color NativeTint( Color tint )
	{
		static float Channel( float value ) => unchecked((byte)(int)(value * 255.0f)) * (1.0f / 255.0f);
		return new Color( Channel( tint.r ), Channel( tint.g ), Channel( tint.b ), Math.Clamp( tint.a, 0.0f, 1.0f ) );
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
	public SceneObject Parent => _parent;

	/// <summary>
	/// Add a named child scene object to this one. The child scene object will have its parent set.
	/// </summary>
	/// <remarks>
	/// The name can be used to look up children by name, but it is not bound. (SceneObject_FindChild)
	/// </remarks>
	public void AddChild( string name, SceneObject child )
	{
		if ( !child.IsValid() || child._parent is not null )
			return;

		child._parent = this;
		(_children ??= new()).Add( child );
	}

	/// <summary>
	/// Unlink given scene object as a child from this one. The child scene object will have its parent set to null. It will not be deleted.
	/// </summary>
	public void RemoveChild( SceneObject child )
	{
		if ( !child.IsValid() || child._parent != this )
			return;

		child._parent = null;
		_children.Remove( child );
	}

	/// <summary>
	/// What a native buffer object (<c>CreateSceneObjectFromBuffers</c>: draws of its own, no model) is drawn as by
	/// renderers that draw models: a model of its draws, made by whoever draws it (<c>Editor.ToolSceneViews</c>). Null for
	/// any other object.
	/// </summary>
	internal Model BufferObjectModel { get; set; }

	/// <summary>
	/// The model this scene object will render.
	/// </summary>
	public Model Model
	{
		get
		{
			// Native's for an object native code made, which native code may give another model
			return _keptModel is not null || !HasNativeObject ? _keptModel : Model.FromNative( native.GetModelHandle() );
		}

		set
		{
			if ( value == Model )
				return;

			var model = value;

			if ( !model.HasRenderMeshes() ) model = Model.Error;

			if ( WritesNativeObject( "ChangeModel" ) ) MeshSystem.ChangeModel( this, model.native );
			_keptModel = model;

			// New mesh instances draw the model's materials; the override list and mesh groups stay
			if ( ResolvesDraws ) StartResolvingDraws( model );
			FlagsAtModelChange( model );
			if ( !ResolvesDraws && _boundsKind == BoundsKind.Rigid ) _boundsKind = BoundsKind.Native;
			ModelChangedBounds();
			NotifyChanged( Rendering.SceneObjectChange.Model );

			OnModelChanged();
		}
	}

	internal virtual void OnModelChanged()
	{
		_materialOverride = null;
	}

	/// <summary>
	/// Every object drawing <paramref name="model"/>, after it was reloaded: native's resource system gave each new mesh
	/// instances of it (<c>CModelTypeManager::FinalizeResource</c> calls <c>CMeshSystem::ChangeModel</c>), so their draws,
	/// flags and bounds are worked out again as after <see cref="Model"/> is set.
	/// </summary>
	internal static void ModelReloaded( Model model )
	{
		List<SceneObject> drawing = null;
		foreach ( var world in SceneWorld.All.ToArray() )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject._alive && sceneObject.Model == model ) (drawing ??= new()).Add( sceneObject );
				}
			}
		}

		foreach ( var sceneObject in drawing ?? [] )
			sceneObject.ModelReloaded();
	}

	void ModelReloaded()
	{
		if ( Model is not { } model ) return;

		// Native's new mesh instances draw the model's materials (no material group or mesh-instance override); the override
		// list, per index overrides and mesh groups stay
		if ( ResolvesDraws ) StartResolvingDraws( model );
		_materialOverride = null;
		FlagsAtModelChange( model );
		ModelChangedBounds();
		NotifyChanged( Rendering.SceneObjectChange.Model );
	}

	/// <summary>
	/// State of all bodygroups of this object's model. You might be looking for <see cref="SceneModel.SetBodyGroup"/>.
	/// </summary>
	public ulong MeshGroupMask
	{
		get => _meshGroupMask ??= HasNativeObject ? native.GetCurrentMeshGroupMask() : 0;
		set
		{
			if ( WritesNativeObject( "ResetMeshGroups" ) ) native.ResetMeshGroups( value );
			_meshGroupMask = value;
			NotifyChanged( Rendering.SceneObjectChange.Material );
		}
	}

	int? _lod;

	/// <summary>
	/// Override current LOD level, -1 to disable. Kept here as native keeps it (<c>CSceneObject::SetLOD</c>: anything outside
	/// 0 to 7 disables it), or read once from native for an object native code made.
	/// </summary>
	internal int LodOverride
	{
		get => _lod ??= HasNativeObject ? native.GetCurrentLODLevel() : -1;
		set
		{
			WritesNativeObject( "SetLOD" );
			_lod = value is < 0 or > 7 ? -1 : value;
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
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
		NotifyChanged( Rendering.SceneObjectChange.Material );

		if ( material != null && material.native.IsValid )
		{
			if ( WritesNativeObject( "SetMaterialOverrideForMeshInstances" ) ) native.SetMaterialOverrideForMeshInstances( material.native );
			OverrideMeshInstances( material );
			FlagsAtMaterialChange();
			return;
		}

		if ( WritesNativeObject( "SetMaterialOverrideForMeshInstances" ) ) native.SetMaterialOverrideForMeshInstances( default );
		OverrideMeshInstances( null );
		FlagsAtMaterialChange();
	}

	/// <summary>
	/// Clear all material replacements.
	/// </summary>
	public void ClearMaterialOverride()
	{
		ClearMaterialOverrideList();
		_materialOverride = default;
	}

	/// <summary>
	/// Clear the override list (<c>CSceneObject::ClearMaterialOverrideList</c>), leaving mesh-instance and per index overrides.
	/// </summary>
	internal void ClearMaterialOverrideList()
	{
		if ( WritesNativeObject( "ClearMaterialOverrideList" ) ) native.ClearMaterialOverrideList();

		// Native works the material flags out again only if there was something to clear, and not the translucency flags
		var had = _overrideList is { Count: > 0 };
		_overrideList?.Clear();
		if ( had ) FlagsAtMaterialChange( translucency: false );
		NotifyChanged( Rendering.SceneObjectChange.Material );
	}

	/// <summary>
	/// Override the model's material at <paramref name="index"/> in <see cref="Model.Materials"/>, or stop overriding it for
	/// null (<c>CSceneObject::SetMaterialOverrideByIndex</c>).
	/// </summary>
	internal void SetMaterialOverrideByIndex( int index, Material material )
	{
		if ( WritesNativeObject( "SetMaterialOverrideByIndex" ) ) native.SetMaterialOverrideByIndex( index, material?.native ?? default );
		SetIndexOverride( index, material is { native.IsValid: true } ? material : null );
		FlagsAtMaterialChange();
		NotifyChanged( Rendering.SceneObjectChange.Material );
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
		StringToken attribute = attributeName;
		if ( WritesNativeObject( "SetMaterialOverride" ) ) native.SetMaterialOverride( material?.native ?? IntPtr.Zero, attribute, attributeValue );
		SetOverrideEntry( material is { native.IsValid: true } ? material : null, attribute.Value, attributeValue );
		FlagsAtMaterialChange();
		NotifyChanged( Rendering.SceneObjectChange.Material );
	}

	/// <summary>
	/// Set material group to replace materials of the model as set up in ModelDoc.
	/// </summary>
	public void SetMaterialGroup( string name )
	{
		if ( WritesNativeObject( "SetMaterialGroup" ) ) native.SetMaterialGroup( name );
		if ( ApplyMaterialGroup( name ) ) FlagsAtMaterialChange();
		NotifyChanged( Rendering.SceneObjectChange.Material );
	}

	internal virtual void OnTransformChanged( in Transform tx )
	{

	}

	/// <summary>
	/// Whether this object may be batched with compatible objects. Different render attribute values
	/// automatically split batches; objects with equal attributes can still batch together.
	/// </summary>
	public bool Batchable
	{
		get => HasNativeObject ? !native.IsNotBatchable() : _batchable;
		set { if ( WritesNativeObject( "SetBatchable" ) ) native.SetBatchable( value ); _batchable = value; }
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
		if ( WritesNativeObject( "UpdateFlagsBasedOnMaterial" ) ) native.UpdateFlagsBasedOnMaterial();
		FlagsAtMaterialChange();
		NotifyChanged( Rendering.SceneObjectChange.Flags );
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

		// Kept on the object (CurrentFlags), as native's HasFlags: any of them
		internal bool HasFlag( Rendering.SceneObjectFlags f )
		{
			return (Object.CurrentFlags & f) != 0;
		}

		internal void SetFlag( Rendering.SceneObjectFlags f, bool val )
		{
			Object.ChangeFlags( val ? f : Rendering.SceneObjectFlags.None, f );
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

		/// <summary>
		/// Whether this object is a static, permanent part of the world that never moves.
		/// Static objects can use cheaper rendering paths (e.g. we dont render them on dynamic shadowmaps ).
		/// </summary>
		public bool IsStatic
		{
			get => HasFlag( Rendering.SceneObjectFlags.StaticObject );
			set => SetFlag( Rendering.SceneObjectFlags.StaticObject, value );
		}
	}

	public sealed override int GetHashCode() => base.GetHashCode();
	public sealed override bool Equals( object obj ) => base.Equals( obj );
}
