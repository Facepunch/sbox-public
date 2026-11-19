using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

/// <summary>
/// References a edge handle and the mesh component it belongs to.
/// </summary>
public struct MeshEdge : IMeshElement, IEquatable<MeshEdge>
{
	[Hide, JsonInclude] public MeshComponent Component { get; private init; }
	[Hide, JsonIgnore] public readonly HalfEdgeHandle Handle => Component.IsValid() ? Component.Mesh.HalfEdgeHandleFromIndex( HandleIndex ) : default;
	[Hide, JsonInclude] public int HandleIndex { get; set; }

	[Hide, JsonIgnore] public readonly bool IsValid => Component.IsValid() && Handle.IsValid;
	[Hide, JsonIgnore] public readonly Transform Transform => IsValid ? Component.WorldTransform : Transform.Zero;

	[Hide, JsonIgnore] public readonly bool IsOpen => IsValid && Component.Mesh.IsEdgeOpen( Handle );
	[Hide, JsonIgnore] public readonly Line Line => IsValid ? Component.Mesh.GetEdgeLine( Handle ) : default;

	public MeshEdge( MeshComponent component, HalfEdgeHandle handle )
	{
		Component = component;
		HandleIndex = handle.IsValid && component.IsValid() && component.Mesh is not null
			? Math.Min( handle.Index, component.Mesh.GetOppositeHalfEdge( handle ).Index )
			: handle.Index;
	}

	[Hide, JsonIgnore]
	public readonly PolygonMesh.EdgeSmoothMode EdgeSmoothing
	{
		get => IsValid ? Component.Mesh.GetEdgeSmoothing( Handle ) : default;
		set => Component?.Mesh.SetEdgeSmoothing( Handle, value );
	}

	public readonly bool Equals( MeshEdge other ) => Component == other.Component &&
		(HandleIndex == other.HandleIndex || IsValid && Component.Mesh.GetOppositeHalfEdge( Handle ) == other.Handle);
	public readonly override bool Equals( object obj ) => obj is MeshEdge other && Equals( other );
	public readonly override int GetHashCode() => HashCode.Combine( Component, nameof( MeshEdge ),
		IsValid ? Math.Min( HandleIndex, Component.Mesh.GetOppositeHalfEdge( Handle ).Index ) : HandleIndex );
	public override readonly string ToString() => IsValid ? $"{Component.GameObject.Name} Edge {Handle}" : "Invalid Edge";
}
