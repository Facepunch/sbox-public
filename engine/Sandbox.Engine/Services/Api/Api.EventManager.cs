namespace Sandbox;

internal static partial class Api
{
	internal static partial class Events
	{
		private static RealTimeSince TimeSincePosted;
		private static Task TaskFlushEvent;
		private static List<EventRecord> Pending = new();

		/// <summary>
		/// Add an event to the queue. You should not use this event again.
		/// </summary>
		private static void Add( EventRecord e )
		{
			if ( !Application.IsRetail || Application.IsStandalone )
				return;

			Pending.Add( e );
		}

		/// <summary>
		/// Force an immediate flush of all events
		/// </summary>
		internal static void Flush()
		{
			TimeSincePosted = 0;

			if ( Pending.Count == 0 ) return;
			_ = Task.Run( FlushEvents );
		}

		internal static async Task Shutdown()
		{
			await FlushEvents();
		}

		/// <summary>
		/// Post a batch of analytic events. Analytic events are things like compile or load times to 
		/// help us find, fix and track performance issues.
		/// </summary>
		internal static void TickEvents()
		{
			// nothing to do
			if ( Pending.Count == 0 )
				return;

			// throttle
			if ( TimeSincePosted < 30 && Pending.Count < 100 )
				return;

			// wait for the last one to finish
			if ( TaskFlushEvent != null && !TaskFlushEvent.IsCompleted )
				return;

			Flush();
		}

		private static async Task FlushEvents()
		{
			if ( Pending.Count <= 0 ) return;

			// Take the records locally to clear the queue
			var records = Pending.ToArray();
			Pending.Clear();

			// Wait for any pending pushes
			if ( TaskFlushEvent != null && !TaskFlushEvent.IsCompleted )
				await TaskFlushEvent;

			if ( records.Count() == 0 )
				return;

			try
			{
				TaskFlushEvent = PostEventsAsync( records );
				await TaskFlushEvent;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when flushing events ({e.Message})" );
			}

			TaskFlushEvent = null;
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

			await Sandbox.Backend.Account.SubmitEvents( values );
		}
	}
}
