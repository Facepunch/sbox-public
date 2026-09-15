using System;
using System.Collections.Generic;
using System.IO;

namespace Facepunch.ShaderProc
{
	public class Program
	{
		public static void Process( string directory, Action<string> log = null )
		{
			var defPath = Path.Combine( directory, "Definitions", "shaders.def" );
			var defs = Definition.FromFile( defPath );
			if ( defs is null )
				throw new Exception( "Definition files was not found!" );

			// TODO This is scuffed shaderdir and InteropOutput handling and should be improved
			var shaderDir = Path.Combine( directory, defs.ShaderDir );
			var shaders = ProcessFolder( shaderDir, defs );
			var cppShaders = new CppPacker( shaders );

			var outPutDir = Path.Combine( directory, defs.InteropOutput );
			WriteOutputToFile( outPutDir, cppShaders.ToString() );

			(log ?? Console.WriteLine)( $"Packed {shaders.Count} shader source files to {outPutDir}" );
		}

		static List<ShaderCode> ProcessFolder( string directory, Definition defs )
		{
			List<ShaderCode> shaderList = new List<ShaderCode>();

			var Folder = new System.IO.DirectoryInfo( directory );

			foreach ( DirectoryInfo folder in Folder.GetDirectories() )
			{
				// Todo: Better way to join these?
				var folderShaders = ProcessFolder( folder.FullName, defs );
				foreach ( var shader in folderShaders )
					shaderList.Add( shader );
			}

			foreach ( FileInfo file in Folder.GetFiles() )
			{
				if ( defs.Whitelist.Count > 0 && !defs.Whitelist.Contains( file.Name ) )
					continue;

				if ( file.Extension != ".fxc" )
					continue;

				shaderList.Add( MinifyShader( file ) );
			}

			return shaderList;
		}

		static ShaderCode MinifyShader( FileInfo file )
		{
			var name = file.Name;
			var content = file.OpenText().ReadToEnd();
			var shader = new ShaderCode( name, content );
			return shader.Minify();
		}

		static bool WriteOutputToFile( string filepath, string buffer )
		{
			// Don't rewrite if contents are equal
			if ( File.Exists( filepath ) )
			{
				if ( File.ReadAllText( filepath ) == buffer )
					return true;
			}

			File.WriteAllText( filepath, buffer );
			return true;
		}
	}
}
