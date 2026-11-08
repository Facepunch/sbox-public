using NativeMapDoc;
using System;
using System.ComponentModel.DataAnnotations;

namespace Editor.MapDoc;

[Display( Name = "Map Game Object" ), Icon( "view_in_ar" )]
public sealed class MapGameObject : MapNode
{
	internal CMapGameObject mapGameObjectNative;

	internal MapGameObject( HandleCreationData _ ) { }

	public GameObject GameObject { get; private set; }

	public MapGameObject( MapDocument mapDocument = null, GameObject gameObject = null )
	{
		ThreadSafe.AssertIsMainThread();

		// Default to the active map document if none specificed
		mapDocument ??= MapEditor.Hammer.ActiveMap;

		Assert.IsValid( mapDocument );

		GameObject = gameObject;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			mapDocument.native.CreateGameObject( true );
		}
	}

	internal override void OnNativeInit( CMapNode ptr )
	{
		base.OnNativeInit( ptr );
		mapGameObjectNative = (CMapGameObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		base.OnNativeDestroy();
		mapGameObjectNative = default;
	}

	internal override void OnAddedToWorld( MapWorld world )
	{
		if ( _isInvalid )
		{
			Remove();
			return;
		}

		// Probably made through new MapGameObject( ... )
		if ( GameObject is not null )
		{
			// GameObject transform is authoritative in this case
			TransformChanged();
			mapGameObjectNative.SetGUID( GameObject.Id.ToString() );
			GameObject.Transform.OnTransformChanged += TransformChanged;
			return;
		}

		// Loaded, sync to existing gameobject
		var guid = mapGameObjectNative.GetGUID();
		if ( !string.IsNullOrEmpty( guid ) )
		{
			GameObject = world.Scene.Directory.FindByGuid( Guid.Parse( guid ) );
		}

		using var sceneScope = world.Scene.Push();

		// Are we copying?
		if ( _toCopyFrom is not null )
		{
			GameObject = _toCopyFrom.Clone();
			_toCopyFrom = null;
		}

		GameObject ??= new( true );
		GameObject.Transform.OnTransformChanged += TransformChanged;

		mapGameObjectNative.SetGUID( GameObject.Id.ToString() );

		// Native has transform authority, make sure it's where it wants it
		GameObject.WorldTransform = new Transform( Position, Rotation.From( Angles ), Scale );

		// If we have a map mesh, make sure we always have this component
		if ( GeneratesEntityModelGeometry )
		{
			var component = GameObject.GetOrAddComponent<HammerMesh>();
			GameObject.Components.Move( component, -10 ); // Push to top, even though this is very likely the only component
		}
	}

	internal override void OnRemovedFromWorld( MapWorld world )
	{
		if ( GameObject is null )
			return;

		GameObject.Transform.OnTransformChanged -= TransformChanged;
		GameObject.DestroyImmediate();
		GameObject = null;
	}

	private bool _isInvalid;
	internal override void PostLoadFromFile()
	{
		// some sanity
		var guid = mapGameObjectNative.GetGUID();
		if ( string.IsNullOrEmpty( guid ) )
		{
			Log.Warning( $"Loaded {this} that links to no GameObject from the map, something fucked it. Marking for deletion..." );
			_isInvalid = true;
		}
	}

	internal override void PreSaveToFile()
	{
		mapGameObjectNative.SetGUID( GameObject.Id.ToString() );
	}

	internal GameObject _toCopyFrom;


	/// <summary>
	/// Mapdoc creates a bunch of map nodes that live on the clipboard, undo system or just for transforming.
	/// These will almost never be part of a World until much later, so anything here should be treated as serialization only.
	/// And defer any actual logic until OnAddedToWorld.
	/// It's confusing, but it's how the entire datamodel works foundationally for Hammer and mapdoclib.
	/// </summary>
	internal override void OnCopyFrom( MapNode copyFrom, int flags )
	{
		// There is a special flags = 1 for undo
		// If we are on the undo stack, I think we should store the serialized gameobject

		if ( copyFrom is not MapGameObject fromMgo )
			return;

		_toCopyFrom = fromMgo.GameObject;

		// We might be a copy of a copy...
		if ( fromMgo.GameObject is null )
			_toCopyFrom = fromMgo._toCopyFrom;

		// Did we copy from an undo node, update position
		if ( GameObject is not null )
		{
			GameObject.WorldTransform = new Transform( Position, Rotation.From( Angles ), Scale );
		}
	}

	internal override void GetMimeData( DragData data )
	{
		data.Object = new[] { GameObject };
	}

	internal string GetGameObjectName()
	{
		return GameObject?.Name ?? "";
	}

	internal override void OnParentChanged( MapNode parent )
	{
		if ( !GameObject.IsValid() ) return;

		// Maybe jump up until we find a MapGameObject parent
		if ( parent is not MapGameObject parentMgo )
		{
			GameObject.SetParent( null );
			return;
		}

		var parentGo = parentMgo.GameObject;
		GameObject.SetParent( parentGo );
	}

	private bool _suppressTransformChanged = false;
	private bool _suppressNativeTransformChanged = false;
	void TransformChanged()
	{
		if ( _suppressTransformChanged ) return;

		_suppressNativeTransformChanged = true;
		Position = GameObject.WorldPosition;
		Angles = GameObject.WorldRotation.Angles();
		Scale = GameObject.WorldScale;
		_suppressNativeTransformChanged = false;

		// dispatches: event is when the user has changed core attributes (origin/angles/scales) by typing directly in the property editor
		native.CoreAttributeChanged();
	}

	internal override void OnNativeTransformChanged( Vector3 position, Angles angle, Vector3 scale )
	{
		if ( _suppressNativeTransformChanged ) return;

		if ( GameObject is null )
			return;

		_suppressTransformChanged = true;
		GameObject.WorldPosition = position;
		GameObject.WorldRotation = angle.ToRotation();
		GameObject.WorldScale = scale;
		_suppressTransformChanged = false;
	}

	internal override void OnSetEnabled( bool enabled )
	{
		if ( GameObject is null )
			return;

		GameObject.Enabled = enabled;
	}

	bool _hideUnselectedMeshes;

	/// <summary>
	/// Draw the meshes tied to this object only while they or it are selected, for meshes that only mark out a
	/// volume a component draws itself, like a <see cref="WaterBody"/>. Not saved.
	/// </summary>
	internal bool HideUnselectedMeshes
	{
		get => _hideUnselectedMeshes;
		set
		{
			if ( _hideUnselectedMeshes == value || mapGameObjectNative.IsNull )
				return;

			_hideUnselectedMeshes = value;
			mapGameObjectNative.SetHideUnselectedChildren( value );
		}
	}

	/// <summary>
	/// Called when meshes are tied to this GameObject
	/// </summary>
	internal void OnMeshesTied()
	{
		if ( GameObject is null )
			return;

		var component = GameObject.GetOrAddComponent<HammerMesh>();
		GameObject.Components.Move( component, -10 ); // Push to top, even though this is very likely the only component
	}
}
