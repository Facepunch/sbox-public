using System.Threading;
using Sandbox.Utility;

namespace Sandbox;

public static partial class Networking
{
	private static bool _isClosing;

	internal static void StartThread()
	{
		_isClosing = false;

		var thread = new Thread( RunThread )
		{
			Name = "Networking (managed)",
			Priority = ThreadPriority.AboveNormal
		};

		thread.Start();
	}

	internal static void StopThread()
	{
		_isClosing = true;
	}

	static readonly Lock NetworkThreadLock = new Lock();

	private static void RunThread()
	{
		try
		{
			while ( !_isClosing )
			{
				var system = System;

				if ( system is not null )
				{
					lock ( NetworkThreadLock )
					{
						system.ProcessMessagesInThread();
					}
				}

				Steam.RunCallbacks();

				Thread.Sleep( 1 );
			}
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Network Thread Error" );
		}
	}
}
