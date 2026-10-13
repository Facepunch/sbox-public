using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// A page of the hub.
/// </summary>
internal abstract class HubTab : Panel
{
	/// <summary>
	/// The name <c>perf_hub &lt;tab&gt;</c> and links use.
	/// </summary>
	public abstract string TabId { get; }

	public abstract string Title { get; }

	/// <summary>
	/// Developer detail rather than something a player needs. Hidden unless <see cref="PerfHub.ShowAdvanced"/>.
	/// </summary>
	public virtual bool Advanced => true;

	/// <summary>
	/// Whether this tab can be shown right now.
	/// </summary>
	public virtual bool Available => !Advanced || PerfHub.ShowAdvanced;

	protected HubTab()
	{
		AddClass( "page" );
	}

	/// <summary>
	/// The analysis moved on. Called a few times a second while this tab is showing.
	/// </summary>
	public abstract void Refresh( PerfHub.Summary summary );
}

/// <summary>
/// The hub's shell: a strip with the tabs and the window buttons, a readout of the numbers every tab needs, then the
/// page, and a console under it. The hub's window is borderless, so the strip is its title bar and drags it.
/// </summary>
internal sealed class HubWindow : Panel
{
	readonly Panel _strip;
	readonly Label _title;
	readonly Label _consoleButton;
	readonly Label _maximize;
	readonly Panel _tabBar;
	readonly Panel _readout;
	readonly Label _paused;
	readonly Label _fps, _frame, _cpu, _gpu, _p99, _limit;
	readonly Label _pause;
	readonly Panel _pageScroll;
	readonly Panel _splitter;
	readonly HubConsole _console;
	readonly List<(HubTab Tab, Label Button)> _tabs = new();
	readonly Dictionary<HubTab, float> _scroll = new();
	HubTab _shown;
	PerfHub.Summary _refreshedWith;
	PerfHub.Summary _readWith;
	bool _consoleOpen;
	float _consoleHeight = 220f;

	public HubWindow()
	{
		AddClass( "perfhub" );

		_strip = AddChild( new Panel( null, "strip window-drag" ) );
		_title = _strip.AddChild( new Label( "Performance", "title" ) );
		_tabBar = _strip.AddChild( new Panel( null, "tabs" ) );
		var controls = _strip.AddChild( new Panel( null, "host-controls" ) );

		_consoleButton = Button( controls, "terminal", "Console", () => _consoleOpen = !_consoleOpen );
		controls.AddChild( new Panel( null, "divider" ) );
		Button( controls, "remove", "Minimize", () => Window?.Minimize() );
		_maximize = Button( controls, "crop_square", "Maximize", () => Window?.ToggleMaximized() );
		Button( controls, "close", "Close (F11)", PerfHub.Close ).AddClass( "close" );

		_readout = AddChild( new Panel( null, "readout" ) );
		_paused = _readout.AddChild( new Label( "", "readout-item paused-flag hidden" ) );
		_fps = Readout( "fps" );
		_frame = Readout( "frame" );
		_cpu = Readout( "cpu" );
		_gpu = Readout( "gpu" );
		_p99 = Readout( "p99" );
		_limit = Readout( "limit" );
		_limit.Parent.AddClass( "grow" );
		_pause = Button( _readout, "pause", null, () => PerfHub.Paused = !PerfHub.Paused );

		var workspace = AddChild( new Panel( null, "workspace" ) );
		_pageScroll = workspace.AddChild( new Panel( null, "page-scroll" ) );
		_splitter = workspace.AddChild( new Splitter( this ) );
		_splitter.AddClass( "console-splitter window-nodrag" );
		_console = workspace.AddChild( new HubConsole() );

		AddTab( new HubOverviewTab() );
		AddTab( new HubCpuTab() );
		AddTab( new HubGpuTab() );
		AddTab( new HubMemoryTab() );
		AddTab( new HubRenderingTab() );
		AddTab( new HubNetworkTab() );
		AddTab( new HubUiTab() );
		AddTab( new HubAudioTab() );
	}

	/// <summary>
	/// The OS window the hub is in.
	/// </summary>
	SurfaceWindow Window => SurfaceWindow.FromPanel( this );

	static Label Button( Panel parent, string icon, string tooltip, Action onClick )
	{
		var button = parent.AddChild( new Label( icon, "icon-button window-nodrag" ) );
		button.Tooltip = tooltip;
		button.AddEventListener( "onclick", onClick );
		return button;
	}

	Label Readout( string key )
	{
		var item = _readout.AddChild( new Panel( null, "readout-item" ) );
		item.AddChild( new Label( key, "readout-key" ) );
		return item.AddChild( new Label( "-", "readout-value" ) );
	}

	void AddTab( HubTab tab )
	{
		var button = _tabBar.AddChild( new Label( tab.Title, "tab window-nodrag" ) );
		button.AddEventListener( "onclick", () => PerfHub.ActiveTab = tab.TabId );

		tab.AddClass( "hidden" );
		_pageScroll.AddChild( tab );
		_tabs.Add( (tab, button) );
	}

