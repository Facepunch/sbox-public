
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
		private readonly MeshTool _meshTool;

		bool TextureTreatAsOne { get; set; } = false;

		[Range( 0, 128, slider: false ), Step( 1 ), WideMode]
		public Vector2Int TextureFit { get; set; } = 1;

		public bool HotspotTiling { get; set; } = false;
		public bool HotspotConforming { get; set; } = true;

		public FaceSelectionWidget( SerializedObject so, MeshTool tool ) : base()
		{
			AddTitle( "Texture Mode", "gradient" );

			HotspotTiling = EditorCookie.Get( nameof( HotspotTiling ), HotspotTiling );
			HotspotConforming = EditorCookie.Get( nameof( HotspotConforming ), HotspotConforming );
			TextureFit = EditorCookie.Get( nameof( TextureFit ), TextureFit );
			TextureTreatAsOne = EditorCookie.Get( nameof( TextureTreatAsOne ), TextureTreatAsOne );

			var target = this.GetSerialized();
			target.OnPropertyChanged = ( e ) =>
			{
				EditorCookie.Set( nameof( HotspotTiling ), HotspotTiling );
				EditorCookie.Set( nameof( HotspotConforming ), HotspotConforming );
				EditorCookie.Set( nameof( TextureFit ), TextureFit );
				EditorCookie.Set( nameof( TextureTreatAsOne ), TextureTreatAsOne );
			};

			_meshTool = tool;
			_faces = [.. so.Targets.OfType<MeshFace>()];

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

				CreateButton( "Fit Both", "hammer/texture_fit_both.png", null, () => DoFit( TextureFit.x, TextureFit.y ), hasSelectedFaces, row );
				CreateButton( "Fit X", "hammer/texture_fit_x.png", null, () => DoFit( TextureFit.x, -1 ), hasSelectedFaces, row );
				CreateButton( "Fit Y", "hammer/texture_fit_y.png", null, () => DoFit( -1, TextureFit.y ), hasSelectedFaces, row );

				group.Add( ControlWidget.Create( target.GetProperty( nameof( TextureFit ) ) ) );
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
				row2.Add( ControlWidget.Create( target.GetProperty( nameof( TextureTreatAsOne ) ) ) );
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

				{
					var row = group.AddRow();
					row.Spacing = 4;
					CreateSmallButton( "Fast Texture Tool", "edit", "mesh.fast-texture-tool", OpenFastTextureTool, true, row );

					var apply = new Button( "Apply Material (Ctrl + RMB)", "format_color_fill" );
					apply.ToolTip = $"{apply.Text} [{EditorShortcuts.GetKeys( "mesh.apply-material" )}]";
					apply.Clicked = () => ApplyMaterial();
					row.Add( apply );
				}
			}

			if ( hasSelectedFaces )
			{
				var group = AddGroup( "Hotspot" );
				var row = group.AddRow();
				row.Spacing = 4;

				{
					row.Add( ControlWidget.Create( target.GetProperty( nameof( HotspotTiling ) ) ) ).FixedHeight = Theme.ControlHeight;
					row.Add( new Label( "Tiling" ) );
				}

				{
					row.Add( ControlWidget.Create( target.GetProperty( nameof( HotspotConforming ) ) ) ).FixedHeight = Theme.ControlHeight;
					row.Add( new Label( "Conforming" ) );
				}

				row.AddStretchCell();

				CreateButton( "Apply by Hotspot", "my_location", "mesh.apply-hotspot", ApplyMaterialByHotspot, true, row );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.fast-texture-tool", "CTRL+G", typeof( SceneViewWidget ) )]
		private void OpenFastTextureTool()
		{
			var selectedFaces = SceneEditorSession.Active.Selection.OfType<MeshFace>().ToArray();
			RectEditor.FastTextureWindow.OpenWith( selectedFaces, _meshTool.ActiveMaterial );
		}

		[Shortcut( "mesh.apply-material", "SHIFT+T", typeof( SceneViewWidget ) )]
		void ApplyMaterial()
		{
			var material = _meshTool.ActiveMaterial;
			if ( !material.IsValid() ) return;

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

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewWidget ) )]
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

		static Vector2 CalculateTextureSize( Material material )
		{
			Vector2 textureSize = 512;
			if ( material is null )
				return textureSize;

			var width = material.Attributes.GetInt( "WorldMappingWidth" );
			var height = material.Attributes.GetInt( "WorldMappingHeight" );
			var texture = material.FirstTexture;

			if ( texture != null )
			{
				textureSize.x = width > 0 ? width : (texture.Size.x * 0.25f);
				textureSize.y = height > 0 ? height : (texture.Size.y * 0.25f);
			}
			else
			{
				if ( width > 0 ) textureSize.x = width;
				if ( height > 0 ) textureSize.y = height;
			}

			return textureSize;
		}

		static readonly RectEditor.RectAssetData EmptyRectData = new();

		[Shortcut( "mesh.apply-hotspot", "Alt+H", typeof( SceneViewWidget ) )]
		void ApplyMaterialByHotspot() => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, false );

		[Shortcut( "mesh.apply-hotspot-per-face", "Alt+T", typeof( SceneViewWidget ) )]
		void ApplyMaterialByHotspotPerFace() => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, true );

		void ApplyMaterialByHotspot( Material material, bool perFace )
		{
			using var scope = SceneEditorSession.Scope();

			var data = RectEditor.RectAssetData.Find( AssetSystem.FindByPath( material.ResourcePath ) ) ?? EmptyRectData;
			var size = CalculateTextureSize( material );

			using ( SceneEditorSession.Active.UndoScope( "Apply Material By Hotspot" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					var faces = group.Select( x => x.Handle ).ToArray();

					foreach ( var face in faces )
					{
						mesh.SetFaceMaterial( face, material );
					}

					ComputeHotspotUVsForFaces( mesh, group.Key.WorldTransform, faces, data, (int)size.x, (int)size.y, perFace, HotspotTiling, HotspotConforming );
				}
			}
		}

		[Shortcut( "mesh.grow-selection", "KP_ADD", typeof( SceneViewWidget ) )]
		private void GrowSelection()
		{
			if ( _faces.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Grow Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var newFaces = new HashSet<MeshFace>();

				foreach ( var face in _faces )
				{
					if ( !face.IsValid() )
						continue;

					newFaces.Add( face );
				}

				foreach ( var face in _faces )
				{
					if ( !face.IsValid() )
						continue;

					var mesh = face.Component.Mesh;
					var edges = mesh.GetFaceEdges( face.Handle );

					foreach ( var edge in edges )
					{
						mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

						if ( faceA.IsValid && faceA != face.Handle )
							newFaces.Add( new MeshFace( face.Component, faceA ) );

						if ( faceB.IsValid && faceB != face.Handle )
							newFaces.Add( new MeshFace( face.Component, faceB ) );
					}
				}

				selection.Clear();
				foreach ( var face in newFaces )
				{
					if ( face.IsValid() )
						selection.Add( face );
				}
			}
		}

		[Shortcut( "mesh.shrink-selection", "KP_MINUS", typeof( SceneViewWidget ) )]
		private void ShrinkSelection()
		{
			if ( _faces.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Shrink Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var facesToKeep = new HashSet<MeshFace>();

				foreach ( var face in _faces )
				{
					if ( !face.IsValid() )
						continue;

					var mesh = face.Component.Mesh;
					var edges = mesh.GetFaceEdges( face.Handle );
					bool isInterior = true;

					foreach ( var edge in edges )
					{
						mesh.GetFacesConnectedToEdge( edge, out var faceA, out var faceB );

						var otherFace = faceA == face.Handle ? faceB : faceA;

						if ( !otherFace.IsValid )
						{
							isInterior = false;
							break;
						}

						var otherMeshFace = new MeshFace( face.Component, otherFace );
						if ( !_faces.Contains( otherMeshFace ) )
						{
							isInterior = false;
							break;
						}
					}

					if ( isInterior )
					{
						facesToKeep.Add( face );
					}
				}

				selection.Clear();
				foreach ( var face in facesToKeep )
				{
					if ( face.IsValid() )
						selection.Add( face );
				}
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
			var sceneView = SceneViewWidget.Current?.LastSelectedViewportWidget;
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

			if ( TextureTreatAsOne )
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
