using Borealis.Backend.Meshlets;
using Borealis.Source2;
using Borealis.Source2.Models;
using Borealis.Source2.World;
using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-cook-meshlets out [-map ident | -map-folder folder] [-meshlets VxT]</c>: a copy of a compiled map package whose
/// meshes have the meshlets the resource compiler now cooks (<see cref="MeshletCooker"/>: each static draw's indices in
/// meshlet order and an <c>MSHL</c> block), for maps whose source isn't at hand. Every <c>maps/*.vpk</c> is written again
/// into <c>out/maps</c>, its <c>.vmesh_c</c> files cooked and the rest as they were; the package's other folders are linked
/// (junctions), not copied. Load it with <c>-map-folder out</c>.
/// <para>
/// <c>-check-meshlets [-map ident | -map-folder folder]</c>: check the meshlets cooked into a package's aggregates (by the
/// resource compiler or the above): each range's records a run over all its triangles, no meshlet over the config, each box
/// its vertices' exact bounds; and count the ranges a build at load from the stored order would cut the same.
/// </para>
/// </summary>
internal static class MeshletCook
{
	public static bool Requested => Args.String( "-cook-meshlets" ) is not null || Args.Has( "-check-meshlets" );

	public static int Run()
	{
		if ( Args.Has( "-check-meshlets" ) ) return Check();

		var output = Path.GetFullPath( Args.String( "-cook-meshlets" ) );
		var ident = Args.String( "-map" ) ?? "facepunch.construct";
		var folder = Args.String( "-map-folder" ) ?? MapRun.FindPackage( ident );
		if ( folder is null || !Directory.Exists( Path.Combine( folder, "maps" ) ) )
		{
			Console.Error.WriteLine( $"{ident} isn't installed: no workshop item with that package ident in any Steam library." );
			return 2;
		}

		folder = Path.GetFullPath( folder );
		if ( string.Equals( folder.TrimEnd( '\\', '/' ), output.TrimEnd( '\\', '/' ), StringComparison.OrdinalIgnoreCase ) )
		{
			Console.Error.WriteLine( "The output can't be the package itself." );
			return 2;
		}

		if ( !MeshOptimizer.Available )
		{
			Console.Error.WriteLine( "meshopt isn't built" );
			return 2;
		}

		var config = MeshletConfig.Parse( Args.String( "-meshlets" ) );
		Console.WriteLine( $"Cooking meshlets ({config}) into a copy of {folder} at {output}" );
		Directory.CreateDirectory( Path.Combine( output, "maps" ) );

		// The package's other folders and files, linked or copied as they are
		foreach ( var entry in Directory.GetFileSystemEntries( folder ) )
		{
			var name = Path.GetFileName( entry );
			if ( name.Equals( "maps", StringComparison.OrdinalIgnoreCase ) ) continue;
			var target = Path.Combine( output, name );
			if ( Path.Exists( target ) ) continue;
			if ( File.Exists( entry ) ) File.Copy( entry, target );
			else Process.Start( new ProcessStartInfo( "cmd.exe", ["/c", "mklink", "/J", target, entry] ) { UseShellExecute = false, RedirectStandardOutput = true } ).WaitForExit();
		}

		var cut = MeshletBuilder.Cut( config );
		foreach ( var vpk in Directory.GetFiles( Path.Combine( folder, "maps" ), "*.vpk", SearchOption.AllDirectories ) )
		{
			var timer = Stopwatch.StartNew();
			using var package = VpkFile.Open( vpk );
			var files = package.Files.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).ToArray();
			var contents = new byte[files.Length][];
			var relative = Path.GetRelativePath( Path.Combine( folder, "maps" ), vpk );
			var fragments = AggregateMeshes( package, Path.ChangeExtension( relative, null ).Replace( '\\', '/' ) );
			int cooked = 0, meshes = 0;
			Parallel.For( 0, files.Length, i =>
			{
				var data = package.Read( files[i] );
				if ( fragments.Contains( files[i] ) )
				{
					Interlocked.Increment( ref meshes );
					try
					{
						if ( MeshletCooker.CookMesh( data, config.MaxVertices, config.MaxTriangles, cut ) is { } made )
						{
							data = made;
							Interlocked.Increment( ref cooked );
						}
					}
					catch ( Exception e )
					{
						Console.Error.WriteLine( $"{files[i]}: {e.Message}" );
					}
				}

				contents[i] = data;
			} );

			var written = Path.Combine( output, "maps", relative );
			Directory.CreateDirectory( Path.GetDirectoryName( written ) );
			Write( written, files, contents );
			Console.WriteLine( $"{relative}: {cooked} of {meshes} aggregate meshes cooked, {files.Length} files, {new FileInfo( written ).Length / 1048576.0:0} MiB ({timer.Elapsed.TotalSeconds:0.0} s)" );
		}

