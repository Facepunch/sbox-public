using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using HalfEdgeMesh;

namespace Sandbox;

public sealed partial class MeshComponent
{
	[Flags]
	public enum MeshRenderFlags
	{
		None = 0,
		Wireframe = 1,
		Vertices = 2
	}

	[Hide, JsonIgnore]
	public MeshRenderFlags EditorRenderFlags
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;
			UpdateEditorRenderData();
		}
	}

	public event Action EditorRenderDataChanged;

	ToolRenderable _editorWireframe;
	ToolRenderable _editorEdges;
	ToolRenderable _editorVertices;
	PolygonMesh _editorMesh;
	Model _editorModel;
	int _editorTopologyVersion;
	VertexHandle[] _editorVertexHandles = [];
	readonly List<Vector3> _editorPositions = [];
	readonly List<Vector3> _editorVisiblePositions = [];
	readonly List<int> _editorEdgeIndices = [];
	Color _editorWireframeColor;
	bool _editorHidden;

	void DeleteEditorRenderData()
	{
		_editorWireframe?.Dispose();
		_editorEdges?.Dispose();
		_editorVertices?.Dispose();
		_editorWireframe = null;
		_editorEdges = null;
		_editorVertices = null;
		_editorMesh = null;
		_editorModel = null;
		_editorVertexHandles = [];
		_editorPositions.Clear();
		_editorVisiblePositions.Clear();
		_editorEdgeIndices.Clear();
		EditorRenderDataChanged?.Invoke();
	}

	void UpdateEditorRenderData()
	{
		if ( Scene?.IsEditor != true || !Active || Mesh is null || Model is null )
			return;

		var topologyChanged = _editorMesh != Mesh || _editorTopologyVersion != Mesh.TopologyVersion;
		var geometryChanged = topologyChanged || _editorModel != Model;
		if ( topologyChanged )
		{
			_editorVertexHandles = Mesh.VertexHandles.ToArray();
			var vertexIndices = new Dictionary<VertexHandle, int>( _editorVertexHandles.Length );
			for ( var i = 0; i < _editorVertexHandles.Length; ++i )
				vertexIndices.Add( _editorVertexHandles[i], i );
			_editorEdgeIndices.Clear();
			foreach ( var edge in Mesh.GetVisibleEdgeHandles() )
			{
				Mesh.GetEdgeVertices( edge, out var a, out var b );
				_editorEdgeIndices.Add( vertexIndices[a] );
				_editorEdgeIndices.Add( vertexIndices[b] );
			}
		}

		if ( geometryChanged )
		{
			CollectionsMarshal.SetCount( _editorPositions, _editorVertexHandles.Length );
			var positions = CollectionsMarshal.AsSpan( _editorPositions );
			_editorVisiblePositions.Clear();
			for ( var i = 0; i < _editorVertexHandles.Length; ++i )
			{
				var vertex = _editorVertexHandles[i];
				var position = Mesh.GetVertexPosition( vertex );
				positions[i] = position;
				if ( !Mesh.IsVertexHidden( vertex ) )
					_editorVisiblePositions.Add( position );
			}
			_editorWireframe?.MarkDirty();
			_editorEdges?.MarkDirty();
			_editorVertices?.MarkDirty();
			_editorMesh = Mesh;
			_editorModel = Model;
			_editorTopologyVersion = Mesh.TopologyVersion;
		}

		_editorWireframe ??= new ToolRenderable( RenderEditorWireframe );
		if ( EditorRenderFlags.HasFlag( MeshRenderFlags.Wireframe ) )
			_editorEdges ??= new ToolRenderable( RenderEditorEdges );
		else
		{
			_editorEdges?.Dispose();
			_editorEdges = null;
		}
		if ( EditorRenderFlags.HasFlag( MeshRenderFlags.Vertices ) )
			_editorVertices ??= new ToolRenderable( RenderEditorVertices );
		else
		{
			_editorVertices?.Dispose();
			_editorVertices = null;
		}

		UpdateEditorRenderState();
		if ( geometryChanged )
			EditorRenderDataChanged?.Invoke();
	}

	void UpdateEditorRenderState()
	{
		var color = GameObject.Root.IsPrefabInstance ? (Color)"#6FB7C9" : Color.White;
		if ( _editorWireframeColor != color )
		{
			_editorWireframeColor = color;
			_editorWireframe?.MarkDirty();
		}
		var hidden = GameObject.Tags.Has( "hidden" );
		var visibilityChanged = _editorHidden != hidden;
		_editorHidden = hidden;
		Update( _editorWireframe );
		Update( _editorEdges );
		Update( _editorVertices );
		if ( visibilityChanged )
			EditorRenderDataChanged?.Invoke();

		void Update( ToolRenderable renderable )
		{
			if ( renderable is null )
				return;
			renderable.Transform = WorldTransform;
			renderable.Enabled = !hidden;
			renderable.Update( Scene.DebugSceneWorld );
		}
	}

	void RenderEditorWireframe( ToolRenderContext context )
	{
		context.Component = this;
		context.Tag = "editor_wireframe";
		context.Color = _editorWireframeColor;
		context.DrawIndexedLines( CollectionsMarshal.AsSpan( _editorPositions ), CollectionsMarshal.AsSpan( _editorEdgeIndices ) );
	}

	void RenderEditorEdges( ToolRenderContext context )
	{
		context.Component = this;
		context.Tag = "editor_overlay";
		context.Color = new Color( 0.3137f, 0.7843f, 1, 1 );
		context.IgnoreDepth = false;
		context.RenderOrder = 1;
		context.LineThickness = 2;
		context.DrawIndexedLines( CollectionsMarshal.AsSpan( _editorPositions ), CollectionsMarshal.AsSpan( _editorEdgeIndices ) );
		context.Color = context.Color.Darken( 0.3f ).WithAlpha( 0.1f );
		context.IgnoreDepth = true;
		context.RenderOrder = 2;
		context.LineThickness = 1;
		context.DrawIndexedLines( CollectionsMarshal.AsSpan( _editorPositions ), CollectionsMarshal.AsSpan( _editorEdgeIndices ) );
	}

	void RenderEditorVertices( ToolRenderContext context )
	{
		context.Component = this;
		context.Tag = "editor_overlay";
		context.Color = new Color( 1, 1, 0.3f, 1 );
		context.IgnoreDepth = false;
		context.RenderOrder = 3;
		context.DrawPoints( CollectionsMarshal.AsSpan( _editorVisiblePositions ) );
		context.Color = context.Color.Darken( 0.3f ).WithAlpha( 0.2f );
		context.IgnoreDepth = true;
		context.RenderOrder = 4;
		context.DrawPoints( CollectionsMarshal.AsSpan( _editorVisiblePositions ) );
	}
}
