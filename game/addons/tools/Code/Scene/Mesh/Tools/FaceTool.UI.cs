using HalfEdgeMesh;
using System.Text.Json.Nodes;

namespace Editor.MeshEditor;

partial class FaceTool
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

		[Range( 0, 64, slider: false ), Step( 1 ), WideMode]
		private Vector2Int NumCuts = 1;

		public FaceSelectionWidget( SerializedObject so, MeshTool tool ) : base()
		{
			AddTitle( "Face Mode", "change_history" );

			_faces = so.Targets
				.OfType<MeshFace>()
				.ToArray();

			_faceGroups = _faces.GroupBy( x => x.Component ).ToList();
			_components = _faceGroups.Select( x => x.Key ).ToList();

			{
				var group = AddGroup( "Move Mode" );
				var row = group.AddRow();
				row.Spacing = 8;
				tool.CreateMoveModeButtons( row );
			}

			{
				var group = AddGroup( "Operations" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Extract Faces", "content_cut", "mesh.extract-faces", ExtractFaces, _faces.Length > 0, grid );
				CreateButton( "Detach Faces", "call_split", "mesh.detach-faces", DetachFaces, _faces.Length > 0, grid );
				CreateButton( "Combine Faces", "join_full", "mesh.combine-faces", CombineFaces, _faces.Length > 0, grid );

				CreateButton( "Collapse Faces", "unfold_less", "mesh.collapse", Collapse, _faces.Length > 0, grid );
				CreateButton( "Remove Bad Faces", "delete_sweep", "mesh.remove-bad-faces", RemoveBadFaces, _faces.Length > 0, grid );
				CreateButton( "Flip All Faces", "flip", "mesh.flip-all-faces", FlipAllFaces, _faces.Length > 0, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			{
				var group = AddGroup( "Slice" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				var control = ControlWidget.Create( this.GetSerialized().GetProperty( nameof( NumCuts ) ) );
				control.FixedHeight = Theme.ControlHeight;
				grid.Add( control );

				CreateSmallButton( "Slice", "line_axis", "mesh.quad-slice", QuadSlice, _faces.Length > 0, grid );

				group.Add( grid );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.collapse", "SHIFT+O", typeof( SceneViewportWidget ) )]
		private void Collapse()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Collapse Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var hFace in _faces )
				{
					if ( !hFace.IsValid )
						continue;

					hFace.Component.Mesh.CollapseFace( hFace.Handle, out _ );
				}
			}
		}

		[Shortcut( "mesh.remove-bad-faces", "", typeof( SceneViewportWidget ) )]
		private void RemoveBadFaces()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Remove Bad Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var component in _components )
				{
					component.Mesh.RemoveBadFaces();
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

		[Shortcut( "mesh.extract-faces", "ALT+N", typeof( SceneViewportWidget ) )]
		private void ExtractFaces()
		{
			using var scope = SceneEditorSession.Scope();

			var options = new GameObject.SerializeOptions();
			var gameObjects = _components.Select( x => x.GameObject );

			using ( SceneEditorSession.Active.UndoScope( "Extract Faces" )
				.WithComponentChanges( _components )
				.WithGameObjectDestructions( gameObjects )
				.WithGameObjectCreations()
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _faceGroups )
				{
					var entry = group.Key.GameObject;
					var json = group.Key.Serialize( options );
					SceneUtility.MakeIdGuidsUnique( json as JsonObject );

					var go = new GameObject( entry.Name );
					go.WorldTransform = entry.WorldTransform;
					go.MakeNameUnique();

					entry.AddSibling( go, false );

					var newMeshComponent = go.Components.Create<MeshComponent>( true );
					newMeshComponent.DeserializeImmediately( json as JsonObject );
					var newMesh = newMeshComponent.Mesh;

					var faceIndices = group.Select( x => x.Handle.Index ).ToArray();
					var facesToRemove = newMesh.FaceHandles
						.Where( f => !faceIndices.Contains( f.Index ) )
						.ToArray();

					newMesh.RemoveFaces( facesToRemove );

					var transform = go.WorldTransform;
					var newBounds = newMesh.CalculateBounds( transform );
					var newTransfrom = transform.WithPosition( newBounds.Center );
					newMesh.ApplyTransform( new Transform( transform.Rotation.Inverse * (transform.Position - newTransfrom.Position) ) );
					go.WorldTransform = newTransfrom;
					newMeshComponent.RebuildMesh();

					foreach ( var hFace in newMesh.FaceHandles )
						selection.Add( new MeshFace( newMeshComponent, hFace ) );

					var mesh = group.Key.Mesh;
					var faces = group.Select( x => x.Handle );

					if ( faces.Count() == mesh.FaceHandles.Count() )
					{
						entry.Destroy();
					}
					else
					{
						mesh.RemoveFaces( faces );
					}
				}
			}
		}

		[Shortcut( "mesh.detach-faces", "N", typeof( SceneViewportWidget ) )]
		private void DetachFaces()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Detach Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _faceGroups )
				{
					group.Key.Mesh.DetachFaces( group.Select( x => x.Handle ).ToArray(), out var newFaces );
					foreach ( var hFace in newFaces )
						selection.Add( new MeshFace( group.Key, hFace ) );
				}
			}
		}

		[Shortcut( "mesh.combine-faces", "Backspace", typeof( SceneViewportWidget ) )]
		private void CombineFaces()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Combine Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.CombineFaces( group.Select( x => x.Handle ).ToArray() );
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
		}

		[Shortcut( "mesh.flip-all-faces", "F", typeof( SceneViewportWidget ) )]
		private void FlipAllFaces()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Flip All Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var component in _components )
				{
					component.Mesh.FlipAllFaces();
				}
			}
		}

		[Shortcut( "mesh.quad-slice", "CTRL+D", typeof( SceneViewportWidget ) )]
		private void QuadSlice()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Quad Slice" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					var newFaces = new List<FaceHandle>();
					mesh.QuadSliceFaces( group.Select( x => x.Handle ).ToArray(), NumCuts.x, NumCuts.y, 60.0f, newFaces );
					mesh.ComputeFaceTextureCoordinatesFromParameters(); // TODO: Shouldn't be needed, something in quad slice isn't computing these

					foreach ( var hFace in newFaces )
					{
						selection.Add( new MeshFace( group.Key, hFace ) );
					}
				}
			}
		}
	}
}
