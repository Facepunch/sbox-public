using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

/// <summary>
/// References a face handle and the mesh component it belongs to.
/// </summary>
public struct MeshFace : IMeshElement
{
	[Hide, JsonInclude] public MeshComponent Component { get; private set; }
	[Hide, JsonIgnore] public readonly FaceHandle Handle => Component.IsValid() ? Component.Mesh.FaceHandleFromIndex( HandleIndex ) : default;
	[Hide, JsonInclude] public int HandleIndex { get; set; }

	[Hide, JsonIgnore] public readonly bool IsValid => Component.IsValid() && Handle.IsValid;
	[Hide, JsonIgnore] public readonly Transform Transform => IsValid ? Component.WorldTransform : Transform.Zero;

	[Hide, JsonIgnore] public readonly Vector3 Center => IsValid ? Component.Mesh.GetFaceCenter( Handle ) : Vector3.Zero;

	public MeshFace( MeshComponent component, FaceHandle handle )
	{
		Component = component;
		HandleIndex = handle.Index;
	}

	public readonly override int GetHashCode() => HashCode.Combine( Component, nameof( MeshFace ), Handle );
	public override readonly string ToString() => IsValid ? $"{Component.GameObject.Name} Face {Handle}" : "Invalid Face";

	[JsonIgnore, Hide]
	public readonly Vector2 TextureOffset
	{
		get => IsValid ? Component.Mesh.GetTextureOffset( Handle ) : default;
		set => Component?.Mesh.SetTextureOffset( Handle, value );
	}

	[JsonIgnore, Hide]
	public readonly Vector2 TextureScale
	{
		get => IsValid ? Component.Mesh.GetTextureScale( Handle ) : default;
		set => Component?.Mesh.SetTextureScale( Handle, value );
	}

	[JsonIgnore, Hide]
	public readonly Material Material
	{
		get => IsValid ? Component.Mesh.GetFaceMaterial( Handle ) : default;
		set => Component?.Mesh.SetFaceMaterial( Handle, value );
	}

	[Hide]
	public readonly MeshVertex GetClosestVertex( Vector2 point, float maxDistance )
	{
		if ( !IsValid )
			return default;

		var transform = Transform;
		var minDistance = maxDistance;
		var closestVertex = VertexHandle.Invalid;

		foreach ( var vertex in Component.Mesh.GetFaceVertices( Handle ) )
		{
			var vertexPosition = transform.PointToWorld( Component.Mesh.GetVertexPosition( vertex ) );
			var vertexCoord = Gizmo.Camera.ToScreen( vertexPosition );
			var distance = vertexCoord.Distance( point );
			if ( distance < minDistance )
			{
				minDistance = distance;
				closestVertex = vertex;
			}
		}

		return new MeshVertex( Component, closestVertex );
	}

	[Hide]
	public readonly MeshEdge GetClosestEdge( Vector3 position, Vector2 point, float maxDistance )
	{
		if ( !IsValid )
			return default;

		if ( !Component.Mesh.GetFaceVerticesConnectedToFace( Handle, out var hEdges ) )
			return default;

		var transform = Transform;
		var minDistance = maxDistance;
		var hClosestEdge = HalfEdgeHandle.Invalid;

		foreach ( var hEdge in hEdges )
		{
			var line = Component.Mesh.GetEdgeLine( hEdge );
			line = new Line( transform.PointToWorld( line.Start ), transform.PointToWorld( line.End ) );
			var closestPoint = line.ClosestPoint( position );
			var pointCoord = Gizmo.Camera.ToScreen( closestPoint );
			var distance = pointCoord.Distance( point );
			if ( distance < minDistance )
			{
				minDistance = distance;
				hClosestEdge = hEdge;
			}
		}

		if ( !hClosestEdge.IsValid )
			return default;

		var hOppositeEdge = Component.Mesh.GetOppositeHalfEdge( hClosestEdge );
		if ( hClosestEdge.Index < hOppositeEdge.Index )
		{
			return new MeshEdge( Component, hClosestEdge );
		}
		else
		{
			return new MeshEdge( Component, hOppositeEdge );
		}
	}
}
