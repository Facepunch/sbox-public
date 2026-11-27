using Sandbox.ShaderCompiler;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Editor;

/// <summary>
/// Upgrading a VFX <c>.shader</c> to Slang, as the asset browser's "Upgrade to Slang" does it (shader spec 3.6). A shader
/// written the <c>common/pixel.hlsl</c> way becomes a surface (<see cref="ShaderConverter"/>) when it converts with nothing
/// left over and compiles; any other, or one that doesn't, becomes a standalone module (<see cref="PassConverter"/>), which
/// is compiled so its errors show where it was written. A shader with a geometry stage is refused: it's rewritten by hand
/// with <c>ExpandPrimitives</c> in its vertex stage. The <c>.shader</c> is never deleted or changed: the Slang file goes
/// beside it, and the <c>.shader</c> keeps drawing its materials until it's moved away.
/// </summary>
public static partial class ShaderUpgrade
{
	/// <summary>What became of a shader.</summary>
	public enum UpgradeKind
	{
		/// <summary>A surface: the material shader as a <c>[Material]</c> struct the engine draws in every pass.</summary>
		Surface,
		/// <summary>A standalone module: the shader's code as it is, its modes declared; <see cref="UpgradeResult.Errors"/> says whether it compiles.</summary>
		Module,
		/// <summary>Not upgraded: a geometry or tessellation stage, which only a hand rewrite takes.</summary>
		Refused,
		/// <summary>Not upgraded: not a VFX shader, or the Slang file is there already.</summary>
		Failed,
	}

	/// <summary>
	/// An upgrade: what it made and where, what the surface converter couldn't take (when it tried), the first compile error
	/// (null when it compiled) and a message for the editor to show.
	/// </summary>
	public sealed record UpgradeResult( UpgradeKind Kind, string Path, IReadOnlyList<string> Notes, string Errors, string Message );

	/// <summary>Where a geometry stage's rewrite is described, and the engine's own shaders rewritten that way.</summary>
	public const string ExpandPrimitivesHelp = "Rewrite the geometry stage in the vertex stage with ExpandPrimitives: docs/reference/borealis-pass-modules.md, \"Shaders with a geometry stage\"; "
		+ "the engine's shaders ported that way are core/shaders/gizmo/gizmo_line.slang, core/shaders/tools_wireframe.slang and core/shaders/postprocess/ObjectHighlight/ObjectHighlight.slang";

	// A VFX shader's top-level blocks: no Slang file starts a line with one of these followed by a brace
	[GeneratedRegex( @"^[ \t]*(MODES|HEADER|FEATURES|COMMON|VS|PS|GS|CS)[ \t]*(\r?\n[ \t]*)?\{", RegexOptions.Multiline )]
	private static partial Regex VfxBlock();

	[GeneratedRegex( @"^[ \t]*(GS|HS|DS)[ \t]*(\r?\n[ \t]*)?\{", RegexOptions.Multiline )]
	private static partial Regex GeometryBlock();

	/// <summary>Whether a shader's text is VFX's (the legacy format): top-level <c>MODES</c>, <c>VS</c>, <c>PS</c>, ... blocks.</summary>
	public static bool IsLegacySource( string text ) => text is not null && VfxBlock().IsMatch( text );

