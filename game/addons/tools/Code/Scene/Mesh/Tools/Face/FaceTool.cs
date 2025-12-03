namespace Editor.MeshEditor;

/// <summary>
/// Move, rotate and scale mesh faces
/// </summary>
[EditorTool( "mesh.face" )]
[Title( "Face Select" )]
[Icon( "change_history" )]
[Alias( "Face" )]
[Group( "Mesh" )]
[Order( 3 )]
public sealed partial class FaceTool : BaseMeshTool
{
	private MeshFace HoverFace;
	private SceneDynamicObject FaceObject;

	public override void OnEnabled()
	{
		base.OnEnabled();

		CreateFaceObject();
	}

	private void CreateFaceObject()
	{
		FaceObject = new SceneDynamicObject( Scene.SceneWorld );
		FaceObject.Material = Material.Load( "materials/tools/vertex_color_translucent.vmat" );
		FaceObject.Attributes.SetCombo( "D_DEPTH_BIAS", 1 );
		FaceObject.Attributes.SetCombo( "D_NO_CULLING", 1 );
		FaceObject.Flags.CastShadows = false;
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		FaceObject?.Delete();
		FaceObject = null;

		HoverFace = default;
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "FaceTool" );

		var result = MeshTrace.Run();
		if ( result.Hit && result.Component is MeshComponent )
			Gizmo.Hitbox.TrySetHovered( result.EndPosition );

		if ( FaceObject.IsValid() && FaceObject.World != Scene.SceneWorld )
		{
			HoverFace = default;
			FaceObject.Delete();

			CreateFaceObject();
		}

		if ( Gizmo.IsHovered )
		{
			SelectFace();

			if ( Gizmo.IsDoubleClicked )
				SelectAllFaces();
		}

		FaceObject.Init( Graphics.PrimitiveType.Triangles );

		if ( HoverFace.IsValid() )
		{
			var hoverColor = Color.Green.WithAlpha( 0.1f );
			var mesh = HoverFace.Component.Mesh;
			var vertices = mesh.CreateFace( HoverFace.Handle, HoverFace.Transform, hoverColor );
			if ( vertices is not null )
			{
				FaceObject.AddVertex( vertices.AsSpan() );
			}

			HoverFace = default;
		}

		var selectionColor = Color.Yellow.WithAlpha( 0.1f );
		foreach ( var face in MeshSelection.OfType<MeshFace>() )
		{
			var mesh = face.Component.Mesh;
			var vertices = mesh.CreateFace( face.Handle, face.Transform, selectionColor );
			if ( vertices is not null )
			{
				foreach ( var vertex in vertices )
					FaceObject.AddVertex( vertex );
			}
		}

