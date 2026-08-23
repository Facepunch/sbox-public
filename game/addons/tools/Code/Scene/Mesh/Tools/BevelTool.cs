using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

public struct BevelEdges
{
	[Hide, JsonInclude] public MeshComponent Component { get; set; }
	[Hide, JsonInclude] public PolygonMesh Mesh { get; set; }
	[Hide, JsonInclude] public List<int> Edges { get; set; }
}

public record struct BevelParameters
{
	[Hide]
	private float _width;

	[Title( "Steps" ), Range( 0, 32 ), WideMode]
	public int Steps { get; set; } = 1;

	[Title( "Shape" ), Range( 0.0f, 1.0f ), WideMode]
	public float Shape { get; set; } = 1.0f;

	[Title( "Width" ), Range( 0.0625f, 256.0f ), WideMode, DefaultValue( 8.0f )]
	public float Width
	{
		readonly get => Math.Clamp( _width, 0.0625f, 256.0f );
		set => _width = Math.Clamp( value, 0.0625f, 256.0f );
	}

	[Title( "Soft Edges" ), WideMode]
	public bool SoftEdges { get; set; } = false;

	[Hide]
	public readonly int SegmentCount => Math.Max( 1, Steps * 2 );

	public BevelParameters() : this( 1, 1.0f, 8.0f, false )
	{
	}

	public BevelParameters( int steps, float shape, float width, bool softEdges )
	{
		Steps = steps;
		Shape = shape;
		_width = default;
		SoftEdges = softEdges;
		Width = width;
	}
}

[Alias( "tools.bevel-tool" )]
public partial class BevelTool( BevelEdges[] edges ) : EditorTool
{
	private const string ParametersCookie = "MeshEditor.Bevel.Parameters";
	private static readonly BevelParameters DefaultParameters = new();

	private readonly Dictionary<MeshComponent, List<HalfEdgeHandle>> _newEdges = [];
	private readonly ToolUndoStack _undo = new();
	private BevelParameters _parametersBeforeEdit;
	private bool _applied;

	[InlineEditor( Label = false )]
	public BevelParameters Parameters { get; set; } = EditorCookie.Get( ParametersCookie, DefaultParameters );

	public override void OnEnabled()
	{
		_undo.Push( "Activate Bevel Tool", CancelFromUndo );
	}

	public override void OnDisabled()
	{
		if ( !_applied )
			RestoreOriginals();

		_undo.Clear();
		_newEdges.Clear();
	}

	public override void OnUpdate()
	{
		if ( edges.Length == 0 ) return;

		var color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );

		foreach ( var group in edges )
		{
			var comp = group.Component;
			if ( !comp.IsValid() ) continue;

			using ( Gizmo.ObjectScope( comp.GameObject, comp.WorldTransform ) )
			using ( Gizmo.Scope( "Edges" ) )
			{
				Gizmo.Draw.Color = color;
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.Lines( comp.Mesh.GetVisibleEdges() );
			}
		}
	}

	public void UpdateBevel()
	{
		_newEdges.Clear();
		var parameters = Parameters;

		foreach ( var edgeGroup in edges )
		{
			var component = edgeGroup.Component;
			if ( !component.IsValid() ) continue;

			var mesh = new PolygonMesh { Transform = edgeGroup.Mesh.Transform };
			mesh.MergeMesh( edgeGroup.Mesh, Transform.Zero, out _, out _, out _ );

			var bevelEdges = edgeGroup.Edges
				.Select( mesh.HalfEdgeHandleFromIndex )
				.ToList();

			var newOuterEdges = new List<HalfEdgeHandle>();
			var newInnerEdges = new List<HalfEdgeHandle>();
			var facesNeedingUVs = new List<FaceHandle>();
			var newFaces = new List<FaceHandle>();

			if ( !mesh.BevelEdges(
				bevelEdges,
				PolygonMesh.BevelEdgesMode.RemoveClosedEdges,
				parameters.SegmentCount,
				parameters.Width,
				parameters.Shape,
				newOuterEdges,
				newInnerEdges,
				newFaces,
				facesNeedingUVs ) )
			{
				component.Mesh = edgeGroup.Mesh;
				continue;
			}

			var smoothMode = parameters.SoftEdges
				? PolygonMesh.EdgeSmoothMode.Soft
				: PolygonMesh.EdgeSmoothMode.Default;

			foreach ( var edge in newInnerEdges )
				mesh.SetEdgeSmoothing( edge, smoothMode );

			foreach ( var face in facesNeedingUVs )
				mesh.TextureAlignToGrid( mesh.Transform, face );

			mesh.ComputeFaceTextureParametersFromCoordinates( newFaces );

			component.Mesh = mesh;
			newOuterEdges.AddRange( newInnerEdges );
			_newEdges[component] = newOuterEdges;
		}
	}

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

	public void Apply()
	{
		var components = edges
			.Select( x => x.Component )
			.Where( x => x.IsValid() )
			.ToArray();

		if ( components.Length == 0 )
		{
			GoBack();
			return;
		}

		var resultMeshes = components.ToDictionary( x => x, x => x.Mesh );
		RestoreOriginals();
		_undo.Clear();

		using var scope = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Bevel Edges" )
			.WithComponentChanges( components )
			.Push() )
		{
			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( var component in components )
				component.Mesh = resultMeshes[component];

			foreach ( var (component, newEdges) in _newEdges )
			{
				foreach ( var edge in newEdges )
					selection.Add( new MeshEdge( component, edge ) );
			}
		}

		_applied = true;
		GoBack();
	}

	public void Cancel()
	{
		using var scope = SceneEditorSession.Scope();

		_undo.Clear();
		RestoreOriginals();
		GoBack();
	}

	private void CancelFromUndo()
	{
		using var scope = SceneEditorSession.Scope();

		RestoreOriginals();
		GoBack();
	}

	private void SetParameters( BevelParameters parameters )
	{
		Parameters = parameters;
		EditorCookie.Set( ParametersCookie, parameters );
		UpdateBevel();
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
}
