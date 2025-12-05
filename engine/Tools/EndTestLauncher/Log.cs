namespace Facepunch.EndTestLauncher;

public static class Log
{
	public static void Info( string message )
	{
		Console.WriteLine( message );
	}

	public static void Warning( string message )
	{
		var colorBefore = Console.ForegroundColor;
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine( message );
		Console.ForegroundColor = colorBefore;
	}

	public static void Error( string message )
	{
		var colored = Console.ForegroundColor;
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine( message );
		Console.ForegroundColor = colored;
	}

	public static void Header( string message )
	{
		var messageWithHeader = $"========== {message} ========== ";
		Console.WriteLine();
		Console.WriteLine( messageWithHeader );
	}
}
