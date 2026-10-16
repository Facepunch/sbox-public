using Editor.MapEditor;
using NativeMapDoc;
using System;
using System.ComponentModel.DataAnnotations;

namespace Editor.MapDoc;

internal enum MapNodeGetRootDocument
{
	/// <summary>
	/// Will return null if the root document is still being loaded 
	/// </summary>
	MustBeLoaded = 0,

	/// <summary>
	/// Will return the root document even if it is not completely loaded
	/// </summary>
	MayBeLoading = 1
};

/// <summary>
/// A common class used for all objects in the world object tree.
/// </summary>
[Display( Name = "Node" ), Icon( "help_outline" )]
public class MapNode : IHandle
{
	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal CMapNode native;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr ) => OnNativeInit( ptr );
	void IHandle.HandleDestroy() => OnNativeDestroy();
	bool IHandle.HandleValid() => !native.IsNull;
	#endregion

	internal MapNode() { }
	internal MapNode( HandleCreationData _ ) { }

	internal string GizmoId;

	internal virtual void OnNativeInit( CMapNode ptr )
	{
		native = ptr;
		GizmoId = Guid.NewGuid().ToString( "N" )[0..8];
	}

	internal virtual void OnNativeDestroy()
	{
		native = IntPtr.Zero;
	}

	/// <summary>
	/// A new node has been added to the world - does not happen when loaded from file
	/// </summary>
	internal virtual void OnAddedToWorld( MapWorld world )
	{

	}

	internal virtual void OnRemovedFromWorld( MapWorld world )
	{

	}

	internal virtual void PreSaveToFile()
	{

	}

	internal virtual void PostLoadFromFile()
	{

	}

	internal virtual void OnNativeTransformChanged( Vector3 position, Angles angle, Vector3 scale )
	{

	}

	internal virtual void OnSetEnabled( bool enabled )
	{

	}

	internal virtual void GetMimeData( DragData data )
	{

	}

	internal virtual void OnCopyFrom( MapNode copyFrom, int flags )
	{

	}

	internal virtual void OnParentChanged( MapNode parent )
	{

	}

	/// <summary>
	/// User specified name of this node
	/// </summary>
	public string Name
	{
		get => native.GetName();
		set
		{
			Assert.NotNull( value );
			native.SetName( value );
		}
	}

	/// <summary>
	/// Hammer's id for this node - unique within its <see cref="MapWorld"/> and saved with the map.
	/// Find the node again with <see cref="MapWorld.FindNode(int)"/>.
	/// </summary>
	public int NodeId => native.GetNodeID();

	/// <summary>
	/// Whether this node is part of the current selection.
	/// </summary>
	public bool IsSelected => native.IsSelected();

	/// <summary>
	/// Native C++ type name for this map node (nice for debug, might disappear at some point)
	/// </summary>
	public string TypeString => native.GetTypeString();

	/// <summary>
	/// World position of this map node.
	/// </summary>
	public Vector3 Position
	{
		get => native.GetOrigin();
		set => native.SetOrigin( value );
	}

	/// <summary>
	/// Euler angles of this map node. 
	/// </summary>
	public Angles Angles
	{
		get => native.GetAngles();
		set => native.SetAngles( value );
	}

	/// <summary>
	/// Non-uniform scalar for this map node.
	/// </summary>
	public Vector3 Scale
	{
		get => native.GetScales();
		set => native.SetScales( value );
	}

	/// <summary>
	/// The parent node, at the top level this will be the <see cref="MapWorld"/>
	/// </summary>
	public MapNode Parent
	{
		get => native.GetParent();
		set => native.SetParent( value );
	}

	/// <summary>
	/// Each MapNode can have many children. Children usually transform with their parents, etc.
	/// </summary>
	public IEnumerable<MapNode> Children
	{
		get
		{
			// TODO: Ideally we'd have a List<MapNode> in managed that gets updated from hooks, but getting that from mapdoclib is a huge pain.
			for ( int i = 0; i < native.GetChildCount(); i++ )
			{
				yield return native.GetChild( i );
			}
		}
	}

	/// <summary>
	/// Visibility of this MapNode, e.g if it's been hidden by the user
	/// </summary>
	public bool Visible
	{
		get => native.IsVisible();
		set => native.SetVisible( value );
	}

	/// <summary>
	/// The world this map node belongs to.
	/// </summary>
	public MapWorld World
	{
		get => native.GetParentWorld();
	}

	/// <summary>
	/// Map node types implement this to describe themselves and their children.
	/// e.g CMapEntity returns "Entity: entity_name" or CMapMesh returns "Mesh (2 faces)"
	/// </summary>
	public override string ToString()
	{
		return $"MapNode: {native.GetDescription()}";
	}

	/// <summary>
	/// Creates a copy of this map node and its children, alongside it in the same parent - the same
	/// as cloning it in Hammer. The copy gets new node ids and is kept for undo, so call
	/// <see cref="History.MarkUndoPosition"/> first. Returns null if the node can't be cloned.
	/// </summary>
	public MapNode Copy()
	{
		// Clone into whichever document owns us, we don't really deal with nodes outside of a mapdoc for now
		var doc = World?.worldNative.GetRootDocument( MapNodeGetRootDocument.MayBeLoading ) ?? Hammer.ActiveMap;

		return doc.native.CloneObject( this );
	}

	public void Remove()
	{
		var doc = World.worldNative.GetRootDocument( MapNodeGetRootDocument.MayBeLoading );
		doc.DeleteNode( this );
	}

	/// <summary>
	/// Does this map node generate models to use?
	/// </summary>
	public bool GeneratesEntityModelGeometry => native.GeneratesEntityModelGeometry();
}
