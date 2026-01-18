
using System.IO;
namespace Sandbox;


internal static class FileSystemStringCaseHelpers
{
	internal static string Casepath( string inPath )
	{
		if ( OperatingSystem.IsWindows() )
		{
			return inPath;
		}
		string outPath = "/";
		List<string> pathTokens = inPath.Split( Path.DirectorySeparatorChar ).ToList();
		int tokensProcessed = 0;
		foreach ( string token in pathTokens )
		{
			EnumerationOptions opts = new EnumerationOptions
			{
				MatchCasing = MatchCasing.CaseInsensitive,

			};

			string[] matches = Directory.GetFileSystemEntries( outPath, token, opts );
			if ( matches.Length == 0 )
			{
				break; //end of known path. Assemble the rest of tokens below.
			}
			else if ( matches.Length == 1 )
			{
				outPath = Path.Combine( outPath, matches[0] );
			}
			else
			{
				//What to do with those?
				throw new Exception( "Ambiguous paths detected. Base Path:  \"" + outPath + " \". Matched directories: \"" + string.Join( "\",\"", matches ) + "\"." );
			}

			tokensProcessed++;
		}

		string[] slicesToAppend = pathTokens.Slice( tokensProcessed, pathTokens.Count - tokensProcessed ).ToArray();

		foreach ( string slice in slicesToAppend )
		{
			outPath = Path.Combine( outPath, slice );
		}
		return outPath;



	}
}

