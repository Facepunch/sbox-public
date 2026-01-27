
using HalfEdgeMesh;
using System;

namespace Editor.MeshEditor;

partial class EdgeTool
{
	public override Widget CreateToolSidebar()
	{
		return new EdgeSelectionWidget( Tool, GetSerializedSelection() );
	}

	public class EdgeSelectionWidget : ToolSidebarWidget
	{
		private readonly MeshEdge[] _edges = null;
		private readonly List<IGrouping<MeshComponent, MeshEdge>> _edgeGroups;
		private readonly List<MeshComponent> _components;
		readonly MeshTool _tool;

		[Range( 0, 16 ), Step( 1 ), WideMode]
		private int NumCuts = 1;

		public EdgeSelectionWidget( MeshTool tool, SerializedObject selection ) : base()
		{
			AddTitle( "Edge Mode", "show_chart" );

			{
				var group = AddGroup( "Move Mode" );
				var row = group.AddRow();
				row.Spacing = 8;
				tool.CreateMoveModeButtons( row );
			}

			_tool = tool;

			_edges = selection.Targets
				.OfType<MeshEdge>()
				.ToArray();

			_edgeGroups = _edges.GroupBy( x => x.Component ).ToList();
			_components = _edgeGroups.Select( x => x.Key ).ToList();

			{
				var group = AddGroup( "Modify" );

				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Dissolve", "blur_off", "mesh.dissolve", Dissolve, CanDissolve(), row.Layout );
				CreateButton( "Collapse", "unfold_less", "mesh.collapse", Collapse, CanCollapse(), row.Layout );
				CreateButton( "Connect", "link", "mesh.connect", Connect, CanConnect(), row.Layout );
				CreateButton( "Extend", "call_made", "mesh.extend", Extend, CanExtend(), row.Layout );

				group.Add( row );
			}

			{
				var group = AddGroup( "Construct" );

				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Merge", "merge_type", "mesh.merge", Merge, CanMerge(), row.Layout );
				CreateButton( "Split", "call_split", "mesh.split", Split, CanSplit(), row.Layout );
				CreateButton( "Snap Edge to Edge", "compare_arrows", "mesh.snap-edge-to-edge", SnapEdgeToEdge, _edges.Length == 2, row.Layout );
				CreateButton( "Fill Hole", "format_color_fill", "mesh.fill-hole", FillHole, CanFillHole(), row.Layout );
				CreateButton( "Bridge", "device_hub", "mesh.bridge-edges", BridgeEdges, CanBridgeEdges(), row.Layout );

				row.Layout.AddStretchCell();

				group.Add( row );
			}

			{
				var group = AddGroup( "Normals" );
				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Hard Normals", "crop_square", "mesh.hard-normals", HardNormals, _edges.Length > 0, row.Layout );
				CreateButton( "Soft Normals", "blur_on", "mesh.soft-normals", SoftNormals, _edges.Length > 0, row.Layout );
				CreateButton( "Default Normals", "trip_origin", "mesh.default-normals", DefaultNormals, _edges.Length > 0, row.Layout );

				row.Layout.AddStretchCell();

				group.Add( row );
			}

			{
				var group = AddGroup( "UV" );
				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Weld UVs", "scatter_plot", "mesh.edge-weld-uvs", WeldUVs, _edges.Length > 0, row.Layout );

				row.Layout.AddStretchCell();

				group.Add( row );
			}

			{
				var group = AddGroup( "Selection" );
				var row = new Widget { Layout = Layout.Row() };
				row.Layout.Spacing = 4;

				CreateButton( "Select Loop", "all_out", "mesh.select-loop", SelectLoop, CanSelectLoop(), row.Layout );
				CreateButton( "Select Ring", "data_array", "mesh.select-ring", SelectRing, CanSelectRing(), row.Layout );
				CreateButton( "Select Ribs", "timeline", "mesh.select-ribs", SelectRibs, CanSelectRibs(), row.Layout );

				row.Layout.AddStretchCell();

				group.Add( row );
			}

			{
				var group = AddGroup( "Tools" );

				{
					var row = new Widget { Layout = Layout.Row() };
					row.Layout.Spacing = 4;

					CreateButton( "Bevel", "straighten", "mesh.edge-bevel", Bevel, CanBevel(), row.Layout );
					CreateButton( "Edge Cut Tool", "content_cut", "mesh.edge-cut-tool", OpenEdgeCutTool, true, row.Layout );
					CreateButton( "Edge Arch", "rounded_corner", "mesh.edge-arch-tool", OpenEdgeArchTool, CanArch(), row.Layout );

					row.Layout.AddStretchCell();

					group.Add( row );
				}

				{
					var row = new Widget { Layout = Layout.Row() };
					row.Layout.Spacing = 4;

					var numCutsControl = ControlWidget.Create( this.GetSerialized().GetProperty( nameof( NumCuts ) ) );
					numCutsControl.FixedHeight = Theme.ControlHeight;
					CreateButton( "Quick Bevel", "carpenter", "mesh.edge-quick-bevel", QuickBevel, CanBevel(), row.Layout );
					row.Layout.Add( numCutsControl );

					row.Layout.AddStretchCell();

					group.Add( row );
				}
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "mesh.edge-cut-tool", "C", typeof( SceneViewWidget ) )]
		void OpenEdgeCutTool()
		{
			var tool = new EdgeCutTool( nameof( EdgeTool ) );
			tool.Manager = _tool.Manager;
			_tool.CurrentTool = tool;
		}

