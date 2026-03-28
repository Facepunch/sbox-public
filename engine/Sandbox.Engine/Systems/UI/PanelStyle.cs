namespace Sandbox.UI;

public sealed class PanelStyle : Styles
{
	Panel panel;

	internal Styles Cached = new Styles();
	internal Styles Final = new Styles();

	/// <summary>
	/// This could be a local variable if we wanted to create a new class every time
	/// </summary>
	List<StyleSelector> activeRules;

	/// <summary>
	/// Store the last active rules so we can compare them when they change and trigger sounds etc on new styles
	/// </summary>
	internal List<StyleSelector> LastActiveRules;

	/// <summary>
	/// Cache of the active rules that are applied, that way we can trigger stuff only if they actually changed
	/// </summary>
	int ActiveRulesGuid;

	private bool isDirty = true;
	internal bool skipTransitions = true;

	bool rulesChanged = true;

	public override void Dirty() => isDirty = true;
	internal bool IsDirty => isDirty;

	internal PanelStyle( Panel panel )
	{
		this.panel = panel;
	}

	/// <summary>
	/// Should be called when a stylesheet in our bundle has changed. This can happen as a result of
	/// editing it in the style editor.
	/// </summary>
	internal void UnderlyingStyleHasChanged()
	{
		ActiveRulesGuid = -1;
	}

	/// <summary>
	/// All these styles could possibly apply to us. To get this list we get the stylesheets from
	/// ourself and our anscestors and then filter them by the broadphase. The broadphase is a check
	/// against classes, element names and ids, things that don't change in a recursive way.
	/// </summary>
	StyleBlock[] StyleBlocks;

	/// <summary>
	/// A hash of the things that are checked in the broadphase.
	/// </summary>
	int broadPhaseHash = 0;

	bool _hasBeforeElement;
	bool _hasAfterElement;

	/// <summary>
	/// This style has a ::before element available. This is signalling to the panel system that if we 
	/// apply this style, we should also create a ::before element.
	/// </summary>
	public bool HasBeforeElement => _hasBeforeElement;

	/// <summary>
	/// This style has a ::after element available. This is signalling to the panel system that if we 
	/// apply this style, we should also create a ::after element.
	/// </summary>
	public bool HasAfterElement => _hasAfterElement;

	/// <summary>
	/// Called when a stylesheet has been added or removed from ourselves or one of
	/// our ancestor panels - because under that condition we need to rebuild our
	/// broadphase.
	/// </summary>
	internal void InvalidateBroadphase()
	{
		if ( StyleBlocks == null )
			return;

		StyleBlocks = null;

		foreach ( var child in panel.Children )
		{
			child.Style?.InvalidateBroadphase();
		}
	}

	void BuildApplicableBlocks()
	{
		StyleBlocks = panel.AllStyleSheets
									.SelectMany( x => x.Nodes )
									.Where( x => x.TestBroadphase( panel ) )
									.ToArray();
	}



	/// <summary>
	/// Called from the root panel in a thread. We replace activeRules with all of the rules that
	/// we want applied and return true if the rules changed.
	/// </summary>
	internal bool BuildRulesInThread()
	{
		activeRules?.Clear();

		var hash = HashCode.Combine( panel.Id, panel.ElementName, panel.Classes );
		if ( StyleBlocks == null || hash != broadPhaseHash )
		{
			BuildApplicableBlocks();
		}
		broadPhaseHash = hash;

		_hasBeforeElement = false;
		_hasAfterElement = false;

		bool isBeforeOrAfter = (panel as IStyleTarget).IsBeforeOrAfter;

		foreach ( var c in StyleBlocks )
		{
			//
			// If we're not a ::before or ::after element, see if we have any styles with ::before or ::after elements.
			//
			if ( !isBeforeOrAfter )
			{
				_hasBeforeElement = _hasBeforeElement || c.Test( panel, PseudoClass.Before ) != null;
				_hasAfterElement = _hasAfterElement || c.Test( panel, PseudoClass.After ) != null;
			}

			var winningSelector = c.Test( panel );
			if ( winningSelector == null ) continue;

			activeRules ??= new();
			activeRules.Add( winningSelector );
		}

		int ruleguid = 0;
		if ( activeRules != null )
		{
			activeRules.Sort( StyleOrderer.Instance );

			foreach ( var entry in activeRules )
			{
				ruleguid = HashCode.Combine( ruleguid, entry );
			}
		}

		rulesChanged = rulesChanged || ruleguid != ActiveRulesGuid;
		ActiveRulesGuid = ruleguid;

		return rulesChanged;
	}

