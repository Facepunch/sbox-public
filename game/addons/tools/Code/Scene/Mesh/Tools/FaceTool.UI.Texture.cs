using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class FaceTool
{
	public partial class FaceSelectionWidget
	{
		public bool TextureTreatAsOne { get; set; } = false;

		[Range( 0, 128, slider: false ), Step( 1 ), WideMode]
		public Vector2Int TextureFit { get; set; } = 1;

		public bool HotspotTiling { get; set; } = false;
		public bool HotspotConforming { get; set; } = true;
		public bool HotspotUseActiveMaterial { get; set; } = true;
		public bool HotspotAllowMirrorHorizontal { get; set; } = false;
		public bool HotspotAllowMirrorVertical { get; set; } = false;

		private void LoadTextureSettings()
		{
			HotspotTiling = EditorCookie.Get( nameof( HotspotTiling ), HotspotTiling );
			HotspotConforming = EditorCookie.Get( nameof( HotspotConforming ), HotspotConforming );
			HotspotUseActiveMaterial = EditorCookie.Get( nameof( HotspotUseActiveMaterial ), HotspotUseActiveMaterial );
			HotspotAllowMirrorHorizontal = EditorCookie.Get( nameof( HotspotAllowMirrorHorizontal ), HotspotAllowMirrorHorizontal );
			HotspotAllowMirrorVertical = EditorCookie.Get( nameof( HotspotAllowMirrorVertical ), HotspotAllowMirrorVertical );
			TextureFit = EditorCookie.Get( nameof( TextureFit ), TextureFit );
			TextureTreatAsOne = EditorCookie.Get( nameof( TextureTreatAsOne ), TextureTreatAsOne );
			_activePanel = EditorCookie.Get<string>( "TextureActivePanel", null );
		}

		private void SaveTextureSettings()
		{
			EditorCookie.Set( nameof( HotspotTiling ), HotspotTiling );
			EditorCookie.Set( nameof( HotspotConforming ), HotspotConforming );
			EditorCookie.Set( nameof( HotspotUseActiveMaterial ), HotspotUseActiveMaterial );
			EditorCookie.Set( nameof( HotspotAllowMirrorHorizontal ), HotspotAllowMirrorHorizontal );
			EditorCookie.Set( nameof( HotspotAllowMirrorVertical ), HotspotAllowMirrorVertical );
			EditorCookie.Set( nameof( TextureFit ), TextureFit );
			EditorCookie.Set( nameof( TextureTreatAsOne ), TextureTreatAsOne );
			EditorCookie.Set( "TextureActivePanel", _activePanel );
		}

		string _activePanel = null;
		Widget _panelContainer;
		SerializedObject _textureTarget;

		private void BuildTextureUI( SerializedObject so, SerializedObject target )
		{
			_textureTarget = target;

			bool hasSelectedFaces = _faces.Length > 0;

			{
				var group = AddGroup( "Texture", collapsible: true );

				var row1 = group.AddRow();
				row1.Spacing = 2;
				row1.AddStretchCell();

				AddToggleButton( "Align", "grid_on", hasSelectedFaces, row1, "align" );
				AddToggleButton( "Scale", "open_in_full", hasSelectedFaces, row1, "scale" );
				AddToggleButton( "Shift", "open_with", hasSelectedFaces, row1, "shift" );
				AddToggleButton( "Fit", "fit_screen", hasSelectedFaces, row1, "fit" );
				AddToggleButton( "Justify", "format_align_center", hasSelectedFaces, row1, "justify" );
				AddToggleButton( "Hotspot", "my_location", hasSelectedFaces, row1, "hotspot" );

				row1.AddStretchCell();

				_panelContainer = new Widget();
				_panelContainer.Layout = Layout.Column();
				_panelContainer.Layout.Spacing = 2;
				group.Add( _panelContainer );

				RebuildPanel();
			}

			if ( hasSelectedFaces )
			{
				var group = AddGroup( "Texture Selection", collapsible: true );

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

					var apply = new Button( "Apply Material (Ctrl + RMB)", "format_color_fill" );
					apply.ToolTip = $"{apply.Text} [{EditorShortcuts.GetKeys( "mesh.apply-material" )}]";
					apply.Clicked = () => ApplyMaterial();
					row.Add( apply );
				}
			}
		}

		private void RebuildPanel()
		{
			if ( _panelContainer is null ) return;

			_panelContainer.Layout.Clear( true );

			if ( _faces.Length == 0 || _activePanel is null )
				return;

			if ( _activePanel == "align" )
			{
				var row = _panelContainer.Layout.AddRow();
				row.Spacing = 2;
				row.AddStretchCell();
				AddIconBtn( "hammer/texture_align_grid.png", AlignToGrid, true, row, "Align to Grid" );
				AddIconBtn( "hammer/texture_align_face.png", AlignToFace, true, row, "Align to Face" );
				AddIconBtn( "hammer/texture_align_view.png", AlignToView, true, row, "Align to View" );
				AddIconBtn( "hammer/texture_rotate_cw.png", () => DoRotate( true ), true, row, "Rotate CW" );
				AddIconBtn( "hammer/texture_rotate_ccw.png", () => DoRotate( false ), true, row, "Rotate CCW" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "scale" )
			{
				var row = _panelContainer.Layout.AddRow();
				row.Spacing = 2;
				row.AddStretchCell();
				AddIconBtn( "hammer/texture_scale_up_x.png", () => DoScaleX( true ), true, row, "Scale X Up" );
				AddIconBtn( "hammer/texture_scale_dn_x.png", () => DoScaleX( false ), true, row, "Scale X Down" );
				AddIconBtn( "hammer/texture_scale_up_y.png", () => DoScaleY( true ), true, row, "Scale Y Up" );
				AddIconBtn( "hammer/texture_scale_dn_y.png", () => DoScaleY( false ), true, row, "Scale Y Down" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "shift" )
			{
				var row = _panelContainer.Layout.AddRow();
				row.Spacing = 2;
				row.AddStretchCell();
				AddIconBtn( "hammer/texture_shift_left.png", () => DoShiftX( true ), true, row, "Shift Left" );
				AddIconBtn( "hammer/texture_shift_right.png", () => DoShiftX( false ), true, row, "Shift Right" );
				AddIconBtn( "hammer/texture_shift_up.png", () => DoShiftY( true ), true, row, "Shift Up" );
				AddIconBtn( "hammer/texture_shift_down.png", () => DoShiftY( false ), true, row, "Shift Down" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "fit" )
			{
				var row = _panelContainer.Layout.AddRow();
				row.Spacing = 2;
				row.AddStretchCell();
				AddIconBtn( "hammer/texture_fit_both.png", () => DoFit( TextureFit.x, TextureFit.y ), true, row, "Fit Both" );
				AddIconBtn( "hammer/texture_fit_x.png", () => DoFit( TextureFit.x, -1 ), true, row, "Fit X" );
				AddIconBtn( "hammer/texture_fit_y.png", () => DoFit( -1, TextureFit.y ), true, row, "Fit Y" );

				var settingsBtn = new IconButton( "settings" )
				{
					IconSize = 24,
					FixedSize = 32,
					ToolTip = "Fit Settings",
				};
				settingsBtn.OnClick = () =>
				{
					var p = new PopupWidget( _panelContainer );
					p.Layout = Layout.Column();
					p.Layout.Spacing = 4;
					p.Layout.Margin = 8;
					p.MaximumWidth = 200;

					p.Layout.Add( ControlSheetRow.Create( _textureTarget.GetProperty( nameof( TextureFit ) ) ) );

					p.AdjustSize();
					p.OpenAt( settingsBtn.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -8 ) );
				};
				row.Add( settingsBtn );
				row.AddStretchCell();
			}
			else if ( _activePanel == "justify" )
			{
				var row = _panelContainer.Layout.AddRow();
				row.Spacing = 2;
				row.AddStretchCell();
				AddIconBtn( "hammer/texture_justify_l.png", () => DoJustify( PolygonMesh.TextureJustification.Left ), true, row, "Left" );
				AddIconBtn( "hammer/texture_justify_t.png", () => DoJustify( PolygonMesh.TextureJustification.Top ), true, row, "Top" );
				AddIconBtn( "hammer/texture_justify_c.png", () => DoJustify( PolygonMesh.TextureJustification.Center ), true, row, "Center" );
				AddIconBtn( "hammer/texture_justify_b.png", () => DoJustify( PolygonMesh.TextureJustification.Bottom ), true, row, "Bottom" );
				AddIconBtn( "hammer/texture_justify_r.png", () => DoJustify( PolygonMesh.TextureJustification.Right ), true, row, "Right" );

				var settingsBtn = new IconButton( "settings" )
				{
					IconSize = 24,
					FixedSize = 32,
					ToolTip = "Justify Settings",
				};
				settingsBtn.OnClick = () =>
				{
					var p = new PopupWidget( _panelContainer );
					p.Layout = Layout.Column();
					p.Layout.Spacing = 4;
					p.Layout.Margin = 8;

					var optRow = p.Layout.AddRow();
					optRow.Spacing = 4;
					optRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( TextureTreatAsOne ) ) ) ).FixedHeight = Theme.ControlHeight;
					optRow.Add( new Label( "Treat as one" ) );
					optRow.AddStretchCell();

					p.AdjustSize();
					p.OpenAt( settingsBtn.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -8 ) );
				};
				row.Add( settingsBtn );
				row.AddStretchCell();
			}
			else if ( _activePanel == "hotspot" )
			{
				var applyRow = _panelContainer.Layout.AddRow();
				applyRow.Spacing = 2;
				applyRow.AddStretchCell();
				AddIconBtn( "my_location", () => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, false ), true, applyRow, "Apply Hotspot" );
				AddIconBtn( "texture", () => ApplyMaterialByHotspot( _meshTool.ActiveMaterial, true ), true, applyRow, "Apply Hotspot (Per Face)" );

				var settingsBtn = new IconButton( "settings" )
				{
					IconSize = 24,
					FixedSize = 32,
					ToolTip = "Hotspot Settings",
				};
				settingsBtn.OnClick = () =>
				{
					var p = new PopupWidget( _panelContainer );
					p.Layout = Layout.Column();
					p.Layout.Spacing = 2;
					p.Layout.Margin = 8;

					var materialSourceRow = p.Layout.AddRow();
					materialSourceRow.Spacing = 4;
					materialSourceRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( HotspotUseActiveMaterial ) ) ) ).FixedHeight = Theme.ControlHeight;
					materialSourceRow.Add( new Label( "Use Active Material" ) );
					materialSourceRow.AddStretchCell();

					var optionsRow = p.Layout.AddRow();
					optionsRow.Spacing = 4;
					optionsRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( HotspotTiling ) ) ) ).FixedHeight = Theme.ControlHeight;
					optionsRow.Add( new Label( "Tiling" ) );
					optionsRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( HotspotConforming ) ) ) ).FixedHeight = Theme.ControlHeight;
					optionsRow.Add( new Label( "Conforming" ) );
					optionsRow.AddStretchCell();

					var mirrorHRow = p.Layout.AddRow();
					mirrorHRow.Spacing = 4;
					mirrorHRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( HotspotAllowMirrorHorizontal ) ) ) ).FixedHeight = Theme.ControlHeight;
					mirrorHRow.Add( new Label( "Mirror H" ) );
					mirrorHRow.Add( ControlWidget.Create( _textureTarget.GetProperty( nameof( HotspotAllowMirrorVertical ) ) ) ).FixedHeight = Theme.ControlHeight;
					mirrorHRow.Add( new Label( "Mirror V" ) );
					mirrorHRow.AddStretchCell();

					p.AdjustSize();
					p.OpenAt( settingsBtn.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -8 ) );
				};
				applyRow.Add( settingsBtn );
				applyRow.AddStretchCell();
			}

			_panelContainer.AdjustSize();
			_panelContainer.UpdateGeometry();
			_panelContainer.Parent?.AdjustSize();
		}

		private void AddToggleButton( string tooltip, string icon, bool enabled, Layout row, string panelName )
		{
			var btn = new IconButton( icon )
			{
				Enabled = enabled,
				IconSize = 24,
				FixedSize = 32,
				ToolTip = tooltip,
				IsActive = _activePanel == panelName,
			};
			btn.OnClick = () =>
			{
				_activePanel = _activePanel == panelName ? null : panelName;
				EditorCookie.Set( "TextureActivePanel", _activePanel );

				foreach ( var sibling in btn.Parent.Children.OfType<IconButton>() )
				{
					sibling.IsActive = sibling == btn && _activePanel == panelName;
				}

				RebuildPanel();
			};
			row.Add( btn );
		}

		static void AddIconBtn( string icon, Action clicked, bool enabled, Layout row, string tooltip = null )
		{
			var btn = new IconButton( icon, clicked )
			{
				Enabled = enabled,
				IconSize = 24,
				FixedSize = 32,
				ToolTip = tooltip,
			};
			row.Add( btn );
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
			if ( HotspotUseActiveMaterial && (material is null || !material.IsValid()) ) return;

			using ( SceneEditorSession.Active.UndoScope( "Apply Material By Hotspot" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					if ( HotspotUseActiveMaterial )
					{
						var faces = group.Select( x => x.Handle ).ToArray();
						foreach ( var face in faces )
						{
							mesh.SetFaceMaterial( face, material );
						}

						ApplyHotspotForFaces( mesh, group.Key.WorldTransform, faces, material, perFace );
					}
					else
					{
						foreach ( var materialGroup in group.GroupBy( face => mesh.GetFaceMaterial( face.Handle ) ) )
						{
							var faces = materialGroup.Select( x => x.Handle ).ToArray();
							ApplyHotspotForFaces( mesh, group.Key.WorldTransform, faces, materialGroup.Key, perFace );
						}
					}
				}
			}
		}

		private void ApplyHotspotForFaces( PolygonMesh mesh, Transform transform, FaceHandle[] faces, Material material, bool perFace )
		{
			if ( faces.Length == 0 ) return;

			var resourcePath = material is not null && material.IsValid() ? material.ResourcePath : null;
			var data = !string.IsNullOrEmpty( resourcePath )
				? RectEditor.RectAssetData.Find( AssetSystem.FindByPath( resourcePath ) ) ?? EmptyRectData
				: EmptyRectData;
			var size = CalculateTextureSize( material );
			ComputeHotspotUVsForFaces( mesh, transform, faces, data, (int)size.x, (int)size.y, perFace, HotspotTiling, HotspotConforming, HotspotAllowMirrorHorizontal, HotspotAllowMirrorVertical );
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