		private void SetNormals( PolygonMesh.EdgeSmoothMode mode )
		{
			using ( SceneEditorSession.Active.UndoScope( "Set Normals" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var edge in _edges )
					edge.EdgeSmoothing = mode;
			}
		}

		[Shortcut( "mesh.edge-weld-uvs", "CTRL+F", typeof( SceneViewWidget ) )]
		private void WeldUVs()
		{
			if ( _edges.Length < 1 )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Weld UVs" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var group in _edgeGroups )
				{
					var component = group.Key;
					var mesh = component.Mesh;
					mesh.AverageEdgeUVs( group.Select( x => x.Handle ).ToList() );
				}
			}
		}

		[Shortcut( "mesh.edge-quick-bevel", "F", typeof( SceneViewWidget ) )]
		private void QuickBevel()
		{
			if ( !CanBevel() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Quick Bevel Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var newEdges = new Dictionary<MeshComponent, List<HalfEdgeHandle>>();

				var bevelWidth = EditorScene.GizmoSettings.GridSpacing;
				int steps = NumCuts;
				const float shape = 1.0f;
				const bool softEdges = false;

				foreach ( var group in _edgeGroups )
				{
					var component = group.Key;
					var mesh = component.Mesh;
					var edges = group.Select( x => x.Handle ).ToList();

					var newOuterEdges = new List<HalfEdgeHandle>();
					var newInnerEdges = new List<HalfEdgeHandle>();
					var facesNeedingUVs = new List<FaceHandle>();
					var newFaces = new List<FaceHandle>();

					if ( !mesh.BevelEdges( edges, PolygonMesh.BevelEdgesMode.RemoveClosedEdges, steps, bevelWidth, shape, newOuterEdges, newInnerEdges, newFaces, facesNeedingUVs ) )
						continue;

					var smoothMode = softEdges
						? PolygonMesh.EdgeSmoothMode.Soft
						: PolygonMesh.EdgeSmoothMode.Default;

					foreach ( var edgeHandle in newInnerEdges )
					{
						mesh.SetEdgeSmoothing( edgeHandle, smoothMode );
					}

					foreach ( var hFace in facesNeedingUVs )
					{
						mesh.TextureAlignToGrid( mesh.Transform, hFace );
					}

					mesh.ComputeFaceTextureParametersFromCoordinates( newFaces );

					newEdges[component] = newOuterEdges.Concat( newInnerEdges ).ToList();
				}

				selection.Clear();
				foreach ( var edgeGroup in newEdges )
				{
					foreach ( var edge in edgeGroup.Value )
					{
						selection.Add( new MeshEdge( edgeGroup.Key, edge ) );
					}
				}
			}
		}

