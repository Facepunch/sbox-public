using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

public struct EdgeArchEdges
{
	[Hide, JsonInclude] public MeshComponent Component { get; set; }
	[Hide, JsonInclude] public PolygonMesh Mesh { get; set; }
	[Hide, JsonInclude] public List<int> Edges { get; set; }
}

public record struct EdgeArchParameters
{
	[Hide]
	private int _steps;

	[Title( "Steps" ), Range( 1, 32 ), WideMode, DefaultValue( 4 )]
	public int Steps
	{
		readonly get => Math.Clamp( _steps, 1, 32 );
		set => _steps = Math.Clamp( value, 1, 32 );
	}

	[Title( "Arc Height" ), Range( -256.0f, 256.0f ), WideMode]
	public float Height { get; set; } = 16.0f;

	[Title( "Arc Offset" ), Range( -256.0f, 256.0f ), WideMode]
	public float Offset { get; set; } = 0.0f;

	public EdgeArchParameters() : this( 4, 16.0f, 0.0f )
	{
	}

	public EdgeArchParameters( int steps, float height, float offset )
	{
		_steps = default;
		Steps = steps;
		Height = height;
		Offset = offset;
	}
}

[Alias( "tools.edge-arch-tool" )]
public partial class EdgeArchTool( EdgeArchEdges[] edges ) : EditorTool
{
	private const string ParametersCookie = "MeshEditor.EdgeArch.Parameters";
	private static readonly EdgeArchParameters DefaultParameters = new();

	/// <summary>
	/// The vertices of each arc we've made, in order from one end to the other. What to draw and
	/// what to select both come from these, so there's one thing to keep straight instead of two.
	/// </summary>
	private readonly Dictionary<MeshComponent, List<List<VertexHandle>>> _arcs = [];

	private readonly ToolUndoStack _undo = new();
	private EdgeArchParameters _parametersBeforeEdit;
	private bool _applied;

	[InlineEditor( Label = false )]
	public EdgeArchParameters Parameters { get; set; } = EditorCookie.Get( ParametersCookie, DefaultParameters );

	public override void OnEnabled()
	{
		base.OnEnabled();

		// Undo steps back through the arcs we've tried, then out of the tool - the mesh is mid
		// preview, so undoing whatever came before it would undo into something that isn't saved.
		_undo.Push( "Activate Edge Arch Tool", CancelFromUndo );
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		if ( !_applied )
			RestoreOriginals();

		_undo.Clear();
		_arcs.Clear();
	}

	/// <summary>
	/// Remember what the arc looked like before an edit starts, so the whole of a slider drag
	/// undoes as the one step rather than every value it passed through.
	/// </summary>
	public void BeginParameterEdit()
	{
		_parametersBeforeEdit = Parameters;
	}

	public void CommitParameterEdit( string title )
	{
		if ( _parametersBeforeEdit == Parameters )
			return;

		var before = _parametersBeforeEdit;
		var after = Parameters;
		SetParameters( after );

		_undo.Push( title,
			undo: () => SetParameters( before ),
			redo: () => SetParameters( after ) );
	}

	/// <summary>
	/// Take the arc. The preview draws straight into the meshes, so they go back to what they were
	/// before the scope opens - what it records is the whole arc, not whatever the sliders last did.
	/// </summary>
	public void Apply()
	{
		var components = new List<MeshComponent>( edges.Length );
		var arched = new List<PolygonMesh>( edges.Length );
		var created = new List<MeshEdge>();

		foreach ( var edgeGroup in edges )
		{
			var component = edgeGroup.Component;
			if ( !component.IsValid() ) continue;

			components.Add( component );
			arched.Add( component.Mesh );

			if ( !_arcs.TryGetValue( component, out var arcs ) )
				continue;

			foreach ( var arc in arcs )
			{
				for ( int i = 1; i < arc.Count; i++ )
				{
					var edge = component.Mesh.FindEdgeConnectingVertices( arc[i - 1], arc[i] );

					if ( edge.IsValid )
						created.Add( new MeshEdge( component, edge ) );
				}
			}
		}

		if ( components.Count == 0 )
		{
			GoBack();
			return;
		}

		RestoreOriginals();
		_undo.Clear();

		using var scope = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Apply Edge Arch" )
			.WithComponentChanges( components )
			.Push() )
		{
			for ( int i = 0; i < components.Count; i++ )
			{
				components[i].Mesh = arched[i];
			}

			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( var edge in created )
			{
				selection.Add( edge );
			}
		}

