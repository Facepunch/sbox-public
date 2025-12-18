
namespace Editor.MeshEditor;

partial class MeshSelection
{
	public override Widget CreateToolSidebar()
	{
		return new MeshSelectionWidget( GetSerializedSelection(), this );
	}

	public class MeshSelectionWidget : ToolSidebarWidget
	{
		readonly MeshComponent[] _meshes;
		readonly MeshSelection _tool;

		public MeshSelectionWidget( SerializedObject so, MeshSelection tool ) : base()
		{
			_tool = tool;

			AddTitle( "Mesh Mode", "layers" );

			_meshes = so.Targets.OfType<GameObject>()
				.Select( x => x.GetComponent<MeshComponent>() )
				.Where( x => x.IsValid() )
				.ToArray();

			{
				var group = AddGroup( "Move Mode" );
				var row = group.AddRow();
				row.Spacing = 8;
				tool.Tool.CreateMoveModeButtons( row );
			}

			{
				var group = AddGroup( "Operations" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Set Origin To Pivot", "gps_fixed", "mesh.set-origin-to-pivot", SetOriginToPivot, _meshes.Length > 0, grid );
				CreateButton( "Center Origin", "center_focus_strong", "mesh.center-origin", CenterOrigin, _meshes.Length > 0, grid );
				CreateButton( "Merge Meshes", "join_full", "mesh.merge-meshes", MergeMeshes, _meshes.Length > 1, grid );
				CreateButton( "Bake Scale", "straighten", "mesh.bake-scale", BakeScale, _meshes.Length > 0, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			{
				var group = AddGroup( "Pivot" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Previous", "chevron_left", "mesh.previous-pivot", PreviousPivot, _meshes.Length > 0, grid );
				CreateButton( "Next", "chevron_right", "mesh.next-pivot", NextPivot, _meshes.Length > 0, grid );
				CreateButton( "Clear", "restart_alt", "mesh.clear-pivot", ClearPivot, _meshes.Length > 0, grid );
				CreateButton( "World Origin", "language", "mesh.zero-pivot", ZeroPivot, _meshes.Length > 0, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.previous-pivot", "N+MWheelDn", typeof( SceneDock ) )]
		public void PreviousPivot() => _tool.PreviousPivot();

		[Shortcut( "mesh.next-pivot", "N+MWheelUp", typeof( SceneDock ) )]
		public void NextPivot() => _tool.NextPivot();

		[Shortcut( "mesh.clear-pivot", "Home", typeof( SceneDock ) )]
		public void ClearPivot() => _tool.ClearPivot();

		[Shortcut( "mesh.zero-pivot", "Ctrl+End", typeof( SceneDock ) )]
		public void ZeroPivot() => _tool.ZeroPivot();

		[Shortcut( "mesh.set-origin-to-pivot", "Ctrl+D", typeof( SceneDock ) )]
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

		[Shortcut( "mesh.center-origin", "End", typeof( SceneDock ) )]
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

		[Shortcut( "mesh.merge-meshes", "M", typeof( SceneDock ) )]
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
			meshComponent.RebuildMesh();
		}
	}
}
