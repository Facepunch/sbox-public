using System;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VtxFile
{
	private readonly float[] _lodSwitchPoints;
	private readonly Dictionary<int, string>[] _materialReplacements;

	public int RenderLodCount => NumLods - (_lodSwitchPoints[^1] < 0 ? 1 : 0);

	public float GetLodSwitchPoint( int lod ) => _lodSwitchPoints[lod];

	public string GetMaterialReplacement( int lod, int material )
	{
		return _materialReplacements[lod]?.GetValueOrDefault( material );
	}

	private Dictionary<int, string>[] ReadMaterialReplacements( Stream stream, int offset, int fileLodCount, int materialCount )
	{
		LumpReader<MaterialReplacementListHeader>.ValidateRange( stream, offset, fileLodCount );
		stream.Position = offset;
		var replacements = new Dictionary<int, string>[NumLods];
		LumpReader<MaterialReplacementListHeader>.ReadLumpFromStream( stream, NumLods, ( lod, header ) =>
		{
			if ( header.NumReplacements < 0 || header.NumReplacements > materialCount )
				throw new InvalidDataException( "Invalid VTX material replacement count." );
			if ( header.NumReplacements == 0 )
				return;

			stream.Seek( header.ReplacementOffset, SeekOrigin.Current );
			var materials = replacements[lod] = new Dictionary<int, string>( header.NumReplacements );
			LumpReader<MaterialReplacementHeader>.ReadLumpFromStream( stream, header.NumReplacements, replacement =>
			{
				if ( replacement.MaterialId < 0 || replacement.MaterialId >= materialCount )
					throw new InvalidDataException( "VTX material replacement references an invalid material." );
				var nameOffset = stream.Position + replacement.ReplacementMaterialNameOffset;
				LumpReader<byte>.ValidateRange( stream, nameOffset, 1 );
				stream.Position = nameOffset;
				using var bytes = new MemoryStream();
				int value;
				while ( (value = stream.ReadByte()) > 0 )
					bytes.WriteByte( (byte)value );
				if ( value < 0 || bytes.Length == 0 )
					throw new InvalidDataException( "Invalid VTX material replacement name." );
				var name = Encoding.UTF8.GetString( bytes.GetBuffer(), 0, (int)bytes.Length ).Replace( '\\', '/' );
				if ( !name.EndsWith( ".vmt", StringComparison.OrdinalIgnoreCase ) )
					name += ".vmt";
				if ( !materials.TryAdd( replacement.MaterialId, name ) )
					throw new InvalidDataException( "Duplicate VTX material replacement." );
			} );
		} );
		return replacements;
	}
}
