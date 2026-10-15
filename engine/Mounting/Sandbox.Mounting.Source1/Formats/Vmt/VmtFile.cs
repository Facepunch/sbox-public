using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

/// <summary>
/// Reads material KeyValues without flattening shader fallbacks and proxy blocks into parameters.
/// </summary>
internal sealed partial class VmtFile
{
	public string ShaderName => Root.Name;
	public IReadOnlyDictionary<string, string> Parameters => Root.Values;
	public VmtNode Root => Roots[0];
	public IReadOnlyList<VmtNode> Roots { get; }
	public IReadOnlyList<VmtDiagnostic> Diagnostics { get; }

	private VmtFile( IReadOnlyList<VmtNode> roots, List<VmtDiagnostic> diagnostics )
	{
		Roots = roots;
		Diagnostics = diagnostics.AsReadOnly();
	}

	public static VmtFile Load( string path, Func<string, Stream> openFile )
	{
		return Load( path, openFile, null );
	}

	/// <summary>
	/// Parses a standalone KeyValues document without material path or patch semantics.
	/// File inclusion requires a filesystem-aware loader and is rejected here.
	/// </summary>
	public static IReadOnlyList<VmtNode> ParseKeyValues( string text, string sourceName,
		Action<VmtDiagnostic> reportDiagnostic = null )
	{
		var context = new LoadContext( _ => throw new NotSupportedException( "Standalone KeyValues file inclusion is not supported." ),
			null, reportDiagnostic );
		var parser = new Parser( text, sourceName, context );
		var roots = parser.ReadDocument();
		if ( parser.Includes.Count > 0 || parser.Bases.Count > 0 )
			throw new NotSupportedException( $"{sourceName}: standalone KeyValues file inclusion is not supported." );
		return roots.AsReadOnly();
	}

	/// <summary>
	/// Loads for a Windows PC, DX90 target. The optional resolver receives symbols without '$';
	/// returning null uses the built-in target defaults. Unknown conditions produce diagnostics,
	/// and entries whose conditions cannot be determined are excluded. Material key prefixes
	/// such as "360?" use the same resolver; renderer predicates such as "srgb" need caller input.
	/// </summary>
	public static VmtFile Load( string path, Func<string, Stream> openFile,
		Func<string, bool?> resolveCondition, Action<VmtDiagnostic> reportDiagnostic = null )
	{
		ArgumentNullException.ThrowIfNull( openFile );
		var context = new LoadContext( openFile, resolveCondition, reportDiagnostic );
		var roots = LoadRoots( NormalizePath( path, ".vmt" ), context );
		if ( roots.Count == 0 )
			throw new InvalidDataException( $"VMT has no active shader root: {path}" );
		return new VmtFile( roots, context.Diagnostics );
	}

	private static IReadOnlyList<VmtNode> LoadRoots( string path, LoadContext context )
	{
		if ( context.Cache.TryGetValue( path, out var cached ) )
			return cached;
		if ( context.Active.Count >= 32 || !context.Active.Add( path ) )
			throw new InvalidDataException( $"Cyclic or excessively nested VMT include: {path}" );
		try
		{
			if ( ++context.FileCount > 256 )
				throw new InvalidDataException( "VMT dependency count exceeds 256 files." );
			using var stream = context.OpenFile( path );
			using var reader = new StreamReader( stream );
			var parser = new Parser( reader.ReadToEnd(), path, context );
			var roots = parser.ReadDocument();
			// Native KeyValues appends #include roots after local roots, regardless of directive position.
			foreach ( var include in parser.Includes )
				roots.AddRange( LoadRoots( ResolveDirectivePath( path, include ), context ) );
			foreach ( var basePath in parser.Bases )
			{
				var bases = LoadRoots( ResolveDirectivePath( path, basePath ), context );
				if ( roots.Count != 0 && bases.Count != 0 )
					roots[0] = Merge( roots[0], bases[0], replace: false );
			}

			if ( roots.Count != 0 && roots[0].Name.Equals( "patch", StringComparison.OrdinalIgnoreCase ) )
			{
				var root = roots[0];
				if ( !root.Values.TryGetValue( "include", out var include ) || string.IsNullOrWhiteSpace( include ) )
					throw new InvalidDataException( $"VMT patch has no include: {path}" );
				var source = LoadRoots( NormalizePath( include, ".vmt" ), context );
				if ( source.Count == 0 )
					throw new InvalidDataException( $"VMT patch include has no active shader root: {path}" );
				var merged = source[0];
				if ( root.GetBlock( "insert" ) is { } insert )
					merged = Merge( merged, insert, replace: false );
				if ( root.GetBlock( "replace" ) is { } replace )
					merged = Merge( merged, replace, replace: true );
				roots[0] = merged;
				roots.InsertRange( 1, GetSiblingRoots( source ) );
			}
			var result = roots.AsReadOnly();
			context.Cache.Add( path, result );
			return result;
		}
		finally
		{
			context.Active.Remove( path );
		}
	}

	private static IEnumerable<VmtNode> GetSiblingRoots( IReadOnlyList<VmtNode> roots )
	{
		for ( var i = 1; i < roots.Count; i++ )
			yield return roots[i];
	}

