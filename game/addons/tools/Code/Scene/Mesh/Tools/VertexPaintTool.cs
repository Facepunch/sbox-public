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

	enum PaintLimitMode
	{
		[Icon( "public" )] Everything,
		[Icon( "category" )] Objects,
		[Icon( "square" )] Faces,
		[Icon( "timeline" )] Edges,
		[Icon( "fiber_manual_record" )] Vertices
	}

	[WideMode]
	PaintLimitMode LimitMode
	{
		get => _limitMode;
		set
		{
			_limitMode = value;
			RebuildSelection();
		}
	}
	PaintLimitMode _limitMode = PaintLimitMode.Objects;
	bool LimitToActiveMaterial { get; set; }
	bool PaintBackfacing { get; set; }
	/// <summary>
	/// Show indicators for vertices that will be affected by the brush.
	/// </summary>
	bool ShowVerts { get; set; } = true;
	/// <summary>
	/// Show an outline highlighting the paintable selection.
	/// </summary>
	bool ShowSelection { get; set; } = false;

	[WideMode] PaintMode Mode { get; set; } = PaintMode.Blend;
	[WideMode, Range( 10, 1000 )] float Radius { get; set; } = 50;
	[WideMode, Range( 0, 1 )] float Strength { get; set; } = 1;
	[WideMode, Range( 0, 1 )] float Hardness { get; set; } = 0.5f;

	[WideMode, ColorUsage( false, false )]
	Color Color { get; set; } = new Color32( 255, 0, 0 );

	Dictionary<HalfEdgeHandle, Vector4> _prevColors;
	Dictionary<HalfEdgeHandle, Vector4> _deltaColors;

	PolygonMesh _activeMesh;

	Vector3 _lastCheckedPos;
	float _distanceSinceLastDrop;
	Vector3 _lastHitPos;
	Vector3 _lastHitNormal;
	Vector2? _cursorLockPosition;

	const float DropSpacing = 8.0f;

	readonly HashSet<MeshComponent> _selectedMeshes = [];
	readonly HashSet<HalfEdgeHandle> _selectedFaceVertices = [];

	IDisposable _undoScope;

	public override void OnEnabled()
	{
		RebuildSelection();
	}

	void RebuildSelection()
	{
		_selectedMeshes.Clear();
		_selectedFaceVertices.Clear();

		switch ( LimitMode )
		{
			case PaintLimitMode.Everything:
				break;

			case PaintLimitMode.Objects:
				_selectedMeshes.UnionWith( Selection
					.OfType<GameObject>()
					.Select( go => go.GetComponent<MeshComponent>() )
					.Where( mc => mc.IsValid() ) );

				GatherMeshComponents<MeshFace>( f => f.Component );
				GatherMeshComponents<MeshEdge>( e => e.Component );
				GatherMeshComponents<MeshVertex>( v => v.Component );
				break;

			case PaintLimitMode.Faces:
				foreach ( var face in SelectionTool.GetAllSelected<MeshFace>() )
				{
					if ( !face.IsValid() ) continue;
					if ( face.Component.Mesh.FindHalfEdgesConnectedToFace( face.Handle, out var edges ) )
						AddEdges( edges );
				}
				break;

			case PaintLimitMode.Edges:
				foreach ( var edge in SelectionTool.GetAllSelected<MeshEdge>() )
				{
					if ( !edge.IsValid() ) continue;
					var mesh = edge.Component.Mesh;
					mesh.GetEdgeVertices( edge.Handle, out var a, out var b );
					mesh.GetFaceVerticesConnectedToVertex( a, out var edgesA );
					mesh.GetFaceVerticesConnectedToVertex( b, out var edgesB );
					AddEdges( edgesA );
					AddEdges( edgesB );
				}
				break;

			case PaintLimitMode.Vertices:
				foreach ( var vert in SelectionTool.GetAllSelected<MeshVertex>() )
				{
					if ( !vert.IsValid() ) continue;
					vert.Component.Mesh.GetFaceVerticesConnectedToVertex( vert.Handle, out var edges );
					AddEdges( edges );
				}
				break;
		}
	}

	void GatherMeshComponents<T>( Func<T, MeshComponent> getComponent ) where T : IValid
	{
		foreach ( var element in SelectionTool.GetAllSelected<T>() )
		{
			if ( !element.IsValid() ) continue;
			var comp = getComponent( element );
			if ( comp.IsValid() )
				_selectedMeshes.Add( comp );
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
		DrawPaintableSelection();

		if ( LimitMode != PaintLimitMode.Everything && Gizmo.IsShiftPressed && Gizmo.WasRightMousePressed )
		{
			var addFace = MeshTrace.TraceFace( out _ );
			if ( addFace.IsValid() )
			{
				var component = addFace.Component;
				var addMesh = component.Mesh;

				switch ( LimitMode )
				{
					case PaintLimitMode.Objects:
						_selectedMeshes.Add( component );
						Selection.Add( component.GameObject );
						break;

					case PaintLimitMode.Faces:
						if ( addMesh.FindHalfEdgesConnectedToFace( addFace.Handle, out var edges ) )
							AddEdges( edges );
						SelectionTool.AddToPreviousSelections( addFace );
						break;
				}

				var faceMaterial = addMesh.GetFaceMaterial( addFace.Handle );
				if ( faceMaterial.IsValid() )
					Tool.ActiveMaterial = faceMaterial;
			}
			return;
		}

		var face = LimitMode != PaintLimitMode.Everything
			? TraceSelectedFace( out var hitPosition )
			: MeshTrace.TraceFace( out hitPosition );

		if ( !face.IsValid() )
			return;

		var mesh = face.Component.Mesh;

		if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
		{
			_cursorLockPosition ??= Application.UnscaledCursorPosition;

			var d = Application.UnscaledCursorPosition - _cursorLockPosition.Value;

			if ( Gizmo.IsShiftPressed )
				Radius = (Radius + d.x * 0.25f).Clamp( 10, 1000 );
			else if ( Gizmo.IsCtrlPressed )
			{
				Strength = (Strength - d.y * 0.002f).Clamp( 0, 1 );
				Hardness = (Hardness + d.x * 0.002f).Clamp( 0, 1 );
			}

			Application.UnscaledCursorPosition = _cursorLockPosition.Value;
			SceneOverlay.Parent.Cursor = CursorShape.Blank;

			DrawBrushAdjustText();
			DrawBrush( _lastHitPos, _lastHitNormal, mesh );
			return;
		}
		else
		{
			if ( _cursorLockPosition.HasValue )
				SceneOverlay.Parent.Cursor = CursorShape.None;

			_cursorLockPosition = null;
		}

		mesh.ComputeFaceNormal( face.Handle, out var faceNormal );

		_lastHitPos = hitPosition;
		_lastHitNormal = faceNormal;

		if ( Gizmo.WasLeftMousePressed )
			BeginStroke( face.Component, hitPosition );

		if ( _prevColors != null && Gizmo.WasLeftMouseReleased )
			EndStroke();

		if ( _activeMesh != null && mesh != _activeMesh )
			return;

		if ( !Gizmo.IsLeftMouseDown )
		{
			DrawBrush( hitPosition, faceNormal, mesh );
			return;
		}

		var frameDist = hitPosition.Distance( _lastCheckedPos );
		_distanceSinceLastDrop += frameDist;
		_lastCheckedPos = hitPosition;

		if ( !Gizmo.WasLeftMousePressed && _distanceSinceLastDrop < DropSpacing )
		{
			DrawBrush( hitPosition, faceNormal, mesh );
			return;
		}

		_distanceSinceLastDrop = 0f;
		var radiusSq = Radius * Radius;

		foreach ( var edge in mesh.HalfEdgeHandles )
		{
			if ( LimitMode != PaintLimitMode.Everything && _selectedMeshes.Count == 0 && !_selectedFaceVertices.Contains( edge ) )
				continue;

			if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
				continue;

			mesh.GetVertexPosition( edge.Vertex, mesh.Transform, out var p );
			mesh.ComputeFaceNormal( edge.Face, out var vertexNormal );

			var distSq = (p - hitPosition).LengthSquared;
			if ( distSq > radiusSq )
				continue;

			if ( !PaintBackfacing && faceNormal.Dot( vertexNormal ) <= 0.0f )
				continue;

			var t = MathF.Sqrt( distSq ) / Radius;
			var falloff = Hardness >= 1f ? 1f : (1f - ((t - Hardness) / (1f - Hardness)).Clamp( 0f, 1f ));

			var prev = _prevColors[edge];
			var delta = _deltaColors[edge];

			_deltaColors[edge] = ApplyColorPaint(
				prev,
				delta,
				GetBrushColor(),
				GetVertexMask(),
				Strength,
				falloff );

			var c = prev + _deltaColors[edge];

			if ( Mode == PaintMode.Color ) mesh.SetVertexColor( edge, new Color( c.x, c.y, c.z, 1 ) );
			else mesh.SetVertexBlend( edge, new Color( c.x, c.y, c.z, c.w ) );
		}

		DrawBrush( hitPosition, faceNormal, mesh );
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

	void DrawPaintableSelection()
	{
		if ( !ShowSelection || LimitMode == PaintLimitMode.Everything )
			return;

		using ( Gizmo.Scope( "PaintableSelection" ) )
		{
			switch ( LimitMode )
			{
				case PaintLimitMode.Objects:
					foreach ( var comp in _selectedMeshes )
					{
						if ( !comp.IsValid() ) continue;
						DrawMeshWireframe( comp, Color.Cyan );
					}
					break;

				case PaintLimitMode.Faces:
					foreach ( var face in SelectionTool.GetAllSelected<MeshFace>() )
					{
						if ( !face.IsValid() ) continue;
						DrawFaceOutline( face.Component, face.Handle, Color.Yellow );
					}
					break;

				case PaintLimitMode.Edges:
					foreach ( var edge in SelectionTool.GetAllSelected<MeshEdge>() )
					{
						if ( !edge.IsValid() ) continue;
						DrawEdgeHighlight( edge.Component, edge.Handle, Color.Yellow );
					}
					break;

				case PaintLimitMode.Vertices:
					foreach ( var vert in SelectionTool.GetAllSelected<MeshVertex>() )
					{
						if ( !vert.IsValid() ) continue;
						DrawVertexHighlight( vert.Component, vert.Handle, Color.Yellow );
					}
					break;
			}
		}
	}

	void DrawMeshWireframe( MeshComponent comp, Color color )
	{
		using ( Gizmo.ObjectScope( comp.GameObject, comp.WorldTransform ) )
		{
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = color;

			var bounds = comp.Mesh.CalculateBounds();
			Gizmo.Draw.LineBBox( bounds );
		}
	}

	void DrawFaceOutline( MeshComponent comp, FaceHandle face, Color color )
	{
		var mesh = comp.Mesh;
		var faceEdges = mesh.GetFaceEdges( face );

		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color;

		foreach ( var edge in faceEdges )
		{
			mesh.GetEdgeVertices( edge, out var a, out var b );
			mesh.GetVertexPosition( a, mesh.Transform, out var posA );
			mesh.GetVertexPosition( b, mesh.Transform, out var posB );
			Gizmo.Draw.Line( posA, posB );
		}
	}

	void DrawEdgeHighlight( MeshComponent comp, HalfEdgeHandle edge, Color color )
	{
		var mesh = comp.Mesh;
		mesh.GetEdgeVertices( edge, out var a, out var b );
		mesh.GetVertexPosition( a, mesh.Transform, out var posA );
		mesh.GetVertexPosition( b, mesh.Transform, out var posB );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Color = color;
		Gizmo.Draw.Line( posA, posB );
	}

	void DrawVertexHighlight( MeshComponent comp, VertexHandle vertex, Color color )
	{
		comp.Mesh.GetVertexPosition( vertex, comp.Mesh.Transform, out var pos );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color;
		Gizmo.Draw.Sprite( pos, 10f, null, false );
	}

	void DrawBrush( Vector3 position, Vector3 normal, PolygonMesh mesh = null )
	{
		using ( Gizmo.Scope( "VertexPaintBrush", position, Rotation.LookAt( normal ) ) )
		{
			var drawColor = Mode == PaintMode.Color ? Color : Blend.ToColor();
			var length = MathX.LerpTo( 25f * 0.75f, 25f * 2f, Strength );

			var sections = (int)(MathF.Sqrt( Radius ) * 5.0f).Clamp( 16, 64 );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = drawColor.WithAlpha( 1 );
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * length );
			Gizmo.Draw.SolidSphere( Vector3.Forward * length, 2 );
			Gizmo.Draw.LineCircle( Vector3.Zero, Radius, 32, sections: sections );

			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.LineCircle( Vector3.Zero, Radius * Hardness, 32, sections: sections );
		}

		if ( ShowVerts && mesh is not null )
			DrawVertexIndicators( position, normal, mesh );
	}

	void DrawBrushAdjustText()
	{
		var textScope = new TextRendering.Scope
		{
			TextColor = Color.White,
			FontSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale,
			FontName = "Roboto Mono",
			FontWeight = 600,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		var offset = Vector2.Up * 24;

		if ( Gizmo.IsShiftPressed )
		{
			textScope.Text = $"Radius: {Radius:0.#}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset );
		}
		else if ( Gizmo.IsCtrlPressed )
		{
			textScope.Text = $"Strength: {Strength:0.##}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset + Vector2.Up * 18 );

			textScope.Text = $"Hardness: {Hardness:0.##}";
			Gizmo.Draw.ScreenText( textScope, _lastHitPos, offset );
		}
	}

	void DrawVertexIndicators( Vector3 brushPosition, Vector3 brushNormal, PolygonMesh mesh )
	{
		var indicatorRadius = Radius * 2f;
		var radiusSq = indicatorRadius * indicatorRadius;

		using ( Gizmo.Scope( "VertexIndicators" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			foreach ( var edge in mesh.HalfEdgeHandles )
			{
				if ( LimitMode != PaintLimitMode.Everything && _selectedMeshes.Count == 0 && !_selectedFaceVertices.Contains( edge ) )
					continue;

				if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
					continue;

				mesh.GetVertexPosition( edge.Vertex, mesh.Transform, out var p );

				if ( (p - brushPosition).LengthSquared > radiusSq )
					continue;

				mesh.ComputeFaceNormal( edge.Face, out var vertexNormal );
				if ( !PaintBackfacing && brushNormal.Dot( vertexNormal ) <= 0.0f )
					continue;

				var tint = GetVertexIndicatorColor( mesh, edge );

				Gizmo.Draw.Color = tint;
				Gizmo.Draw.Sprite( p, 8f, null, false );
			}
		}
	}

	Color GetVertexIndicatorColor( PolygonMesh mesh, HalfEdgeHandle edge )
	{
		if ( Mode == PaintMode.Color )
			return mesh.GetVertexColor( edge );

		var blend = mesh.GetVertexBlend( edge );
		return new Color( blend.r, blend.g, blend.b, 1 );
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

	MeshFace TraceSelectedFace( out Vector3 hitPosition )
	{
		var ray = Gizmo.CurrentRay;
		var depth = Gizmo.RayDepth;

		for ( int i = 0; i < 32 && depth > 0f; i++ )
		{
			var result = MeshTrace.Ray( ray, depth ).Run();
			if ( !result.Hit )
				break;

			var advance = result.Distance + 0.01f;
			ray = new Ray( ray.Project( advance ), ray.Forward );
			depth -= advance;

			if ( result.Component is not MeshComponent component )
				continue;

			var face = new MeshFace( component, component.Mesh.TriangleToFace( result.Triangle ) );
			if ( face.IsValid() && IsFaceSelected( face ) )
			{
				hitPosition = result.HitPosition;
				return face;
			}
		}

		hitPosition = default;
		return default;
	}

	bool IsFaceSelected( MeshFace face )
	{
		if ( _selectedFaceVertices.Count > 0 )
		{
			return face.Component.Mesh.FindHalfEdgesConnectedToFace( face.Handle, out var edges )
				&& edges.Any( e => _selectedFaceVertices.Contains( e ) );
		}

		return _selectedMeshes.Contains( face.Component );
	}
}
