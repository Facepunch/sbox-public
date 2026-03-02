using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Diagnostics;

/// <summary>
/// The dedicated server's console status bar. It draws some timings and the player count, which is updated every time a log comes in.
/// </summary>
internal class DedicatedServerConsole
{
	ConsoleInput input;
	Stopwatch uptime;

	public DedicatedServerConsole()
	{
		input = new ConsoleInput();
		input.OnInputText += OnInput;

		uptime = Stopwatch.StartNew();
	}


	RealTimeSince timeSinceUpdate = 10;

	void UpdateStatus()
	{
		if ( timeSinceUpdate < 0.5f ) return;
		timeSinceUpdate = 0;

		var uptimeString = $"{uptime.Elapsed.TotalHours:n0}:{uptime.Elapsed.ToString( "mm\\:ss" )}";

		int width;
		try
		{
			width = Console.BufferWidth - 1;
		}
		catch
		{
			// Fall back to a default width
			width = 80;
		}

		var name = string.IsNullOrEmpty( Networking.ServerName ) ? "s&box server" : Networking.ServerName;
		var maxPlayers = Application.GamePackage?.GetCachedMeta( "MaxPlayers", 32 ) ?? 32;
		var topLeft = $"{name} ({Connection.All.Count}/{maxPlayers}) [{uptimeString}]";
		var topRight = $"Network {PerformanceStats.Timings.Network.AverageMs( 1 ):0.00}ms";

		var bottomLeft = $"Physics {PerformanceStats.Timings.Physics.AverageMs( 1 ):0.00}ms,";
		bottomLeft += $" NavMesh {PerformanceStats.Timings.NavMesh.AverageMs( 1 ):0.00}ms,";
		bottomLeft += $" Animation {PerformanceStats.Timings.Animation.AverageMs( 1 ):0.00}ms";

		var bottomRight = $"Update {PerformanceStats.Timings.Update.AverageMs( 60 ):0.00}ms";

		var lineA = topRight.PadLeft( width );
		lineA = topLeft + ((topLeft.Length < lineA.Length) ? lineA.Substring( topLeft.Length ) : "");

		var lineB = bottomRight.PadLeft( width );
		lineB = bottomLeft + ((bottomLeft.Length < lineB.Length) ? lineB.Substring( bottomLeft.Length ) : "");

		input.SetStatus( 1, lineA );
		input.SetStatus( 2, lineB );
	}

	public void Clear()
	{
		var width = Console.BufferWidth - 1;
		var height = Console.BufferHeight;
		var outputEnd = height - 4;
		var (x, y) = Console.GetCursorPosition();

		if ( y >= outputEnd )
		{
			var fillSize = (width - x) + (width * (height - outputEnd - 1));
			Span<char> whitespace = stackalloc char[fillSize];
			whitespace.Fill( ' ' );

			Console.Out.Write( whitespace );
			Console.SetCursorPosition( x, y );
		}
	}

	public void Update()
	{
		UpdateStatus();
		input.Update();
	}

	void OnInput( string input )
	{
		if ( input == "clear" )
		{
			Console.Clear();
			return;
		}

		ConVarSystem.Run( input );
	}
}
