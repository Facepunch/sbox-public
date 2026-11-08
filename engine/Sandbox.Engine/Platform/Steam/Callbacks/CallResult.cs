using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Steamworks
{
	/// <summary>
	/// An awaitable version of a SteamAPICall_t.
	/// </summary>
	internal struct CallResult<T> : INotifyCompletion where T : struct, ICallbackData
	{
		SteamAPICall_t call;
		ISteamUtils utils;
		bool server;

		internal CallResult( SteamAPICall_t call, bool server )
		{
			this.call = call;
			this.server = server;

			utils = (server ? SteamUtils.InterfaceServer : SteamUtils.InterfaceClient) as ISteamUtils;

			if ( utils == null )
				utils = SteamUtils.Interface as ISteamUtils;
		}

		/// <summary>
		/// This gets called if IsComplete returned false on the first call.
		/// The Action "continues" the async call. We pass it to the Dispatch
		/// to be called when the callback returns.
		/// </summary>
		public void OnCompleted( Action continuation )
		{
			if ( IsCompleted )
				continuation();
			else
				Dispatch.OnCallComplete<T>( call, continuation, server );
		}

		/// <summary>
		/// Gets the result. This is called internally by the async shit.
		/// </summary>
		internal T? GetResult()
		{
			bool failed = false;
			if ( !utils.IsAPICallCompleted( call, ref failed ) || failed )
				return null;

			var t = default( T );
			var size = t.DataSize;

			// DataSize is the native size, which can be smaller than the managed struct
			// when a layout differs per platform (see LobbyCreated_t), so never read past the buffer.
			var ptr = Marshal.AllocHGlobal( Math.Max( size, Marshal.SizeOf<T>() ) );

			try
			{
				if ( !utils.GetAPICallResult( call, ptr, size, (int)t.CallbackType, ref failed ) || failed )
				{
					//Dispatch.OnDebugCallback?.Invoke( t.CallbackType, "!GetAPICallResult or failed", server );
					return null;
				}

				//Dispatch.OnDebugCallback?.Invoke( t.CallbackType, Dispatch.CallbackToString( t.CallbackType, ptr, size ), server );

				return ((T)Marshal.PtrToStructure( ptr, typeof( T ) ));
			}
			finally
			{
				Marshal.FreeHGlobal( ptr );
			}
		}

		/// <summary>
		/// Return true if complete or failed
		/// </summary>
		internal bool IsCompleted
		{
			get
			{
				bool failed = false;
				if ( utils.IsAPICallCompleted( call, ref failed ) || failed )
					return true;

				return false;
			}
		}

		/// <summary>
		/// This is what makes this struct awaitable
		/// </summary>
		internal CallResult<T> GetAwaiter()
		{
			return this;
		}
	}
}
