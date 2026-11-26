
using Sandbox.Utility;
namespace Sandbox;

/// <summary>
/// Keeps a list of callbacks
/// The intention of this is that in the future we'll have a nice window that will
/// show the relative performance of each callback, and allow you to disable them to debug.
/// </summary>
class TimedCallbackList
{
	private List<CallbackEntry> entries = new();

	public TimedCallbackList()
	{
	}

	private void Add( CallbackEntry entry )
	{
		int index = entries.BinarySearch( entry );
		if ( index < 0 ) index = ~index;
		entries.Insert( index, entry );
	}

	private void Remove( CallbackEntry entry )
	{
		entries.Remove( entry );
	}

	internal IDisposable Add( int order, Action action, string className, string description )
	{
		var entry = new CallbackEntry( order, action, className, description );

		Add( entry );

		return DisposeAction.Create( () => Remove( entry ) );
	}

	static Superluminal _instrument = new Superluminal( "Callback", "#6fced3" );

	public void Run()
	{
		for ( int i = 0; i < entries.Count; i++ )
		{
			using ( _instrument.Start( entries[i].Description ) )
			{
				entries[i].Run();
			}
		}
	}

	internal void ClearMetrics()
	{
		for ( int i = 0; i < entries.Count; i++ )
		{
			entries[i].ClearMetrics();
		}
	}

	internal object[] GetMetrics()
	{
		return entries.Select( x => x.GetMetric() ).ToArray();
	}

	public class CallbackEntry : IComparable<CallbackEntry>
	{
		private Action action;

		public int Order { get; private set; }
		public string ClassName { get; private set; }
		public string Description { get; private set; }

		int _totalRuns;
		double _totalMilliseconds;

		public CallbackEntry( int order, Action action, string className, string description )
		{
			this.action = action;

			Order = order;
			ClassName = className;
			Description = description;
		}

		internal void ClearMetrics()
		{
			_totalMilliseconds = 0;
			_totalRuns = 0;
		}

		internal object GetMetric()
		{
			return new { Name = Description, ClassName, Count = _totalRuns, TotalMs = _totalMilliseconds, Avg = _totalRuns > 0 ? _totalMilliseconds / _totalRuns : 0 };
		}

		public void Run()
		{
			try
			{
				var timer = FastTimer.StartNew();

				action();

				_totalRuns++;
				_totalMilliseconds += timer.ElapsedMilliSeconds;
			}
			catch ( System.Exception e )
			{
				if ( ExceptionThrottle.ShouldLog( e, action.Method.DeclaringType, Description, out var suffix ) )
					Log.Error( e, $"{ClassName}.{Description}: {e.Message}{suffix}" );
			}
		}

		public int CompareTo( CallbackEntry other )
		{
			if ( other == null ) return 1;
			return Order.CompareTo( other.Order );
		}
	}
}
