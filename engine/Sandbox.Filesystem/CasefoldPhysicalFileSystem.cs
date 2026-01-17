using Zio;
using Zio.FileSystems;
using System.IO;
using System.Drawing;
using System.Reflection;
namespace Sandbox;


internal class CasefoldPhysicalFileSystem : PhysicalFileSystem
{

    /// <summary>
    /// An adaptation of POSIX implementation of casepath in C# world.
    /// WARNING: Handles only absolute paths. Also assumes that it's run under Linux or MacOS.
    /// </summary>
    /// <param name="inPath">path as fed through the parameter</param>
    /// <returns>the same path with proper casing. This is just partially adjusted if only part of the path exists.</returns>
    private UPath casepath( UPath inPath)
    {
        Assert.False(OperatingSystem.IsWindows());
        UPath outPath = new UPath("/");
        List<string> pathTokens = inPath.Split();
        int tokensProcessed =0;
        foreach (string token in pathTokens )
        {
            EnumerationOptions opts = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,

            };
            
            string[] matches = Directory.GetFileSystemEntries(ConvertPathToInternalImpl(outPath),token,opts);
            if(matches.Length ==0)
            {
                break; //end of known path. Assemble the rest of tokens below.
            } else if (matches.Length ==1)
            {
                outPath = UPath.Combine(outPath,matches[0]);
            } else
            {
                //What to do with those?
                throw new Exception("Ambiguous paths detected. Base Path:  \""+outPath+" \". Matched directories: \""+string.Join("\",\"",matches)+"\".");
            }

            tokensProcessed++;
        }
        
        string[] slicesToAppend = pathTokens.Slice(tokensProcessed,pathTokens.Count-tokensProcessed).ToArray();

        foreach (string slice in slicesToAppend)
        {
            outPath = UPath.Combine(outPath,slice);
        }
        return outPath;



    }

	//directory API

	protected override void CreateDirectoryImpl( UPath path )
	{
		base.CreateDirectoryImpl( casepath(path) );
	}

	protected override bool DirectoryExistsImpl( UPath path )
	{
		return base.DirectoryExistsImpl( casepath(path) );
	}


	protected override bool CanWatchImpl( UPath path )
	{
        return base.CanWatchImpl(casepath(path));
    }

}