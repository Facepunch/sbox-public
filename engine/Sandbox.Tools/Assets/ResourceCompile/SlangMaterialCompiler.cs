using Borealis.Source2.Shaders;
using NativeEngine;
using Sandbox.Resources;
using Sandbox.ShaderCompiler;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Materials of shaders only a Slang module describes (no <c>.shader</c> or <c>.shader_c</c>, a <c>.slang</c> where they
/// would be), which the native material compiler can't compile: it reads a shader's features, texture inputs and texture
/// processing from its compiled <c>.shader_c</c>. Here they're read from the module (<see cref="MaterialInputs"/>) and the
/// material is written as the native compiler writes one (<see cref="MaterialCompile"/>), its made textures compiled as
/// children. Every other material returns false here, so the native compiler compiles it as before.
/// </summary>
[Expose]
[ResourceIdentity( "vmat" )]
internal class SlangMaterialCompiler : ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		var bytes = Context.ReadSource();
		if ( bytes is null ) return Task.FromResult( false );

		var material = VmatSource.Parse( System.Text.Encoding.UTF8.GetString( bytes ) );
		var shader = MaterialCompile.ShaderName( material.Shader );
		if ( shader is null ) return Task.FromResult( false );

		var roots = Roots();
		if ( ModuleOnly( shader, roots ) is not var (modulePath, standsFor) ) return Task.FromResult( false );

		// A name standing for another shader (complex for standard) compiles as that one's material
		shader = standsFor;
		var relative = Context.RelativePath.Replace( '\\', '/' );
		Context.AddCompileReference( Path.ChangeExtension( shader, ".slang" ) );

		var module = Module( modulePath, roots );
		var inputs = MaterialInputs.Read( module, material.Features );
		var compiled = MaterialCompile.Compile( material, relative, shader, inputs, file => roots.Exists( r => File.Exists( Path.Combine( r, file ) ) ), file => ImageSize( file, roots ) );

		foreach ( var prerequisite in compiled.Prerequisites ) Context.AddCompiledReference( prerequisite );
		if ( Context.Describing ) return Task.FromResult( true );

		var root = roots[0];
		foreach ( var texture in compiled.Generated )
		{
			var child = Context.CreateChild( Path.Combine( root, texture.Path ) );
			child.SetInputData( texture.Definition );
			if ( !child.Compile() )
			{
				Context.Fail( $"Couldn't compile {texture.Path} for {shader}" );
				return Task.FromResult( true );
			}
		}

		foreach ( var texture in compiled.Textures ) Context.AddRuntimeReference( texture );

		var data = Kv3Binary( compiled.Data );
		if ( data is null )
		{
			Context.Fail( $"Couldn't write the material's data for {shader}" );
			return Task.FromResult( true );
		}

		Context.ResourceVersion = 1; // VMAT_RESOURCE_VERSION_KV3
		Context.Data.Write( data );
		return Task.FromResult( true );
	}

	/// <summary>The folders a material's files are looked for in: its own content folder first, then the engine's core.</summary>
	List<string> Roots()
	{
		var roots = new List<string>();
		var absolute = Context.AbsolutePath.Replace( '\\', '/' );
		var relative = Context.RelativePath.Replace( '\\', '/' );
		if ( absolute.EndsWith( relative, StringComparison.OrdinalIgnoreCase ) ) roots.Add( absolute[..^relative.Length] );

		if ( EngineFileSystem.Root?.GetFullPath( "/core" ) is { } core && !roots.Exists( r => SameFolder( r, core ) ) ) roots.Add( core );
		return roots;
	}

	/// <summary>A relative image's width and height, from the first root that has it; null when none has it or it isn't read.</summary>
	static (int Width, int Height)? ImageSize( string file, List<string> roots )
	{
		var path = roots.Select( r => Path.Combine( r, file ) ).FirstOrDefault( File.Exists );
		if ( path is null ) return null;
		try
		{
			return MaterialCompile.ImageSize( File.ReadAllBytes( path ) );
		}
		catch ( IOException )
		{
			return null;
		}
	}

	static bool SameFolder( string a, string b ) => Path.GetFullPath( a ).TrimEnd( '/', '\\' ).Equals( Path.GetFullPath( b ).TrimEnd( '/', '\\' ), StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The module of a shader only a module describes (as materialsystem2's <c>ShaderIsSlangModuleOnly</c> decides it): no
	/// <c>.shader</c> in any root, a <c>.slang</c> in one, where the shader would be or, for a name standing for another
	/// (<see cref="ShaderAliases"/>: complex is standard), where that one would be. With it, the shader the material compiles
	/// for: the one the name stands for. Null otherwise. A <c>.shader_c</c> without its <c>.shader</c> is a leftover (a build
	/// machine's or a checkout's from before the <c>.shader</c> went) and doesn't count.
	/// </summary>
	static (string Path, string Shader)? ModuleOnly( string shader, List<string> roots )
	{
		var standsFor = ShaderAliases.Resolve( shader );
		if ( roots.Exists( r => File.Exists( Path.Combine( r, shader ) ) || File.Exists( Path.Combine( r, standsFor ) ) ) ) return null;
		var stem = standsFor[..^".shader".Length];
		return roots.Select( r => Path.Combine( r, stem + ".slang" ) ).FirstOrDefault( File.Exists ) is { } path ? (path, standsFor) : null;
	}

	static readonly Dictionary<string, (DateTime Written, PassModule Module)> modules = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A module read once while its file is unchanged (many materials share a shader), its includes found in the roots' shaders.</summary>
	static PassModule Module( string path, List<string> roots )
	{
		var written = File.GetLastWriteTimeUtc( path );
		lock ( modules )
		{
			if ( modules.TryGetValue( path, out var known ) && known.Written == written ) return known.Module;
			var module = PassModule.Read( path, [.. roots.Select( r => Path.Combine( r, "shaders" ) ).Where( Directory.Exists )] );
			modules[path] = (written, module);
			return module;
		}
	}

	/// <summary>KV3 text as the binary KV3 native resources keep in their data block.</summary>
	static byte[] Kv3Binary( string text )
	{
		var kv = EngineGlue.LoadKeyValues3( text );
		if ( kv.IsNull ) return null;

		try
		{
			using var buffer = kv.Save();
			var bytes = new byte[buffer.TellMaxPut()];
			Marshal.Copy( buffer.Base(), bytes, 0, bytes.Length );
			return bytes;
		}
		finally
		{
			kv.DeleteThis();
		}
	}
}