	private static VmtNode Merge( VmtNode target, VmtNode changes, bool replace )
	{
		var children = new List<VmtNode>( target.Children );
		foreach ( var change in changes.Children )
		{
			var index = change.IsBlock
				? children.FindIndex( child => child.IsBlock && child.Name.Equals( change.Name, StringComparison.OrdinalIgnoreCase ) )
				: children.FindLastIndex( child => !child.IsBlock && child.Name.Equals( change.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( index < 0 )
			{
				if ( !replace ) children.Add( change );
				continue;
			}
			if ( children[index].IsBlock && change.IsBlock )
				children[index] = Merge( children[index], change, replace );
			else if ( replace )
				children[index] = change;
		}
		return new VmtNode( target.Name, target.Value, children );
	}

	private static string ResolveDirectivePath( string source, string reference )
	{
		if ( string.IsNullOrWhiteSpace( reference ) )
			throw new InvalidDataException( "Empty VMT directive path." );
		reference = reference.Replace( '\\', '/' );
		if ( reference.StartsWith( '/' ) || reference.Contains( ':' ) || reference.Contains( '\0' ) )
			throw new InvalidDataException( $"VMT directive path must be relative: {reference}" );
		var path = source[..(source.LastIndexOf( '/' ) + 1)] + reference;
		var segments = new List<string>();
		foreach ( var segment in path.Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( segment == ".." )
			{
				if ( segments.Count <= 1 )
					throw new InvalidDataException( $"VMT directive escapes the materials directory: {reference}" );
				segments.RemoveAt( segments.Count - 1 );
			}
			else if ( segment != "." )
			{
				segments.Add( segment );
			}
		}
		if ( segments.Count < 2 )
			throw new InvalidDataException( $"VMT directive does not name a file: {reference}" );
		return string.Join( '/', segments ).ToLowerInvariant();
	}

	public static string NormalizePath( string path, string extension )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidDataException( "Empty VMT resource path." );
		path = path.Trim().Replace( '\\', '/' ).TrimStart( '/' );
		if ( path.Contains( ':' ) || path.Contains( '\0' ) )
			throw new InvalidDataException( $"VMT resource path must be mount-relative: {path}" );
		var segments = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var normalized = new List<string>( segments.Length );
		foreach ( var segment in segments )
		{
			if ( segment == ".." )
			{
				if ( normalized.Count == 0 || (normalized.Count == 1 && normalized[0].Equals( "materials", StringComparison.OrdinalIgnoreCase )) )
					throw new InvalidDataException( $"VMT resource path traverses its mount: {path}" );
				normalized.RemoveAt( normalized.Count - 1 );
			}
			else if ( segment != "." )
				normalized.Add( segment );
		}
		if ( normalized.Count == 1 && normalized[0].Equals( "materials", StringComparison.OrdinalIgnoreCase ) && path.Contains( '/' ) )
			throw new InvalidDataException( "Empty VMT resource path." );
		path = string.Join( '/', normalized ).ToLowerInvariant();
		if ( !path.StartsWith( "materials/", StringComparison.Ordinal ) )
			path = "materials/" + path;
		if ( path == "materials/" )
			throw new InvalidDataException( "Empty VMT resource path." );
		if ( extension.Equals( ".vtf", StringComparison.OrdinalIgnoreCase ) )
		{
			var dot = path.LastIndexOf( '.' );
			if ( dot > path.LastIndexOf( '/' ) && !path.EndsWith( ".hdr", StringComparison.OrdinalIgnoreCase ) )
				path = path[..dot];
			path += extension;
		}
		else if ( !path.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
			path += extension;
		return path;
	}

	private sealed class LoadContext( Func<string, Stream> openFile, Func<string, bool?> resolveCondition,
		Action<VmtDiagnostic> reportDiagnostic )
	{
		public Func<string, Stream> OpenFile { get; } = openFile;
		public HashSet<string> Active { get; } = new( StringComparer.OrdinalIgnoreCase );
		public Dictionary<string, IReadOnlyList<VmtNode>> Cache { get; } = new( StringComparer.OrdinalIgnoreCase );
		public List<VmtDiagnostic> Diagnostics { get; } = new();
		public int FileCount;

		public void Report( string path, int line, VmtDiagnosticKind kind, string message )
		{
			var diagnostic = new VmtDiagnostic( path, line, kind, message );
			Diagnostics.Add( diagnostic );
			reportDiagnostic?.Invoke( diagnostic );
		}

		public bool? Resolve( string symbol )
		{
			return resolveCondition?.Invoke( symbol ) ?? symbol.ToUpperInvariant() switch
			{
				"WIN32" or "WINDOWS" or "PC" or "DX90" => true,
				"WIN64" => Environment.Is64BitProcess,
				"360" or "X360" or "XBOX" or "XBOX360" or "PS3" or "GAMECONSOLE" or "OSX" or "LINUX" or "POSIX"
					or "WINXP" or "DX60" or "DX70" or "DX80" or "DX81" or "DX95" => false,
				_ => null
			};
		}
	}
}
