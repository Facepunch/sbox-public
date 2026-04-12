
using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class ObjectSelection
{
	public override Widget CreateToolSidebar()
	{
		return new ObjectSelectionWidget( GetSerializedSelection(), this );
	}

	public class ObjectSelectionWidget : ToolSidebarWidget
	{
		readonly MeshComponent[] _meshes;
		readonly ModelRenderer[] _modelRenderers;
		readonly GameObject[] _gos;
		readonly ObjectSelection _tool;

		public ObjectSelectionWidget( SerializedObject so, ObjectSelection tool ) : base()
		{
			_tool = tool;

			AddTitle( "Object Mode", "layers" );

			_meshes = so.Targets.OfType<GameObject>()
				.Select( x => x.GetComponent<MeshComponent>() )
				.Where( x => x.IsValid() )
				.ToArray();

			_modelRenderers = so.Targets.OfType<GameObject>()
				.Select( x => x.GetComponent<ModelRenderer>() )
				.Where( x => x.IsValid() && x.Model.IsValid() && x.Model.HasRenderMeshes() )
				.ToArray();

			_gos = so.Targets.OfType<GameObject>()
				.ToArray();

			{
				var group = AddGroup( "Move Mode" );
				var row = group.AddRow();
				row.Spacing = 8;
				tool.Tool.CreateMoveModeButtons( row );
			}

			{
				var group = AddGroup( "Operations" );

				{
					var grid = Layout.Row();
					grid.Spacing = 4;

					CreateButton( "Set Origin To Pivot", "gps_fixed", "mesh.set-origin-to-pivot", SetOriginToPivot, _meshes.Length > 0, grid );
					CreateButton( "Center Origin", "center_focus_strong", "mesh.center-origin", CenterOrigin, _meshes.Length > 0, grid );
					CreateButton( "Merge Meshes", "join_full", "mesh.merge-meshes", MergeMeshes, _meshes.Length > 1, grid );
					CreateButton( "Merge Meshes By Edge", "link", null, MergeMeshesByEdge, _meshes.Length > 1, grid );

					grid.AddStretchCell();

					group.Add( grid );
				}

				{
					var grid = Layout.Row();
					grid.Spacing = 4;

					CreateButton( "Flip Faces", "flip", "mesh.flip-all-mesh-faces", FlipMesh, _meshes.Length > 0, grid );
					CreateButton( "Bake Scale", "straighten", null, BakeScale, _meshes.Length > 0, grid );
					CreateButton( "Convert To Mesh", "auto_mode", "mesh.convert-model-to-mesh", ConvertModelsToMeshes, _modelRenderers.Length > 0, grid );
					CreateButton( "Save To Model", "save", null, SaveToModel, _meshes.Length > 0, grid );

					grid.AddStretchCell();

					group.Add( grid );
				}
			}

			{
				var group = AddGroup( "Pivot" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Previous", "chevron_left", "mesh.previous-pivot", PreviousPivot, _gos.Length > 0, grid );
				CreateButton( "Next", "chevron_right", "mesh.next-pivot", NextPivot, _gos.Length > 0, grid );
				CreateButton( "Clear", "restart_alt", "mesh.clear-pivot", ClearPivot, _gos.Length > 0, grid );
				CreateButton( "Center", "center_focus_strong", "mesh.center-pivot", CenterPivot, _gos.Length > 0, grid );
				CreateButton( "World Origin", "language", "mesh.zero-pivot", ZeroPivot, _gos.Length > 0, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			{
				var group = AddGroup( "Tools" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Clipping Tool", "content_cut", "mesh.open-clipping-tool", OpenClippingTool, _meshes.Length > 0, grid );
				CreateButton( "Mirror Tool", "flip", "mesh.mirror-tool", OpenMirrorTool, _gos.Length > 0, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.mirror-tool", "SHIFT+F", typeof( SceneViewWidget ) )]
		void OpenMirrorTool()
		{
			var tool = new MirrorTool( nameof( ObjectSelection ) );
			tool.Manager = _tool.Tool.Manager;
			_tool.Tool.CurrentTool = tool;
		}

		[Shortcut( "mesh.open-clipping-tool", "SHIFT+X", typeof( SceneViewWidget ) )]
		void OpenClippingTool()
		{
			var tool = new ClipTool();
			tool.Manager = _tool.Tool.Manager;
			_tool.Tool.CurrentTool = tool;
		}

		[Shortcut( "mesh.previous-pivot", "N+MWheelDn", typeof( SceneViewWidget ) )]
		public void PreviousPivot() => _tool.PreviousPivot();

		[Shortcut( "mesh.next-pivot", "N+MWheelUp", typeof( SceneViewWidget ) )]
		public void NextPivot() => _tool.NextPivot();

		[Shortcut( "mesh.center-pivot", "Ctrl+Home", typeof( SceneViewWidget ) )]
		public void CenterPivot() => _tool.CenterPivot();

		[Shortcut( "mesh.clear-pivot", "Home", typeof( SceneViewWidget ) )]
		public void ClearPivot() => _tool.ClearPivot();

		[Shortcut( "mesh.zero-pivot", "Ctrl+End", typeof( SceneViewWidget ) )]
		public void ZeroPivot() => _tool.ZeroPivot();

		[Shortcut( "mesh.set-origin-to-pivot", "Ctrl+D", typeof( SceneViewWidget ) )]
		public void SetOriginToPivot()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Set Origin To Pivot" )
				.WithGameObjectChanges( _meshes.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
				.WithComponentChanges( _meshes )
				.Push() )
			{
				foreach ( var mesh in _meshes )
				{
					SetMeshOrigin( mesh, _tool.Pivot );
				}
			}
		}

		[Shortcut( "mesh.center-origin", "End", typeof( SceneViewWidget ) )]
		public void CenterOrigin()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Center Origin" )
				.WithGameObjectChanges( _meshes.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
				.WithComponentChanges( _meshes )
				.Push() )
			{
				foreach ( var mesh in _meshes )
				{
					CenterMeshOrigin( mesh );
				}
			}

			_tool.ClearPivot();
		}

		public void BakeScale()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Bake Scale" )
				.WithGameObjectChanges( _meshes.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
				.WithComponentChanges( _meshes )
				.Push() )
			{
				foreach ( var mesh in _meshes )
				{
					BakeScale( mesh );
				}
			}
		}

		public void FlipMesh()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Flip Mesh" )
				.WithComponentChanges( _meshes )
				.Push() )
			{
				foreach ( var mesh in _meshes )
				{
					mesh.Mesh.FlipAllFaces();
				}
			}
		}

		[Shortcut( "mesh.convert-model-to-mesh", "CTRL+SHIFT+T", typeof( SceneViewWidget ) )]
		public void ConvertModelsToMeshes()
		{
			if ( _modelRenderers.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();
			var destroyedComponents = _modelRenderers
				.SelectMany( x => x.GameObject.Components.GetAll().Where( c => c.IsValid() && c is not MeshComponent ) )
				.ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Convert Model(s) To Mesh" )
				.WithComponentChanges( _modelRenderers )
				.WithComponentCreations()
				.WithComponentDestructions( destroyedComponents )
				.WithGameObjectChanges( _modelRenderers.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
				.Push() )
			{
				var newSelection = new List<GameObject>( _modelRenderers.Length );
				var failed = 0;

				foreach ( var modelRenderer in _modelRenderers )
				{
					if ( !TryBuildMeshFromModel( modelRenderer, out var polygonMesh ) )
					{
						failed++;
						continue;
					}

					var gameObject = modelRenderer.GameObject;
					var meshComponent = gameObject.Components.GetOrCreate<MeshComponent>();
					meshComponent.Mesh = polygonMesh;
					meshComponent.SmoothingAngle = 180.0f;

					meshComponent.RebuildMesh();
					foreach ( var component in gameObject.Components.GetAll().Where( c => c.IsValid() && c != meshComponent ).ToArray() )
					{
						component.Destroy();
					}

					newSelection.Add( gameObject );
				}

				if ( newSelection.Count > 0 )
					SceneEditorSession.Active.Selection.Set( newSelection.ToArray() );

				if ( newSelection.Count == 0 )
				{
					Log.Warning( "Convert To Mesh failed: no usable render mesh data on selected ModelRenderer(s)." );
				}
				else if ( failed > 0 )
				{
					Log.Warning( $"Convert To Mesh partially failed: converted {newSelection.Count}, skipped {failed}." );
				}
			}
		}

		[Shortcut( "mesh.merge-meshes", "M", typeof( SceneViewWidget ) )]
		public void MergeMeshes()
		{
			if ( _meshes.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Merge Meshes" )
				.WithGameObjectDestructions( _meshes.Skip( 1 ).Select( x => x.GameObject ) )
				.WithComponentChanges( _meshes[0] )
				.Push() )
			{
				var sourceMesh = _meshes[0];

				for ( int i = 1; i < _meshes.Length; ++i )
				{
					var mesh = _meshes[i];
					var transform = sourceMesh.WorldTransform.ToLocal( mesh.WorldTransform );
					sourceMesh.Mesh.MergeMesh( mesh.Mesh, transform, out _, out _, out _ );

					mesh.GameObject.Destroy();
				}

				var selection = SceneEditorSession.Active.Selection;
				selection.Set( sourceMesh.GameObject );
			}
		}

		public void MergeMeshesByEdge()
		{
			if ( _meshes.Length < 2 ) return;

			var touching = new List<(int a, int b)>();
			for ( int i = 0; i < _meshes.Length; i++ )
			{
				for ( int j = i + 1; j < _meshes.Length; j++ )
				{
					if ( HasTouchingVertices( _meshes[i], _meshes[j], 0.1f ) )
						touching.Add( (i, j) );
				}
			}

			if ( touching.Count == 0 )
				return;

			var parent = Enumerable.Range( 0, _meshes.Length ).ToArray();
			int Find( int x ) => parent[x] == x ? x : parent[x] = Find( parent[x] );
			void Union( int x, int y ) => parent[Find( x )] = Find( y );

			foreach ( var (a, b) in touching )
				Union( a, b );

			var groups = Enumerable.Range( 0, _meshes.Length )
				.GroupBy( Find )
				.Where( g => g.Count() > 1 )
				.Select( g => g.ToList() )
				.ToList();

			using var scope = SceneEditorSession.Scope();

			var toDestroy = groups.SelectMany( g => g.Skip( 1 ) ).Select( i => _meshes[i].GameObject ).ToList();

			using ( SceneEditorSession.Active.UndoScope( "Merge Meshes By Edge" )
				.WithGameObjectDestructions( toDestroy )
				.WithComponentChanges( groups.Select( g => _meshes[g[0]] ) )
				.Push() )
			{
				int totalWelded = 0;

				foreach ( var group in groups )
				{
					var target = _meshes[group[0]];

					foreach ( var i in group.Skip( 1 ) )
					{
						var source = _meshes[i];
						target.Mesh.MergeMesh( source.Mesh, target.WorldTransform.ToLocal( source.WorldTransform ), out _, out _, out _ );
						source.GameObject.Destroy();
					}

					totalWelded += target.Mesh.MergeVerticesWithinDistance( target.Mesh.VertexHandles.ToList(), 0.01f, true, false, out _ );
					target.Mesh.ComputeFaceTextureCoordinatesFromParameters();
					target.RebuildMesh();
				}

				SceneEditorSession.Active.Selection.Set( _meshes[groups[0][0]].GameObject );
			}
		}

		static bool HasTouchingVertices( MeshComponent meshA, MeshComponent meshB, float threshold )
		{
			var boundsA = meshA.GetWorldBounds();
			var boundsB = meshB.GetWorldBounds();

			var expandedB = new BBox( boundsB.Mins - threshold, boundsB.Maxs + threshold );

			if ( !boundsA.Overlaps( expandedB ) )
				return false;

			foreach ( var vA in meshA.Mesh.VertexHandles )
			{
				meshA.Mesh.GetVertexPosition( vA, meshA.WorldTransform, out var posA );
				foreach ( var vB in meshB.Mesh.VertexHandles )
				{
					meshB.Mesh.GetVertexPosition( vB, meshB.WorldTransform, out var posB );
					if ( posA.Distance( posB ) < threshold )
						return true;
				}
			}
			return false;
		}

		static void CenterMeshOrigin( MeshComponent meshComponent )
		{
			if ( !meshComponent.IsValid() ) return;

			var mesh = meshComponent.Mesh;
			if ( mesh is null ) return;

			var children = meshComponent.GameObject.Children
				.Select( x => (GameObject: x, Transform: x.WorldTransform) )
				.ToArray();

			var world = meshComponent.WorldTransform;
			var bounds = mesh.CalculateBounds( world );
			var center = bounds.Center;
			var localCenter = world.PointToLocal( center );
			meshComponent.WorldPosition = center;
			meshComponent.Mesh.ApplyTransform( new Transform( -localCenter ) );
			meshComponent.RebuildMesh();

			foreach ( var child in children )
			{
				child.GameObject.WorldTransform = child.Transform;
			}
		}

		static void SetMeshOrigin( MeshComponent meshComponent, Vector3 origin )
		{
			if ( !meshComponent.IsValid() ) return;

			var mesh = meshComponent.Mesh;
			if ( mesh is null ) return;

			var world = meshComponent.WorldTransform;
			var localCenter = world.PointToLocal( origin );
			meshComponent.Mesh.ApplyTransform( new Transform( -localCenter ) );
			meshComponent.WorldPosition = origin;
			meshComponent.RebuildMesh();
		}

		static void BakeScale( MeshComponent meshComponent )
		{
			if ( !meshComponent.IsValid() ) return;

			var scale = meshComponent.WorldScale;
			meshComponent.WorldScale = 1.0f;
			meshComponent.Mesh.Scale( scale );
			meshComponent.Mesh.ComputeFaceTextureParametersFromCoordinates();
			meshComponent.RebuildMesh();
		}

		void SaveToModel()
		{
			if ( _meshes.Length == 0 ) return;

			var targetPath = EditorUtility.SaveFileDialog( "Create Model..", "vmdl", "" );
			if ( targetPath is null ) return;

			EditorUtility.CreateModelFromMeshComponents( _meshes, targetPath );
		}

		static bool TryBuildMeshFromModel( ModelRenderer renderer, out PolygonMesh polygonMesh )
		{
			return TryBuildMeshFromRenderData( renderer, out polygonMesh );
		}

		static bool TryBuildMeshFromRenderData( ModelRenderer renderer, out PolygonMesh polygonMesh )
		{
			polygonMesh = null;
			if ( !renderer.IsValid() || !renderer.Model.IsValid() || !renderer.Model.HasRenderMeshes() )
				return false;

			var vertices = renderer.Model.GetVertices();
			var indices = renderer.Model.GetIndices();
			if ( vertices is null || indices is null || vertices.Length == 0 || indices.Length < 3 )
				return false;

			polygonMesh = new PolygonMesh
			{
				Transform = renderer.WorldTransform
			};

			var hasAnyFaces = false;
			var vertexMap = new Dictionary<int, VertexHandle>( vertices.Length );
			var usedDrawCalls = false;
			var materialSlots = renderer.Model.Materials;

			for ( int drawCall = 0; drawCall < materialSlots.Length; drawCall++ )
			{
				var indexStart = renderer.Model.GetIndexStart( drawCall );
				var indexCount = renderer.Model.GetIndexCount( drawCall );
				var baseVertex = renderer.Model.GetBaseVertex( drawCall );
				if ( indexCount < 3 || indexStart < 0 )
					continue;

				var material = ResolveRenderMaterial( renderer, drawCall );
				usedDrawCalls = true;

				var end = Math.Min( indices.Length, indexStart + indexCount );
				for ( int i = indexStart; i + 2 < end; i += 3 )
				{
					var ia = baseVertex + (int)indices[i];
					var ib = baseVertex + (int)indices[i + 1];
					var ic = baseVertex + (int)indices[i + 2];

					if ( ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length )
						continue;

					if ( !vertexMap.TryGetValue( ia, out var va ) )
					{
						va = polygonMesh.AddVertex( vertices[ia].Position );
						vertexMap[ia] = va;
					}

					if ( !vertexMap.TryGetValue( ib, out var vb ) )
					{
						vb = polygonMesh.AddVertex( vertices[ib].Position );
						vertexMap[ib] = vb;
					}

					if ( !vertexMap.TryGetValue( ic, out var vc ) )
					{
						vc = polygonMesh.AddVertex( vertices[ic].Position );
						vertexMap[ic] = vc;
					}

					var verts = new[] { va, vb, vc };
					var face = polygonMesh.AddFace( verts );
					if ( !face.IsValid )
						continue;

					polygonMesh.SetFaceTextureCoords( face, new[]
					{
						new Vector2( vertices[ia].TexCoord0.x, vertices[ia].TexCoord0.y ),
						new Vector2( vertices[ib].TexCoord0.x, vertices[ib].TexCoord0.y ),
						new Vector2( vertices[ic].TexCoord0.x, vertices[ic].TexCoord0.y )
					} );

					if ( material is not null )
						polygonMesh.SetFaceMaterial( face, material );

					hasAnyFaces = true;
				}
			}

			if ( !hasAnyFaces && !usedDrawCalls )
			{
				var material = ResolveRenderMaterial( renderer, 0 );

				for ( int i = 0; i + 2 < indices.Length; i += 3 )
				{
					var ia = (int)indices[i];
					var ib = (int)indices[i + 1];
					var ic = (int)indices[i + 2];

					if ( ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length )
						continue;

					if ( !vertexMap.TryGetValue( ia, out var va ) )
					{
						va = polygonMesh.AddVertex( vertices[ia].Position );
						vertexMap[ia] = va;
					}

					if ( !vertexMap.TryGetValue( ib, out var vb ) )
					{
						vb = polygonMesh.AddVertex( vertices[ib].Position );
						vertexMap[ib] = vb;
					}

					if ( !vertexMap.TryGetValue( ic, out var vc ) )
					{
						vc = polygonMesh.AddVertex( vertices[ic].Position );
						vertexMap[ic] = vc;
					}

					var verts = new[] { va, vb, vc };
					var face = polygonMesh.AddFace( verts );
					if ( !face.IsValid )
						continue;

					polygonMesh.SetFaceTextureCoords( face, new[]
					{
						new Vector2( vertices[ia].TexCoord0.x, vertices[ia].TexCoord0.y ),
						new Vector2( vertices[ib].TexCoord0.x, vertices[ib].TexCoord0.y ),
						new Vector2( vertices[ic].TexCoord0.x, vertices[ic].TexCoord0.y )
					} );

					if ( material is not null )
						polygonMesh.SetFaceMaterial( face, material );

					hasAnyFaces = true;
				}
			}

			if ( !hasAnyFaces )
			{
				polygonMesh = null;
				return false;
			}

			polygonMesh.ComputeFaceTextureParametersFromCoordinates();

			return true;
		}

		static Material ResolveRenderMaterial( ModelRenderer renderer, int drawCall )
		{
			if ( renderer.MaterialOverride is not null )
				return renderer.MaterialOverride;

			var overrideMaterial = renderer.Materials.GetOverride( drawCall );
			if ( overrideMaterial is not null )
				return overrideMaterial;

			var originalMaterial = renderer.Materials.GetOriginal( drawCall );
			if ( originalMaterial is not null )
				return originalMaterial;

			return renderer.Model.Materials.ElementAtOrDefault( drawCall );
		}

	}
}
