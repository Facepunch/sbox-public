using Sandbox;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Editor;

/// <summary>Queries selectable objects, using Hammer's bounds broad phase and geometry containment tests.</summary>
public static class BoxSelection
{
	/// <summary>Hand an object click over to box selection while retaining the background gesture.</summary>
	public static void ReleaseObjectClick() => Gizmo.Active.ClearPress( releasePointer: false );

	public static HashSet<GameObject> Find( Scene scene, in Frustum frustum, bool partially = false ) => Find( scene, frustum, partially, true );

	/// <summary>Exclude handles when the viewport has gizmos disabled, regardless of their last drawn state.</summary>
	public static HashSet<GameObject> Find( Scene scene, in Frustum frustum, bool partially, bool includeGizmoHandles )
	{
		var result = new HashSet<GameObject>();
		ReadOnlySpan<Plane> planes = [frustum.LeftPlane, frustum.RightPlane, frustum.TopPlane, frustum.BottomPlane, frustum.NearPlane, frustum.FarPlane];
		foreach ( var plane in planes )
			if ( !plane.Normal.IsFinite || plane.Normal.IsNearZeroLength || !float.IsFinite( plane.Distance ) ) return result;
		var query = new Query( frustum, partially, includeGizmoHandles );
		// Resume siblings instead of pushing them all: storage follows tree depth, not scene width.
		var pending = new Stack<(GameObject Object, GameObject SelectionBase, int Child)>();
		pending.Push( (scene, null, 0) );
		while ( pending.TryPop( out var item ) )
		{
			var go = item.Object;
			if ( item.Child == 0 )
			{
				if ( !go.IsValid() || !go.Enabled || go.IsNetworkCulled || go.Flags.Contains( GameObjectFlags.Hidden ) || go.Tags.Has( "hidden" ) ) continue;
				var hit = go != scene && query.Contains( go );
				if ( go != scene && (hit || go.Children.Count > 0) && go.IsSelectionBase ) item.SelectionBase = go;
				if ( hit ) result.Add( item.SelectionBase ?? go );
			}
			if ( item.Child >= go.Children.Count ) continue;
			if ( item.Child + 1 < go.Children.Count ) pending.Push( (go, item.SelectionBase, item.Child + 1) );
			pending.Push( (go.Children[item.Child], item.SelectionBase, 0) );
		}

		return result;
	}

	/// <summary>Applies one completed drag. Shift adds, Ctrl removes, and Shift+Ctrl toggles.</summary>
	public static void Apply( SelectionSystem selection, HashSet<GameObject> inside, bool add, bool remove )
	{
		if ( !add && !remove )
			foreach ( var previous in selection.ToArray() )
				if ( previous is not GameObject go || !inside.Contains( go ) ) selection.Remove( previous );
		foreach ( var go in inside )
		{
			if ( remove && (!add || selection.Contains( go )) ) selection.Remove( go );
			else selection.Add( go );
		}
	}

	sealed class Query( Frustum frustum, bool partially, bool includeGizmoHandles )
	{
		Dictionary<(Model Model, ulong BodyGroups), Vector3[]> _modelPoints;

		static bool Visible( Component c ) => c.IsValid() && c.Active && !c.Flags.Contains( ComponentFlags.Hidden );
		static bool Renderable( Component c ) => c is Renderer and Component.IHasBounds || c is MeshComponent { Mesh: not null };

		static bool TryBounds( Component c, out BBox bounds )
		{
			if ( c is ModelRenderer renderer )
			{
				if ( renderer.BodyGroups == 0 ) { bounds = default; return false; }
				var hasGeometry = false;
				if ( renderer.Model is not null )
					foreach ( var modelMesh in renderer.Model.MeshInfo.Meshes )
						if ( Includes( modelMesh, renderer.BodyGroups ) ) { hasGeometry = true; break; }
				if ( renderer.Model is not null && !hasGeometry ) { bounds = default; return false; }
			}
			if ( c is MeshComponent { Mesh: not null } mesh )
			{
				if ( !mesh.Mesh.VertexHandles.Any() ) { bounds = default; return false; }
				bounds = mesh.Model is not null && !mesh.Mesh.IsDirty ? mesh.Model.RenderBounds : mesh.Mesh.CalculateBounds();
			}
			else if ( c is ModelRenderer { Model: null } missingModel )
			{
				var fallback = missingModel.SceneObject.IsValid() ? missingModel.SceneObject.Model : null;
				bounds = fallback?.RenderBounds ?? BBox.FromPositionAndSize( 0, 16 );
			}
			else if ( c is Component.IHasBounds bounded ) bounds = bounded.LocalBounds;
			else { bounds = default; return false; }
			return bounds.Mins.IsFinite && bounds.Maxs.IsFinite && bounds.Size.IsFinite;
		}

