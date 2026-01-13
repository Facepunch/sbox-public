using NativeEngine;
using Sandbox.Utility;

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

	static Input()
	{
		// We should always have a context available
		// This will just get GC'd on client as we replace it with frame/tick context
		var d = Context.Create( "Default" );
		defaultContextScope = d.Push();
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

			// previous actions are current
			ActionsPrevious = SavedActions;
			KeysPrevious = SavedKeys;

			// Current keys are all pressed
			KeysCurrent = AccumKeysPressed;
			SavedKeys = KeysCurrent;

			// Current actions are all pressed
			ActionsCurrent = AccumActionsPressed;
			SavedActions = ActionsCurrent;

			// Remove released actions
			AccumActionsPressed &= ~AccumActionsReleased;
			AccumActionsReleased = default;

			// Remove released keys
			AccumKeysPressed = AccumKeysPressed.Except( AccumKeysReleased ).ToHashSet();
			AccumKeysReleased.Clear();
		}

		/// <summary>
		/// Make this the current active context. You can optionally use the returned
		/// IDisposable to restore back to the previous context when you're done.
		/// </summary>
		public IDisposable Push()
		{
			var oc = CurrentContext;

			CurrentContext = this;

			return DisposeAction.Create( () => oc?.Push() );
		}
	}
}
