using NativeEngine;

namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// The current input context, pushed using Context.Push
	/// </summary>
	static Context CurrentContext { get; set; }

	// weak list of contexts
	static List<WeakReference<Context>> contexts = new List<WeakReference<Context>>();
	static IDisposable defaultContextScope;

	/// <summary>
	/// Get all of the contexts
	/// </summary>
	internal static IEnumerable<Context> Contexts
	{
		get
		{
			// remove collected
			contexts.RemoveAll( x => x.TryGetTarget( out _ ) == false );

			foreach ( var t in contexts )
			{
				if ( t.TryGetTarget( out var x ) )
					yield return x;
			}
		}
	}

	// Independent of the frame and physics contexts: a tap between network sends must survive until sampled.
	private static Context NetworkContext;
	private static ushort[] _lastNetworkKeys;

	static Input()
	{
		NetworkContext = Context.Create( "Network" );

		// We should always have a context available
		// This will just get GC'd on client as we replace it with frame/tick context
		var d = Context.Create( "Default" );
		defaultContextScope = d.Push();
	}

	// Finish the sample and discard pending taps. Held actions carry into the next sample.
	internal static void ResetNetworkInput()
	{
		NetworkContext.Flip();
		NetworkContext.PendingNetworkActions = 0;

		foreach ( var controller in Controller.All )
		{
			controller.InputContext.PendingNetworkActions = 0;
		}
	}

	/// <summary>
	/// Sample input once per outgoing command, preserving short taps for one command.
	/// </summary>
	internal static void CaptureNetworkInput( ref UserCommand command )
	{
		var actions = Actions | (CurrentController?.InputContext.PendingNetworkActions ?? 0);

		if ( CurrentPlayerScope <= 0 )
		{
			actions |= (NetworkContext.AccumActionsPressed & ~CurrentContext.ActionsCleared)
				| NetworkContext.PendingNetworkActions;
		}

		ResetNetworkInput();

		if ( Suppressed )
			return;

		if ( CurrentPlayerScope <= 0 && NetworkContext.KeysCurrent.Count > 0 )
		{
			Span<ushort> codes = stackalloc ushort[NetworkContext.KeysCurrent.Count];
			var i = 0;
			foreach ( var key in NetworkContext.KeysCurrent )
				codes[i++] = (ushort)key;

			command.SetKeys( codes, _lastNetworkKeys );
		}

		_lastNetworkKeys = command.Keys;
		command.Actions = actions;
		command.MouseDelta = NetworkContext.MouseDelta;
		command.MouseWheel = NetworkContext.MouseWheel;

		for ( var i = 0; i < UserCommand.AnalogCount; i++ )
		{
			var analog = (InputAnalog)i;
			command.SetAnalog( analog, GetAnalog( analog ) );
		}
	}

	/// <summary>
	/// If the input is suppressed then everything will act like there is no input
	/// </summary>
	public static bool Suppressed { get; set; }


	/// <summary>
	/// Allows tracking states of button changes and input deltas in a custom period (such as a tick) rather
	/// than in a per frame manner. This allows frame and tick to have legit data.
	/// </summary>
	internal class Context
	{
		public string Name { get; private set; }

		//
		// These are accumulated values. We collect these during input, then on
		// Flip we put these into the actual values and reset.
		//
		internal ulong AccumActionsPressed { get; set; }
		internal ulong AccumActionsReleased { get; set; }
		internal Vector2 AccumMouseDelta { get; set; }
		internal Vector2 AccumMouseWheel { get; set; }
		internal HashSet<ButtonCode> AccumKeysPressed { get; set; } = new();
		internal HashSet<ButtonCode> AccumKeysReleased { get; set; } = new();

		//
		// Last tick
		//
		internal ulong SavedActions { get; set; }
		internal HashSet<ButtonCode> SavedKeys { get; set; } = new();

		//
		// The input states for this context. These are accessed via
		// the Input class, when it's been pushed.
		//
		internal Vector2 MouseDelta { get; set; }
		internal Vector2 MouseWheel { get; set; }
		internal ulong ActionsCurrent { get; set; }
		internal ulong ActionsPrevious { get; set; }

		/// <summary>
		/// Actions cleared by game code in this context, overriding physical input for this sample.
		/// </summary>
		internal ulong ActionsCleared { get; set; }

		/// <summary>
		/// Presses retained for the next network sample, independent of this context's update rate.
		/// </summary>
		internal ulong PendingNetworkActions { get; set; }

		internal HashSet<ButtonCode> KeysCurrent { get; set; } = new();
		internal HashSet<ButtonCode> KeysPrevious { get; set; } = new();
		internal bool MouseCursorVisible { get; set; }

		public static Context Create( string name )
		{
			var context = new Context( name );
			contexts.Add( new( context ) );
			return context;
		}

		Context( string name )
		{
			this.Name = name;
		}

		internal void AccumulateAction( ulong mask, bool down )
		{
			if ( down )
			{
				AccumActionsPressed |= mask;
				AccumActionsReleased &= ~mask;
			}
			else
			{
				AccumActionsReleased |= mask;
			}
		}

		/// <summary>
		/// Copy accumulated values. Flip previous actions to current actions etc.
		/// </summary>
		public void Flip()
		{
			// Accumulate and reset mouse input
			MouseDelta = AccumMouseDelta;
			AccumMouseDelta = default;
			MouseWheel = AccumMouseWheel;
			AccumMouseWheel = default;

			// previous actions are current. The set leaving KeysPrevious isn't referenced anywhere
			// after this, so it becomes the next accumulation set instead of allocating one every flip.
			var recycledKeys = KeysPrevious;
			ActionsPrevious = SavedActions;
			KeysPrevious = SavedKeys;

			// Current keys are all pressed
			KeysCurrent = AccumKeysPressed;
			SavedKeys = KeysCurrent;

			// Current actions are all pressed
			ActionsCurrent = AccumActionsPressed;
			SavedActions = ActionsCurrent;
			ActionsCleared = 0;

			// Remove released actions
			AccumActionsPressed &= ~AccumActionsReleased;
			AccumActionsReleased = default;

			// Remove released keys
			if ( ReferenceEquals( recycledKeys, KeysPrevious ) || ReferenceEquals( recycledKeys, KeysCurrent ) )
				recycledKeys = new();

			recycledKeys.Clear();
			recycledKeys.UnionWith( AccumKeysPressed );
			recycledKeys.ExceptWith( AccumKeysReleased );
			AccumKeysPressed = recycledKeys;
			AccumKeysReleased.Clear();
		}

		/// <summary>
		/// Make this the current active context. You can optionally dispose the returned
		/// scope to restore back to the previous context when you're done.
		/// </summary>
		public ContextScope Push()
		{
			var oc = CurrentContext;

			CurrentContext = this;

			return new ContextScope( oc );
		}
	}

	/// <summary>
	/// Returned by <see cref="Context.Push"/>. A struct so pushing a context every frame and tick doesn't allocate.
	/// </summary>
	internal readonly struct ContextScope : IDisposable
	{
		readonly Context _previous;

		public ContextScope( Context previous )
		{
			_previous = previous;
		}

		public void Dispose()
		{
			if ( _previous is not null )
				CurrentContext = _previous;
		}
	}
}
