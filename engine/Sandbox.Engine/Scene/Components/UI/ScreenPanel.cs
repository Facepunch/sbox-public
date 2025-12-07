using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Renders any attached PanelComponents to the screen. Acts as the root for all your UI components.
/// </summary>
[Title( "Screen Panel" )]
[Category( "UI" )]
[Icon( "desktop_windows" )]
[EditorHandle( "materials/gizmo/ui.png" )]
[Alias( "PanelRoot" )]
public sealed class ScreenPanel : Component, IRootPanelComponent
{
	[Property, Range( 0, 1 )] public float Opacity { get; set; } = 1.0f;
	[Property, Range( 0, 5 )] public float Scale { get; set; } = 1.0f;
	[Property] public bool AutoScreenScale { get; set; } = true;
	[Property, ShowIf( "AutoScreenScale", true )] public AutoScale ScaleStrategy { get; set; }
	[Property] public int ZIndex { get; set; } = 100;
	[Property] public CameraComponent TargetCamera { get; set; }

	private GameRootPanel rootPanel;

	public enum AutoScale
	{
		/// <summary>
		/// The height is scaled on the assumptiuon that we're always 1080p
		/// </summary>
		ConsistentHeight,

		/// <summary>
		/// We use the same scaling as the desktop
		/// </summary>
		FollowDesktopScaling,
	}

	protected override void OnValidate()
	{
		if ( Scale < 0.001f ) Scale = 0.001f;
	}

	protected override void OnAwake()
	{
		rootPanel = new GameRootPanel();
		rootPanel.GameObject = GameObject;
		rootPanel.RenderedManually = true;
		rootPanel.Style.Display = DisplayMode.None;

		OnUpdate();
	}

	protected override void OnEnabled()
	{
		if ( !rootPanel.IsValid() )
			return;

		// todo RootPanel enable
		rootPanel.Style.Display = DisplayMode.Flex;

		EnsurePanelParents();
	}

	void EnsurePanelParents()
	{
		Components.ExecuteEnabledInSelfAndDescendants<PanelComponent>( c => c.EnsureParentPanel() );
	}

	protected override void OnDisabled()
	{
		// todo disable rootpanel
		if ( !rootPanel.IsValid() )
			return;

		rootPanel.Style.Display = DisplayMode.None;
	}

	protected override void OnDestroy()
	{
		rootPanel?.Delete();
		rootPanel = null;
	}

	public Panel GetPanel()
	{
		return rootPanel;
	}

	protected override void OnUpdate()
	{
		if ( !rootPanel.IsValid() )
			return;

		rootPanel.Style.ZIndex = ZIndex;
		rootPanel.AutoScale = AutoScreenScale;
		rootPanel.ManualScale = Scale;
		rootPanel.ScaleStrategy = ScaleStrategy;
	}

	internal void Render()
	{
		if ( !rootPanel.IsValid() ) return;

		rootPanel.RenderManual( Opacity );
	}
}

class GameRootPanel : RootPanel
{
	public bool AutoScale = true;
	public float ManualScale = 1.0f;
	public ScreenPanel.AutoScale ScaleStrategy;

	override public bool IsWorldPanel => false;

	protected override void UpdateScale( Rect screenSize )
	{
		if ( AutoScale )
		{
			if ( ScaleStrategy == ScreenPanel.AutoScale.ConsistentHeight )
			{
				Scale = screenSize.Height / 1080.0f;
			}
			else if ( ScaleStrategy == ScreenPanel.AutoScale.FollowDesktopScaling )
			{
				Scale = Screen.DesktopScale;

				var minimumHeight = 1080.0f * Screen.DesktopScale;

				// If the screen height is less than 1080, it's less than supported
				// so scale the screen size down.
				if ( screenSize.Height < minimumHeight )
				{
					Scale *= screenSize.Height / minimumHeight;
				}
			}

			// wtf
			if ( Game.IsRunningOnHandheld )
			{
				Scale = Scale * 1.333f;
			}

			// wtf
			if ( IsVR && IsHighQualityVR )
			{
				Scale = 2.33f;
			}
		}
		else
		{
			Scale = ManualScale;
		}
	}
}
