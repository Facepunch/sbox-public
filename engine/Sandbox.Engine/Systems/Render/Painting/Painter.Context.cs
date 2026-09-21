using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Holds drawing state and manages recordings into a command list.
	/// </summary>
	internal class Context : CommandList.IResource
	{
		internal readonly List<Sandbox.UI.GPUBoxInstance> TextInstances = [];

		internal readonly PainterBatcher Batcher;
		internal CommandList CommandList => Batcher.CommandList;
		PainterBatcher.Checkpoint _start;
		int _commandStart;
		PainterBatcher.Target _target;
		Context _nested;

		public State State;
		internal bool HasState;
		internal BlendMode InitialBlendMode;
		internal int DestinationDepth;
		internal string ActiveLayer;
		internal Matrix LegacyTransform = Matrix.Identity;
		internal BlendMode LegacyBlendMode;

		public float ScaleToScreen = 1;
		public float InheritedOpacity = 1;
		public Rect Bounds;
		public Matrix BaseTransform = Matrix.Identity;
		internal long Recording { get; private set; }
		bool _active;
		int _thread;

		internal Context( CommandList commandList ) : this( new PainterBatcher( commandList ) )
		{
			commandList.RegisterResource( this );
		}

		Context( PainterBatcher batcher )
		{
			Batcher = batcher;
		}

		internal static Context Get( CommandList commandList )
		{
			ArgumentNullException.ThrowIfNull( commandList );

			lock ( commandList.SyncRoot )
			{
				return commandList.FindResource<Context>() ?? new Context( commandList );
			}
		}

		internal void InitializeState()
		{
			if ( HasState ) return;
			State = new() { OverrideBlendMode = InitialBlendMode };
			HasState = true;
		}

		void ClearState()
		{
			State = default;
			HasState = false;
		}

		internal void ResetDrawingState( BlendMode blendMode )
		{
			HasState = false;
			InitialBlendMode = blendMode;
		}

		internal Painter Begin( Rect bounds, bool legacy = false )
		{
			lock ( CommandList.SyncRoot )
			{
				if ( IsPainting && DestinationDepth > 0 )
				{
					_nested ??= new Context( Batcher );
					_nested.LegacyTransform = LegacyTransform;
					_nested.LegacyBlendMode = LegacyBlendMode;
					return _nested.Begin( bounds, legacy );
				}

				if ( IsPainting )
				{
					Reset();
					Batcher.Rewind( _start );
					CommandList.Rewind( _commandStart );
					Batcher.Destination = _target;
				}

				_start = Batcher.GetCheckpoint();
				_target = Batcher.Destination;
				Batcher.Destination = new();
				_commandStart = CommandList.GetCheckpoint();
				ResetDrawingState( legacy ? LegacyBlendMode : BlendMode.Normal );
				InitializeState();
				Recording++;
				ScaleToScreen = 1;
				InheritedOpacity = 1;
				Bounds = bounds;
				BaseTransform = legacy ? LegacyTransform : Matrix.Identity;
				_thread = Environment.CurrentManagedThreadId;
				_active = true;
				return new Painter( this, ownsContext: true );
			}
		}

		internal Painter Painter => IsActive ? new Painter( this ) : throw new ObjectDisposedException( nameof( Painter ), "The paint context has ended." );
		internal bool IsActive => _active && _thread == Environment.CurrentManagedThreadId;
		internal bool IsPainting => _active;
		internal bool CanClear => ActiveLayer is null && DestinationDepth == 0;

		internal void End()
		{
			lock ( CommandList.SyncRoot )
			{
				if ( !_active ) return;
				if ( _thread != Environment.CurrentManagedThreadId ) throw new InvalidOperationException( "End painting on the thread that began it." );
				if ( ActiveLayer is not null ) throw new InvalidOperationException( "Dispose layers before ending painting." );
				_active = false;
				try
				{
					OnEnd();
				}
				finally
				{
					ClearState();
					Batcher.RestoreScopeDepth( _start );
					Batcher.Destination = _target;
				}
			}
		}

		protected virtual void OnEnd()
		{
			Batcher.Flush();
		}

		internal void Reset()
		{
			if ( _active && _thread != Environment.CurrentManagedThreadId )
				throw new InvalidOperationException( "Reset painting on the thread that began it." );

			_nested?.Reset();
			_active = false;
			Recording++;
			DestinationDepth = 0;
			ActiveLayer = null;
			ClearState();
		}

		void CommandList.IResource.BeginExecute()
		{
			for ( var context = this; context is not null; context = context._nested )
			{
				if ( context.IsPainting )
					throw new InvalidOperationException( "Finish painting before executing the command list." );
			}

			Batcher.BeginExecute();
		}

		void CommandList.IResource.Reset()
		{
			Reset();
			Batcher.Clear();
			LegacyTransform = Matrix.Identity;
			LegacyBlendMode = BlendMode.Normal;
		}
	}
}
