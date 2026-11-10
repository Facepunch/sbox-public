using HalfEdgeMesh;
using Sandbox.Helpers;

namespace Editor.MeshEditor;

/// <summary>
/// Paint and blend vertices.
/// </summary>
[Title( "Vertex Paint Tool" )]
[Icon( "meshtools/sub-tools/vertex_paint_tool.png" )]
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

	struct Channel
	{
		public bool Enabled;
		[Range( 0, 1 )] public float Value;
	}

	readonly Channel[] _channels = new Channel[4];

	Channel ChannelR { get => _channels[1]; set => _channels[1] = value; }
	Channel ChannelG { get => _channels[2]; set => _channels[2] = value; }
	Channel ChannelB { get => _channels[3]; set => _channels[3] = value; }
	Channel ChannelA { get => _channels[0]; set => _channels[0] = value; }

	void SetChannelEnableOther( int channel )
	{
		_channels[channel].Value = 1;
		_channels[channel].Enabled = true;

		for ( int i = 1; i < _channels.Length; i++ )
		{
			var id = (channel + i) % _channels.Length;
			_channels[id].Value = 0;
			_channels[id].Enabled = id >= channel;
		}
	}

	void SetChannelDisableOther( int channel )
	{
		_channels[channel].Value = 1;
		_channels[channel].Enabled = true;

		for ( int i = 1; i < _channels.Length; i++ )
		{
			var id = (channel + i) % _channels.Length;
			_channels[id].Value = 0;
			_channels[id].Enabled = false;
		}
	}

	Color Blend => new Color( ChannelR.Value, ChannelG.Value, ChannelB.Value, ChannelA.Value );

	enum PaintLimitMode
	{
		[Icon( "public" )] Everything,
		[Icon( "category" )] Objects,
		[Icon( "square" )] Faces,
		[Icon( "timeline" )] Edges,
		[Icon( "fiber_manual_record" )] Vertices
	}

	[WideMode, Description( "Controls which vertices the brush can paint." )]
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

	[Description( "When enabled the brush will only paint vertices that are part of the active material." )]
	bool LimitToActiveMaterial { get; set; }

	[Description( "When enabled the brush will paint vertices that are in the brush radius." )]
	bool PaintBackfacing { get; set; }

	[Description( "When enabled the brush radius scales with camera distance so the brush covers a constant screen size." )]
	bool ScaleWithDistance { get; set; } = false;
	/// <summary>
	/// Show indicators for vertices that will be affected by the brush.
	/// </summary>
	bool ShowVerts { get; set; } = true;
	/// <summary>
	/// Show an outline highlighting the paintable selection.
	/// </summary>
	bool ShowSelection { get; set; } = false;

	[WideMode] PaintMode Mode { get; set; } = PaintMode.Blend;

	[WideMode, Range( 10, 1000 ), Description( "Controls the size of the brush." )]
	float Radius { get; set; } = 50;

	[WideMode, Range( 0, 1 ), Description( "Controls how much the brush affects the vertex color. 0 = no effect, 1 = full effect." )]
	float Strength { get; set; } = 1;

	[WideMode, Range( 0, 1 ), Description( "Controls the falloff of the brush. 0 = linear, 1 = hard edge." )]
	float Hardness { get; set; } = 0.5f;

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
	UndoSystem _subscribedUndoSystem;

	const float BrushReferenceDistance = 500f;

	/// <summary>
	/// Brush radius in world units. When <see cref="ScaleWithDistance"/> is enabled the
	/// radius scales with camera distance so the brush covers a constant screen size.
	/// </summary>
	float GetWorldRadius( Vector3 position )
	{
		if ( !ScaleWithDistance )
			return Radius;

		var distance = MathF.Max( Gizmo.Camera.Position.Distance( position ), 1f );
		return Radius * (distance / BrushReferenceDistance);
	}

	enum PaintFillMode
	{
		[Icon( "format_color_fill" )] Flood,
		[Icon( "grain" )] Noise,
		[Icon( "gradient" )] Occlusion,
		[Icon( "rounded_corner" )] Curvature
	}

	[WideMode, EnumDropdown] PaintFillMode FillMode { get; set; } = PaintFillMode.Flood;

	float GetFillFalloff( PolygonMesh mesh, VertexHandle vertex, Vector3 worldPosition, Vector3 worldNormal )
	{
		return FillMode switch
		{
			PaintFillMode.Noise => GetNoiseFalloff( worldPosition ),
			PaintFillMode.Occlusion => GetOcclusionFalloff( worldPosition, worldNormal ),
			PaintFillMode.Curvature => GetCurvatureFalloff( mesh, vertex ),
			_ => 1f
		};
	}

	/// <summary>
	/// Size of the noise pattern. Larger values create bigger patches.
	/// </summary>
	[WideMode, Range( 0.05f, 10.0f )] public float NoiseScale { get; set; } = 1f;

	/// <summary>
	/// Offsets the noise pattern. Change to get a different random pattern.
	/// </summary>
	[WideMode, Range( 0, 100 ), Step( 1 )] public int NoiseSeed { get; set; }

	/// <summary>
	/// Sharpens the transition between painted and unpainted areas.
	/// </summary>
	[WideMode, Range( 0, 1 )] public float NoiseContrast { get; set; } = 0.25f;

	public float GetNoiseFalloff( Vector3 worldPosition )
	{
		var p = worldPosition / MathF.Max( NoiseScale, 0.01f );

		var n = Sandbox.Utility.Noise.Perlin(
			p.x + NoiseSeed * 137.31f,
			p.y + NoiseSeed * 269.17f,
			p.z + NoiseSeed * 419.53f );

		var sharpness = MathX.Lerp( 1f, 8f, NoiseContrast );
		return ((n - 0.5f) * sharpness + 0.5f).Clamp( 0f, 1f );
	}

	/// <summary>
	/// Corner size. Geometry within this distance counts as occlusion, and the
	/// paint fades out over this distance from a corner.
	/// </summary>
	[WideMode, Range( 8, 256 )] public float OcclusionRadius { get; set; } = 64f;

	const int OcclusionRayCount = 32;
	const float OcclusionLift = 2f;

	// Fibonacci hemisphere around +x, oriented along the vertex normal when cast
	static readonly Vector3[] OcclusionRays = Enumerable.Range( 0, OcclusionRayCount ).Select( i =>
	{
		var x = (i + 0.5f) / OcclusionRayCount;
		var r = MathF.Sqrt( 1f - x * x );
		var a = i * MathF.PI * (3f - MathF.Sqrt( 5f ));
		return new Vector3( x, MathF.Cos( a ) * r, MathF.Sin( a ) * r );
	} ).ToArray();

	/// <summary>
	/// 0 on open surfaces, 1 in corners. Counts how much of the hemisphere around
	/// the vertex normal is blocked within <see cref="OcclusionRadius"/> - a
	/// two-plane corner blocks about half, which maps to fully painted.
	/// </summary>
	float GetOcclusionFalloff( Vector3 position, Vector3 normal )
	{
		var rotation = Rotation.LookAt( normal );
		var origin = position + normal * OcclusionLift;

		var trace = Scene.Trace
			.UseRenderMeshes( true, true )
			.WithoutTags( "hidden" )
			.UsePhysicsWorld( false );

		var hits = 0;

		foreach ( var dir in OcclusionRays )
		{
			if ( trace.Ray( new Ray( origin, rotation * dir ), OcclusionRadius ).Run().Hit )
				hits++;
		}

		return (2f * hits / OcclusionRayCount).Clamp( 0f, 1f );
	}

	/// <summary>
	/// Edge sharpness for full wear. Edges that bend at least this many degrees are
	/// fully painted, shallower bends fade out proportionally.
	/// </summary>
	[WideMode, Range( 5, 180 )] public float CurvatureAngle { get; set; } = 45f;

	/// <summary>
	/// Averaged normal of the faces around a vertex - points diagonally out of corners.
	/// </summary>
	static Vector3 GetVertexNormal( PolygonMesh mesh, VertexHandle vertex )
	{
		mesh.GetFaceVerticesConnectedToVertex( vertex, out var edges );

		var normal = Vector3.Zero;

		foreach ( var edge in edges )
		{
			if ( !edge.Face.IsValid )
				continue;

			mesh.ComputeFaceNormal( edge.Face, out var faceNormal );
			normal += faceNormal;
		}

		return normal.LengthSquared < 0.001f ? Vector3.Up : normal.Normal;
	}

	public override void OnEnabled()
	{
		SetChannelEnableOther( 1 );
		RebuildSelection();

		_subscribedUndoSystem = Manager.CurrentSession.UndoSystem;
		_subscribedUndoSystem.OnUndo += OnUndoRedo;
		_subscribedUndoSystem.OnRedo += OnUndoRedo;
	}

	public override void OnDisabled()
	{
		if ( _subscribedUndoSystem is not null )
		{
			_subscribedUndoSystem.OnUndo -= OnUndoRedo;
			_subscribedUndoSystem.OnRedo -= OnUndoRedo;
			_subscribedUndoSystem = null;
		}
	}

	public override void OnSelectionChanged()
	{
		RebuildSelection();
	}

	void OnUndoRedo( object _ ) => RebuildSelection();

	internal IEnumerable<T> GetSelectedElements<T>() where T : struct, IValid
	{
		return SelectionTool.GetAllSelected<T>()
			.Concat( Selection.OfType<T>() )
			.Where( x => x.IsValid() )
			.Distinct();
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
				foreach ( var face in GetSelectedElements<MeshFace>() )
				{
					if ( face.Component.Mesh.FindHalfEdgesConnectedToFace( face.Handle, out var edges ) )
						AddEdges( edges );
				}
				break;

			case PaintLimitMode.Edges:
				foreach ( var edge in GetSelectedElements<MeshEdge>() )
				{
					var mesh = edge.Component.Mesh;
					mesh.GetEdgeVertices( edge.Handle, out var a, out var b );
					mesh.GetFaceVerticesConnectedToVertex( a, out var edgesA );
					mesh.GetFaceVerticesConnectedToVertex( b, out var edgesB );
					AddEdges( edgesA );
					AddEdges( edgesB );
				}
				break;

			case PaintLimitMode.Vertices:
				foreach ( var vert in GetSelectedElements<MeshVertex>() )
				{
					vert.Component.Mesh.GetFaceVerticesConnectedToVertex( vert.Handle, out var edges );
					AddEdges( edges );
				}
				break;
		}
	}

	void GatherMeshComponents<T>( Func<T, MeshComponent> getComponent ) where T : struct, IValid
	{
		foreach ( var element in GetSelectedElements<T>() )
		{
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

		if ( Gizmo.IsCtrlPressed && Gizmo.WasRightMousePressed )
		{
			PickColorFromMesh( face, hitPosition );
			return;
		}

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

		if ( Mode == PaintMode.Color && _activeMesh is null && Application.IsKeyDown( KeyCode.V ) )
		{
			UpdateColorSample( face, hitPosition, faceNormal );
			DrawBrush( hitPosition, faceNormal, mesh );
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
		var worldRadius = GetWorldRadius( hitPosition );
		var radiusSq = worldRadius * worldRadius;

		foreach ( var edge in mesh.HalfEdgeHandles )
		{
			if ( !edge.Face.IsValid )
				continue;

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

			var t = MathF.Sqrt( distSq ) / worldRadius;
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

	public override void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
		menu.AddSeparator();

		AddMenuOption( menu, "Toggle Paint Mode", "format_paint", "vertexpainttool.togglepaintmode", true );
		AddMenuOption( menu, "Color Picker", "palette", "vertexpainttool.OpenColorPicker", Mode == PaintMode.Color );

		if ( GatherFillTargets().Count == 0 )
			return;

		menu.AddSeparator();

		var fill = menu.AddMenu( "Fill Selection", "format_color_fill" );
		AddMenuOption( fill, "Flood", "format_color_fill", "vertexpainttool.floodfill", true );
		AddMenuOption( fill, "Invert", "invert_colors", "vertexpainttool.invertfill", true );
		AddMenuOption( fill, "Reset", "format_color_reset", "vertexpainttool.resetpaint", true );
	}

	void UpdateColorSample( MeshFace face, Vector3 hitPosition, Vector3 faceNormal )
	{
		var mesh = face.Component.Mesh;

		var closest = FindClosestFaceVertex( mesh, face.Handle, hitPosition, out var cornerPosition );
		if ( !closest.IsValid )
			return;

		Color sampled = mesh.GetVertexColor( closest );

		if ( Gizmo.WasLeftMousePressed )
		{
			Color = sampled;
			return;
		}

		DrawVertexIndicators( hitPosition, faceNormal, mesh );

		using ( Gizmo.Scope( "ColorSample" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			DrawCornerCluster( mesh, closest, cornerPosition, sampled );

			var textHexScope = new TextRendering.Scope
			{
				Text = $"■ {sampled.Hex} \n {sampled.Rgb}",
				TextColor = sampled,
				FontSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale,
				FontName = "Roboto Mono",
				FontWeight = 600,
				LineHeight = 1,
				Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
			};

			Gizmo.Draw.ScreenText( textHexScope, hitPosition, Vector2.Up * 24 );
		}
	}

	void DrawCornerCluster( PolygonMesh mesh, HalfEdgeHandle closest, Vector3 cornerPosition, Color sampled )
	{
		mesh.GetFaceVerticesConnectedToVertex( closest.Vertex, out var corners );

		var nudge = Gizmo.Camera.Position.Distance( cornerPosition ) * 0.125f;

		foreach ( var corner in corners )
		{
			if ( !corner.IsValid || !corner.Face.IsValid )
				continue;

			var isPicked = corner == closest;
			Color c = isPicked ? sampled : mesh.GetVertexColor( corner );

			var dir = GetFaceCenterWorld( mesh, corner.Face ) - cornerPosition;
			var offset = cornerPosition + dir.Normal * MathF.Min( dir.Length * 0.3f, nudge );

			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = c.WithAlpha( 1 );
			Gizmo.Draw.Line( cornerPosition, offset );

			Gizmo.Draw.Color = c.WithAlpha( 1 );
			Gizmo.Draw.Sprite( offset, 16f, null, false );
		}
	}

	static Vector3 GetFaceCenterWorld( PolygonMesh mesh, FaceHandle face )
	{
		mesh.GetVerticesConnectedToFace( face, out var verts );

		var center = Vector3.Zero;
		foreach ( var v in verts )
		{
			mesh.GetVertexPosition( v, mesh.Transform, out var p );
			center += p;
		}

		return center / verts.Length;
	}

	static HalfEdgeHandle FindClosestFaceVertex( PolygonMesh mesh, FaceHandle face, Vector3 hitPosition, out Vector3 position )
	{
		HalfEdgeHandle closest = default;
		var closestDist = float.MaxValue;
		position = default;

		if ( !mesh.FindHalfEdgesConnectedToFace( face, out var edges ) )
			return default;

		foreach ( var edge in edges )
		{
			mesh.GetVertexPosition( edge.Vertex, mesh.Transform, out var p );

			var dist = (p - hitPosition).LengthSquared;

			if ( dist < closestDist )
			{
				closestDist = dist;
				closest = edge;
				position = p;
			}
		}

		return closest;
	}

	void PickColorFromMesh( MeshFace face, Vector3 hitPosition )
	{
		var closest = FindClosestFaceVertex( face.Component.Mesh, face.Handle, hitPosition, out _ );

		if ( !closest.IsValid )
			return;

		Color = face.Component.Mesh.GetVertexColor( closest );
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

		return Mode == PaintMode.Color ? Color : Blend;
	}

	Vector4 GetVertexMask() => Mode == PaintMode.Color ?
		new Vector4( 1, 1, 1, 0 ) :
		new Vector4( ChannelR.Enabled ? 1 : 0, ChannelG.Enabled ? 1 : 0, ChannelB.Enabled ? 1 : 0, ChannelA.Enabled ? 1 : 0 );

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
			if ( !edge.Face.IsValid )
				continue;

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
					foreach ( var face in GetSelectedElements<MeshFace>() )
					{
						DrawFaceOutline( face.Component, face.Handle, Color.Yellow );
					}
					break;

				case PaintLimitMode.Edges:
					foreach ( var edge in GetSelectedElements<MeshEdge>() )
					{
						DrawEdgeHighlight( edge.Component, edge.Handle, Color.Yellow );
					}
					break;

				case PaintLimitMode.Vertices:
					foreach ( var vert in GetSelectedElements<MeshVertex>() )
					{
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
			var drawColor = Mode == PaintMode.Color ? Color : Blend;
			var length = MathX.LerpTo( 25f * 0.75f, 25f * 2f, Strength );

			var worldRadius = GetWorldRadius( position );
			var sections = (int)(MathF.Sqrt( worldRadius ) * 5.0f).Clamp( 16, 64 );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = drawColor.WithAlpha( 1 );
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * length );
			Gizmo.Draw.SolidSphere( Vector3.Forward * length, 2 );
			Gizmo.Draw.LineCircle( Vector3.Zero, worldRadius, 32, sections: sections );

			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.LineCircle( Vector3.Zero, worldRadius * Hardness, 32, sections: sections );
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
		var indicatorRadius = GetWorldRadius( brushPosition ) * 2f;
		var radiusSq = indicatorRadius * indicatorRadius;

		using ( Gizmo.Scope( "VertexIndicators" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			foreach ( var edge in mesh.HalfEdgeHandles )
			{
				if ( !edge.Face.IsValid )
					continue;

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

	/// <summary>
	/// Collects the half-edges that fill operations apply to, grouped by component,
	/// based on the current limit mode.
	/// </summary>
	Dictionary<MeshComponent, List<HalfEdgeHandle>> GatherFillTargets()
	{
		var targets = new Dictionary<MeshComponent, List<HalfEdgeHandle>>();

		void Add( MeshComponent comp, IEnumerable<HalfEdgeHandle> edges )
		{
			if ( !comp.IsValid() ) return;
			if ( !targets.TryGetValue( comp, out var list ) )
				targets[comp] = list = [];
			list.AddRange( edges );
		}

		switch ( LimitMode )
		{
			case PaintLimitMode.Everything:
			case PaintLimitMode.Objects:
				{
					var meshes = LimitMode == PaintLimitMode.Objects
						? _selectedMeshes.AsEnumerable()
						: Selection.OfType<GameObject>().Select( go => go.GetComponent<MeshComponent>() ).Where( mc => mc.IsValid() );

					foreach ( var mc in meshes )
						Add( mc, mc.Mesh.HalfEdgeHandles.Where( e => e.Face.IsValid ) );
					break;
				}

			case PaintLimitMode.Faces:
				foreach ( var face in GetSelectedElements<MeshFace>() )
				{
					if ( face.Component.Mesh.FindHalfEdgesConnectedToFace( face.Handle, out var edges ) )
						Add( face.Component, edges );
				}
				break;

			case PaintLimitMode.Edges:
				foreach ( var edge in GetSelectedElements<MeshEdge>() )
				{
					var mesh = edge.Component.Mesh;
					mesh.GetEdgeVertices( edge.Handle, out var a, out var b );
					mesh.GetFaceVerticesConnectedToVertex( a, out var edgesA );
					mesh.GetFaceVerticesConnectedToVertex( b, out var edgesB );
					Add( edge.Component, edgesA );
					Add( edge.Component, edgesB );
				}
				break;

			case PaintLimitMode.Vertices:
				foreach ( var vert in GetSelectedElements<MeshVertex>() )
				{
					vert.Component.Mesh.GetFaceVerticesConnectedToVertex( vert.Handle, out var edges );
					Add( vert.Component, edges );
				}
				break;
		}

		return targets;
	}

	internal void FillSelection()
	{
		var targets = GatherFillTargets();

		if ( targets.Count == 0 )
			return;

		using var undo = SceneEditorSession.Active
			.UndoScope( "Vertex Paint Fill" )
			.WithComponentChanges( targets.Keys.ToArray() )
			.Push();

		var brush = Mode == PaintMode.Color ? (Vector4)Color : (Vector4)Blend;
		var mask = GetVertexMask();

		foreach ( var (comp, edges) in targets )
		{
			var mesh = comp.Mesh;
			var falloffCache = new Dictionary<VertexHandle, float>();

			foreach ( var edge in edges.Distinct() )
			{
				if ( !edge.Face.IsValid )
					continue;

				if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
					continue;

				Vector4 prev = Mode == PaintMode.Color ?
					mesh.GetVertexColor( edge ).ToColor() :
					mesh.GetVertexBlend( edge ).ToColor();

				if ( !falloffCache.TryGetValue( edge.Vertex, out var falloff ) )
				{
					mesh.GetVertexPosition( edge.Vertex, mesh.Transform, out var vertexPosition );
					var vertexNormal = mesh.Transform.NormalToWorld( GetVertexNormal( mesh, edge.Vertex ) );
					falloff = GetFillFalloff( mesh, edge.Vertex, vertexPosition, vertexNormal );
					falloffCache[edge.Vertex] = falloff;
				}

				var delta = ApplyColorPaint( prev, Vector4.Zero, brush, mask, Strength, falloff );
				var c = prev + delta;

				if ( Mode == PaintMode.Color ) mesh.SetVertexColor( edge, new Color( c.x, c.y, c.z, 1 ) );
				else mesh.SetVertexBlend( edge, new Color( c.x, c.y, c.z, c.w ) );
			}
		}
	}

	float GetCurvatureFalloff( PolygonMesh mesh, VertexHandle vertex )
	{
		mesh.GetFaceVerticesConnectedToVertex( vertex, out var faceVerts );

		var maxConvex = 0f;

		foreach ( var faceVert in faceVerts )
		{
			if ( !faceVert.Face.IsValid )
				continue;

			foreach ( var edge in mesh.GetFaceEdges( faceVert.Face ) )
			{
				mesh.GetEdgeVertices( edge, out var a, out var b );
				if ( a != vertex && b != vertex )
					continue;

				mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );
				if ( !faceA.IsValid || !faceB.IsValid )
					continue;

				mesh.ComputeFaceNormal( faceA, out var normalA );
				mesh.ComputeFaceNormal( faceB, out var normalB );

				if ( normalA.Dot( GetFaceCenter( mesh, faceB ) - GetFaceCenter( mesh, faceA ) ) >= 0f )
					continue;

				var angle = MathF.Acos( normalA.Dot( normalB ).Clamp( -1f, 1f ) ).RadianToDegree();
				maxConvex = MathF.Max( maxConvex, angle );
			}
		}

		return (maxConvex / MathF.Max( CurvatureAngle, 1f )).Clamp( 0f, 1f );
	}

	static Vector3 GetFaceCenter( PolygonMesh mesh, FaceHandle face )
	{
		mesh.GetVerticesConnectedToFace( face, out var verts );

		var center = Vector3.Zero;
		foreach ( var v in verts )
			center += mesh.GetVertexPosition( v );

		return center / verts.Length;
	}

	internal void InvertSelection()
	{
		var targets = GatherFillTargets();

		if ( targets.Count == 0 )
			return;

		using var undo = SceneEditorSession.Active
			.UndoScope( "Vertex Paint Invert" )
			.WithComponentChanges( targets.Keys.ToArray() )
			.Push();

		foreach ( var (comp, edges) in targets )
		{
			var mesh = comp.Mesh;

			foreach ( var edge in edges.Distinct() )
			{
				if ( !edge.Face.IsValid )
					continue;

				if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
					continue;

				Vector4 prev = Mode == PaintMode.Color ?
					mesh.GetVertexColor( edge ).ToColor() :
					mesh.GetVertexBlend( edge ).ToColor();

				var c = Vector4.One - prev;

				if ( Mode == PaintMode.Color ) mesh.SetVertexColor( edge, new Color( c.x, c.y, c.z, 1 ) );
				else mesh.SetVertexBlend( edge, new Color( c.x, c.y, c.z, c.w ) );
			}
		}
	}

	internal void ResetPaintData()
	{
		var targets = GatherFillTargets();

		if ( targets.Count == 0 )
			return;

		using var undo = SceneEditorSession.Active
			.UndoScope( "Vertex Paint Reset" )
			.WithComponentChanges( targets.Keys.ToArray() )
			.Push();

		foreach ( var (comp, edges) in targets )
		{
			var mesh = comp.Mesh;

			foreach ( var edge in edges.Distinct() )
			{
				if ( !edge.Face.IsValid )
					continue;

				if ( LimitToActiveMaterial && mesh.GetFaceMaterial( edge.Face ) != Tool.ActiveMaterial )
					continue;

				if ( Mode == PaintMode.Color ) mesh.SetVertexColor( edge, Color.White );
				else mesh.SetVertexBlend( edge, Color.Transparent );
			}
		}
	}
}
