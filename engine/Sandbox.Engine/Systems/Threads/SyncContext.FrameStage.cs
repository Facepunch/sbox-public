using System.Runtime.CompilerServices;

namespace Sandbox.Tasks;

internal static partial class SyncContext
{
	public static class FrameStage
	{
		public static FrameStageAwaiter Update = new FrameStageAwaiter();
		public static FrameStageAwaiter FixedUpdate = new FrameStageAwaiter();
		public static FrameStageAwaiter PreRender = new FrameStageAwaiter();

		public class FrameStageAwaiter
		{
			public ulong Value { get; private set; }

			// Swapped out before running, so waiting again from a continuation lands in the next trigger
			List<Action> _queue = new();
			List<Action> _spare;

			public void Trigger()
			{
				Value++;

				List<Action> running;
				lock ( this )
				{
					running = _queue;
					_queue = _spare ?? new();
					_spare = null;
				}

				try
				{
					foreach ( var continuation in running )
					{
						continuation();
					}
				}
				finally
				{
					running.Clear();
					_spare = running;
				}
			}

			internal void Enqueue( Action continuation )
			{
				lock ( this )
				{
					_queue.Add( continuation );
				}
			}

			internal void Remove( Action continuation )
			{
				lock ( this )
				{
					_queue.Remove( continuation );
				}
			}

			internal UpdateAwaiter Await() => new UpdateAwaiter( this, Value );
		}

		internal struct UpdateAwaiter : INotifyCompletion
		{
			ulong startValue;
			FrameStageAwaiter source;

			internal UpdateAwaiter( FrameStageAwaiter source, ulong startValue )
			{
				this.source = source;
				this.startValue = startValue;
			}

			public void OnCompleted( Action continuation )
			{
				source.Enqueue( continuation );
			}

			public bool IsCompleted => startValue != source.Value;
			public UpdateAwaiter GetAwaiter() => this;
			public void GetResult() { }
		}

	}
}