		return 0;
	}

	static int Check()
	{
		var ident = Args.String( "-map" ) ?? "facepunch.construct";
		var folder = Args.String( "-map-folder" ) ?? MapRun.FindPackage( ident );
		if ( folder is null || !Directory.Exists( Path.Combine( folder, "maps" ) ) || !MeshOptimizer.Available )
		{
			Console.Error.WriteLine( "No package, or meshopt isn't built" );
			return 2;
		}

		var config = MeshletConfig.Parse( Args.String( "-meshlets" ) );
		int ranges = 0, cooked = 0, bad = 0, rebuiltSame = 0;
		foreach ( var vpk in Directory.GetFiles( Path.Combine( folder, "maps" ), "*.vpk", SearchOption.AllDirectories ) )
		{
			using var package = VpkFile.Open( vpk );
			var content = new ContentFiles();
			content.AddPackage( package );
			var name = Path.ChangeExtension( Path.GetRelativePath( Path.Combine( folder, "maps" ), vpk ), null ).Replace( '\\', '/' );
			if ( content.TryRead( $"maps/{name}/world.vwrld" ) is null ) continue;

			var world = WorldFile.Load( content, $"maps/{name}/world.vwrld" );
			foreach ( var path in world.Nodes.SelectMany( n => n.Aggregates ).Select( a => a.Model ).Where( m => !string.IsNullOrEmpty( m ) ).Distinct( StringComparer.OrdinalIgnoreCase ) )
			{
				if ( content.TryRead( path ) is not { } data ) continue;
				foreach ( var mesh in ModelFile.Read( data, content.TryRead ).Meshes )
				{
					foreach ( var draw in mesh.DrawCalls )
					{
						if ( draw.VertexBuffers.Count == 0 || draw.IndexCount < 3 ) continue;
						ranges++;
						var (vbIndex, vbOffset) = draw.VertexBuffers[0];
						var (ibIndex, ibOffset) = draw.IndexBuffer;
						var vb = mesh.VertexBuffers[vbIndex];
						var ib = mesh.IndexBuffers[ibIndex];
						var start = ibOffset / ib.Size + draw.StartIndex;
						var baseVertex = vbOffset / vb.Stride + draw.BaseVertex;
						var records = ib.Meshlets?.Find( start, draw.IndexCount, vb, baseVertex ) ?? default;
						if ( records.IsEmpty || vb.Find( "POSITION" ) is not { } position ) continue;
						cooked++;

						var indices = Enumerable.Range( start, draw.IndexCount ).Select( k => ib[k] ).ToArray();
						System.Numerics.Vector3 At( int v ) { var p = vb.Read( position, v ); return new System.Numerics.Vector3( p.X, p.Y, p.Z ); }
						var set = MeshletSet.FromCooked( indices, records.Span, config );
						var good = set is not null;
						foreach ( var meshlet in set?.Meshlets ?? [] )
						{
							var lo = new System.Numerics.Vector3( float.MaxValue );
							var hi = new System.Numerics.Vector3( float.MinValue );
							var vertices = new HashSet<uint>();
							for ( var k = meshlet.FirstTriangle * 3; k < (meshlet.FirstTriangle + meshlet.TriangleCount) * 3; k++ )
							{
								vertices.Add( indices[k] );
								lo = System.Numerics.Vector3.Min( lo, At( baseVertex + (int)indices[k] ) );
								hi = System.Numerics.Vector3.Max( hi, At( baseVertex + (int)indices[k] ) );
							}

							good &= vertices.Count <= config.MaxVertices && meshlet.Center == (lo + hi) * 0.5f && meshlet.Extents == (hi - lo) * 0.5f;
						}

						if ( !good ) bad++;
						if ( MeshletBuilder.Build( indices, At, baseVertex, config ) is { } rebuilt && rebuilt.Indices.AsSpan().SequenceEqual( indices )
							&& System.Runtime.InteropServices.MemoryMarshal.AsBytes( rebuilt.Meshlets.AsSpan() ).SequenceEqual( records.Span ) ) rebuiltSame++;
					}
				}
			}
		}

		Console.WriteLine( $"{folder}: {ranges} aggregate draws, {cooked} cooked, {bad} bad, {rebuiltSame} that a build at load from the stored order cuts the same" );
		return bad == 0 ? 0 : 1;
	}

	/// <summary>
	/// The compiled meshes (<c>.vmesh_c</c>) of the world's aggregates' models: what the compiler cooks, as every draw of
	/// them is a fragment, a range Borealis reorders at load anyway. Scene objects' meshes (overlays, props) keep their order.
	/// </summary>
	static HashSet<string> AggregateMeshes( VpkFile package, string name )
	{
		var meshes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var content = new ContentFiles();
		content.AddPackage( package );
		if ( content.TryRead( $"maps/{name}/world.vwrld" ) is null ) return meshes;

		var world = WorldFile.Load( content, $"maps/{name}/world.vwrld" );
		foreach ( var model in world.Nodes.SelectMany( n => n.Aggregates ).Select( a => a.Model ).Where( m => !string.IsNullOrEmpty( m ) ).Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			if ( content.TryRead( model ) is not { } data ) continue;
			ModelFile.Read( data, path =>
			{
				meshes.Add( (path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? path : path + "_c").Replace( '\\', '/' ) );
				return content.TryRead( path );
			} );
		}

		return meshes;
	}

	/// <summary>A single-file VPK (version 2): the directory tree, then every file's bytes after it.</summary>
	static void Write( string path, string[] files, byte[][] contents )
	{
		var tree = new MemoryStream();
		void Text( string s )
		{
			tree.Write( Encoding.UTF8.GetBytes( s ) );
			tree.WriteByte( 0 );
		}

		var order = new List<int>();
		var groups = Enumerable.Range( 0, files.Length )
			.Select( i => (Index: i, Extension: Path.GetExtension( files[i] ).TrimStart( '.' ), Folder: Path.GetDirectoryName( files[i] )?.Replace( '\\', '/' ) is { Length: > 0 } f ? f : " ", Name: Path.GetFileNameWithoutExtension( files[i] )) )
			.GroupBy( x => x.Extension, StringComparer.OrdinalIgnoreCase );

		Span<byte> entry = stackalloc byte[18];
		uint offset = 0;
		foreach ( var extension in groups )
		{
			Text( extension.Key );
			foreach ( var inFolder in extension.GroupBy( x => x.Folder, StringComparer.OrdinalIgnoreCase ) )
			{
				Text( inFolder.Key );
				foreach ( var file in inFolder )
				{
					Text( file.Name );
					var length = (uint)contents[file.Index].Length;
					BinaryPrimitives.WriteUInt32LittleEndian( entry, 0 );
					BinaryPrimitives.WriteUInt16LittleEndian( entry[4..], 0 );
					BinaryPrimitives.WriteUInt16LittleEndian( entry[6..], 0x7FFF );
					BinaryPrimitives.WriteUInt32LittleEndian( entry[8..], offset );
					BinaryPrimitives.WriteUInt32LittleEndian( entry[12..], length );
					BinaryPrimitives.WriteUInt16LittleEndian( entry[16..], 0xFFFF );
					tree.Write( entry );
					order.Add( file.Index );
					offset += length;
				}

				Text( "" );
			}

			Text( "" );
		}

		Text( "" );

		using var stream = File.Create( path );
		Span<byte> header = stackalloc byte[28];
		BinaryPrimitives.WriteUInt32LittleEndian( header, 0x55AA1234 );
		BinaryPrimitives.WriteUInt32LittleEndian( header[4..], 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( header[8..], (uint)tree.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( header[12..], offset );
		stream.Write( header );
		tree.Position = 0;
		tree.CopyTo( stream );
		foreach ( var index in order ) stream.Write( contents[index] );
	}
}