		private bool CanBevel()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.edge-bevel", "ALT+F", typeof( SceneViewWidget ) )]
		private void Bevel()
		{
			if ( !CanBevel() )
				return;

			using ( SceneEditorSession.Active.UndoScope( "Bevel Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var bevelEdges = new List<BevelEdges>();

				foreach ( var group in _edgeGroups )
				{
					var component = group.Key;
					var mesh = component.Mesh;

					var newMesh = new PolygonMesh();
					newMesh.Transform = mesh.Transform;
					newMesh.MergeMesh( mesh, Transform.Zero, out _, out var newEdges, out _ );
					var edges = group.Select( x => newEdges[x.Handle].Index ).ToList();

					bevelEdges.Add( new BevelEdges()
					{
						Component = component,
						Mesh = newMesh,
						Edges = edges,
					} );
				}

				var tool = new BevelTool( [.. bevelEdges] );
				tool.Manager = _tool.Manager;
				_tool.CurrentTool = tool;
			}
		}

		private bool CanMerge()
		{
			if ( _edges.Length != 2 )
				return false;

			var edgeA = _edges[0];
			if ( !edgeA.IsValid() )
				return false;

			var edgeB = _edges[1];
			if ( !edgeB.IsValid() )
				return false;

			if ( !edgeA.IsOpen )
				return false;

			if ( !edgeB.IsOpen )
				return false;

			return true;
		}

		private static MeshEdge MergeMeshesOfEdges( MeshEdge edgeA, MeshEdge edgeB )
		{
			if ( edgeB.Component != edgeA.Component )
			{
				var meshA = edgeA.Component;
				var meshB = edgeB.Component;

				var transform = meshA.WorldTransform.ToLocal( meshB.WorldTransform );
				meshA.Mesh.MergeMesh( meshB.Mesh, transform, out _, out var newHalfEdges, out _ );

				meshB.DestroyGameObject();

				edgeB = new MeshEdge( meshA, newHalfEdges[edgeB.Handle] );
			}

			return edgeB;
		}

		[Shortcut( "mesh.merge", "M", typeof( SceneViewWidget ) )]
		private void Merge()
		{
			if ( !CanMerge() )
				return;

			using var scope = SceneEditorSession.Scope();

			var edgeA = _edges[0];
			var edgeB = _edges[1];

			var undoScope = SceneEditorSession.Active.UndoScope( "Merge Edges" );

			if ( edgeA.Component != edgeB.Component )
			{
				undoScope = undoScope.WithComponentChanges( edgeA.Component )
					.WithGameObjectDestructions( edgeB.Component.GameObject );
			}
			else
			{
				undoScope = undoScope.WithComponentChanges( [edgeA.Component, edgeB.Component] );
			}

			using ( undoScope.Push() )
			{
				edgeB = MergeMeshesOfEdges( edgeA, edgeB );
				var mesh = edgeA.Component.Mesh;

				if ( mesh.MergeEdges( edgeA.Handle, edgeB.Handle, out var hEdge ) )
				{
					mesh.ComputeFaceTextureCoordinatesFromParameters();

					var selection = SceneEditorSession.Active.Selection;
					selection.Set( new MeshEdge( edgeA.Component, hEdge ) );
				}
			}
		}

		private bool CanSplit()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.split", "ALT+N", typeof( SceneViewWidget ) )]
		private void Split()
		{
			if ( !CanSplit() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Split Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.SplitEdges( group.Select( x => x.Handle ).ToArray(), out var newEdgesA, out var newEdgesB );
					if ( newEdgesA is not null )
					{
						foreach ( var hEdge in newEdgesA )
							selection.Add( new MeshEdge( group.Key, hEdge ) );
					}
					if ( newEdgesB is not null )
					{
						foreach ( var hEdge in newEdgesB )
							selection.Add( new MeshEdge( group.Key, hEdge ) );
					}
				}
			}
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewWidget ) )]
		private void DeleteSelection()
		{
			var groups = _edges.GroupBy( face => face.Component );

			if ( !groups.Any() )
				return;

			var components = groups.Select( x => x.Key ).ToArray();

			using ( SceneEditorSession.Active.UndoScope( "Delete Edges" ).WithComponentChanges( components ).Push() )
			{
				foreach ( var group in groups )
					group.Key.Mesh.RemoveEdges( group.Select( x => x.Handle ) );
			}
		}

		private bool CanConnect()
		{
			return _edges.Length > 1;
		}

		[Shortcut( "mesh.connect", "V", typeof( SceneViewWidget ) )]
		private void Connect()
		{
			if ( !CanConnect() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Connect Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.ConnectEdges( group.Select( x => x.Handle ).ToArray(), out var newEdges );
					foreach ( var hEdge in newEdges )
						selection.Add( new MeshEdge( group.Key, hEdge ) );

					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
		}

		private bool CanExtend()
		{
			return _edges.Any( x => x.IsOpen );
		}

		[Shortcut( "mesh.extend", "N", typeof( SceneViewWidget ) )]
		private void Extend()
		{
			if ( !CanExtend() )
				return;

			using var scope = SceneEditorSession.Scope();

			var amount = EditorScene.GizmoSettings.GridSpacing;

			using ( SceneEditorSession.Active.UndoScope( "Extend Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					if ( !group.Key.Mesh.ExtendEdges( group.Select( x => x.Handle ).ToArray(), amount, out var newEdges, out _ ) )
						continue;

					if ( newEdges is not null )
					{
						foreach ( var hEdge in newEdges )
						{
							selection.Add( new MeshEdge( group.Key, hEdge ) );
						}
					}
				}
			}
		}

		[Shortcut( "mesh.bridge-edges", "ALT+B", typeof( SceneViewWidget ) )]
		private void BridgeEdges()
		{
			if ( !CanBridgeEdges() )
				return;

			using var scope = SceneEditorSession.Scope();

			var edgeA = _edges[0];
			var edgeB = _edges[1];

			using ( SceneEditorSession.Active.UndoScope( "Bridge Edges" )
				.WithComponentChanges( [edgeA.Component, edgeB.Component] )
				.Push() )
			{
				if ( edgeA.Component.Mesh.BridgeEdges( edgeA.Handle, edgeB.Handle, out var hFace ) )
				{
					var selection = SceneEditorSession.Active.Selection;
					selection.Clear();
				}
			}
		}

		private bool CanBridgeEdges()
		{
			if ( _edges.Length != 2 )
				return false;

			var edgeA = _edges[0];
			if ( !edgeA.IsValid() )
				return false;

			var edgeB = _edges[1];
			if ( !edgeB.IsValid() )
				return false;

			if ( edgeA.Component != edgeB.Component )
				return false;

			if ( !edgeA.IsOpen )
				return false;

			if ( !edgeB.IsOpen )
				return false;

			return true;
		}

		private bool CanDissolve()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.dissolve", "Backspace", typeof( SceneViewWidget ) )]
		private void Dissolve()
		{
			if ( !CanDissolve() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Dissolve Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					var mesh = group.Key.Mesh;
					mesh.DissolveEdges( group.Select( x => x.Handle ).ToArray(), false, PolygonMesh.DissolveRemoveVertexCondition.InteriorOrColinear );
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
		}

		private bool CanCollapse()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.collapse", "SHIFT+O", typeof( SceneViewWidget ) )]
		private void Collapse()
		{
			if ( !CanCollapse() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Collapse Edges" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					group.Key.Mesh.CollapseEdges( group.Select( x => x.Handle ).ToArray() );
				}
			}
		}

		private bool CanFillHole()
		{
			return _edges.Any( x => x.IsOpen );
		}

		[Shortcut( "mesh.fill-hole", "P", typeof( SceneViewWidget ) )]
		private void FillHole()
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Fill Hole" )
				.WithComponentChanges( _components )
				.Push() )
			{
				foreach ( var edge in _edges )
				{
					edge.Component.Mesh.CreateFaceInEdgeLoop( edge.Handle, out var _ );
				}
			}
		}

		private bool CanSelectRibs()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.select-ribs", "CTRL+G", typeof( SceneViewWidget ) )]
		private void SelectRibs()
		{
			if ( !CanSelectRibs() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Select Edge Ribs" ).Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					var mesh = group.Key.Mesh;
					if ( mesh is null )
						continue;

					mesh.FindEdgeIslands( group.Select( x => x.Handle ).ToArray(), out var edgeIslands );

					foreach ( var edgeIsland in edgeIslands )
					{
						var numRibs = mesh.FindEdgeRibs( edgeIsland, out var leftEdgeRibs, out var rightEdgeRibs );
						for ( var i = 0; i < numRibs; ++i )
						{
							var leftRib = leftEdgeRibs[i];
							var rightRib = rightEdgeRibs[i];

							foreach ( var rib in leftRib )
								selection.Add( new MeshEdge( group.Key, rib ) );

							foreach ( var rib in rightRib )
								selection.Add( new MeshEdge( group.Key, rib ) );
						}
					}
				}
			}
		}

		private bool CanSelectRing()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.select-ring", "G", typeof( SceneViewWidget ) )]
		private void SelectRing()
		{
			if ( !CanSelectRing() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Select Edge Ring" ).Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var hEdge in _edges )
				{
					if ( !hEdge.IsValid )
						continue;

					hEdge.Component.Mesh.FindEdgeRing( hEdge.Handle, out var edgeRing );
					foreach ( var hNewEdge in edgeRing )
						selection.Add( new MeshEdge( hEdge.Component, hNewEdge ) );
				}
			}
		}

		private bool CanSelectLoop()
		{
			return _edges.Length != 0;
		}

		[Shortcut( "mesh.select-loop", "L", typeof( SceneViewWidget ) )]
		private void SelectLoop()
		{
			if ( !CanSelectLoop() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Select Edge Loop" ).Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				selection.Clear();

				foreach ( var group in _edgeGroups )
				{
					group.Key.Mesh.FindEdgeLoopForEdges( group.Select( x => x.Handle ).ToArray(), out var edgeLoop );
					foreach ( var hNewEdge in edgeLoop )
						selection.Add( new MeshEdge( group.Key, hNewEdge ) );
				}
			}
		}

		[Shortcut( "mesh.snap-edge-to-edge", "I", typeof( SceneViewWidget ) )]
		private void SnapEdgeToEdge()
		{
			if ( _edges.Length != 2 )
				return;

			var edgeA = _edges[0];
			if ( !edgeA.IsValid() )
				return;

			var edgeB = _edges[1];
			if ( !edgeB.IsValid() )
				return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Snap Edges" )
				.WithComponentChanges( [edgeA.Component, edgeB.Component] )
				.Push() )
			{
				var meshA = edgeA.Component.Mesh;
				var meshB = edgeB.Component.Mesh;

				meshB.GetEdgeVertices( edgeB.Handle, out var hVertexA, out var hVertexB );
				var targetPosA = edgeB.Transform.PointToWorld( meshB.GetVertexPosition( hVertexA ) );
				var targetPosB = edgeB.Transform.PointToWorld( meshB.GetVertexPosition( hVertexB ) );
				var edgeDirB = targetPosB - targetPosA;

				meshA.GetEdgeVertices( edgeA.Handle, out hVertexA, out hVertexB );
				var currentPosA = edgeA.Transform.PointToWorld( meshA.GetVertexPosition( hVertexA ) );
				var currentPosB = edgeA.Transform.PointToWorld( meshA.GetVertexPosition( hVertexB ) );
				var edgeDirA = currentPosB - currentPosA;

				if ( edgeDirA.Dot( edgeDirB ) < 0 )
					(targetPosA, targetPosB) = (targetPosB, targetPosA);

				meshA.SetVertexPosition( hVertexA, edgeA.Transform.PointToLocal( targetPosA ) );
				meshA.SetVertexPosition( hVertexB, edgeA.Transform.PointToLocal( targetPosB ) );
			}
		}

		[Shortcut( "mesh.hard-normals", "H", typeof( SceneViewWidget ) )]
		void HardNormals()
		{
			SetNormals( PolygonMesh.EdgeSmoothMode.Hard );
		}

		[Shortcut( "mesh.soft-normals", "J", typeof( SceneViewWidget ) )]
		void SoftNormals()
		{
			SetNormals( PolygonMesh.EdgeSmoothMode.Soft );
		}

		[Shortcut( "mesh.default-normals", "K", typeof( SceneViewWidget ) )]
		void DefaultNormals()
		{
			SetNormals( PolygonMesh.EdgeSmoothMode.Default );
		}
		private bool CanArch()
		{
			return _edges.Any( x => x.IsOpen );
		}

		[Shortcut( "mesh.edge-arch-tool", "Y", typeof( SceneViewWidget ) )]
		void OpenEdgeArchTool()
		{
			if ( !CanArch() )
				return;

			var edgeGroups = new List<EdgeArchEdges>();

			foreach ( var group in _edgeGroups )
			{
				var component = group.Key;
				var mesh = component.Mesh;

				var originalMesh = new PolygonMesh();
				originalMesh.Transform = mesh.Transform;
				originalMesh.MergeMesh( mesh, Transform.Zero, out _, out _, out _ );

				var openEdges = group
					.Where( x => x.IsOpen )
					.Select( x => x.Handle.Index )
					.ToList();

				if ( openEdges.Count > 0 )
				{
					edgeGroups.Add( new EdgeArchEdges
					{
						Component = component,
						Mesh = originalMesh,
						Edges = openEdges
					} );
				}
			}

			if ( edgeGroups.Count == 0 )
				return;

			var tool = new EdgeArchTool( edgeGroups.ToArray() );
			tool.Manager = _tool.Manager;
			_tool.CurrentTool = tool;
		}

		[Shortcut( "mesh.grow-selection", "KP_ADD", typeof( SceneViewWidget ) )]
		private void GrowSelection()
		{
			if ( _edges.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Grow Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var newEdges = new HashSet<MeshEdge>();

				foreach ( var edge in _edges )
				{
					if ( !edge.IsValid() )
						continue;

					newEdges.Add( edge );
				}

				foreach ( var edge in _edges )
				{
					if ( !edge.IsValid() )
						continue;

					var mesh = edge.Component.Mesh;

					mesh.GetEdgeVertices( edge.Handle, out var vertexA, out var vertexB );

					mesh.GetEdgesConnectedToVertex( vertexA, out var edgesA );
					mesh.GetEdgesConnectedToVertex( vertexB, out var edgesB );

					foreach ( var adjacentEdge in edgesA.Concat( edgesB ) )
					{
						if ( adjacentEdge.IsValid )
							newEdges.Add( new MeshEdge( edge.Component, adjacentEdge ) );
					}
				}

				selection.Clear();
				foreach ( var edge in newEdges )
				{
					if ( edge.IsValid() )
						selection.Add( edge );
				}
			}
		}

		[Shortcut( "mesh.shrink-selection", "KP_MINUS", typeof( SceneViewWidget ) )]
		private void ShrinkSelection()
		{
			if ( _edges.Length == 0 ) return;

			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Shrink Selection" )
				.WithComponentChanges( _components )
				.Push() )
			{
				var selection = SceneEditorSession.Active.Selection;
				var edgesToKeep = new HashSet<MeshEdge>();

				foreach ( var edge in _edges )
				{
					if ( !edge.IsValid() )
						continue;

					var mesh = edge.Component.Mesh;
					mesh.GetEdgeVertices( edge.Handle, out var vertexA, out var vertexB );

					mesh.GetEdgesConnectedToVertex( vertexA, out var edgesA );
					bool allEdgesASelected = edgesA.All( e =>
						_edges.Any( selectedEdge => selectedEdge.Component == edge.Component && selectedEdge.Handle == e )
					);

					mesh.GetEdgesConnectedToVertex( vertexB, out var edgesB );
					bool allEdgesBSelected = edgesB.All( e =>
						_edges.Any( selectedEdge => selectedEdge.Component == edge.Component && selectedEdge.Handle == e )
					);

					if ( allEdgesASelected && allEdgesBSelected )
					{
						edgesToKeep.Add( edge );
					}
				}

				selection.Clear();
				foreach ( var edge in edgesToKeep )
				{
					if ( edge.IsValid() )
						selection.Add( edge );
				}
			}
		}
	}
}
