using static Sandbox.Internal.GlobalGameNamespace;
using System.Diagnostics;

namespace Sandbox.PanelGallery;

/// <summary>
/// Opens and closes popup windows by itself, no mouse involved - each one lives a few frames,
/// round and round, for a few seconds or until stopped. Reports how long each step took while
/// it runs. Logs every cycle before attempting it, so if the app hangs the log says which one.
/// </summary>
public class PopupStress : Panel
{
	/// <summary>
	/// How long a run lasts, in seconds.
	/// </summary>
	public float Duration { get; set; } = 5;

	// How many frames each popup stays open, cycled through in turn. Zero closes it before it
	// has drawn once; one closes it the frame after it appears.
	static readonly int[] Lifetimes = [0, 1, 2, 3, 5, 8, 20];

	readonly Sandbox.UI.Button _button;
	readonly Metric _cycles, _rate, _open, _close, _frame, _elapsed;

	PanelWindow _popup;
	int _cycle;
	int _framesLeft;
	bool _running;
	bool _closedLastTick;

	readonly Stopwatch _run = new();
	readonly Stopwatch _tick = new();
	readonly Stopwatch _opening = new();
	Stat _openStat, _closeStat, _frameStat;

	public PopupStress()
	{
		AddClass( "popup-stress" );

		_button = new Sandbox.UI.Button( $"Run for {Duration:0} seconds", "play_arrow", "flatbutton", Toggle );
		AddChild( _button );

		var metrics = Add.Panel( "metrics" );
		_cycles = new Metric( metrics, "Cycles" );
		_rate = new Metric( metrics, "Popups per second" );
		_open = new Metric( metrics, "Open - call to on screen" );
		_close = new Metric( metrics, "Close - the frame it's destroyed in" );
		_frame = new Metric( metrics, "Frame time" );
		_elapsed = new Metric( metrics, "Elapsed" );
	}

	void Toggle()
	{
		if ( _running ) Stop( "stopped" );
		else Start();
	}

	void Start()
	{
		_cycle = 0;
		_openStat = _closeStat = _frameStat = default;
		_closedLastTick = false;

		_run.Restart();
		_tick.Reset();
		_running = true;

		_button.Text = "Stop";
		_button.Icon = "stop";

		Log.Info( "popup stress: start" );
	}

	void Stop( string why )
	{
		_running = false;
		_run.Stop();

		if ( _popup is { IsOpen: true } )
			_popup.Dispose();

		_popup = null;

		_button.Text = $"Run for {Duration:0} seconds";
		_button.Icon = "play_arrow";

		Log.Info( $"popup stress: {why} after {_cycle} cycles - open avg {_openStat.Average:0.0}ms max {_openStat.Max:0.0}ms, close frame avg {_closeStat.Average:0.0}ms max {_closeStat.Max:0.0}ms, frame max {_frameStat.Max:0.0}ms" );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_running )
			return;

		// The frame that follows a destroy is where the swap chain actually goes, and where
		// the render device waits for its last present - so that one is counted separately
		if ( _tick.IsRunning )
		{
			var ms = _tick.Elapsed.TotalMilliseconds;
			_frameStat.Add( ms );
			if ( _closedLastTick ) _closeStat.Add( ms );
		}

		_tick.Restart();
		_closedLastTick = false;

		if ( _run.Elapsed.TotalSeconds >= Duration )
		{
			Stop( "done" );
			Report();
			return;
		}

		Step();
		Report();
	}

	void Step()
	{
		if ( _popup is null )
		{
			var lifetime = Lifetimes[_cycle % Lifetimes.Length];
			Log.Info( $"popup stress: cycle {_cycle} open for {lifetime} frames" );

			var window = PanelWindow.FromPanel( this );
			var position = Box.Rect.BottomLeft + new Vector2( (_cycle % 8) * 60, 10 + (_cycle % 3) * 40 );

			_opening.Restart();
			_popup = PanelWindow.Popup( window, position );

			var menu = _popup.Root.Add.Panel( "dropdown" );
			menu.StyleSheet.Load( "/styles/editor.scss" );

			for ( int i = 0; i < 3 + (_cycle % 4); i++ )
			{
				var row = new Sandbox.UI.Button( $"Item {i} of cycle {_cycle}", "circle" );
				row.AddClass( "row" );
				menu.AddChild( row );
			}

			_framesLeft = lifetime;
			return;
		}

		// Something else took it down - a click outside, say. Count it and move on.
		if ( !_popup.IsOpen )
		{
			_popup = null;
			_cycle++;
			return;
		}

		if ( _opening.IsRunning && _popup.IsShown )
		{
			_openStat.Add( _opening.Elapsed.TotalMilliseconds );
			_opening.Reset();
		}

		if ( _framesLeft-- > 0 )
			return;

		Log.Info( $"popup stress: cycle {_cycle} close" );

		_popup.Dispose();
		_popup = null;
		_opening.Reset();
		_closedLastTick = true;
		_cycle++;
	}

	void Report()
	{
		var seconds = _run.Elapsed.TotalSeconds;

		_cycles.Value = _cycle.ToString();
		_rate.Value = seconds > 0 ? $"{_cycle / seconds:0.0}" : "-";
		_open.Value = _openStat.ToString();
		_close.Value = _closeStat.ToString();
		_frame.Value = _frameStat.ToString();
		_elapsed.Value = $"{seconds:0.0}s";
	}

	/// <summary>
	/// A name and a value, one under the other in the metrics block.
	/// </summary>
	class Metric
	{
		readonly Sandbox.UI.Label _value;

		public Metric( Panel parent, string name )
		{
			var row = parent.Add.Panel( "metric" );
			row.Add.Label( name, "name" );
			_value = row.Add.Label( "-", "value" );
		}

		public string Value
		{
			set => _value.Text = value;
		}
	}

	struct Stat
	{
		public int Count;
		public double Total;
		public double Max;

		public double Average => Count > 0 ? Total / Count : 0;

		public void Add( double ms )
		{
			Count++;
			Total += ms;
			if ( ms > Max ) Max = ms;
		}

		public override string ToString() => Count > 0 ? $"avg {Average:0.0}ms   max {Max:0.0}ms   ({Count})" : "-";
	}
}
