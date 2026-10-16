namespace Sandbox;

internal static partial class Api
{
	internal static partial class Events
	{
		private static RealTimeSince TimeSincePosted;
		private static Task TaskFlushEvent;
		private static List<EventRecord> Pending = new();
		private static readonly System.Threading.Lock QueueLock = new();
		private static readonly System.Threading.SemaphoreSlim FlushMutex = new( 1, 1 );

		/// <summary>
		/// Add an event to the queue. You should not use this event again.
		/// </summary>
		private static void Add( EventRecord e )
		{
			if ( !Application.IsRetail || Application.IsStandalone )
				return;

			if ( !AccountInformation.UseAnalytics )
				return;

			lock ( QueueLock ) Pending.Add( e );
		}

		/// <summary>
		/// Force an immediate flush of all events
		/// </summary>
		internal static void Flush()
		{
			lock ( QueueLock )
			{
				if ( Pending.Count == 0 || TaskFlushEvent is { IsCompleted: false } ) return;
				TimeSincePosted = 0;
				TaskFlushEvent = Task.Run( FlushEvents );
			}
		}

		internal static Task Shutdown() => FlushEvents();

		/// <summary>
		/// Post a batch of analytic events. Analytic events are things like compile or load times to 
		/// help us find, fix and track performance issues.
		/// </summary>
		internal static void TickEvents()
		{
			lock ( QueueLock )
			{
				if ( Pending.Count == 0 || (TimeSincePosted < 30 && Pending.Count < 100) ) return;
			}

			Flush();
		}

		private static async Task FlushEvents()
		{
			await FlushMutex.WaitAsync().ConfigureAwait( false );
			try
			{
				// Shutdown waits for any send already in progress before draining the remaining queue.
				if ( Sandbox.Backend.Account is null ) return;
				EventRecord[] records;
				lock ( QueueLock )
				{
					if ( Pending.Count == 0 ) return;
					records = Pending.ToArray();
					Pending.Clear();
				}

				await PostEventsAsync( records ).ConfigureAwait( false );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when flushing events ({e.Message})" );
			}

			finally
			{
				FlushMutex.Release();
			}
		}


		/// <summary>
		/// Post a batch of analytic events. Analytic events are things like compile or load times to 
		/// help us find, fix and track performance issues.
		/// </summary>
		internal static async Task PostEventsAsync( EventRecord[] records )
		{
			if ( Sandbox.Backend.Account is null )
				return;

			var values = new
			{
				Events = records
			};

			await Sandbox.Backend.Account.SubmitEvents( values ).ConfigureAwait( false );
		}
	}
}
