using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

/// <summary>
/// References a vertex handle and the mesh component it belongs to.
/// </summary>
public struct MeshVertex : IMeshElement
{
	[Hide, JsonInclude] public MeshComponent Component { get; private init; }
	[Hide, JsonIgnore] public readonly VertexHandle Handle => Component.IsValid() ? Component.Mesh.VertexHandleFromIndex( HandleIndex ) : default;
	[Hide, JsonInclude] public int HandleIndex { get; set; }

	[Hide, JsonIgnore] public readonly bool IsValid => Component.IsValid() && Handle.IsValid;
	[Hide, JsonIgnore] public readonly Transform Transform => IsValid ? Component.WorldTransform : Transform.Zero;

	[Hide, JsonIgnore] public readonly Vector3 PositionLocal => IsValid ? Component.Mesh.GetVertexPosition( Handle ) : Vector3.Zero;
	[Hide, JsonIgnore] public readonly Vector3 PositionWorld => IsValid ? Transform.PointToWorld( PositionLocal ) : Vector3.Zero;

	public MeshVertex( MeshComponent component, VertexHandle handle )
	{
		Component = component;
		HandleIndex = handle.Index;
	}

	public readonly override int GetHashCode() => HashCode.Combine( Component, nameof( MeshVertex ), Handle );
	public override readonly string ToString() => IsValid ? $"{Component.GameObject.Name} Vertex {Handle}" : "Invalid Vertex";
}
