using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class VertexTool
{
	public override Widget CreateToolSidebar()
	{
		return new VertexSelectionWidget( GetSerializedSelection(), Tool );
	}

	public class VertexSelectionWidget : ToolSidebarWidget
	{
		private readonly MeshVertex[] _vertices;
		private readonly List<IGrouping<MeshComponent, MeshVertex>> _vertexGroups;
		private readonly List<MeshComponent> _components;

		public enum MergeRange
		{
			Infinite,
			Grid,
			Fixed,
		}

		private static MergeRange MergeRangeMode { get; set; } = MergeRange.Infinite;
		private static float MergeDistance { get; set; } = 0.1f;

		private struct MergeProperties
		{
			[Group( "Merge" )]
			public readonly MergeRange Range { get => MergeRangeMode; set => MergeRangeMode = value; }

			[Group( "Merge" )]
			public readonly float Distance { get => MergeDistance; set => MergeDistance = value; }
		}

		[InlineEditor( Label = false )]
		private readonly MergeProperties mergeProperties = new();

		public VertexSelectionWidget( SerializedObject so, MeshTool tool ) : base()
		{
			AddTitle( "Vertex Mode", "workspaces" );

			{
				var group = AddGroup( "Move Mode" );
				var row = group.AddRow();
				row.Spacing = 8;
				tool.CreateMoveModeButtons( row );
			}

			_vertices = so.Targets
				.OfType<MeshVertex>()
				.ToArray();

			_vertexGroups = _vertices.GroupBy( x => x.Component ).ToList();
			_components = _vertexGroups.Select( x => x.Key ).ToList();

			{
				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Merge", "merge", "mesh.merge", Merge, _vertices.Length > 1, row.Layout );

				var mergeObject = mergeProperties.GetSerialized();
				var range = ControlWidget.Create( mergeObject.GetProperty( nameof( MergeProperties.Range ) ) );
				var distance = ControlWidget.Create( mergeObject.GetProperty( nameof( MergeProperties.Distance ) ) );
				distance.HorizontalSizeMode = SizeMode.Expand;

				range.FixedHeight = Theme.ControlHeight;
				distance.FixedHeight = Theme.ControlHeight;

				row.Layout.Add( range );
				row.Layout.Add( distance );

				Layout.Add( row );
			}
			{
				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Snap To Vertex", "gps_fixed", "mesh.snap_to_vertex", SnapToVertex, _vertices.Length > 1, row.Layout );
				CreateButton( "Weld UVs", "scatter_plot", "mesh.vertex-weld-uvs", WeldUVs, _vertices.Length > 0, row.Layout );
				CreateButton( "Bevel", "straighten", "mesh.bevel", Bevel, _vertices.Length > 0, row.Layout );
				CreateButton( "Connect", "link", "mesh.connect", Connect, _vertices.Length > 1, row.Layout );

				row.Layout.AddStretchCell();

				Layout.Add( row );
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.connect", "V", typeof( SceneViewportWidget ) )]
		private void Connect()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			var pairs = new Dictionary<PolygonMesh, List<(VertexHandle, VertexHandle)>>();

			using ( SceneEditorSession.Active.UndoScope( "Connect Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _vertexGroups )
				{
					var mesh = group.Key.Mesh;
					pairs[mesh] = [];

					foreach ( var hVertex in group )
					{
						mesh.GetFacesConnectedToVertex( hVertex.Handle, out var connectedFaces );

						foreach ( var hFace in connectedFaces )
						{
							var hFaceVertex = mesh.FindFaceVertexConnectedToVertex( hVertex.Handle, hFace );
							var hNextFaceVertex = mesh.GetNextVertexInFace( hFaceVertex );

							while ( hNextFaceVertex != hFaceVertex )
							{
								var hNextVertex = mesh.GetVertexConnectedToFaceVertex( hNextFaceVertex );

								if ( _vertices.FirstOrDefault( x => x.Handle == hNextVertex ).IsValid() )
								{
									pairs[mesh].Add( (hVertex.Handle, hNextVertex) );
									break;
								}

								hNextFaceVertex = mesh.GetNextVertexInFace( hNextFaceVertex );
							}
						}
					}
				}

				foreach ( var group in _vertexGroups )
				{
					var mesh = group.Key.Mesh;
					var vertexPairs = pairs[mesh];
					var numPairs = vertexPairs.Count;

					if ( vertexPairs.Count == 0 )
						continue;

					foreach ( var pair in vertexPairs )
					{
						mesh.ConnectVertices( pair.Item1, pair.Item2, out _ );
					}

					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
		}

		[Shortcut( "mesh.snap_to_vertex", "B", typeof( SceneViewportWidget ) )]
		private void SnapToVertex()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Snap Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var position = _vertices[^1].PositionWorld;
				foreach ( var vertex in _vertices )
					vertex.Component.Mesh.SetVertexPosition( vertex.Handle, vertex.Transform.PointToLocal( position ) );
			}
		}

		[Shortcut( "mesh.vertex-weld-uvs", "CTRL+F", typeof( SceneViewportWidget ) )]
		private void WeldUVs()
		{
			if ( _vertices.Length < 1 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Weld UVs" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _vertexGroups )
				{
					var component = group.Key;
					var mesh = component.Mesh;
					mesh.AverageVertexUVs( group.Select( x => x.Handle ).ToList() );
				}
			}
		}

		[Shortcut( "mesh.bevel", "F", typeof( SceneViewportWidget ) )]
		private void Bevel()
		{
			if ( _vertices.Length <= 0 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Bevel Vertices" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var bevelWidth = EditorScene.GizmoSettings.GridSpacing;

				foreach ( var group in _vertexGroups )
				{
					if ( !group.Key.Mesh.BevelVertices( group.Select( x => x.Handle ).ToArray(), bevelWidth, out var newVertices ) )
						continue;

					var selection = SceneEditorSession.Active.Selection;
					selection.Clear();

					foreach ( var hVertex in newVertices )
						selection.Add( new MeshVertex( group.Key, hVertex ) );
				}
			}
		}

		[Shortcut( "mesh.merge", "M", typeof( SceneViewportWidget ) )]
		private void Merge()
		{
			if ( _vertices.Length < 2 )
				return;

			using var scope = SceneEditorSession.Scope();

			var gameObjects = _components.Skip( 1 ).Select( x => x.GameObject ).ToList();
			var meshA = _components[0];
			var vertices = _vertexGroups[0].ToList();

			using ( SceneEditorSession.Active.UndoScope( "Merge Vertices" )
				.WithComponentChanges( _components )
				.WithGameObjectDestructions( gameObjects )
				.Push() )
			{
				foreach ( var group in _vertexGroups.Skip( 1 ) )
				{
					var meshB = group.Key;
					var transform = meshA.WorldTransform.ToLocal( meshB.WorldTransform );
					meshA.Mesh.MergeMesh( meshB.Mesh, transform, out var remapVertices, out _, out _ );
					vertices.AddRange( group.Select( v => new MeshVertex( meshA, remapVertices[v.Handle] ) ) );

					meshB.DestroyGameObject();
				}

				var mergeDistance = MergeRangeMode switch
				{
					MergeRange.Grid => EditorScene.GizmoSettings.GridSpacing,
					MergeRange.Fixed => MergeDistance,
					_ => -1.0f
				};

				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				if ( meshA.Mesh.MergeVerticesWithinDistance( vertices.Select( x => x.Handle ).ToList(), mergeDistance, true, false, out var finalVertices ) > 0 )
				{
					foreach ( var hVertex in finalVertices )
						selection.Add( new MeshVertex( meshA, hVertex ) );

					meshA.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
				else
				{
					foreach ( var hVertex in vertices )
						selection.Add( hVertex );
				}
			}
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewportWidget ) )]
		private void DeleteSelection()
		{
			var groups = _vertices.GroupBy( face => face.Component );

			if ( !groups.Any() )
				return;

			var components = groups.Select( x => x.Key ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Delete Vertices" ).WithComponentChanges( components ).Push() )
			{
				foreach ( var group in groups )
					group.Key.Mesh.RemoveVertices( group.Select( x => x.Handle ) );
			}
		}
	}
}
