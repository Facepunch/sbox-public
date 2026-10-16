using NativeMapDoc;
using System.ComponentModel.DataAnnotations;

namespace Editor.MapDoc;

/// <summary>
/// A map node which allows a target group and its children to be placed with a new position
/// and orientation in the world without creating a new copy.
/// 
/// Multiple MapInstance classes may reference the same target allowing it to be placed in
/// multiple locations, but allowing any edits to be applied to all instances.
/// </summary>
[Display( Name = "Instance" ), Icon( "content_copy" )]
public sealed class MapInstance : MapNode
{
	internal CMapInstance instanceNative;

	internal MapInstance( HandleCreationData _ ) { }

	public MapInstance( MapDocument mapDocument = null )
	{
		ThreadSafe.AssertIsMainThread();

		// Default to the active map document if none specificed
		mapDocument ??= MapEditor.Hammer.ActiveMap;

		Assert.IsValid( mapDocument );

		using var h = IHandle.MakeNextHandle( this );
		mapDocument.native.CreateMapInstance();
	}

	internal override void OnNativeInit( CMapNode ptr )
	{
		base.OnNativeInit( ptr );

		instanceNative = (CMapInstance)ptr;
	}

	internal override void OnNativeDestroy()
	{
		base.OnNativeDestroy();

		instanceNative = default;
	}

	/// <summary>
	/// The target map node this MapInstance references to copy.
	/// </summary>
	public MapNode Target
	{
		get => instanceNative.GetTarget();
		set => instanceNative.SetTarget( value );
	}

	/// <summary>
	/// For a prefab, the map file it places, like "maps/prefabs/door.vmap". Null for an instance of a group in the same map.
	/// </summary>
	public string TargetMapPath
	{
		get
		{
			var path = instanceNative.GetTargetMapPath();
			return string.IsNullOrEmpty( path ) ? null : path;
		}
	}
}
