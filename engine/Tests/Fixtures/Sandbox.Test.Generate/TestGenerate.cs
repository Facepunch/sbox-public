

using System;

public static class Program
{
	public static int Main()
	{
		GenerateRazorFiles();

		return 0;
	}

	static void GenerateRazorFiles()
	{
		var files = System.IO.Directory.GetFiles( ".", "*.razor", System.IO.SearchOption.AllDirectories );

		foreach ( var file in files )
		{
			var text = System.IO.File.ReadAllText( file );
			var output = Sandbox.Razor.RazorProcessor.GenerateFromSource( text, file );

			var filename = $"{file}.cs";
			System.IO.File.WriteAllText( filename, output );

			Console.WriteLine( filename );
		}
	}
}
