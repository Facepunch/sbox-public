using Steamworks.Data;

namespace Steamworks
{
	/// <summary>
	/// Responsible for all callback/call result handling. This manually pumps Steam's message queue
	/// and dispatches those events to any waiting callbacks/call results.
	/// </summary>
	internal static class Dispatch
	{
		/// <summary>
		/// Called if an exception happens during a callback/call result. This is needed because the
		/// exception isn't always accessible when running async.. and can fail silently. With
		/// this hooked you won't be stuck wondering what happened.
		/// </summary>

		internal static void OnClientCallback( int type, IntPtr data, int dataSize, bool isServer )
		{
			try
			{
				ProcessCallback( (CallbackType)type, data, dataSize, isServer );
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}

		/// <summary>
		/// To be safe we don't call the continuation functions while iterating
		/// the Callback list. This is maybe overly safe because the only way this
		/// could be an issue is if the callback list is modified in the continuation
		/// which would only happen if starting or shutting down in the callback.
		/// </summary>
		static readonly List<Action<IntPtr>> actionsToCall = new();

		/// <summary>
		/// A callback is a general global message.
		/// </summary>
		private static void ProcessCallback( CallbackType type, IntPtr data, int dataSize, bool isServer )
		{
			// Is this a special callback telling us that the call result is ready?
			if ( type == CallbackType.SteamAPICallCompleted )
			{
				ProcessResult( type, data, dataSize );
				return;
			}

			if ( Callbacks.TryGetValue( type, out var list ) )
			{
				actionsToCall.Clear();

				foreach ( var item in list )
				{
					if ( item.isServer != isServer )
						continue;

					actionsToCall.Add( item.action );
				}

				foreach ( var action in actionsToCall )
				{
					action( data );
				}

				actionsToCall.Clear();
			}
		}

		/// <summary>
		/// Given a callback, try to turn it into a string
		/// </summary>
		internal static string CallbackToString( CallbackType type, IntPtr data, int expectedsize )
		{
			if ( !CallbackTypeFactory.All.TryGetValue( type, out var t ) )
				return $"[{type} not in sdk]";

			var strct = data.ToType( t );
			if ( strct == null )
				return "[null]";

			var str = "";

			var fields = t.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );

			if ( fields.Length == 0 )
				return "[no fields]";

			var columnSize = fields.Max( x => x.Name.Length ) + 1;

			if ( columnSize < 10 )
				columnSize = 10;

			foreach ( var field in fields )
			{
				var spaces = (columnSize - field.Name.Length);
				if ( spaces < 0 ) spaces = 0;

				str += $"{new String( ' ', spaces )}{field.Name}: {field.GetValue( strct )}\n";
			}

			return str.Trim( '\n' );
		}

		/// <summary>
		/// A result is a reply to a specific command
		/// </summary>
		private static void ProcessResult( CallbackType type, IntPtr data, int dataSize )
		{
			var result = data.ToType<SteamAPICallCompleted_t>();

			//
			// Do we have an entry added via OnCallComplete
			//
			if ( !ResultCallbacks.TryGetValue( result.AsyncCall, out var callbackInfo ) )
			{
				//
				// This can happen if the callback result was immediately available
				// so we just returned that without actually going through the callback
				// dance. It's okay for this to fail.
				//

				//
				// But still let everyone know that this happened..
				//
				//OnDebugCallback?.Invoke( (CallbackType)result.Callback, $"[no callback waiting/required]", false );
				return;
			}

			// Remove it before we do anything, incase the continuation throws exceptions
			ResultCallbacks.Remove( result.AsyncCall );

			// At this point whatever async routine called this 
			// continues running.
			callbackInfo.continuation();
		}

		struct ResultCallback
		{
			internal Action continuation;
			internal bool server;
		}

		static Dictionary<ulong, ResultCallback> ResultCallbacks = new();

		/// <summary>
		/// Watch for a Steam API call.
		/// </summary>
		internal static void OnCallComplete<T>( SteamAPICall_t call, Action continuation, bool isServer ) where T : struct, ICallbackData
		{
			ResultCallbacks[call.Value] = new ResultCallback
			{
				continuation = continuation,
				server = isServer
			};
		}

		struct Callback
		{
			internal Action<IntPtr> action;
			internal bool isServer;
		}

		static readonly Dictionary<CallbackType, List<Callback>> Callbacks = new();

		/// <summary>
		/// Install a global callback. The passed function will get called if it's all good.
		/// </summary>
		internal static void Install<T>( Action<T> p, bool isServer = false ) where T : ICallbackData
		{
			var t = default( T );
			var type = t.CallbackType;

			if ( !Callbacks.TryGetValue( type, out var list ) )
			{
				list = new List<Callback>();
				Callbacks[type] = list;
			}

			list.Add( new()
			{
				action = x => p( x.ToType<T>() ),
				isServer = isServer
			} );
		}

		internal static void ShutdownServer()
		{
			foreach ( var callback in Callbacks )
			{
				Callbacks[callback.Key].RemoveAll( x => x.isServer );
			}

			ResultCallbacks = ResultCallbacks
				.Where( x => !x.Value.server )
				.ToDictionary( x => x.Key, x => x.Value );
		}

		internal static void ShutdownClient()
		{
			foreach ( var callback in Callbacks )
			{
				Callbacks[callback.Key].RemoveAll( x => !x.isServer );
			}

			ResultCallbacks = ResultCallbacks
				.Where( x => x.Value.server )
				.ToDictionary( x => x.Key, x => x.Value );
		}
	}
}