	/// <summary>
	/// Drag to give the console more or less of the window.
	/// </summary>
	sealed class Splitter( HubWindow window ) : Panel
	{
		public override bool WantsDrag => true;

		protected override void OnDrag( DragEvent e )
		{
			e.StopPropagation();
			var available = window.Box.Rect.Height / window.ScaleToScreen - 200;
			window._consoleHeight = Math.Clamp( window._consoleHeight - e.MouseDelta.y / ScaleToScreen, 120, MathF.Max( 120, available ) );
		}
	}

	public override void Tick()
	{
		base.Tick();

		var advanced = PerfHub.ShowAdvanced;

		// Width classes: grids rearrange themselves, but some layouts change shape outright
		var width = Box.Rect.Width / ScaleToScreen;
		SetClass( "narrow", width > 0 && width < 600 );
		SetClass( "wide", width >= 1000 );
		SetClass( "player", !advanced );

		_consoleButton.SetClass( "active", _consoleOpen );
		_maximize.Text = Window?.IsMaximized == true ? "filter_none" : "crop_square";

		_console.SetClass( "hidden", !_consoleOpen );
		_splitter.SetClass( "hidden", !_consoleOpen );
		if ( _consoleOpen ) _console.Style.Height = _consoleHeight;

		UpdateReadout();
		UpdateTabs( advanced );
	}

	void UpdateReadout()
	{
		var paused = PerfHub.Paused;
		_readout.SetClass( "paused", paused );
		_paused.SetClass( "hidden", !paused );
		_paused.Text = paused ? $"paused {PerfHub.PausedAt:HH:mm:ss}" : "";
		_pause.Text = paused ? "play_arrow" : "pause";
		_pause.Tooltip = paused ? "Resume" : "Hold everything the hub shows. Collection carries on underneath.";

		var s = PerfHub.Current;
		if ( s == _readWith ) return;
		_readWith = s;

		if ( s.Frames == 0 )
		{
			_fps.Text = _frame.Text = _cpu.Text = _gpu.Text = _p99.Text = _limit.Text = "-";
			return;
		}

		var target = s.TargetMs;
		_fps.Text = $"{s.Fps:0}";
		_frame.Text = $"{s.FrameAvg:0.00}";
		_cpu.Text = $"{s.CpuWorkAvg:0.00}";
		_gpu.Text = s.GpuAvg > 0 ? $"{s.GpuAvg:0.00}" : "-";
		_p99.Text = $"{s.FrameP99:0.0}";
		_limit.Text = s.LimitText;

		Grade( _fps, s.MeetingTarget ? null : s.Rating );
		Grade( _cpu, s.Limiter == PerfHub.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null );
		Grade( _gpu, s.Limiter == PerfHub.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null );
		Grade( _p99, s.FrameP99 > s.HitchMs ? PerfHub.Severity.Bad : s.FrameP99 > target * 1.25f ? PerfHub.Severity.Warning : null );

		_fps.Parent.Tooltip = $"Target {s.TargetFps:0} fps ({target:0.00} ms), from {s.TargetSource}";
		_limit.Parent.Tooltip = s.Limiter switch
		{
			PerfHub.Limiter.Capped => $"Waiting on {s.TargetSource}, {s.CapWaitAvg:0.00} ms a frame spare",
			PerfHub.Limiter.VSync => "Frames land on the display's refresh",
			PerfHub.Limiter.Gpu => "The GPU takes about as long as the whole frame",
			PerfHub.Limiter.Cpu => "The GPU has slack, so the frame's CPU work is what takes the time",
			_ => null,
		};
	}

	static void Grade( Label label, PerfHub.Severity? severity )
	{
		label.SetClass( "warning", severity == PerfHub.Severity.Warning );
		label.SetClass( "bad", severity == PerfHub.Severity.Bad );
	}

	void UpdateTabs( bool advanced )
	{
		HubTab active = null;

		foreach ( var (tab, button) in _tabs )
		{
			var available = tab.Available;
			button.SetClass( "hidden", !available );

			if ( available && string.Equals( tab.TabId, PerfHub.ActiveTab, StringComparison.OrdinalIgnoreCase ) )
				active = tab;
		}

		// An unknown or unavailable tab falls back to the overview
		if ( active is null )
		{
			active = _tabs[0].Tab;
			PerfHub.ActiveTab = active.TabId;
		}

		// Players get the overview alone - a title rather than a one-item tab bar
		_tabBar.SetClass( "hidden", !advanced );
		_title.SetClass( "hidden", advanced );

		if ( active != _shown )
		{
			// Each tab keeps its own scroll position
			if ( _shown is not null )
			{
				_scroll[_shown] = _pageScroll.ScrollOffset.y;
				_shown.AddClass( "hidden" );
			}

			active.RemoveClass( "hidden" );
			_pageScroll.ScrollOffset = new Vector2( 0, _scroll.GetValueOrDefault( active ) );
			_shown = active;
			_refreshedWith = null;
		}

		foreach ( var (tab, button) in _tabs )
			button.SetClass( "active", tab == active );

		var summary = PerfHub.Current;
		if ( summary != _refreshedWith )
		{
			_refreshedWith = summary;
			active.Refresh( summary );
		}
	}
}
