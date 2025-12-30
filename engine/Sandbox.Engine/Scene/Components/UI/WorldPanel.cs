using Sandbox.UI;
namespace Sandbox;

/// <summary>
/// Renders any attached PanelComponents to the world in 3D space.
/// </summary>
[Title( "World Panel" )]
[Category( "UI" )]
[Icon( "panorama_horizontal" )]
[EditorHandle( "materials/gizmo/ui.png" )]
public sealed class WorldPanel : Renderer, IRootPanelComponent
{
	Sandbox.UI.WorldPanel worldPanel;

	[Property] public float RenderScale { get; set; } = 1.0f;
	[Property] public bool LookAtCamera { get; set; }
	[Property] public Vector2 PanelSize { get; set; } = new Vector2( 512 );

	// todo: show these as group buttons

	[Property] public HAlignment HorizontalAlign { get; set; } = HAlignment.Center;
	[Property] public VAlignment VerticalAlign { get; set; } = VAlignment.Center;

	/// <summary>
	/// How far can we interact with this world panel?
	/// </summary>
	[Property, MakeDirty] public float InteractionRange { get; set; } = 1000.0f;

	public enum HAlignment
	{
		[Icon( "align_horizontal_left" )]
		Left = 1,

		[Icon( "align_horizontal_center" )]
		Center = 2,

		[Icon( "align_horizontal_right" )]
		Right = 3,
	}

	public enum VAlignment
	{
		[Icon( "align_vertical_top" )]
		Top = 1,

		[Icon( "align_vertical_center" )]
		Center = 2,

		[Icon( "align_vertical_bottom" )]
		Bottom = 3,
	}

	Rect CalculateRect()
	{
		var r = new Rect( 0, PanelSize );

		if ( HorizontalAlign == HAlignment.Center ) r.Position -= new Vector2( PanelSize.x * 0.5f, 0 );
		if ( HorizontalAlign == HAlignment.Right ) r.Position -= new Vector2( PanelSize.x, 0 );

		if ( VerticalAlign == VAlignment.Center ) r.Position -= new Vector2( 0, PanelSize.y * 0.5f );
		if ( VerticalAlign == VAlignment.Bottom ) r.Position -= new Vector2( 0, PanelSize.y );


		return r;
	}

	protected override void OnDirty()
	{
		if ( !worldPanel.IsValid() )
			return;

		worldPanel.MaxInteractionDistance = InteractionRange;

		if ( worldPanel.SceneObject.IsValid() )
		{
			RenderOptions.Apply( worldPanel.SceneObject );
		}
	}

	protected override void DrawGizmos()
	{
		using ( Gizmo.Scope( null, new Transform( 0, Rotation.From( 0, 90, -90 ), Sandbox.UI.WorldPanel.ScreenToWorldScale ) ) )
		{
			var r = CalculateRect();

			Gizmo.Draw.Line( r.TopLeft, r.TopRight );
			Gizmo.Draw.Line( r.TopLeft, r.BottomLeft );
			Gizmo.Draw.Line( r.TopRight, r.BottomRight );
			Gizmo.Draw.Line( r.BottomLeft, r.BottomRight );

			Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.2f );
			Gizmo.Draw.SolidTriangle( new Triangle( r.TopLeft, r.TopRight, r.BottomRight ) );
			Gizmo.Draw.SolidTriangle( new Triangle( r.BottomRight, r.BottomLeft, r.TopLeft ) );
		}
	}

	protected override void OnEnabled()
	{
		worldPanel = new Sandbox.UI.WorldPanel( Scene.SceneWorld );
		worldPanel.GameObject = GameObject;
		worldPanel.Transform = WorldTransform;
		worldPanel.Tags.SetFrom( GameObject.Tags );
		worldPanel.MaxInteractionDistance = InteractionRange;

		OnSceneObjectCreated( worldPanel.SceneObject );
		OnRenderOptionsChanged();

		EnsurePanelParents();
	}

	void EnsurePanelParents()
	{
		Components.ExecuteEnabledInSelfAndDescendants<PanelComponent>( c => c.EnsureParentPanel() );
	}

	protected override void OnDisabled()
	{
		if ( worldPanel.IsValid() )
		{
			BackupRenderAttributes( worldPanel?.SceneObject?.Attributes );
			worldPanel.Delete( true );
			worldPanel = null;
		}
	}

	protected override void OnPreRender()
	{
		if ( !worldPanel.IsValid() )
			return;

		var currentRot = WorldRotation;
		var currentScale = WorldScale;

		if ( LookAtCamera && Scene.Camera is not null )
		{
			var camPos = Scene.Camera.WorldPosition;
			var camDelta = camPos - WorldPosition;
			currentRot = Rotation.LookAt( camDelta, Scene.Camera.WorldRotation.Up );
		}

		worldPanel.Transform = WorldTransform.WithRotation( currentRot ).WithScale( currentScale * RenderScale );

		var rect = CalculateRect();

		rect.Left /= RenderScale;
		rect.Right /= RenderScale;
		rect.Top /= RenderScale;
		rect.Bottom /= RenderScale;

		worldPanel.PanelBounds = rect;
	}

	public Panel GetPanel()
	{
		return worldPanel;
	}

	/// <summary>
	/// Tags have been updated
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( !worldPanel.IsValid() )
			return;

		worldPanel?.Tags.SetFrom( Tags );
	}

	protected override void OnRenderOptionsChanged()
	{
		if ( worldPanel?.SceneObject.IsValid() ?? false )
		{
			RenderOptions.Apply( worldPanel.SceneObject );
		}
	}

}
