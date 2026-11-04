using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal SourcePropData GetPropData()
	{
		lock ( indexLock )
		{
			if ( index.PropData is not null )
				return index.PropData;

			var catalog = new SourcePropData();
			Read( "scripts/propdata.txt" );
			foreach ( var path in index.PropDataScripts.Order( StringComparer.OrdinalIgnoreCase ) )
				Read( path );
			return index.PropData = catalog;

			void Read( string path )
			{
				if ( !TryOpenContentFile( path, out var stream ) )
					return;
				using ( stream )
				using ( var reader = new StreamReader( stream ) )
					catalog.AddScript( VmtFile.ParseKeyValues( reader.ReadToEnd(), path,
						diagnostic => throw new NotSupportedException( $"{path}: {diagnostic.Message}" ) ), path );
			}
		}
	}
}