		DrawBounds();
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var group in MeshSelection.OfType<MeshFace>()
			.GroupBy( x => x.Component ) )
		{
			var component = group.Key;
			foreach ( var hFace in component.Mesh.FaceHandles )
				yield return new MeshFace( component, hFace );
		}
	}

	private void SelectFace()
	{
		HoverFace = TraceFace();
		UpdateSelection( HoverFace );

		if ( Gizmo.IsAltPressed && Gizmo.WasRightMousePressed )
		{
			if ( Gizmo.IsShiftPressed )
			{
				WrapTextureToSelection();
			}
			else
			{
				WrapTexture();
			}
		}
	}

	private void WrapTextureToSelection()
	{
		foreach ( var face in MeshSelection.OfType<MeshFace>() )
		{
			WrapTexture( HoverFace, face );
		}
	}

	private void WrapTexture()
	{
		if ( !HoverFace.IsValid() || MeshSelection.LastOrDefault() is not MeshFace face )
			return;

		WrapTexture( face, HoverFace );
	}

	private static void WrapTexture( MeshFace sourceFace, MeshFace targetFace )
	{
		if ( !sourceFace.IsValid() )
			return;

		if ( !targetFace.IsValid() )
			return;

		var sourceMesh = sourceFace.Component.Mesh;
		var targetMesh = targetFace.Component.Mesh;

		targetFace.Material = sourceFace.Material;
		sourceMesh.GetFaceTextureParameters( sourceFace.Handle, out var vAxisU, out var vAxisV, out var vScale );

		PolygonMesh.GetBestPlanesForEdgeBetweenFaces( sourceMesh, sourceFace.Handle, sourceFace.Transform,
			targetMesh, targetFace.Handle, targetFace.Transform,
			out var fromPlane, out var toPlane );

		RotateTextureCoordinatesAroundEdge( fromPlane, toPlane, ref vAxisU, ref vAxisV, vScale );

		targetMesh.SetFaceTextureParameters( targetFace.Handle, vAxisU, vAxisV, vScale );
	}

	private static void RotateTextureCoordinatesAroundEdge( Plane fromPlane, Plane toPlane, ref Vector4 pInOutAxisU, ref Vector4 pInOutAxisV, Vector2 scale )
	{
		Vector3 vAxisUOld = (Vector3)pInOutAxisU;
		Vector3 vAxisVOld = (Vector3)pInOutAxisV;
		var flShiftUOld = pInOutAxisU.w * scale.x;
		var flShiftVOld = pInOutAxisV.w * scale.y;

		var vEdge = fromPlane.Normal.Cross( toPlane.Normal ).Normal;
		var vEdgePoint = Plane.GetIntersection( fromPlane, toPlane, new Plane( vEdge, 0.0f ) );

		var vAxisUNew = vAxisUOld;
		var vAxisVNew = vAxisVOld;
		var flShiftUNew = flShiftUOld;
		var flShiftVNew = flShiftVOld;

		if ( vEdgePoint.HasValue )
		{
			var vProjFromNormal = fromPlane.Normal - vEdge * vEdge.Dot( fromPlane.Normal );
			var vProjToNormal = toPlane.Normal - vEdge * vEdge.Dot( toPlane.Normal );

			vProjFromNormal = vProjFromNormal.Normal;
			vProjToNormal = vProjToNormal.Normal;

			var flPlanesDot = vProjFromNormal.Dot( vProjToNormal ).Clamp( -1.0f, 1.0f );
			var flRotationAngle = System.MathF.Acos( flPlanesDot ) * (180.0f / System.MathF.PI);

			if ( flPlanesDot < 0.0f )
			{
				flRotationAngle = 180.0f - flRotationAngle;
			}

			var mEdgeRotation = Rotation.FromAxis( vEdge, flRotationAngle );
			vAxisUNew = vAxisUOld * mEdgeRotation;
			vAxisVNew = vAxisVOld * mEdgeRotation;

			var edgePoint = vEdgePoint.Value;
			var flPointU = (Vector3.Dot( vAxisUOld, edgePoint ) + flShiftUOld) / scale.x;
			var flPointV = (Vector3.Dot( vAxisVOld, edgePoint ) + flShiftVOld) / scale.y;

			var flNewPointUnshiftedU = Vector3.Dot( vAxisUNew, edgePoint ) / scale.x;
			var flNewPointUnshiftedV = Vector3.Dot( vAxisVNew, edgePoint ) / scale.y;

			var flNeededShiftU = flPointU - flNewPointUnshiftedU;
			var flNeededShiftV = flPointV - flNewPointUnshiftedV;

			flShiftUNew = flNeededShiftU * scale.x;
			flShiftVNew = flNeededShiftV * scale.y;
		}

		pInOutAxisU = new Vector4( vAxisUNew, flShiftUNew / scale.x );
		pInOutAxisV = new Vector4( vAxisVNew, flShiftVNew / scale.y );
	}

	private void SelectAllFaces()
	{
		var face = TraceFace();
		if ( !face.IsValid() )
			return;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			MeshSelection.Clear();

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			foreach ( var hFace in face.Component.Mesh.FaceHandles )
				MeshSelection.Add( new MeshFace( face.Component, hFace ) );
		}
	}

	public override List<MeshFace> ExtrudeSelection( Vector3 delta = default )
	{
		var faces = MeshSelection.OfType<MeshFace>().ToArray();

		Selection.Clear();

		var connectingFaces = new List<MeshFace>();

		var components = faces.Select( x => x.Component ).Distinct();

		foreach ( var group in faces.GroupBy( x => x.Component ) )
		{
			var offset = group.Key.WorldRotation.Inverse * delta;
			group.Key.Mesh.ExtrudeFaces( group.Select( x => x.Handle ).ToArray(), out var newFaces, out var newConnectingFaces, offset );

			foreach ( var hFace in newFaces )
				Selection.Add( new MeshFace( group.Key, hFace ) );

			foreach ( var hFace in newConnectingFaces )
				connectingFaces.Add( new MeshFace( group.Key, hFace ) );
		}

		CalculateSelectionVertices();

		return connectingFaces;
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( Gizmo.Settings.GlobalSpace )
			return Rotation.Identity;

		var face = MeshSelection.OfType<MeshFace>().FirstOrDefault();
		if ( face.IsValid() )
		{
			face.Component.Mesh.ComputeFaceNormal( face.Handle, out var normal );
			var vAxis = ComputeTextureVAxis( normal );
			var basis = Rotation.LookAt( normal, vAxis * -1.0f );
			return face.Transform.RotationToWorld( basis );
		}

		return Rotation.Identity;
	}

	private void DrawBounds()
	{
		using ( Gizmo.Scope( "Face Size" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineThickness = 4;

			//TODO: Draw Text for edges closest to the camera
			var box = CalculateSelectionBounds();
			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

			Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.LineBBox( box );

			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Gizmo.Colors.Left;
			if ( box.Size.y > 0.01f )
				Gizmo.Draw.ScreenText( $"L: {box.Size.y:0.#}", box.Maxs.WithY( box.Center.y ), Vector2.Up * 32, size: textSize );
			Gizmo.Draw.Line( box.Maxs.WithY( box.Mins.y ), box.Maxs.WithY( box.Maxs.y ) );
			Gizmo.Draw.Color = Gizmo.Colors.Forward;
			if ( box.Size.x > 0.01f )
				Gizmo.Draw.ScreenText( $"W: {box.Size.x:0.#}", box.Maxs.WithX( box.Center.x ), Vector2.Up * 32, size: textSize );
			Gizmo.Draw.Line( box.Maxs.WithX( box.Mins.x ), box.Maxs.WithX( box.Maxs.x ) );
			Gizmo.Draw.Color = Gizmo.Colors.Up;
			if ( box.Size.z > 0.01f )
				Gizmo.Draw.ScreenText( $"H: {box.Size.z:0.#}", box.Maxs.WithZ( box.Center.z ), Vector2.Up * 32, size: textSize );
			Gizmo.Draw.Line( box.Maxs.WithZ( box.Mins.z ), box.Maxs.WithZ( box.Maxs.z ) );
		}
	}

	[Shortcut( "mesh.face", "3" )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( FaceTool ) );
	}
}
