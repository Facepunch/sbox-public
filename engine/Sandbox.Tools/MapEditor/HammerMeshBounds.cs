using Editor.MapDoc;

namespace Editor.MapEditor;

/// <summary>
/// Hammer only fills a <see cref="HammerMesh"/>'s model when the map compiles. A <see cref="WaterBody"/> takes its
/// extent from the mesh tied to its object, so while the map is edited the tied meshes are measured here instead.
/// They only mark out the water, so they're hidden until selected.
/// </summary>
static class HammerMeshBounds
{
	static readonly List<WaterBody> _bodies = [];
	static readonly Dictionary<GameObject, MapGameObject> _nodes = [];
	static readonly HashSet<MapGameObject> _hidden = [];
	static readonly HashSet<MapGameObject> _seen = [];

	[EditorEvent.Frame]
	static void Frame()
	{
		if ( !Hammer.Open || Hammer.ActiveMap?.World is not { } world || world.Scene is not { } scene )
			return;

		_bodies.Clear();
		_seen.Clear();
		scene.GetAll( _bodies );
		foreach ( var body in _bodies )
		{
			if ( body.Components.Get<HammerMesh>( FindMode.EverythingInSelf ) is not { } hammerMesh )
				continue;

			if ( !_nodes.TryGetValue( body.GameObject, out var node ) || node.GameObject != body.GameObject )
			{
				FindNodes( world );
				if ( !_nodes.TryGetValue( body.GameObject, out node ) )
					continue;
			}

			var bounds = Measure( node );
			if ( hammerMesh.EditorBounds != bounds )
				hammerMesh.EditorBounds = bounds;

			node.HideUnselectedMeshes = body.Active;
			_seen.Add( node );
			_hidden.Add( node );
		}
		_bodies.Clear();

		// Show the meshes again once their object no longer has an active water body.
		_hidden.RemoveWhere( node =>
		{
			if ( _seen.Contains( node ) )
				return false;
			if ( node.GameObject.IsValid() )
				node.HideUnselectedMeshes = false;
			return true;
		} );
	}

	/// <summary>
	/// Map each game object to its node. Walking the map is slow, so this only runs when a body's node is unknown.
	/// </summary>
	static void FindNodes( MapWorld world )
	{
		_nodes.Clear();
		foreach ( var node in world.Children.OfType<MapGameObject>() )
		{
			if ( node.GameObject.IsValid() )
				_nodes[node.GameObject] = node;
		}
	}

	/// <summary>
	/// Bounds of the meshes tied to the node, in its game object's local space. Hidden meshes count too.
	/// </summary>
	static BBox? Measure( MapGameObject node )
	{
		var transform = node.GameObject.WorldTransform;
		var points = node.Children.OfType<MapMesh>()
			.SelectMany( x => x.GetFaces() ).SelectMany( x => x.Vertices )
			.Select( x => transform.PointToLocal( x ) ).ToArray();
		return points.Length == 0 ? null : BBox.FromPoints( points );
	}
}
