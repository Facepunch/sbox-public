using System.Diagnostics.Tracing;
using System.Threading;

namespace Sandbox.Diagnostics;

/// <summary>
/// Tools for diagnosing heap allocations
/// </summary>
[SkipHotload]
public static class Allocations
{
	static GCEventListener listener;
	static Lock _lock = new Lock();

	public struct Entry
	{
		public string Name { get; set; }
		public ulong Count { get; set; }
		public ulong TotalBytes { get; set; }
	}

	static HashSet<Scope> scopes = new();

	public class Scope
	{
		Dictionary<string, Entry> _entries;

		public Scope()
		{

		}

		public void Start()
		{
			lock ( _lock )
			{
				Flip();
				scopes.Add( this );
				UpdateEnableState();
			}
		}

		public void Stop()
		{
			lock ( _lock )
			{
				Flip();
				scopes.Remove( this );
				UpdateEnableState();
			}
		}

		public void Clear()
		{
			_entries?.Clear();
		}

		public IEnumerable<Entry> Entries
		{
			get
			{
				Flip();
				return _entries?.Values ?? Enumerable.Empty<Entry>();
			}
		}

		internal void Add( Dictionary<string, GCEventListener.Stats> incoming )
		{
			_entries ??= new Dictionary<string, Entry>();

			foreach ( var e in incoming )
			{
				if ( _entries.TryGetValue( e.Key, out var entry ) )
				{
					entry.Count += e.Value.Count;
					entry.TotalBytes += e.Value.Bytes;
					_entries[e.Key] = entry;
				}
				else
				{
					entry = new Entry { Name = e.Key };
					entry.Count += e.Value.Count;
					entry.TotalBytes += e.Value.Bytes;
					_entries[e.Key] = entry;
				}
			}
		}
	}

	static bool _enableState;

	static void UpdateEnableState()
	{
		bool wantsEnabled = scopes.Count > 0;

		if ( wantsEnabled == _enableState )
			return;

		_enableState = wantsEnabled;

		if ( wantsEnabled )
		{
			listener = new GCEventListener();
		}
		else
		{
			listener?.Dispose();
			listener = null;
		}
	}

	internal static void Flip()
	{
		if ( listener is null )
			return;

		lock ( _lock )
		{
			var entries = listener.Flip();
			if ( entries.Count == 0 ) return;

			foreach ( var scope in scopes )
			{
				scope.Add( entries );
			}
		}
	}
}

[SkipHotload]
class GCEventListener : EventListener
{
	public struct Stats
	{
		public ulong Count;
		public ulong Bytes;
	}

	Dictionary<string, Stats> _writer = new( 1024 );
	Dictionary<string, Stats> _reader = new( 1024 );

	private const int GC_KEYWORD = 0x0000001;

	internal Dictionary<string, Stats> Flip()
	{
		_reader.Clear();
		_reader = Interlocked.Exchange( ref _writer, _reader );

		return _reader;
	}

	protected override void OnEventSourceCreated( EventSource eventSource )
	{
		if ( eventSource.Name.Equals( "Microsoft-Windows-DotNETRuntime" ) )
		{
			EnableEvents( eventSource, EventLevel.Verbose, (EventKeywords)GC_KEYWORD );
		}
	}

	protected override void OnEventWritten( EventWrittenEventArgs eventData )
	{
		//Log.Info( $"{eventData.EventSource.Name} {eventData.EventName}" );

		switch ( eventData.EventName )
		{
			case "GCAllocationTick_V4":
				ProcessAllocationEvent( eventData );
				break;
		}
	}

	private void ProcessAllocationEvent( EventWrittenEventArgs eventData )
	{
		var tl = eventData.Payload[5] as string;

		//if ( "System.String" == tl ) return;
		//if ( "System.Diagnostics.StackFrame" == tl ) return;
		//if ( "System.RuntimeMethodInfoStub" == tl ) return;
		//if ( "System.Text.StringBuilder" == tl ) return;
		//if ( "System.Diagnostics.Tracing.EventWrittenEventArgs" == tl ) return;

		Stats value = default;
		_writer.TryGetValue( tl, out value );

		value.Count++;
		value.Bytes += (ulong)eventData.Payload[3];

		_writer[tl] = value;
	}
}
