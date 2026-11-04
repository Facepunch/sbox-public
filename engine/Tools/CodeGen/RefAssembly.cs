using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Facepunch;

/// <summary>
/// Generated code puts a [SourceLocation( file, line )] on every member, and attributes are part of
/// the reference assembly. Adding a line to a method moves every member below it, which would make
/// everything that references the assembly compile again even though its API is the same.
/// </summary>
static class RefAssembly
{
	/// <summary>
	/// If the freshly compiled reference assembly only differs from the current one by those lines,
	/// replace it with the current one. Msbuild then sees the same MVID and leaves the reference
	/// assembly that other projects build against alone.
	/// </summary>
	public static void KeepIfOnlyLinesMoved( string compiled, string current )
	{
		if ( !File.Exists( compiled ) || !File.Exists( current ) )
			return;

		if ( !Normalized( compiled ).AsSpan().SequenceEqual( Normalized( current ) ) )
			return;

		File.Copy( current, compiled, true );

		// Still newer than the sources, or the compile won't be up to date next time
		File.SetLastWriteTimeUtc( compiled, DateTime.UtcNow );

		Console.WriteLine( "Reference assembly only moved source lines, keeping the old one" );
	}

	/// <summary>
	/// The assembly with everything that changes when only source lines move cleared out: the
	/// lines themselves, and the MVID, timestamp and checksum a deterministic build derives from
	/// the content.
	/// </summary>
	static byte[] Normalized( string path )
	{
		var bytes = File.ReadAllBytes( path );
		using var pe = new PEReader( new MemoryStream( bytes ) );
		var headers = pe.PEHeaders;
		var md = pe.GetMetadataReader();

		Clear( bytes, headers.CoffHeaderStartOffset + 4, 4 ); // TimeDateStamp
		Clear( bytes, headers.PEHeaderStartOffset + 64, 4 ); // CheckSum

		var mvid = md.GetModuleDefinition().Mvid;
		Clear( bytes, headers.MetadataStartOffset + md.GetHeapMetadataOffset( HeapIndex.Guid ) + (MetadataTokens.GetHeapOffset( mvid ) - 1) * 16, 16 );

		// Reference assemblies keep a copy of the MVID in their own section, for msbuild to read
		var mvidSection = headers.SectionHeaders.FirstOrDefault( x => x.Name == ".mvid" );
		if ( mvidSection.SizeOfRawData > 0 )
			Clear( bytes, mvidSection.PointerToRawData, 16 );

		var blobHeap = headers.MetadataStartOffset + md.GetHeapMetadataOffset( HeapIndex.Blob );

		foreach ( var handle in md.CustomAttributes )
		{
			var attribute = md.GetCustomAttribute( handle );
			if ( !IsSourceLocation( md, attribute.Constructor ) ) continue;

			// Prolog, file, line
			var reader = md.GetBlobReader( attribute.Value );
			reader.ReadUInt16();
			reader.ReadSerializedString();

			var start = blobHeap + MetadataTokens.GetHeapOffset( attribute.Value );
			var lengthSize = (bytes[start] & 0x80) == 0 ? 1 : (bytes[start] & 0xC0) == 0x80 ? 2 : 4;
			Clear( bytes, start + lengthSize + reader.Offset, 4 );
		}

		return bytes;
	}

	static bool IsSourceLocation( MetadataReader md, EntityHandle constructor )
	{
		if ( constructor.Kind != HandleKind.MemberReference )
			return false;

		var parent = md.GetMemberReference( (MemberReferenceHandle)constructor ).Parent;
		if ( parent.Kind != HandleKind.TypeReference )
			return false;

		var type = md.GetTypeReference( (TypeReferenceHandle)parent );
		return md.StringComparer.Equals( type.Namespace, "Sandbox.Internal" ) && md.StringComparer.Equals( type.Name, "SourceLocationAttribute" );
	}

	static void Clear( byte[] bytes, int offset, int length ) => Array.Clear( bytes, offset, length );
}
