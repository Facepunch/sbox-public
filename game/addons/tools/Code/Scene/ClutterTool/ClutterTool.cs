using Editor.TerrainEditor;
using Sandbox.Clutter;
using System;

namespace Editor;

[EditorTool( "clutter" )]
[Title( "Clutter" )]
[Icon( "forest" )]
public sealed class ClutterTool : EditorTool
{
	private BrushPreviewSceneObject _brushPreview;
	private ClutterList _clutterList;
	public BrushSettings BrushSettings { get; private set; } = new();
	[Property] public ClutterDefinition SelectedClutter { get; set; }

	public ClutterTool()
	{
		RebuildSidebarOnSelectionChange = false;
	}

	private bool _erasing = false;
	private bool _dragging = false;
	private bool _painting = false;
	private Vector3 _lastPaintPosition;
	private float _paintDistanceThreshold => BrushSettings.Size * 0.5f;

	public override Widget CreateToolSidebar()
	{
		var sidebar = new ToolSidebarWidget();
		sidebar.AddTitle( "Clutter Brush Settings", "brush" );
		sidebar.MinimumWidth = 300;

		// Brush Properties
		{
			var group = sidebar.AddGroup( "Brush Properties" );
			var so = BrushSettings.GetSerialized();
			group.Add( ControlSheet.CreateRow( so.GetProperty( nameof( BrushSettings.Size ) ) ) );
			group.Add( ControlSheet.CreateRow( so.GetProperty( nameof( BrushSettings.Opacity ) ) ) );
		}

		// Clutter Selection
		{
			var group = sidebar.AddGroup( "Clutter Definitions", SizeMode.Flexible );
			_clutterList = new ClutterList( sidebar );
			_clutterList.MinimumHeight = 300;
			_clutterList.OnClutterSelected = ( clutter ) =>
			{
				SelectedClutter = clutter;
			};

			SelectedClutter = _clutterList.SelectedClutter;
			group.Add( _clutterList );
		}

		// Clear All
		{
			var group = sidebar.AddGroup( "Actions" );
			var clearBtn = new Button( "Clear All", "delete_sweep" );
			clearBtn.Clicked += () =>
			{
				var system = Scene.GetSystem<ClutterGridSystem>();
				system?.ClearAllPainted();
			};
			clearBtn.ToolTip = "Remove all painted clutter";
			group.Add( clearBtn );
		}

		return sidebar;
	}

	public override void OnUpdate()
	{
		var ctlrHeld = Gizmo.IsCtrlPressed;
		if ( Gizmo.IsCtrlPressed && !_erasing )
		{
			_erasing = true;
		}

		DrawBrushPreview();

		Gizmo.Hitbox.BBox( BBox.FromPositionAndSize( Vector3.Zero, 999999 ) );

		if ( Gizmo.IsLeftMouseDown )
		{
			if ( !_dragging )
			{
				_dragging = true;
				OnPaintBegin();
			}

			OnPaintUpdate();
		}
		else if ( _dragging )
		{
			_dragging = false;
			OnPaintEnded();
		}

		if ( !ctlrHeld )
		{
			_erasing = false;
		}
	}

	public override void OnDisabled()
	{
		_brushPreview?.Delete();
	}

	private void OnPaintBegin()
	{
		_lastPaintPosition = Vector3.Zero;
	}

	private void OnPaintUpdate()
	{
		if ( SelectedClutter?.Scatterer == null ) return;

		var tr = Scene.Trace.Ray( Gizmo.CurrentRay, 100000 )
			.UseRenderMeshes( true )
			.WithTag( "solid" )
			.WithoutTags( "clutter" )
			.Run();

		if ( !tr.Hit ) return;
		if ( _lastPaintPosition != Vector3.Zero &&
			Vector3.DistanceBetween( tr.HitPosition, _lastPaintPosition ) < _paintDistanceThreshold )
			return;

		_lastPaintPosition = tr.HitPosition;

		var system = Scene.GetSystem<ClutterGridSystem>();
		var brushRadius = (float)BrushSettings.Size;
		var bounds = BBox.FromPositionAndSize( tr.HitPosition, brushRadius * 2f );

		if ( _erasing )
		{
			system.Erase( tr.HitPosition, brushRadius );
		}
		else
		{
			var instances = SelectedClutter.Scatterer.Value.Scatter( bounds, SelectedClutter, Random.Shared.Next(), Scene );
			var desired = instances.Count * BrushSettings.Opacity;
			var count = (int)desired;
			if ( Random.Shared.NextSingle() < (desired - count) )
				count++;


			var placed = 0;
			var radiusSq = brushRadius * brushRadius;
			var center = tr.HitPosition;
			foreach ( var instance in instances )
			{
				if ( placed >= count ) break;

				// Skip instances outside the circular brush
				var pos = instance.Transform.Position;
				var dx = pos.x - center.x;
				var dy = pos.y - center.y;
				if ( dx * dx + dy * dy > radiusSq ) continue;

				if ( instance.Entry?.HasAsset is not true ) continue;

				var t = instance.Transform;
				system.Paint( instance.Entry, t.Position, t.Rotation, t.Scale.x );
				placed++;
			}
		}

		_painting = true;
	}

	private void OnPaintEnded()
	{
		_lastPaintPosition = Vector3.Zero;

		if ( _painting )
		{
			var system = Scene.GetSystem<ClutterGridSystem>();
			system.Flush();
		}

		_painting = false;
	}

	private void DrawBrushPreview()
	{
		var tr = Scene.Trace.Ray( Gizmo.CurrentRay, 50000 )
			.UseRenderMeshes( true )
			.WithTag( "solid" )
			.WithoutTags( "clutter" )
			.Run();

		if ( !tr.Hit )
			return;

		_brushPreview ??= new BrushPreviewSceneObject( Gizmo.World );

		var brushRadius = BrushSettings.Size;
		var color = _erasing ? Color.FromBytes( 250, 150, 150 ) : Color.FromBytes( 150, 150, 250 );
		color.a = BrushSettings.Opacity;

		var brush = TerrainEditorTool.Brush;
		var previewPosition = tr.HitPosition + tr.Normal * 1f;
		var surfaceRotation = Rotation.LookAt( tr.Normal );

		_brushPreview.RenderLayer = SceneRenderLayer.OverlayWithDepth;
		_brushPreview.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
		_brushPreview.Transform = new Transform( previewPosition, surfaceRotation );
		_brushPreview.Radius = brushRadius;
		_brushPreview.Texture = brush?.Texture;
		_brushPreview.Color = color;
	}
}