		_applied = true;

		GoBack();
	}

	/// <summary>
	/// Drop the arc. It was only ever a preview, so there's nothing to undo - the meshes go back to
	/// what they were and the undo stack is left how we found it.
	/// </summary>
	public void Cancel()
	{
		using var scope = SceneEditorSession.Scope();

		_undo.Clear();
		RestoreOriginals();

		SceneEditorSession.Active.Selection.Clear();

		GoBack();
	}

	private void CancelFromUndo()
	{
		using var scope = SceneEditorSession.Scope();

		RestoreOriginals();
		GoBack();
	}

	private void SetParameters( EdgeArchParameters parameters )
	{
		Parameters = parameters;
		EditorCookie.Set( ParametersCookie, parameters );
		UpdateArch();
	}

	private void RestoreOriginals()
	{
		foreach ( var edgeGroup in edges )
		{
			if ( edgeGroup.Component.IsValid() )
				edgeGroup.Component.Mesh = edgeGroup.Mesh;
		}
	}

	private static void GoBack()
	{
		EditorToolManager.SetSubTool( nameof( EdgeTool ) );
	}

	public override void OnUpdate()
	{
		foreach ( var edgeGroup in edges )
		{
			var component = edgeGroup.Component;
			if ( !component.IsValid() ) continue;

			if ( !_arcs.TryGetValue( component, out var arcs ) )
				continue;

			using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
			using ( Gizmo.Scope( "EdgeArcs" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var arc in arcs )
				{
					DrawArc( component.Mesh, arc );
				}
			}
		}
	}

	/// <summary>
	/// One arc - a line through its vertices with a dot on each cut it made, and a brighter dot on
	/// each end so it's clear which edge the arc belongs to.
	/// </summary>
	private static void DrawArc( PolygonMesh mesh, List<VertexHandle> vertices )
	{
		if ( vertices.Count < 2 ) return;

		Gizmo.Draw.Color = Color.Yellow;

		mesh.GetVertexPosition( vertices[0], Transform.Zero, out var start );
		var previous = start;

		for ( int i = 1; i < vertices.Count; i++ )
		{
			mesh.GetVertexPosition( vertices[i], Transform.Zero, out var position );

			Gizmo.Draw.Line( previous, position );

			if ( i < vertices.Count - 1 )
				Gizmo.Draw.Sprite( position, 8.0f, null, false );

			previous = position;
		}

		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.Sprite( start, 8.0f, null, false );
		Gizmo.Draw.Sprite( previous, 8.0f, null, false );
	}

	/// <summary>
	/// The curve an arc follows, as a cubic bezier.
	/// </summary>
	private readonly record struct ArcCurve( Vector3 Start, Vector3 ControlA, Vector3 ControlB, Vector3 End )
	{
		public Vector3 Evaluate( float t )
		{
			var u = 1.0f - t;

			return (u * u * u * Start) +
				(3.0f * u * u * t * ControlA) +
				(3.0f * u * t * t * ControlB) +
				(t * t * t * End);
		}
	}

	/// <summary>
	/// Work out the curve for one edge. It bows away from the face the edge belongs to, so an edge
	/// with no face on either side has nothing to bow away from and stays straight.
	/// </summary>
	private static ArcCurve ComputeArc( PolygonMesh mesh, HalfEdgeHandle edgeHandle, in EdgeArchParameters parameters )
	{
		mesh.GetVerticesConnectedToEdge( edgeHandle, out var vertexA, out var vertexB );
		mesh.GetVertexPosition( vertexA, Transform.Zero, out var start );
		mesh.GetVertexPosition( vertexB, Transform.Zero, out var end );

		var flip = false;
		var faceHandle = mesh.GetHalfEdgeFace( edgeHandle );

		if ( !faceHandle.IsValid )
		{
			faceHandle = mesh.GetHalfEdgeFace( mesh.GetOppositeHalfEdge( edgeHandle ) );
			flip = true;
		}

		if ( !faceHandle.IsValid )
			return new ArcCurve( start, start, end, end );

		mesh.ComputeFaceNormal( faceHandle, out var faceNormal );

		var edgeVector = end - start;
		var edgeDirection = edgeVector.Normal;
		var arcDirection = faceNormal.Cross( edgeDirection ).Normal * (flip ? -1.0f : 1.0f);

		// Pulling the controls in past the far end folds the arc back on itself
		var offset = Math.Max( parameters.Offset, -edgeVector.Length * 0.75f );
		var lift = arcDirection * parameters.Height;

		return new ArcCurve( start,
			start + lift - (edgeDirection * offset),
			end + lift + (edgeDirection * offset),
			end );
	}

	/// <summary>
	/// Build the arc from the meshes we started with. Every change comes through here, so a mesh is
	/// only ever arced once however many times the sliders move.
	/// </summary>
	public void UpdateArch()
	{
		var parameters = Parameters;

		_arcs.Clear();

		foreach ( var edgeGroup in edges )
		{
			var component = edgeGroup.Component;
			if ( !component.IsValid() ) continue;

			// The mesh we were handed is what the component started with, and we never touch it -
			// the arc is built into a copy so the sliders always work from the same shape.
			var original = edgeGroup.Mesh;

			var mesh = new PolygonMesh { Transform = original.Transform };
			mesh.MergeMesh( original, Transform.Zero, out _, out _, out _ );

			var arcs = new List<List<VertexHandle>>( edgeGroup.Edges.Count );

			foreach ( var edgeIndex in edgeGroup.Edges )
			{
				var edgeHandle = mesh.HalfEdgeHandleFromIndex( edgeIndex );

				if ( !mesh.IsEdgeOpen( edgeHandle ) )
					continue;

				mesh.GetVerticesConnectedToEdge( edgeHandle, out var startVertex, out var endVertex );

				// The curve comes off the original - arcing one edge moves the vertices the next
				// one would read its face normal from.
				var curve = ComputeArc( original, original.HalfEdgeHandleFromIndex( edgeIndex ), parameters );
				var vertices = SubdivideEdge( mesh, startVertex, endVertex, parameters.Steps );

				for ( int i = 0; i < vertices.Count; i++ )
				{
					mesh.SetVertexPosition( vertices[i], curve.Evaluate( i / (float)(vertices.Count - 1) ) );
				}

				arcs.Add( vertices );
			}

			mesh.ComputeFaceTextureCoordinatesFromParameters();

			component.Mesh = mesh;
			_arcs[component] = arcs;
		}
	}

	/// <summary>
	/// Cut an edge into <paramref name="steps"/> even pieces, handing back its vertices in order
	/// from one end to the other.
	/// </summary>
	private static List<VertexHandle> SubdivideEdge( PolygonMesh mesh, VertexHandle startVertex, VertexHandle endVertex, int steps )
	{
		var vertices = new List<VertexHandle>( steps + 1 ) { startVertex };
		var current = startVertex;

		for ( int i = 1; i < steps; i++ )
		{
			// Each cut is a fraction of what's left of the edge, not of the whole of it
			if ( !mesh.AddVertexToEdge( current, endVertex, 1.0f / (steps - i + 1), out var vertex ) )
				break;

			vertices.Add( vertex );
			current = vertex;
		}

		vertices.Add( endVertex );

		return vertices;
	}
}
