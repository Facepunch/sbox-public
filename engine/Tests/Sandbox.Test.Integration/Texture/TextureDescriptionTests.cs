using Borealis.Source2.Materials;
using NativeEngine;
using System;
using System.IO;
using System.Linq;

namespace TextureTests;

/// <summary>
/// A texture describes itself, from what it was made with or from its compiled file's header, as Source 2 describes it.
/// </summary>
[TestClass]
public class TextureDescriptionTests
{
	static string GameFolder => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );

	const RuntimeTextureSpecificationFlags None = 0;

	/// <summary>Compiled textures in core, each kind once: the header's description and sheet.</summary>
	[TestMethod]
	public void FileHeadersDescribeTextures()
	{
		var cases = new (string Path, int Width, int Height, int Mips, ImageFormat Format, RuntimeTextureSpecificationFlags Flags, (int Frames, float Length, bool Clamp)[] Sheet)[]
		{
			("materials/debug/debugempty.vtex", 64, 64, 1, ImageFormat.DXT1, RuntimeTextureSpecificationFlags.TSPEC_NO_LOD, []),
			("dev/env_cubemap_black.vtex", 4, 4, 1, ImageFormat.BC6H, RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE, []),
			("dev/helper/reflectivity_90b_color_tga_*.generated.vtex", 32, 32, 4, ImageFormat.DXT1, None, []),
			("textures/beams/beam_blood_01.vtex", 128, 256, 1, ImageFormat.DXT5, None, [(1, 1, true)]),
			("textures/particles/flame/explosion/explosion_01.vtex", 2048, 2048, 1, ImageFormat.DXT5, None, [(64, 64, false)]),
		};

		foreach ( var item in cases )
		{
			var c = item with { Path = Compiled( item.Path ) };
			var header = TextureHeader.Read( File.ReadAllBytes( Path.Combine( GameFolder, "core", c.Path + "_c" ) ) );
			var desc = Texture.DescriptionOf( header );
			Assert.IsNotNull( desc, $"{c.Path}: no description from VTEX format {header.Format}" );
			Assert.AreEqual( c.Width, desc.Value.m_nWidth, $"{c.Path}: width" );
			Assert.AreEqual( c.Height, desc.Value.m_nHeight, $"{c.Path}: height" );
			Assert.AreEqual( 1, desc.Value.m_nDepth, $"{c.Path}: depth" );
			Assert.AreEqual( c.Mips, desc.Value.m_nNumMipLevels, $"{c.Path}: mips" );
			Assert.AreEqual( c.Format, desc.Value.m_nImageFormat, $"{c.Path}: format" );
			Assert.AreEqual( c.Flags, desc.Value.m_nFlags, $"{c.Path}: flags" );

			Assert.AreEqual( c.Sheet.Length, header.Sheet?.Count ?? 0, $"{c.Path}: sequences" );
			for ( int i = 0; i < c.Sheet.Length; i++ )
			{
				Assert.AreEqual( c.Sheet[i].Frames, header.Sheet[i].Frames.Count, $"{c.Path}: sequence {i} frames" );
				Assert.AreEqual( c.Sheet[i].Length, header.Sheet[i].TotalTime, $"{c.Path}: sequence {i} length" );
				Assert.AreEqual( c.Sheet[i].Clamp, header.Sheet[i].Clamp, $"{c.Path}: sequence {i} clamp" );
			}

			// Loaded, the texture describes itself the same way. Without graphics (SBOX_TEST_GRAPHICS unset, as on CI)
			// a unit test loads no textures at all
			if ( !Graphics.IsAvailable )
				continue;

			var texture = Texture.Load( c.Path );
			Assert.IsNotNull( texture, c.Path );
			Assert.AreEqual( c.Width, texture.Width, $"{c.Path}: loaded width" );
			Assert.AreEqual( c.Height, texture.Height, $"{c.Path}: loaded height" );
			Assert.AreEqual( c.Format, texture.ImageFormat, $"{c.Path}: loaded format" );
			Assert.AreEqual( c.Sheet.Length, texture.SequenceCount, $"{c.Path}: loaded sequences" );
		}
	}

	/// <summary>
	/// A core path to a compiled texture. A generated texture's name carries a hash of how it was made, which changes with the
	/// compiler, so a <c>*</c> stands for it and finds the one the content build made. A workspace that isn't cleaned between
	/// builds (the CI runners) keeps the files of older hashes, so the newest is the one.
	/// </summary>
	static string Compiled( string path )
	{
		if ( !path.Contains( '*' ) )
			return path;

		var folder = path[..path.LastIndexOf( '/' )];
		var files = Directory.GetFiles( Path.Combine( GameFolder, "core", folder ), path[(folder.Length + 1)..] + "_c" );
		Assert.AreNotEqual( 0, files.Length, $"{path}: compiled files" );
		var newest = files.MaxBy( File.GetLastWriteTimeUtc );
		return $"{folder}/{Path.GetFileName( newest )[..^2]}";
	}

	/// <summary>A texture made at run time is described by what it was made with.</summary>
	[TestMethod]
	public void CreatedTexturesDescribeThemselves()
	{
		const RuntimeTextureSpecificationFlags GeneratedMips = RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_GEN_MIP_MAPS | RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET | RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET_SAMPLEABLE;

		var made = new (string What, Texture Texture, int Width, int Height, int Depth, int Mips, ImageFormat? Format, RuntimeTextureSpecificationFlags Has)[]
		{
			("2d with data", Texture.Create( 8, 4 ).WithData( new byte[8 * 4 * 4] ).Finish(), 8, 4, 1, 1, ImageFormat.RGBA8888, None),
			("2d with mips to make", Texture.Create( 8, 8 ).WithMips( 4 ).WithData( new byte[8 * 8 * 4] ).Finish(), 8, 8, 1, 4, ImageFormat.RGBA8888, GeneratedMips),
			("2d with every mip", Texture.Create( 4, 4 ).WithMips( 3 ).WithData( new byte[(16 + 4 + 1) * 4] ).Finish(), 4, 4, 1, 3, ImageFormat.RGBA8888, None),
			("2d empty", Texture.Create( 16, 16, ImageFormat.RGBA16161616F ).Finish(), 16, 16, 1, 1, ImageFormat.RGBA16161616F, None),
			("render target", Texture.CreateRenderTarget().WithSize( 32, 16 ).WithFormat( ImageFormat.RGBA8888 ).Create(), 32, 16, 1, 1, ImageFormat.RGBA8888, RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET),
			("depth target", Texture.CreateRenderTarget().WithSize( 32, 16 ).WithDepthFormat().Create(), 32, 16, 1, 1, null, RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET),
			("uav with mips", Texture.CreateRenderTarget().WithSize( 64, 64 ).WithFormat( ImageFormat.RGBA16161616F ).WithMips( 4 ).WithUAVBinding().Create(), 64, 64, 1, 4, ImageFormat.RGBA16161616F, RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET | RuntimeTextureSpecificationFlags.TSPEC_UAV),
			("msaa target", Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( ImageFormat.RGBA8888 ).WithMSAA( MultisampleAmount.Multisample4x ).Create(), 32, 32, 1, 1, ImageFormat.RGBA8888, RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET),
			("array", Texture.CreateArray( 4, 4, 3 ).WithMips( 3 ).WithData( new byte[3 * (16 + 4 + 1) * 4] ).Finish(), 4, 4, 3, 3, ImageFormat.RGBA8888, RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY),
			("volume", Texture.CreateVolume( 4, 4, 3 ).WithData( new byte[4 * 4 * 3 * 4] ).Finish(), 4, 4, 3, 1, ImageFormat.RGBA8888, RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE),
			("cube", Texture.CreateCube( 8, 8 ).Finish(), 8, 8, -1, 1, ImageFormat.RGBA8888, RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE),
		};

		try
		{
			foreach ( var m in made )
			{
				Assert.IsNotNull( m.Texture, m.What );
				var desc = m.Texture.Desc;
				Assert.AreEqual( m.Width, desc.m_nWidth, $"{m.What}: width" );
				Assert.AreEqual( m.Height, desc.m_nHeight, $"{m.What}: height" );
				if ( m.Depth >= 0 ) Assert.AreEqual( m.Depth, desc.m_nDepth, $"{m.What}: depth" );
				Assert.AreEqual( m.Mips, desc.m_nNumMipLevels, $"{m.What}: mips" );
				if ( m.Format is { } format ) Assert.AreEqual( format, desc.m_nImageFormat, $"{m.What}: format" );
				Assert.AreEqual( m.Has, desc.m_nFlags & m.Has, $"{m.What}: flags {desc.m_nFlags}" );
				Assert.AreEqual( 0, m.Texture.SequenceCount, m.What );
			}
		}
		finally
		{
			foreach ( var m in made ) m.Texture?.Dispose();
		}
	}
}
