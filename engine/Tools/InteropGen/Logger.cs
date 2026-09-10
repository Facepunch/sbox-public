using System;
using System.Collections.Generic;

namespace Facepunch.InteropGen;

/// <summary>
/// Console logger for the generator. Because .def files are processed on parallel threads, each thread
/// buffers its group's lines (see <see cref="Group"/>) and flushes them together in
/// <see cref="Completion"/>, so output from different files never interleaves.
/// </summary>
public static class Log
{
	private static readonly object consoleLock = new();

	private record LogMessage( ConsoleColor Color, string Text, int Indent );

	// Files are processed on separate threads. Each thread buffers its group's lines so they
	// print together at the end instead of interleaving with other threads.
	[ThreadStatic]
	private static List<LogMessage> buffer;

	[ThreadStatic]
	private static string currentGroup;

	[ThreadStatic]
	internal static int Indentation;

	public static void WriteLine( string message )
	{
		WriteLine( ConsoleColor.White, message );
	}

	public static void Warning( string message )
	{
		WriteLine( ConsoleColor.Red, message );
	}

	public static void WriteLine( ConsoleColor color, string message )
	{
		if ( buffer == null )
		{
			Print( color, message );
			return;
		}

		buffer.Add( new LogMessage( color, message, Indentation ) );
	}

	/// <summary>
	/// Start a group. Lines logged until <see cref="Completion"/> are buffered and printed together.
	/// </summary>
	public static IDisposable Group( ConsoleColor color, string groupName )
	{
		currentGroup = groupName;
		buffer = [new LogMessage( color, groupName, 0 )];

		Print( color, $"Started: {groupName}" );

		Indentation = 2;
		return new Indent();
	}

	/// <summary>
	/// Flush the current group's buffered lines followed by a green (success) or red (failure) result line.
	/// </summary>
	public static void Completion( string message, bool success )
	{
		ConsoleColor resultColor = success ? ConsoleColor.Green : ConsoleColor.Red;

		if ( buffer == null )
		{
			Print( resultColor, message );
			return;
		}

		lock ( consoleLock )
		{
			foreach ( LogMessage log in buffer )
			{
				Print( log.Color, new string( ' ', log.Indent ) + log.Text );
			}

			Print( resultColor, $"{currentGroup}: {message}" );
		}

		buffer = null;
		currentGroup = null;
		Indentation = 0;
	}

	private static void Print( ConsoleColor color, string message )
	{
		lock ( consoleLock )
		{
			ConsoleColor original = Console.ForegroundColor;
			try
			{
				Console.ForegroundColor = color;
				Console.WriteLine( message );
			}
			finally
			{
				Console.ForegroundColor = original;
			}
		}
	}

	public class Indent : IDisposable
	{
		public Indent()
		{
			Indentation += 2;
		}

		public void Dispose()
		{
			Indentation -= 2;
		}
	}
}
