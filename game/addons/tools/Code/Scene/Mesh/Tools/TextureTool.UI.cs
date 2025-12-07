
namespace Editor.MeshEditor;

partial class TextureTool
{
	public override Widget CreateToolSidebar()
	{
		return new FaceSelectionWidget( GetSerializedSelection(), Tool );
	}

	public class FaceSelectionWidget : ToolSidebarWidget
	{
		private readonly MeshFace[] _faces;
		private readonly List<IGrouping<MeshComponent, MeshFace>> _faceGroups;
		private readonly List<MeshComponent> _components;

		private bool TreatAsOne = false;

		[Range( 0, 128, slider: false ), Step( 1 ), WideMode]
		private Vector2Int Fit = 1;

		public FaceSelectionWidget( SerializedObject so, MeshTool tool ) : base()
		{
			AddTitle( "Texture Mode", "gradient" );

			_faces = so.Targets
				.OfType<MeshFace>()
				.ToArray();

			_faceGroups = _faces.GroupBy( x => x.Component ).ToList();
			_components = _faceGroups.Select( x => x.Key ).ToList();

			bool hasSelectedFaces = _faces.Length > 0;

			{
				var group = AddGroup( "Align & Rotate" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Align to Grid", "hammer/texture_align_grid.png", null, AlignToGrid, hasSelectedFaces, row );
				CreateButton( "Align to Face", "hammer/texture_align_face.png", null, AlignToFace, hasSelectedFaces, row );
				CreateButton( "Align to View", "hammer/texture_align_view.png", null, AlignToView, hasSelectedFaces, row );
				CreateButton( "Rotate CW", "hammer/texture_rotate_cw.png", null, () => DoRotate( true ), hasSelectedFaces, row );
				CreateButton( "Rotate CCW", "hammer/texture_rotate_ccw.png", null, () => DoRotate( false ), hasSelectedFaces, row );
			}

			{
				var group = AddGroup( "Scale" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Scale X Up", "hammer/texture_scale_up_x.png", null, () => DoScaleX( true ), hasSelectedFaces, row );
				CreateButton( "Scale X Down", "hammer/texture_scale_dn_x.png", null, () => DoScaleX( false ), hasSelectedFaces, row );
				CreateButton( "Scale Y Up", "hammer/texture_scale_up_y.png", null, () => DoScaleY( true ), hasSelectedFaces, row );
				CreateButton( "Scale Y Down", "hammer/texture_scale_dn_y.png", null, () => DoScaleY( false ), hasSelectedFaces, row );
			}

			{
				var group = AddGroup( "Shift" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Shift Left", "hammer/texture_shift_left.png", null, () => DoShiftX( true ), hasSelectedFaces, row );
				CreateButton( "Shift Right", "hammer/texture_shift_right.png", null, () => DoShiftX( false ), hasSelectedFaces, row );
				CreateButton( "Shift Up", "hammer/texture_shift_up.png", null, () => DoShiftY( true ), hasSelectedFaces, row );
				CreateButton( "Shift Down", "hammer/texture_shift_down.png", null, () => DoShiftY( false ), hasSelectedFaces, row );
			}

			{
				var group = AddGroup( "Fit" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Fit Both", "hammer/texture_fit_both.png", null, () => DoFit( Fit.x, Fit.y ), hasSelectedFaces, row );
				CreateButton( "Fit X", "hammer/texture_fit_x.png", null, () => DoFit( Fit.x, -1 ), hasSelectedFaces, row );
				CreateButton( "Fit Y", "hammer/texture_fit_y.png", null, () => DoFit( -1, Fit.y ), hasSelectedFaces, row );

				group.Add( ControlWidget.Create( this.GetSerialized().GetProperty( nameof( Fit ) ) ) );
			}

			{
				var group = AddGroup( "Justify" );
				var row = group.AddRow();
				row.Spacing = 4;

				CreateButton( "Justify Left", "hammer/texture_justify_l.png", null, () => DoJustify( PolygonMesh.TextureJustification.Left ), hasSelectedFaces, row );
				CreateButton( "Justify Right", "hammer/texture_justify_r.png", null, () => DoJustify( PolygonMesh.TextureJustification.Right ), hasSelectedFaces, row );
				CreateButton( "Justify Top", "hammer/texture_justify_t.png", null, () => DoJustify( PolygonMesh.TextureJustification.Top ), hasSelectedFaces, row );
				CreateButton( "Justify Bottom", "hammer/texture_justify_b.png", null, () => DoJustify( PolygonMesh.TextureJustification.Bottom ), hasSelectedFaces, row );
				CreateButton( "Justify Center", "hammer/texture_justify_c.png", null, () => DoJustify( PolygonMesh.TextureJustification.Center ), hasSelectedFaces, row );

				var row2 = group.AddRow();
				row2.Spacing = 4;
				row2.Add( ControlWidget.Create( this.GetSerialized().GetProperty( nameof( TreatAsOne ) ) ) );
				row2.Add( new Label( "Treat as one" ) );
			}

			if ( hasSelectedFaces )
			{
				var group = AddGroup( "Selection" );

				{
					var r = group.AddRow();
					r.Spacing = 4;
					r.Add( new IconLabel( "swap_horiz" ) );
					r.Add( ControlWidget.Create( so.GetProperty( nameof( MeshFace.TextureOffset ) ) ) );
				}

				{
					var r = group.AddRow();
					r.Spacing = 4;
					r.Add( new IconLabel( "open_in_full" ) );
					r.Add( ControlWidget.Create( so.GetProperty( nameof( MeshFace.TextureScale ) ) ) );
				}

				var apply = new Button( "Apply Material (Ctrl + RMB)", "format_color_fill" );
				apply.Clicked = () => ApplyMaterial( tool.ActiveMaterial );
				group.Add( apply );
			}

			Layout.AddStretchCell();
		}

		void ApplyMaterial( Material material )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Apply Material" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					mesh.SetFaceMaterial( face.Handle, material );
				}
			}
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewportWidget ) )]
		private void DeleteSelection()
		{
			var groups = _faces.GroupBy( face => face.Component );

			if ( !groups.Any() )
				return;

			var components = groups.Select( x => x.Key ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Delete Faces" ).WithComponentChanges( components ).Push() )
			{
				foreach ( var group in groups )
					group.Key.Mesh.RemoveFaces( group.Select( x => x.Handle ) );
			}
		}

		private void AlignToGrid()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Align to Grid" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					face.Component.Mesh.TextureAlignToGrid( face.Transform, face.Handle );
				}
			}
		}

		private void AlignToFace()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Align to Face" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					face.Component.Mesh.TextureAlignToFace( face.Transform, face.Handle );
				}
			}
		}

		private void AlignToView()
		{
			var sceneView = SceneViewportWidget.LastSelected;
			if ( !sceneView.IsValid() )
				return;

			using var scope = SceneEditorSession.Scope();

			var position = sceneView.State.CameraPosition;
			var rotation = sceneView.State.CameraRotation;
			var uAxis = rotation.Right;
			var vAxis = rotation.Up;
			var offset = new Vector2( uAxis.Dot( position ), vAxis.Dot( position ) );

			using ( SceneEditorSession.Active.UndoScope( "Align to View" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					face.Component.Mesh.SetFaceTextureParameters( face.Handle, offset, uAxis, vAxis );
				}
			}
		}

		private void DoRotate( bool clockwise )
		{
			using var scope = SceneEditorSession.Scope();

			var amount = EditorScene.GizmoSettings.AngleSpacing * (clockwise ? 1 : -1);

			using ( SceneEditorSession.Active.UndoScope( "Rotate" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					mesh.GetFaceTextureParameters( face.Handle, out var axisU, out var axisV, out var scale );

					Vector3 newAxisU = (Vector3)axisU;
					Vector3 newAxisV = (Vector3)axisV;
					var axis = Vector3.Cross( newAxisU, newAxisV );
					axis = axis.Normal;

					var rotation = Rotation.FromAxis( axis, amount );
					newAxisU *= rotation;
					newAxisV *= rotation;
					newAxisU = newAxisU.Normal;
					newAxisV = newAxisV.Normal;

					mesh.SetFaceTextureParameters( face.Handle, new Vector4( newAxisU, axisU.w ), new Vector4( newAxisV, axisV.w ), scale );
				}
			}
		}

		private void DoShiftX( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			var gridSpacing = EditorScene.GizmoSettings.GridSpacing;

			using ( SceneEditorSession.Active.UndoScope( "Shift X" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle ).x;
					scale = scale.AlmostEqual( 0.0f ) ? 0.25f : scale;
					var amount = gridSpacing / scale;
					var offset = mesh.GetTextureOffset( face.Handle );
					offset = offset.WithX( offset.x + amount * (positive ? 1.0f : -1.0f) );
					mesh.SetTextureOffset( face.Handle, offset );
				}
			}
		}

		private void DoShiftY( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			var gridSpacing = EditorScene.GizmoSettings.GridSpacing;

			using ( SceneEditorSession.Active.UndoScope( "Shift Y" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle ).y;
					scale = scale.AlmostEqual( 0.0f ) ? 0.25f : scale;
					var amount = gridSpacing / scale;
					var offset = mesh.GetTextureOffset( face.Handle );
					offset = offset.WithY( offset.y + amount * (positive ? 1.0f : -1.0f) );
					mesh.SetTextureOffset( face.Handle, offset );
				}
			}
		}

		private void DoScaleX( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Scale X" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle );
					scale = scale.WithX( scale.x * (positive ? 2.0f : 0.5f) );
					mesh.SetTextureScale( face.Handle, scale );
				}
			}
		}

		private void DoScaleY( bool positive )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Scale Y" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					var scale = mesh.GetTextureScale( face.Handle );
					scale = scale.WithY( scale.y * (positive ? 2.0f : 0.5f) );
					mesh.SetTextureScale( face.Handle, scale );
				}
			}
		}

		private void DoJustify( PolygonMesh.TextureJustification justification )
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Justify" )
				.WithComponentChanges( _components )
				.Push() )
			{
				JustifyTexturesForFaceSelection( justification );

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ComputeFaceTextureCoordinatesFromParameters( group.Select( x => x.Handle ) );
				}
			}
		}

		private void DoFit( int repeatX, int repeatY )
		{
			using var scope = SceneEditorSession.Scope();

			var justification = PolygonMesh.TextureJustification.Fit;
			if ( repeatX == -1 ) justification = PolygonMesh.TextureJustification.FitY;
			else if ( repeatY == -1 ) justification = PolygonMesh.TextureJustification.FitX;

			using ( SceneEditorSession.Active.UndoScope( "Fit" )
				.WithComponentChanges( _components )
				.Push() )
			{
				JustifyTexturesForFaceSelection( justification );

				if ( repeatX > 0 || repeatY > 0 )
				{
					foreach ( var face in _faces )
					{
						var mesh = face.Component.Mesh;
						var scale = mesh.GetTextureScale( face.Handle );

						if ( repeatX > 0 )
							scale.x /= repeatX;

						if ( repeatY > 0 )
							scale.y /= repeatY;

						mesh.SetTextureScale( face.Handle, scale );
					}
				}

				if ( repeatX != -1 )
					JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification.Left );

				if ( repeatY != -1 )
					JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification.Top );

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ComputeFaceTextureCoordinatesFromParameters( group.Select( x => x.Handle ) );
				}
			}
		}

		private void JustifyTexturesForFaceSelection( PolygonMesh.TextureJustification justification )
		{
			PolygonMesh.FaceExtents extents = null;

			if ( TreatAsOne )
			{
				extents = new PolygonMesh.FaceExtents();

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.UnionExtentsForFaces( group.Select( x => x.Handle ), mesh.Transform, extents );
				}
			}

			foreach ( var group in _faceGroups )
			{
				var mesh = group.Key.Mesh;
				mesh.JustifyFaceTextureParameters( group.Select( x => x.Handle ), justification, extents );
			}
		}
	}
}