	/// <summary>Whether the file is a VFX <c>.shader</c> (by its content, not its name); false when it can't be read.</summary>
	public static bool IsLegacy( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) return false;
		try
		{
			return File.Exists( path ) && IsLegacySource( File.ReadAllText( path ) );
		}
		catch ( IOException )
		{
			return false;
		}
		catch ( UnauthorizedAccessException )
		{
			return false;
		}
	}

	static readonly HashSet<string> warned = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// What the editor says when it compiles a VFX <c>.shader</c> of a project: once a session per shader.
	/// </summary>
	public static string LegacyWarning( string name ) => $"{name}: legacy shader format (VFX). It's still supported for the legacy window, 12 months after the release that makes Slang the .shader format. Right-click it in the asset browser and choose Upgrade to Slang.";

	/// <summary>Warn, once a session, that the shader being compiled is a VFX <c>.shader</c>.</summary>
	internal static void WarnIfLegacy( string absolutePath, string name )
	{
		if ( !IsLegacy( absolutePath ) ) return;
		lock ( warned )
		{
			if ( !warned.Add( absolutePath ) ) return;
		}

		Log.Warning( LegacyWarning( name ) );
	}

	/// <summary>
	/// Upgrade the <c>.shader</c> at <paramref name="shaderPath"/>: write its surface or standalone module beside it, as
	/// <c>name.slang</c>, and compile it. An existing <c>name.slang</c> is kept unless <paramref name="overwrite"/>.
	/// </summary>
	public static async Task<UpgradeResult> UpgradeAsync( string shaderPath, bool overwrite = false )
	{
		var text = File.ReadAllText( shaderPath );
		if ( !IsLegacySource( text ) )
			return new UpgradeResult( UpgradeKind.Failed, null, [], null, $"{Path.GetFileName( shaderPath )} isn't a VFX shader: it has no MODES, VS or PS blocks" );

		if ( GeometryBlock().IsMatch( text ) )
			return new UpgradeResult( UpgradeKind.Refused, null, [], null, $"{Path.GetFileName( shaderPath )} has a geometry or tessellation stage, which Slang shaders don't have. {ExpandPrimitivesHelp}" );

		var name = Path.GetFileNameWithoutExtension( shaderPath );
		var target = Path.ChangeExtension( shaderPath, ".slang" );
		if ( File.Exists( target ) && !overwrite )
			return new UpgradeResult( UpgradeKind.Failed, target, [], null, $"{Path.GetFileName( target )} is there already: upgrading again would replace it" );

		var lineEnding = text.Contains( "\r\n", StringComparison.Ordinal ) ? "\r\n" : "\n";
		var folders = SearchFolders( shaderPath );

		// A material shader as a surface, when the converter takes all of it and it compiles
		IReadOnlyList<string> notes = [];
		var conversion = ShaderConverter.Convert( text, name, include => ReadInclude( include, folders ) );
		notes = conversion.Notes;
		if ( conversion.Source is not null && conversion.Notes.Count == 0 )
		{
			await File.WriteAllTextAsync( target, conversion.Source.Replace( "\r\n", "\n" ).Replace( "\n", lineEnding ) );
			var surfaceErrors = await CompileSurfaceAsync( target, folders );
			if ( surfaceErrors is null )
				return new UpgradeResult( UpgradeKind.Surface, target, notes, null, $"Upgraded to a surface: {Path.GetFileName( target )}. Compare its picture with the .shader's; the .shader draws its materials until you move it away." );

			notes = [$"the surface didn't compile, so it's a standalone module instead: {FirstLine( surfaceErrors )}"];
		}

		// Anything else: the shader's code as a standalone module, compiled so its errors show
		var module = PassConverter.ConvertStandalone( text, name );
		await File.WriteAllTextAsync( target, module.Replace( "\r\n", "\n" ).Replace( "\n", lineEnding ) );
		var errors = CompileModule( target, folders );
		var message = errors is null
			? $"Upgraded to a standalone module: {Path.GetFileName( target )}. The .shader draws its materials until you move it away."
			: $"Upgraded to a standalone module: {Path.GetFileName( target )}, which doesn't compile yet: {FirstLine( errors )}";

		return new UpgradeResult( UpgradeKind.Module, target, notes, errors, message );
	}

	/// <summary>The shader's own folder, then the engine's core shaders.</summary>
	static List<string> SearchFolders( string shaderPath )
	{
		var folders = new List<string> { Path.GetDirectoryName( Path.GetFullPath( shaderPath ) ) };
		if ( EngineFileSystem.Root?.GetFullPath( "/core/shaders" ) is { } core && Directory.Exists( core ) ) folders.Add( core );
		return folders;
	}

	static string ReadInclude( string include, List<string> folders )
	{
		foreach ( var folder in folders )
		{
			var path = Path.Combine( folder, include );
			if ( File.Exists( path ) ) return File.ReadAllText( path );
		}

		return null;
	}

	static SlangCompiler Compiler( List<string> folders )
	{
		var game = EngineFileSystem.Root?.GetFullPath( "/" ) ?? throw new InvalidOperationException( "The engine's file system isn't up" );
		// The native folder Slang is in, as Borealis.Backend's EngineFolders names it
		var native = OperatingSystem.IsWindows() ? "win64" : OperatingSystem.IsMacOS() ? "osxarm64" : "linuxsteamrt64";
		return new SlangCompiler( Path.Combine( game, "bin", native ), folders );
	}

	/// <summary>The surface compiled with every pass; the first error, or null.</summary>
	public static async Task<string> CompileSurfaceAsync( string path ) => await CompileSurfaceAsync( path, SearchFolders( path ) );

	static async Task<string> CompileSurfaceAsync( string path, List<string> folders )
	{
		try
		{
			await Compiler( folders ).CompileSurfaceAsync( path );
			return null;
		}
		catch ( Sandbox.ShaderCompiler.ShaderCompileException e )
		{
			return e.Errors;
		}
	}

	/// <summary>The module's stages, each with its combos at their first values and each at its last in turn; the first error, or null.</summary>
	public static string CompileModule( string path ) => CompileModule( path, SearchFolders( path ) );

	static string CompileModule( string path, List<string> folders )
	{
		try
		{
			var compiler = Compiler( folders );
			var module = PassModule.Read( path, folders );
			foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
			{
				if ( !module.Has( stage ) ) continue;
				var combos = module.Combos( stage );
				var sets = new List<Dictionary<string, int>> { combos.ToDictionary( c => c.Name, c => c.Min ) };
				foreach ( var combo in combos.Where( c => c.Max > c.Min ) )
				{
					var set = combos.ToDictionary( c => c.Name, c => c.Min );
					set[combo.Name] = combo.Max;
					sets.Add( set );
				}

				foreach ( var values in sets )
				{
					if ( !module.Has( stage, values ) ) continue;
					compiler.CompilePass( module, stage, values );
				}
			}

			return null;
		}
		catch ( Sandbox.ShaderCompiler.ShaderCompileException e )
		{
			return e.Errors;
		}
		catch ( Exception e ) when ( e is FormatException or InvalidOperationException or IOException )
		{
			return e.Message;
		}
	}

	/// <summary>A compile error's gist: its first line, and the line naming what it's about (Slang puts that under the source line).</summary>
	static string FirstLine( string text )
	{
		var lines = text?.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ?? [];
		if ( lines.Length == 0 ) return "";
		var marked = lines.Skip( 1 ).FirstOrDefault( l => l.Contains( '^' ) );
		var detail = marked?[(marked.LastIndexOf( '^' ) + 1)..].Trim();
		return string.IsNullOrEmpty( detail ) ? lines[0] : $"{lines[0]}: {detail}";
	}
}
