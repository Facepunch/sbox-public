using Sandbox.Engine;
using Sandbox.Rendering;
using System.Diagnostics;

namespace Sandbox.UI;

/// <summary>
/// A root panel. Serves as a container for other panels, handles things such as rendering.
/// </summary>
public partial class RootPanel : Panel
{
	/// <summary>
	/// Bounds of the panel, i.e. its size and position on the screen.
	/// </summary>
	public Rect PanelBounds { get; set; } = new Rect( 0, 0, 512, 512 );

	/// <summary>
	/// If any of our panels are visible and want mouse input (pointer-events != none) then
	/// this will be set to true.
	/// </summary>
	internal bool ChildrenWantMouseInput { get; set; }

	/// <summary>
	/// The scale of this panel and its children.
	/// </summary>
	public float Scale { get; protected set; } = 1.0f;

	/// <summary>
	/// If set to true this panel won't be rendered to the screen like a normal panel.
	/// Its command list is still prepared during the UI update for rendering elsewhere.
	/// </summary>
	public bool RenderedManually { get; set; }

	/// <summary>
	/// True if this is a world panel, so should be skipped when determining cursor visibility etc
	/// </summary>
	public virtual bool IsWorldPanel { get; set; }

	/// <summary>
	/// If this panel belongs to a VR overlay
	/// </summary>
	public bool IsVR { get; internal set; }

	/// <summary>
	/// If this panel should be rendered with ~4K resolution.
	/// </summary>
	public bool IsHighQualityVR { get; internal set; }

	/// <summary>
	/// Current global mouse position, projected onto plane for world panels.
	/// </summary>
	internal Vector2 MousePos;

	/// <summary>
	/// Single flat command list used by the flat rendering path.
	/// All panels record into this one list instead of per-panel lists.
	/// </summary>
	internal readonly CommandList PanelCommandList;

	/// <summary>
	/// The UI system this root belongs to. Told to us when we're created and never changes - a root
	/// can't move between surfaces.
	/// </summary>
	internal override UISystem UISystem => system;

	readonly UISystem system;

	/// <summary>
	/// A root in the game's UI. This is the one addon code wants.
	/// </summary>
	public RootPanel() : this( GlobalContext.Current.UISystem )
	{
	}

	/// <summary>
	/// A root in a particular UI system - a surface creates its root this way.
	/// </summary>
	internal RootPanel( UISystem system )
	{
		Style.Width = Length.Percent( 100 );
		Style.Height = Length.Percent( 100 );

		PanelCommandList = new CommandList( $"UI Root: {GetType().Name}" );
		_onRenderStarted = RenderStarted;
		_onRenderCompleted = RenderCompleted;

		this.system = system;
		system.AddRoot( this );
		AddToLists();

		StyleSheet.Load( "/styles/base/rootpanel.scss" );
	}

	public override void Delete( bool immediate = true )
	{
		base.Delete( immediate );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		fixedOverlays.Clear();
		PanelCommandList.Enabled = false;
		PanelCommandList.Reset();

		UISystem.RemoveRoot( this );
	}

	internal override void AddToLists()
	{
		base.AddToLists();

		Sandbox.Internal.IPanel.InspectablePanels.Add( this );
	}

	internal override void RemoveFromLists()
	{
		base.RemoveFromLists();

		Sandbox.Internal.IPanel.InspectablePanels.Remove( this );
	}

	/// <summary>
	/// This is called from tests to emulate the regular root panel simulate loop
	/// </summary>
	internal void Layout()
	{
		TickInternal();
		PreLayout();
		CalculateLayout();
		PostLayout();
	}

	int layoutHash;

	/// <summary>
	/// Called before layout to lock the bounds of this root panel to the screen size (which is passed).
	/// Internally this sets PanelBounds to rect and calls UpdateScale.
	/// </summary>
	protected virtual void UpdateBounds( Rect rect )
	{
		PanelBounds = rect;

		if ( IsVR && IsHighQualityVR )
		{
			PanelBounds = new Rect( 0, 0, 3840, 2400 );
			return;
		}
	}

	/// <summary>
	/// Work out scaling here. Default is to scale relative to the screen being
	/// 1920 wide. ie - scale = screensize.Width / 1920.0f;
	/// </summary>
	protected virtual void UpdateScale( Rect screenSize )
	{
		Scale = screenSize.Height / 1080.0f;

		if ( Game.IsRunningOnHandheld )
		{
			Scale = Scale * 1.333f;
		}

		if ( IsVR && IsHighQualityVR )
		{
			Scale = 2.33f;
		}
	}

	internal void TickInputInternal()
	{
		ChildrenWantMouseInput = WantsMouseInput();
	}

	internal void PreLayout( Rect screenSize )
	{
		UpdateBounds( screenSize );
		UpdateScale( PanelBounds );

		Scale = MathX.Clamp( Scale, 0.1f, 10.0f );

		PreLayout();
	}

	internal void PreLayout()
	{
		var started = Stopwatch.GetTimestamp();
		var cascade = new LayoutCascade
		{
			Scale = Scale,
			Root = this,
		};

		Style.Left = 0.0f;
		Style.Top = 0.0f;
		Style.Width = PanelBounds.Width * (1 / Scale);
		Style.Height = PanelBounds.Height * (1 / Scale);

		var hash = HashCode.Combine( PanelBounds.Width, PanelBounds.Height, Scale );
		if ( hash != layoutHash )
		{
			layoutHash = hash;
			StyleSelectorsChanged( true, true );
			SkipAllTransitions();

			cascade.SelectorChanged = true;
		}

		BuildStyleRules();

		PushRootValues();

		PreLayout( cascade );
		_ = FixedOverlays;
		_layoutTime = Stopwatch.GetElapsedTime( started );
	}

