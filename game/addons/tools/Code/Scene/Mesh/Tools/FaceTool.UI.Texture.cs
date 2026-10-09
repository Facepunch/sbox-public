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

				var modeLabelRow = group.AddRow();
				modeLabelRow.Add( new Label.Small( "Mode" ) );
				modeLabelRow.AddStretchCell();

				var row1 = group.AddRow();
				row1.Spacing = 4;
				row1.AddStretchCell();

				AddToggleButton( "Align", "meshtools/texture_tool_buttons/texture_align.png", hasSelectedFaces, row1, "align" );
				AddToggleButton( "Scale", "meshtools/texture_tool_buttons/texture_scale.png", hasSelectedFaces, row1, "scale" );
				AddToggleButton( "Shift", "meshtools/texture_tool_buttons/texture_shift.png", hasSelectedFaces, row1, "shift" );
				AddToggleButton( "Fit", "meshtools/texture_tool_buttons/texture_fit.png", hasSelectedFaces, row1, "fit" );
				AddToggleButton( "Justify", "meshtools/texture_tool_buttons/texture_justify.png", hasSelectedFaces, row1, "justify" );
				AddToggleButton( "Hotspot", "meshtools/texture_tool_buttons/texture_hotspot.png", hasSelectedFaces, row1, "hotspot" );

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

			if ( _faces.Length == 0 )
				return;

			_activePanel ??= "align";

			var submodeLabelRow = _panelContainer.Layout.AddRow();
			submodeLabelRow.Add( new Label.Small( _activePanel switch
			{
				"align" => "Align",
				"scale" => "Scale",
				"shift" => "Shift",
				"fit" => "Fit",
				"justify" => "Justify",
				"hotspot" => "Hotspot",
				_ => "Align"
			} ) );
			submodeLabelRow.AddStretchCell();

			var row = _panelContainer.Layout.AddRow();
			row.Spacing = 4;
			row.AddStretchCell();

			if ( _activePanel == "align" )
			{
				AddIconBtn( "meshtools/texture_tool_buttons/align_to_grid.png", AlignToGrid, true, row, "Align to Grid", "mesh.texture-align-grid" );
				AddIconBtn( "meshtools/texture_tool_buttons/align_to_face.png", AlignToFace, true, row, "Align to Face", "mesh.texture-align-face" );
				AddIconBtn( "meshtools/texture_tool_buttons/align_to_view.png", AlignToView, true, row, "Align to View", "mesh.texture-align-view" );
				AddIconBtn( "meshtools/texture_tool_buttons/rotate_cw.png", RotateClockwise, true, row, "Rotate CW", "mesh.texture-rotate-clockwise" );
				AddIconBtn( "meshtools/texture_tool_buttons/rotate_ccw.png", RotateCounterclockwise, true, row, "Rotate CCW", "mesh.texture-rotate-counterclockwise" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "scale" )
			{
				AddIconBtn( "meshtools/texture_tool_buttons/scale_x_up.png", ScaleXUp, true, row, "Scale X Up", "mesh.texture-scale-x-up" );
				AddIconBtn( "meshtools/texture_tool_buttons/scale_x_down.png", ScaleXDown, true, row, "Scale X Down", "mesh.texture-scale-x-down" );
				AddIconBtn( "meshtools/texture_tool_buttons/scale_y_up.png", ScaleYUp, true, row, "Scale Y Up", "mesh.texture-scale-y-up" );
				AddIconBtn( "meshtools/texture_tool_buttons/scale_y_down.png", ScaleYDown, true, row, "Scale Y Down", "mesh.texture-scale-y-down" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "shift" )
			{
				AddIconBtn( "meshtools/texture_tool_buttons/shift_left.png", ShiftLeft, true, row, "Shift Left", "mesh.texture-shift-left" );
				AddIconBtn( "meshtools/texture_tool_buttons/shift_right.png", ShiftRight, true, row, "Shift Right", "mesh.texture-shift-right" );
				AddIconBtn( "meshtools/texture_tool_buttons/shift_up.png", ShiftUp, true, row, "Shift Up", "mesh.texture-shift-up" );
				AddIconBtn( "meshtools/texture_tool_buttons/shift_down.png", ShiftDown, true, row, "Shift Down", "mesh.texture-shift-down" );
				row.AddStretchCell();
			}
			else if ( _activePanel == "fit" )
			{
				AddIconBtn( "meshtools/texture_tool_buttons/fit_both.png", FitBoth, true, row, "Fit Both", "mesh.texture-fit-both" );
				AddIconBtn( "meshtools/texture_tool_buttons/fit_x.png", FitX, true, row, "Fit X", "mesh.texture-fit-x" );
				AddIconBtn( "meshtools/texture_tool_buttons/fit_y.png", FitY, true, row, "Fit Y", "mesh.texture-fit-y" );

				var settingsBtn = new IconButton( "meshtools/additionals/settings.png" )
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
				AddIconBtn( "meshtools/texture_tool_buttons/justify_left.png", JustifyLeft, true, row, "Left", "mesh.texture-justify-left" );
				AddIconBtn( "meshtools/texture_tool_buttons/justify_top.png", JustifyTop, true, row, "Top", "mesh.texture-justify-top" );
				AddIconBtn( "meshtools/texture_tool_buttons/justify_center.png", JustifyCenter, true, row, "Center", "mesh.texture-justify-center" );
				AddIconBtn( "meshtools/texture_tool_buttons/justify_bottom.png", JustifyBottom, true, row, "Bottom", "mesh.texture-justify-bottom" );
				AddIconBtn( "meshtools/texture_tool_buttons/justify_right.png", JustifyRight, true, row, "Right", "mesh.texture-justify-right" );

				var settingsBtn = new IconButton( "meshtools/additionals/settings.png" )
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
				AddIconBtn( "meshtools/texture_tool_buttons/apply_by_hotspot.png", ApplyMaterialByHotspot, true, applyRow, "Apply Hotspot", "mesh.apply-hotspot" );
				AddIconBtn( "meshtools/texture_tool_buttons/apply_by_hotspot_(per_face).png", ApplyMaterialByHotspotPerFace, true, applyRow, "Apply Hotspot (Per Face)", "mesh.apply-hotspot-per-face" );

				var settingsBtn = new IconButton( "meshtools/additionals/settings.png" )
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
				if ( _activePanel == panelName )
					return;

				_activePanel = panelName;
				EditorCookie.Set( "TextureActivePanel", _activePanel );

				foreach ( var sibling in btn.Parent.Children.OfType<IconButton>() )
				{
					sibling.IsActive = sibling == btn;
				}

				RebuildPanel();
			};
			row.Add( btn );
		}

		static void AddIconBtn( string icon, Action clicked, bool enabled, Layout row, string tooltip, string shortcut )
		{
			var keys = EditorShortcuts.GetDisplayKeys( shortcut );
			var btn = new IconButton( icon, clicked )
			{
				Enabled = enabled,
				IconSize = 24,
				FixedSize = 32,
				ToolTip = string.IsNullOrEmpty( keys ) ? tooltip : $"{tooltip} [{keys}]",
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
				foreach ( var group in _faces.GroupBy( x => x.Component.Mesh ) )
				{
					group.Key.AssignMaterialToFaces( group.Select( x => x.Handle ), material );
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
						mesh.AssignMaterialToFaces( faces, material );

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


		[Shortcut( "mesh.find-replace-material-tool", "SHIFT+R", typeof( SceneViewWidget ) )]
		void OpenFindReplaceMaterialTool()
		{
			var tool = new FindReplaceMaterialTool( _meshTool, nameof( FaceTool ) );
			tool.Manager = _meshTool.Manager;
			_meshTool.CurrentTool = tool;
		}

		[Shortcut( "mesh.texture-align-grid", "CTRL+SHIFT+T", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.texture-align-face", "CTRL+SHIFT+F", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.texture-align-view", "V", typeof( SceneViewWidget ) )]
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
			var scale = new Vector2( 0.25f, 0.25f );

			using ( SceneEditorSession.Active.UndoScope( "Align to View" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var face in _faces )
				{
					var mesh = face.Component.Mesh;
					mesh.SetFaceTextureParameters( face.Handle, new Vector4( uAxis, offset.x ), new Vector4( vAxis, offset.y ), scale );
				}
			}
		}

		[Shortcut( "mesh.texture-rotate-clockwise", "ALT+S", typeof( SceneViewWidget ) )]
		void RotateClockwise() => DoRotate( true );

		[Shortcut( "mesh.texture-rotate-counterclockwise", "ALT+Q", typeof( SceneViewWidget ) )]
		void RotateCounterclockwise() => DoRotate( false );

		[Shortcut( "mesh.texture-scale-x-up", "", typeof( SceneViewWidget ) )]
		void ScaleXUp() => DoScaleX( true );

		[Shortcut( "mesh.texture-scale-x-down", "", typeof( SceneViewWidget ) )]
		void ScaleXDown() => DoScaleX( false );

		[Shortcut( "mesh.texture-scale-y-up", "", typeof( SceneViewWidget ) )]
		void ScaleYUp() => DoScaleY( true );

		[Shortcut( "mesh.texture-scale-y-down", "", typeof( SceneViewWidget ) )]
		void ScaleYDown() => DoScaleY( false );

		[Shortcut( "mesh.texture-shift-left", "", typeof( SceneViewWidget ) )]
		void ShiftLeft() => DoShiftX( true );

		[Shortcut( "mesh.texture-shift-right", "", typeof( SceneViewWidget ) )]
		void ShiftRight() => DoShiftX( false );

		[Shortcut( "mesh.texture-shift-up", "", typeof( SceneViewWidget ) )]
		void ShiftUp() => DoShiftY( true );

		[Shortcut( "mesh.texture-shift-down", "", typeof( SceneViewWidget ) )]
		void ShiftDown() => DoShiftY( false );

		[Shortcut( "mesh.texture-fit-both", "CTRL+SHIFT+KP_DEL", typeof( SceneViewWidget ) )]
		void FitBoth() => DoFit( TextureFit.x, TextureFit.y );

		[Shortcut( "mesh.texture-fit-x", "", typeof( SceneViewWidget ) )]
		void FitX() => DoFit( TextureFit.x, -1 );

		[Shortcut( "mesh.texture-fit-y", "", typeof( SceneViewWidget ) )]
		void FitY() => DoFit( -1, TextureFit.y );

		[Shortcut( "mesh.texture-justify-left", "CTRL+SHIFT+KP_4", typeof( SceneViewWidget ) )]
		void JustifyLeft() => DoJustify( PolygonMesh.TextureJustification.Left );

		[Shortcut( "mesh.texture-justify-top", "CTRL+SHIFT+KP_8", typeof( SceneViewWidget ) )]
		void JustifyTop() => DoJustify( PolygonMesh.TextureJustification.Top );

		[Shortcut( "mesh.texture-justify-center", "CTRL+SHIFT+KP_5", typeof( SceneViewWidget ) )]
		void JustifyCenter() => DoJustify( PolygonMesh.TextureJustification.Center );

		[Shortcut( "mesh.texture-justify-bottom", "CTRL+SHIFT+KP_2", typeof( SceneViewWidget ) )]
		void JustifyBottom() => DoJustify( PolygonMesh.TextureJustification.Bottom );

		[Shortcut( "mesh.texture-justify-right", "CTRL+SHIFT+KP_6", typeof( SceneViewWidget ) )]
		void JustifyRight() => DoJustify( PolygonMesh.TextureJustification.Right );

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
