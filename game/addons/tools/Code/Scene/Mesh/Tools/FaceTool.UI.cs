using HalfEdgeMesh;
using System.Text.Json.Nodes;

namespace Editor.MeshEditor;

partial class FaceTool
{
	private const string ClipboardFaceDataType = "mesh_faces";

	/// <summary>
	/// Data structure for serializing face geometry to clipboard.
	/// Faces reference vertices by index to preserve shared vertices between connected faces.
	/// </summary>
	private record struct ClipboardFaceData( int[] VertexIndices, string Material, Vector4 AxisU, Vector4 AxisV, Vector2 Scale );
	private record struct ClipboardMeshData( Vector3[] Vertices, ClipboardFaceData[] Faces );

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

		[Range( 0, 64, slider: false ), Step( 1 ), WideMode]
		private Vector2Int NumCuts = 1;

		public FaceSelectionWidget( SerializedObject so, MeshTool tool ) : base()
		{
			AddTitle( "Face Mode", "change_history" );

			_meshTool = tool;
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
				CreateButton( "Thicken Faces", "layers", "mesh.thicken-faces", ThickenFaces, _faces.Length > 0, grid );

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

			{
				var group = AddGroup( "Tools" );

				var grid = Layout.Row();
				grid.Spacing = 4;

				CreateButton( "Fast Texture Tool", "texture", "mesh.fast-texture-tool", OpenFastTextureTool, true, grid );
				CreateButton( "Edge Cut Tool", "content_cut", "mesh.edge-cut-tool", OpenEdgeCutTool, true, grid );

				grid.AddStretchCell();

				group.Add( grid );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.edge-cut-tool", "C", typeof( SceneViewWidget ) )]
		void OpenEdgeCutTool()
		{
			var tool = new EdgeCutTool( nameof( FaceTool ) );
			tool.Manager = _meshTool.Manager;
			_meshTool.CurrentTool = tool;
		}

		[Shortcut( "mesh.fast-texture-tool", "CTRL+G", typeof( SceneViewWidget ) )]
		public void OpenFastTextureTool()
		{
			var selectedFaces = SceneEditorSession.Active.Selection.OfType<MeshFace>().ToArray();
			RectEditor.FastTextureWindow.OpenWith( selectedFaces, _meshTool.ActiveMaterial );
		}

		[Shortcut( "mesh.collapse", "SHIFT+O", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.remove-bad-faces", "", typeof( SceneViewWidget ) )]
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

		[Shortcut( "editor.copy", "CTRL+C", typeof( SceneViewWidget ) )]
		private void CopySelection()
		{
			if ( !_faceGroups.Any() )
				return;

			var vertexList = new List<Vector3>();
			var vertexIndexMap = new Dictionary<Vector3, int>();
			var faceDataList = new List<ClipboardFaceData>();

			foreach ( var group in _faceGroups )
			{
				var mesh = group.Key.Mesh;

				foreach ( var face in group )
				{
					if ( !face.IsValid )
						continue;

					var faceVertexIndices = new List<int>();

					foreach ( var vertexHandle in mesh.GetFaceVertices( face.Handle ) )
					{
						var position = mesh.GetVertexPosition( vertexHandle );

						if ( !vertexIndexMap.TryGetValue( position, out var index ) )
						{
							index = vertexList.Count;
							vertexList.Add( position );
							vertexIndexMap[position] = index;
						}

						faceVertexIndices.Add( index );
					}

					mesh.GetFaceTextureParameters( face.Handle, out var axisU, out var axisV, out var scale );

					faceDataList.Add( new ClipboardFaceData(
						faceVertexIndices.ToArray(),
						face.Material?.ResourcePath,
						axisU,
						axisV,
						scale
					) );
				}
			}

			var meshData = new ClipboardMeshData( vertexList.ToArray(), faceDataList.ToArray() );

			var json = new JsonObject
			{
				["_type"] = ClipboardFaceDataType,
				["_data"] = JsonNode.Parse( Json.Serialize( meshData ) )
			};

			EditorUtility.Clipboard.Copy( json.ToJsonString() );
		}

		[Shortcut( "editor.paste", "CTRL+V", typeof( SceneViewWidget ) )]
		private void PasteSelection()
		{
			var clipboard = EditorUtility.Clipboard.Paste();
			if ( string.IsNullOrWhiteSpace( clipboard ) || !clipboard.StartsWith( "{" ) )
				return;

			ClipboardMeshData meshData;
			try
			{
				var json = JsonNode.Parse( clipboard );
				if ( json?["_type"]?.ToString() != ClipboardFaceDataType )
					return;

				meshData = Json.Deserialize<ClipboardMeshData>( json["_data"].ToJsonString() );
			}
			catch
			{
				return;
			}

			if ( meshData.Faces == null || meshData.Faces.Length == 0 )
				return;

			if ( meshData.Vertices == null || meshData.Vertices.Length == 0 )
				return;

			if ( _components.Count == 0 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Paste Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				// Paste into the first selected component
				var targetComponent = _components.First();
				var mesh = targetComponent.Mesh;
				var newVertices = mesh.AddVertices( meshData.Vertices );

				// Create faces using the shared vertex handles
				foreach ( var faceData in meshData.Faces )
				{
					if ( faceData.VertexIndices == null || faceData.VertexIndices.Length < 3 )
						continue;
					if ( faceData.VertexIndices.Any( i => i < 0 || i >= newVertices.Length ) )
						continue;

					var faceVertices = faceData.VertexIndices.Select( i => newVertices[i] ).ToArray();
					var newFaceHandle = mesh.AddFace( faceVertices );
					if ( !newFaceHandle.IsValid )
						continue;

					var material = string.IsNullOrEmpty( faceData.Material ) ? null : Material.Load( faceData.Material );
					mesh.SetFaceMaterial( newFaceHandle, material );
					mesh.SetFaceTextureParameters( newFaceHandle, faceData.AxisU, faceData.AxisV, faceData.Scale );

					selection.Add( new MeshFace( targetComponent, newFaceHandle ) );
				}
			}
		}

		[Shortcut( "mesh.extract-faces", "ALT+N", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.detach-faces", "N", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.combine-faces", "Backspace", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.flip-all-faces", "F", typeof( SceneViewWidget ) )]
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

		[Shortcut( "mesh.thicken-faces", "G", typeof( SceneViewWidget ) )]
		private void ThickenFaces()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Thicken Faces" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				var amount = EditorScene.GizmoSettings.GridSpacing;

				foreach ( var group in _faceGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ThickenFaces( [.. group.Select( x => x.Handle )], amount, out var newFaces );
					mesh.ComputeFaceTextureCoordinatesFromParameters();

					foreach ( var hFace in newFaces )
					{
						selection.Add( new MeshFace( group.Key, hFace ) );
					}
				}
			}
		}

		[Shortcut( "mesh.quad-slice", "CTRL+D", typeof( SceneViewWidget ) )]
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
