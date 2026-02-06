using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Paint and blend vertices.
/// </summary>
[Title( "Vertex Paint Tool" )]
[Icon( "brush" )]
[Alias( "tools.vertex-paint-tool" )]
[Group( "6" )]
public partial class VertexPaintTool( MeshTool tool ) : EditorTool
{
	protected MeshTool Tool { get; private init; } = tool;

	enum PaintMode
	{
		Blend,
		Color
	}

	public enum BlendMask
	{
		R,
		G,
		B,
		A
	}

	[Property]
	public BlendMask ActiveBlendMask { get; set; }

	Color32 Blend => ActiveBlendMask switch
	{
		BlendMask.R => new Color( 1, 0, 0, 0 ),
		BlendMask.G => new Color( 0, 1, 0, 0 ),
		BlendMask.B => new Color( 0, 0, 1, 0 ),
		BlendMask.A => new Color( 0, 0, 0, 1 ),
		_ => Color.Black
	};

	[Title( "Selected" )] bool PaintOnSelected { get; set; }
	bool LimitToActiveMaterial { get; set; }

	[WideMode] PaintMode Mode { get; set; } = PaintMode.Blend;
	[WideMode, Range( 10, 1000 )] float Radius { get; set; } = 50;
	[WideMode, Range( 0, 1 )] float Strength { get; set; } = 1;

	[WideMode, ColorUsage( false, false )]
	Color Color { get; set; } = new Color32( 255, 0, 0 );

	Dictionary<HalfEdgeHandle, Vector4> _prevColors;
	Dictionary<HalfEdgeHandle, Vector4> _deltaColors;

	PolygonMesh _activeMesh;

	Vector3 _lastCheckedPos;
	float _distanceSinceLastDrop;
	Vector3 _lastHitPos;
	Vector3 _lastHitNormal;

	const float DropSpacing = 8.0f;

	readonly HashSet<MeshComponent> _selectedMeshes = [];
	readonly HashSet<HalfEdgeHandle> _selectedFaceVertices = [];

	IDisposable _undoScope;

	public override void OnEnabled()
	{
		_selectedMeshes.Clear();
		_selectedMeshes.UnionWith( Selection
			.OfType<GameObject>()
			.Select( go => go.GetComponent<MeshComponent>() )
			.Where( mc => mc.IsValid() ) );

		_selectedFaceVertices.Clear();

		foreach ( var element in Selection )
		{
			switch ( element )
			{
				case MeshEdge edge when edge.IsValid():
					{
						var mesh = edge.Component.Mesh;
						mesh.GetEdgeVertices( edge.Handle, out var a, out var b );
						mesh.GetFaceVerticesConnectedToVertex( a, out var edgesA );
						mesh.GetFaceVerticesConnectedToVertex( b, out var edgesB );
						AddEdges( edgesA );
						AddEdges( edgesB );
						_selectedFaceVertices.Add( edge.Handle );
						break;
					}
				case MeshVertex vertex when vertex.IsValid():
					{
						var mesh = vertex.Component.Mesh;
						mesh.GetFaceVerticesConnectedToVertex( vertex.Handle, out var edges );
						AddEdges( edges );
						break;
					}
				case MeshFace face when face.IsValid():
					{
						var mesh = face.Component.Mesh;
						if ( mesh.FindHalfEdgesConnectedToFace( face.Handle, out var edges ) )
							AddEdges( edges );
						break;
					}
			}
		}
	}

	void AddEdges( IEnumerable<HalfEdgeHandle> edges )
	{
		foreach ( var edge in edges )
			if ( edge.IsValid )
				_selectedFaceVertices.Add( edge );
	}