		internal bool Contains( GameObject go )
		{
			var handle = includeGizmoHandles && go.HasGizmoHandle;
			if ( handle && frustum.IsInside( go.WorldPosition ) == partially ) return partially;
			if ( go.Components.Count == 0 ) return handle && !partially;
			// Most objects have a single bounds contributor. Avoid collecting or merging it.
			if ( go.Components.Count == 1 )
			{
				var c = go.Components[0];
				return Visible( c ) && TryWorldBounds( c, out var local, out var world )
					? TestComponent( c, local, world ) : handle && !partially;
			}
			var renderable = false;
			for ( var i = 0; i < go.Components.Count; ++i )
				if ( Visible( go.Components[i] ) && Renderable( go.Components[i] ) ) { renderable = true; break; }
			BBox? worldBounds = null;
			for ( var i = 0; i < go.Components.Count; ++i )
			{
				var c = go.Components[i];
				if ( !Visible( c ) || renderable && !Renderable( c ) || !TryWorldBounds( c, out var local, out var world ) ) continue;
				worldBounds = worldBounds?.AddBBox( world ) ?? world;
			}
			if ( worldBounds is not { } box ) return handle && !partially;
			if ( !frustum.IsInside( box, true ) ) return false;
			if ( frustum.IsInside( box ) ) return true;
			for ( var i = 0; i < go.Components.Count; ++i )
			{
				var c = go.Components[i];
				if ( !Visible( c ) || renderable && !Renderable( c ) || !TryWorldBounds( c, out var local, out var world ) ) continue;
				if ( TestComponent( c, local, world ) == partially ) return partially;
			}
			return !partially;
		}

		static bool TryWorldBounds( Component c, out BBox local, out BBox world )
		{
			world = default;
			if ( !TryBounds( c, out local ) ) return false;
			world = local.Transform( c.WorldTransform );
			return world.Mins.IsFinite && world.Maxs.IsFinite && world.Size.IsFinite;
		}

		bool TestComponent( Component c, BBox local, BBox world )
		{
			if ( !frustum.IsInside( world, true ) ) return false;
			if ( frustum.IsInside( world ) ) return true;
			if ( c is MeshComponent { Mesh: not null } mesh ) return TestMesh( mesh );
			// Rest-model vertices do not represent an animated or ragdolled pose.
			if ( c is SkinnedModelRenderer ) return TestBounds( local, c.WorldTransform );
			if ( c is ModelRenderer { Model: not null } renderer ) return TestPoints( ModelPoints( renderer.Model, renderer.BodyGroups ), renderer.WorldTransform );
			return TestBounds( local, c.WorldTransform );
		}

		bool TestMesh( MeshComponent component )
		{
			var any = false;
			var transform = component.WorldTransform;
			foreach ( var vertex in component.Mesh.VertexHandles )
			{
				any = true;
				var point = transform.PointToWorld( component.Mesh.GetVertexPosition( vertex ) );
				if ( !point.IsFinite ) return false;
				if ( frustum.IsInside( point ) == partially ) return partially;
			}
			return any && !partially;
		}

		bool TestPoints( Vector3[] points, Transform transform )
		{
			if ( points is null ) return false;
			foreach ( var position in points )
			{
				var point = transform.PointToWorld( position );
				if ( !point.IsFinite ) return false;
				if ( frustum.IsInside( point ) == partially ) return partially;
			}
			return points.Length > 0 && !partially;
		}

		bool TestBounds( BBox bounds, Transform transform )
		{
			// Hammer's non-mesh leaf objects use their oriented bounds, even in intersection mode.
			for ( var i = 0; i < 8; ++i )
			{
				var corner = new Vector3( (i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x,
					(i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y, (i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z );
				if ( !frustum.IsInside( transform.PointToWorld( corner ) ) ) return false;
			}
			return true;
		}

		static bool Includes( Model.ModelMeshInfo.MeshData mesh, ulong bodyGroups ) => (mesh.LodMask & 1) != 0 && (mesh.MeshGroupMask & bodyGroups) != 0 && mesh.DrawCalls.Length > 0;

		Vector3[] ModelPoints( Model model, ulong bodyGroups )
		{
			_modelPoints ??= new();
			if ( _modelPoints.TryGetValue( (model, bodyGroups), out var points ) ) return points;
			var positions = new List<Vector3>();
			var meshes = model.MeshInfo.Meshes;
			for ( var m = 0; m < meshes.Length; ++m )
			{
				if ( !Includes( meshes[m], bodyGroups ) ) continue;
				for ( var d = 0; d < meshes[m].DrawCalls.Length; ++d )
				{
					if ( !model.TryReadDrawCall( m, d, out var vertices, out _, out var streams ) || (streams & VertexStreams.Position) == 0 )
						return _modelPoints[(model, bodyGroups)] = null;
					// Readback already compacts to the distinct vertices referenced by this draw.
					foreach ( var vertex in vertices ) positions.Add( vertex.Position );
				}
			}
			return _modelPoints[(model, bodyGroups)] = positions.ToArray();
		}
	}
}
