using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;

namespace Sandbox.Diagnostics;

/// <summary>
/// The stack of a log entry, captured without source lines because finding them is the slow part. The first time it's
/// read it's formatted like <see cref="StackTrace.ToString()"/> with them, from the assemblies' portable pdbs.
/// </summary>
[SkipHotload]
internal sealed class LoggedStack
{
	StackTrace _trace;
	string _text;

	// Until formatted, a stack holds the methods on it, which keeps a game's assemblies alive after they unload
	static readonly ConditionalWeakTable<LoggedStack, object> Unformatted = new();
	static readonly ConditionalWeakTable<Assembly, MetadataReaderProvider> Symbols = new();

	internal LoggedStack( StackTrace trace )
	{
		_trace = trace;
		Unformatted.AddOrUpdate( this, null );
	}

	public override string ToString()
	{
		if ( Volatile.Read( ref _trace ) is { } trace )
		{
			_text = Format( trace );
			Volatile.Write( ref _trace, null );
			Unformatted.Remove( this );
		}

		return _text;
	}

	/// <summary>
	/// Called when a game's assemblies unload: format every stack that references collectible assemblies, and stop
	/// tracking the ones that don't, so each stack is only checked once.
	/// </summary>
	internal static void FormatCollectibleStacks( AssemblyLoadContext context )
	{
		foreach ( var stack in Unformatted.Select( x => x.Key ).ToArray() )
		{
			if ( stack._trace?.GetFrames().Any( x => x.GetMethod()?.Module.Assembly.IsCollectible == true ) == true )
				stack.ToString();
			else
				Unformatted.Remove( stack );
		}
	}

	/// <summary>
	/// Portable pdb of an assembly that was loaded from memory, so has no file to find it next to.
	/// </summary>
	internal static void AddSymbols( Assembly assembly, byte[] portablePdb )
	{
		Symbols.AddOrUpdate( assembly, MetadataReaderProvider.FromPortablePdbImage( ImmutableCollectionsMarshal.AsImmutableArray( portablePdb ) ) );
	}

	static string Format( StackTrace trace )
	{
		var text = trace.ToString();
		var lines = text.Split( Environment.NewLine );

		// ToString writes one line per shown frame (it never hides the last one), and a newline after the last line
		var frames = trace.GetFrames();
		var shown = frames.Where( ( frame, i ) => frame.GetMethod() is { } method && (IsShown( method ) || i == frames.Length - 1) ).ToArray();
		if ( shown.Length != lines.Length - 1 )
			return text;

		var sb = new StringBuilder( text.Length * 2 );

		for ( var i = 0; i < shown.Length; i++ )
		{
			sb.Append( lines[i] );

			if ( TryGetSourceLine( shown[i], out var file, out var line ) )
				sb.Append( " in " ).Append( file ).Append( ":line " ).Append( line );

			sb.Append( Environment.NewLine );
		}

		return sb.ToString();
	}

	// Mirrors StackTrace.ShowInStackTrace
	static bool IsShown( MethodBase method )
	{
		if ( (method.MethodImplementationFlags & MethodImplAttributes.AggressiveInlining) != 0 )
			return false;

		try
		{
			return !method.IsDefined( typeof( StackTraceHiddenAttribute ), false )
				&& method.DeclaringType?.IsDefined( typeof( StackTraceHiddenAttribute ), false ) != true;
		}
		catch ( Exception )
		{
			return true;
		}
	}

	// Same lookup as the runtime's: the last visible sequence point at or before the frame's IL offset
	static bool TryGetSourceLine( StackFrame frame, out string file, out int line )
	{
		file = null;
		line = 0;

		try
		{
			var offset = frame.GetILOffset();
			if ( frame.GetMethod() is not { } method || offset < 0 )
				return false;

			var reader = Symbols.GetValue( method.Module.Assembly, OpenSymbols )?.GetMetadataReader();
			var handle = MetadataTokens.EntityHandle( method.MetadataToken );
			if ( reader is null || handle.Kind != HandleKind.MethodDefinition )
				return false;

			SequencePoint? found = null;
			foreach ( var point in reader.GetMethodDebugInformation( (MethodDefinitionHandle)handle ).GetSequencePoints() )
			{
				if ( point.Offset > offset ) break;
				if ( !point.IsHidden ) found = point;
			}

			if ( found is not { } sequencePoint )
				return false;

			file = reader.GetString( reader.GetDocument( sequencePoint.Document ).Name );
			line = sequencePoint.StartLine;
			return true;
		}
		catch ( Exception )
		{
			return false;
		}
	}

	static MetadataReaderProvider OpenSymbols( Assembly assembly )
	{
		try
		{
			if ( assembly.IsDynamic || string.IsNullOrEmpty( assembly.Location ) )
				return null;

			using var pe = new PEReader( File.OpenRead( assembly.Location ) );
			return pe.TryOpenAssociatedPortablePdb( assembly.Location, path => File.Exists( path ) ? new MemoryStream( File.ReadAllBytes( path ) ) : null, out var provider, out _ ) ? provider : null;
		}
		catch ( Exception )
		{
			return null;
		}
	}
}