	public override void OnUpdate()
	{
		var face = MeshTrace.TraceFace( out var hitPosition );
		if ( !face.IsValid() )
			return;

		var mesh = face.Component.Mesh;

		if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
		{
			var d = Gizmo.CursorDragDelta;

			if ( Gizmo.IsShiftPressed )
				Radius = (Radius + d.x * 0.25f).Clamp( 10, 1000 );
			else if ( Gizmo.IsCtrlPressed )
				Strength = (Strength - d.y * 0.002f).Clamp( 0, 1 );

			DrawBrush( _lastHitPos, _lastHitNormal );
			return;
		}

		mesh.ComputeFaceNormal( face.Handle, out var faceNormal );

		_lastHitPos = hitPosition;
		_lastHitNormal = faceNormal;

		if ( PaintOnSelected )
		{
			if ( _selectedFaceVertices.Count == 0 && !_selectedMeshes.Contains( face.Component ) )
				return;
		}

		if ( Gizmo.WasLeftMousePressed )
			BeginStroke( face.Component, hitPosition );

		if ( _prevColors != null && Gizmo.WasLeftMouseReleased )
			EndStroke();

		if ( _activeMesh != null && mesh != _activeMesh )
			return;

		if ( !Gizmo.IsLeftMouseDown )
		{
			DrawBrush( hitPosition, faceNormal );
			return;
		}

		var frameDist = hitPosition.Distance( _lastCheckedPos );
		_distanceSinceLastDrop += frameDist;
		_lastCheckedPos = hitPosition;

		if ( !Gizmo.WasLeftMousePressed && _distanceSinceLastDrop < DropSpacing )
		{
			DrawBrush( hitPosition, faceNormal );
			return;
		}

		_distanceSinceLastDrop = 0f;
		var radiusSq = Radius * Radius;

		foreach ( var edge in mesh.HalfEdgeHandles )
		{
			if ( PaintOnSelected && _selectedMeshes.Count == 0 && !_selectedFaceVertices.Contains( edge ) )
				continue;

			if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
				continue;

			mesh.GetVertexPosition( edge.Vertex, mesh.Transform, out var p );
			mesh.ComputeFaceNormal( edge.Face, out var vertexNormal );

			if ( (p - hitPosition).LengthSquared > radiusSq )
				continue;

			if ( faceNormal.Dot( vertexNormal ) <= 0.0f )
				continue;

			var prev = _prevColors[edge];
			var delta = _deltaColors[edge];

			_deltaColors[edge] = ApplyColorPaint(
				prev,
				delta,
				GetBrushColor(),
				GetVertexMask(),
				Strength,
				1 );

			var c = prev + _deltaColors[edge];

			if ( Mode == PaintMode.Color ) mesh.SetVertexColor( edge, new Color( c.x, c.y, c.z, 1 ) );
			else mesh.SetVertexBlend( edge, new Color( c.x, c.y, c.z, c.w ) );
		}

		DrawBrush( hitPosition, faceNormal );
	}

	Vector4 GetBrushColor()
	{
		if ( Gizmo.IsCtrlPressed )
		{
			return Mode switch
			{
				PaintMode.Blend => Vector4.Zero,
				PaintMode.Color => Vector4.One,
				_ => Vector4.Zero
			};
		}

		return Mode == PaintMode.Color ?
			Color :
			Blend.ToColor();
	}

	Vector4 GetVertexMask() => Mode == PaintMode.Color ?
		new Vector4( 1, 1, 1, 0 ) :
		new Vector4( 1, 1, 1, 1 );

	void BeginStroke( MeshComponent component, Vector3 hitPosition )
	{
		var mesh = component.Mesh;
		_activeMesh = mesh;

		_undoScope ??= SceneEditorSession.Active
			.UndoScope( "Vertex Paint Stroke" )
			.WithComponentChanges( component )
			.Push();

		_prevColors = [];
		_deltaColors = [];

		foreach ( var edge in mesh.HalfEdgeHandles )
		{
			_prevColors[edge] = Mode == PaintMode.Color ?
				mesh.GetVertexColor( edge ).ToColor() :
				mesh.GetVertexBlend( edge ).ToColor();

			_deltaColors[edge] = Vector4.Zero;
		}

		_lastCheckedPos = hitPosition;
		_distanceSinceLastDrop = 0f;
	}

	void EndStroke()
	{
		_prevColors = null;
		_deltaColors = null;
		_activeMesh = null;

		_undoScope?.Dispose();
		_undoScope = null;
	}

	void DrawBrush( Vector3 position, Vector3 normal )
	{
		using ( Gizmo.Scope( "VertexPaintBrush", position, Rotation.LookAt( normal ) ) )
		{
			var drawColor = Mode == PaintMode.Color ? Color : Blend.ToColor();
			var length = MathX.LerpTo( 25f * 0.75f, 25f * 2f, Strength );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = drawColor.WithAlpha( 1 );
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * length );
			Gizmo.Draw.SolidSphere( Vector3.Forward * length, 2 );
			Gizmo.Draw.LineCircle( Vector3.Zero, Radius, 32 );
		}
	}

	static Vector4 ApplyColorPaint( Vector4 prevColor, Vector4 currentDelta, Vector4 brushColor, Vector4 brushMask, float strength, float falloff )
	{
		var current = prevColor + currentDelta;
		var desired = current.LerpTo( brushColor, strength * falloff );

		desired.x = MathX.LerpTo( current.x, desired.x, brushMask.x );
		desired.y = MathX.LerpTo( current.y, desired.y, brushMask.y );
		desired.z = MathX.LerpTo( current.z, desired.z, brushMask.z );
		desired.w = MathX.LerpTo( current.w, desired.w, brushMask.w );

		return desired - prevColor;
	}
}
