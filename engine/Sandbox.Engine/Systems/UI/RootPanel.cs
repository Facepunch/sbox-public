using Sandbox.Engine;

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
	/// This is true when the panel is drawn via other means (like as a world panel).
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

	public RootPanel()
	{
		Style.Width = Length.Percent( 100 );
		Style.Height = Length.Percent( 100 );

		GlobalContext.Current.UISystem.AddRoot( this );
		AddToLists();

		StyleSheet.Load( "/styles/rootpanel.scss" );
	}

	public override void Delete( bool immediate = true )
	{
		base.Delete( immediate );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		GlobalContext.Current.UISystem.RemoveRoot( this );
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
	}

	internal void CalculateLayout()
	{
		if ( YogaNode == null )
			return;

		using var perfScope = Performance.Scope( "CalculateLayout" );
		PushRootValues();
		YogaNode.CalculateLayout();
	}

	internal void PostLayout()
	{
		PushRootValues();
		FinalLayout( Vector2.Zero );
	}

	internal void PushRootValues()
	{
		Length.RootSize = new Vector2( PanelBounds.Width, PanelBounds.Height );
		Length.RootFontSize = ComputedStyle?.FontSize ?? Length.Pixels( 13 ).Value;
		Length.RootScale = ScaleToScreen;
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		layoutRect = PanelBounds;
	}

	internal void Render( float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();
		GlobalContext.Current.UISystem.Renderer.Render( this, opacity );
	}

	/// <summary>
	/// Build command lists for this panel and all children.
	/// Called during the tick phase, before rendering.
	/// </summary>
	internal void BuildCommandLists( float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();
		GlobalContext.Current.UISystem.Renderer.BuildCommandLists( this, opacity );
	}

	/// <summary>
	/// Render this panel manually. This gives more flexibility to where UI is rendered, to texture for example.
	/// <see cref="RenderedManually"/> must be set to true.
	/// </summary>
	public void RenderManual( float opacity = 1.0f )
	{
		Graphics.AssertRenderBlock();

		if ( !RenderedManually && !IsWorldPanel )
			throw new Exception( $"{nameof( RenderedManually )} must be set to true to render this panel manually." );

		Render( opacity );
	}

	[Event( "ui.skiptransitions" )]
	internal void SkipAllTransitions()
	{
		SkipTransitions();
	}

	[Event( "language.changed" )]
	internal void OnLanguageChanged()
	{
		LanguageChanged();
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
		int locks = 0;

		var l = new object();

		//
		// Anything in BuildRules should be thread safe
		//
#if true
		{
			Parallel.ForEach( styleRuleUpdates, panel =>
			{
				if ( !panel.IsValid )
					return;

				if ( panel.Style.BuildRulesInThread() )
				{
					lock ( l )
					{
						locks++;
						panel.SetNeedsPreLayout();
					}
				}

				panel.MarkStylesRebuilt();

			} );

		}
#else
		{
			foreach ( var panel in styleRuleUpdates )
			{
				if ( !panel.IsValid )
					return;

				if ( panel.Style.BuildRulesInThread() )
				{
					lock ( l )
					{
						locks++;
						panel.SetNeedsPreLayout();
					}
				}
			};
		}
#endif

		styleRuleUpdates.Clear();

		if ( timer.ElapsedMilliSeconds > 0.5 )
		{
			Log.Trace( $"BuildStyleRules {count:n0} ({locks}) took {timer.ElapsedMilliSeconds}ms" );
		}
	}
}