	internal void CalculateLayout()
	{
		if ( LayoutTree == null )
			return;

		// Dirtiness propagates to the root, so a clean root means nothing to do
		if ( !LayoutTree.IsDirty )
			return;

		var started = Stopwatch.GetTimestamp();
		using var perfScope = Performance.Scope( "CalculateLayout" );
		PushRootValues();
		LayoutTree.CalculateLayout();
		_layoutTime += Stopwatch.GetElapsedTime( started );
	}

	internal void PostLayout()
	{
		var started = Stopwatch.GetTimestamp();
		PushRootValues();
		FinalLayout( Vector2.Zero );
		foreach ( var panel in FixedOverlays )
		{
			if ( !panel.IsVisible && !panel.HasIntro ) continue;
			try
			{
				panel.FinalLayout( PanelBounds.Position );
			}
			catch ( Exception e )
			{
				Log.Warning( e );
			}
		}

		_layoutTime += Stopwatch.GetElapsedTime( started );
	}

	internal void PushRootValues()
	{
		Length.RootSize = new Vector2( PanelBounds.Width, PanelBounds.Height );
		Length.RootFontSize = ComputedStyle?.FontSize ?? Length.Pixels( 13 ).Value;
		Length.RootScale = ScaleToScreen;
	}

	/// <summary>
	/// Tab and Shift+Tab that nothing below us handled move focus through the tree.
	/// </summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "tab" && e.Pressed && UISystem.MoveFocus( UISystem.CurrentFocus, e.HasShift ) )
			return;

		base.OnButtonTyped( e );
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		layoutRect = PanelBounds;
	}

	internal void Render()
	{
		PanelCommandList.ExecuteOnRenderThread();
	}

	/// <summary>
	/// Execute the command list prepared during the latest UI update in the current render block.
	/// <see cref="RenderedManually"/> must be set to true.
	/// </summary>
	/// <param name="opacity">Opacity multiplier for this execution.</param>
	public void RenderManual( float opacity = 1.0f )
	{
		Graphics.AssertRenderBlock();

		if ( !RenderedManually && !IsWorldPanel )
			throw new Exception( $"{nameof( RenderedManually )} must be set to true to render this panel manually." );

		var attributes = Graphics.Attributes;
		var previousOpacity = attributes.GetFloat( "UIPanelOpacity", 1 );
		var previousCombo = attributes.GetComboBool( "D_PANEL_OPACITY" );

		attributes.Set( "UIPanelOpacity", opacity );
		attributes.SetCombo( "D_PANEL_OPACITY", opacity != 1 );
		try
		{
			Render();
		}
		finally
		{
			attributes.Set( "UIPanelOpacity", previousOpacity );
			attributes.SetCombo( "D_PANEL_OPACITY", previousCombo );
		}
	}

	[Event( "ui.skiptransitions" )]
	internal void SkipAllTransitions()
	{
		SkipTransitions();
	}

	/// <summary>
	/// A list of panels that are waiting to have their styles re-evaluated
	/// </summary>
	readonly HashSet<Panel> styleRuleUpdates = new();

	/// <summary>
	/// Add this panel to a list to have their styles re-evaluated. This should be done any
	/// time the panel changes in a way that could affect its style selector.. like if its child
	/// index changed, or classes added or removed, or became hovered etc.
	/// </summary>
	internal void AddToBuildStyleRulesList( Panel panel )
	{
		styleRuleUpdates.Add( panel );
	}

	/// <summary>
	/// Run through all panels that are pending a re-check on their style rules.
	/// Only properly invalidate them if their rules actually change.
	/// </summary>
	internal void BuildStyleRules()
	{
		if ( styleRuleUpdates.Count == 0 )
			return;

		var timer = FastTimer.StartNew();
		int count = styleRuleUpdates.Count;
		_styleRuleChanges = 0;

		// A handful of panels is quicker inline than through the parallel machinery
		if ( count < 32 )
		{
			foreach ( var panel in styleRuleUpdates )
				BuildStyleRule( panel );
		}
		else
		{
			_buildStyleRule ??= BuildStyleRule;
			Parallel.ForEach( styleRuleUpdates, _buildStyleRule );
		}

		styleRuleUpdates.Clear();

		if ( timer.ElapsedMilliSeconds > 0.5 )
		{
			Log.Trace( $"BuildStyleRules {count:n0} ({_styleRuleChanges}) took {timer.ElapsedMilliSeconds}ms" );
		}
	}

	readonly object _styleRuleLock = new();
	int _styleRuleChanges;
	Action<Panel> _buildStyleRule;

	/// <summary>
	/// Re-evaluate one panel's rules. Safe to run from a worker thread.
	/// </summary>
	void BuildStyleRule( Panel panel )
	{
		try
		{
			if ( !panel.IsValid() || panel.Style is null )
				return;

			if ( panel.Style.BuildRulesInThread() )
			{
				lock ( _styleRuleLock )
				{
					_styleRuleChanges++;
					panel.SetNeedsPreLayout();
				}
			}

			panel.MarkStylesRebuilt();
		}
		catch ( Exception e )
		{
			Log.Warning( e, e.Message );
		}
	}
}