	internal bool BuildCached( ref LayoutCascade cascade )
	{
		if ( !isDirty && !cascade.SelectorChanged && !rulesChanged )
			return false;

		isDirty = false;

		Cached.From( Styles.Default );

		if ( activeRules != null )
		{
			foreach ( var entry in activeRules )
			{
				Cached.Add( entry.Block.Styles );
			}
		}

		//
		// Rules changed
		//
		if ( rulesChanged )
		{
			rulesChanged = false;
			cascade.SelectorChanged = true;

			LastActiveRules ??= new();
			activeRules ??= new();

			foreach ( var rule in activeRules.Except( LastActiveRules ) )
			{
				OnRuleAdded( rule );
			}

			foreach ( var rule in LastActiveRules.Except( activeRules ) )
			{
				OnRuleRemoved( rule );
			}

			LastActiveRules.Clear();
			LastActiveRules.AddRange( activeRules );
		}

		Cached.Add( this );

		Cached.ApplyScale( cascade.Scale );
		return true;
	}

	internal Styles BuildFinal( ref LayoutCascade cascade, out bool changed )
	{
		var time = panel.TimeNow;
		var timeDelta = panel.TimeDelta;

		cascade.SkipTransitions = skipTransitions || cascade.SkipTransitions;
		changed = BuildCached( ref cascade );

		if ( !cascade.SkipTransitions && !HasTransitions && !HasAnimation )
		{
			Final.CopyShadows( Cached );
		}

		if ( cascade.SkipTransitions )
		{
			Final.CopyShadows( Cached );
			panel.Transitions.Kill();
			skipTransitions = false;
		}
		else if ( changed && Final != null )
		{
			panel.Transitions.Kill( Final );
			panel.Transitions.Add( Final, Cached, time - timeDelta );
		}

		Final.From( Cached );
		cascade.ApplyCascading( Final );
		Final.FillDefaults();

		if ( panel.Transitions.Run( Final, time ) )
		{
			changed = true;
		}

		if ( Final.ApplyAnimation( panel ) )
		{
			changed = true;
		}

		return Final;
	}

	public override bool Set( string property, string value )
	{
		isDirty = true;

		return base.Set( property, value );
	}

	internal void OnRuleAdded( StyleSelector selector )
	{
		if ( selector.Block.Styles.SoundIn != null )
		{
			panel.PlaySound( selector.Block.Styles.SoundIn );
		}
	}

	internal void OnRuleRemoved( StyleSelector selector )
	{
		if ( selector.Block.Styles.SoundOut != null )
		{
			panel.PlaySound( selector.Block.Styles.SoundOut );
		}
	}

	//
	// Helpers
	//


	public void SetBackgroundImage( Texture texture )
	{
		BackgroundImage = texture;
	}

	public void SetBackgroundImage( string image )
	{
		SetBackgroundImage( Texture.Load( image ) );
	}

	public async Task SetBackgroundImageAsync( string image )
	{
		SetBackgroundImage( await Texture.LoadAsync( image ) );
	}

	public void SetRect( Rect rect )
	{
		Left = rect.Left;
		Top = rect.Top;
		Width = rect.Width;
		Height = rect.Height;

		Dirty();
	}

	/// <summary>
	/// Returns true if we have the style
	/// </summary>
	internal bool ContainsStyle( Styles style )
	{
		if ( LastActiveRules == null ) return false;

		return LastActiveRules.Any( x => x.Block.Styles == style );
	}
}

internal class StyleOrderer : IComparer<StyleSelector>
{
	internal static StyleOrderer Instance = new StyleOrderer();

	public int Compare( StyleSelector x, StyleSelector y )
	{
		return x.Score - y.Score;
	}
}
