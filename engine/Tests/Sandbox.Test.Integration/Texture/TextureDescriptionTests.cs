using Borealis.Source2.Materials;
using NativeEngine;
using System;
using System.IO;
using System.Linq;

namespace TextureTests;

/// <summary>
/// Without Source 2's device a texture describes itself, from what it was made with or from its compiled file's header:
/// each must say what Source 2's descriptions say.
/// </summary>
[TestClass]
public class TextureDescriptionTests
{
	static string GameFolder => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );

	static void AssertSame( CTextureDesc expected, CTextureDesc actual, string what )
	{
		Assert.AreEqual( expected.m_nWidth, actual.m_nWidth, $"{what}: width" );
		Assert.AreEqual( expected.m_nHeight, actual.m_nHeight, $"{what}: height" );
		Assert.AreEqual( expected.m_nDepth, actual.m_nDepth, $"{what}: depth" );
		Assert.AreEqual( expected.m_nNumMipLevels, actual.m_nNumMipLevels, $"{what}: mips" );
		Assert.AreEqual( expected.m_nDecodeFlags, actual.m_nDecodeFlags, $"{what}: decode flags" );
		Assert.AreEqual( expected.m_nImageFormat, actual.m_nImageFormat, $"{what}: format" );
		Assert.AreEqual( expected.m_nFlags, actual.m_nFlags, $"{what}: flags" );
		Assert.AreEqual( expected.m_nDisplayRectWidth, actual.m_nDisplayRectWidth, $"{what}: display width" );
		Assert.AreEqual( expected.m_nDisplayRectHeight, actual.m_nDisplayRectHeight, $"{what}: display height" );
		Assert.AreEqual( expected.m_nMotionVectorsMaxDistanceInPixels, actual.m_nMotionVectorsMaxDistanceInPixels, $"{what}: motion vectors" );
	}

	static CTextureDesc Native( ITexture texture )
	{
		ref var desc = ref g_pRenderDevice.GetOnDiskTextureDesc( texture );
		Assert.IsFalse( System.Runtime.CompilerServices.Unsafe.IsNullRef( ref desc ), "Source 2 has no description" );
		return desc;
	}

	/// <summary>Every compiled texture in core: the header's description and sheet are Source 2's.</summary>
	[TestMethod]
	public void FileHeadersDescribeTexturesAsSourceTwoDoes()
	{
		var root = Path.Combine( GameFolder, "core" );
		var files = Directory.EnumerateFiles( root, "*.vtex_c", SearchOption.AllDirectories ).ToList();
		Assert.IsTrue( files.Count > 100, $"only {files.Count} compiled textures under {root}" );

		int compared = 0, sheets = 0;
		foreach ( var file in files )
		{
			var path = Path.GetRelativePath( root, file ).Replace( '\\', '/' )[..^2];
			var header = TextureHeader.Read( File.ReadAllBytes( file ) );

			var handle = NativeGlue.Resources.GetTexture( path, Guid.Empty );
			try
			{
				if ( handle.IsNull || handle.IsError() ) continue;

				var described = Texture.DescriptionOf( header );
				Assert.IsNotNull( described, $"{path}: no description from VTEX format {header.Format}" );
				AssertSame( Native( handle ), described.Value, path );

				var count = g_pRenderDevice.GetSequenceCount( handle );
				Assert.AreEqual( count, header.Sheet?.Count ?? 0, $"{path}: sequences" );
				for ( int i = 0; i < count; i++ )
				{
					var sequence = g_pRenderDevice.GetSequence( handle, i );
					Assert.AreEqual( sequence.FrameCount(), header.Sheet[i].Frames.Count, $"{path}: sequence {i} frames" );
					Assert.AreEqual( sequence.m_flTotalTime, header.Sheet[i].TotalTime, $"{path}: sequence {i} length" );
					Assert.AreEqual( sequence.m_bClamp, header.Sheet[i].Clamp, $"{path}: sequence {i} clamp" );
				}

				sheets += count > 0 ? 1 : 0;
				compared++;
			}
			finally
			{
				if ( !handle.IsNull ) handle.DestroyStrongHandle();
			}
		}

		Assert.IsTrue( compared > 100, $"only {compared} textures loaded" );
		Assert.IsTrue( sheets > 0, "no texture with a sheet was compared" );
	}

	/// <summary>A texture made at run time is described by what it was made with, as Source 2 describes it.</summary>
	[TestMethod]
	public void CreatedTexturesDescribeThemselvesAsSourceTwoDoes()
	{
		var made = new (string What, Texture Texture)[]
		{
			("2d with data", Texture.Create( 8, 4 ).WithData( new byte[8 * 4 * 4] ).Finish()),
			("2d with mips to make", Texture.Create( 8, 8 ).WithMips( 4 ).WithData( new byte[8 * 8 * 4] ).Finish()),
			("2d with every mip", Texture.Create( 4, 4 ).WithMips( 3 ).WithData( new byte[(16 + 4 + 1) * 4] ).Finish()),
			("2d empty", Texture.Create( 16, 16, ImageFormat.RGBA16161616F ).Finish()),
			("render target", Texture.CreateRenderTarget().WithSize( 32, 16 ).WithFormat( ImageFormat.RGBA8888 ).Create()),
			("depth target", Texture.CreateRenderTarget().WithSize( 32, 16 ).WithDepthFormat().Create()),
			("uav with mips", Texture.CreateRenderTarget().WithSize( 64, 64 ).WithFormat( ImageFormat.RGBA16161616F ).WithMips( 4 ).WithUAVBinding().Create()),
			("msaa target", Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( ImageFormat.RGBA8888 ).WithMSAA( MultisampleAmount.Multisample4x ).Create()),
			("array", Texture.CreateArray( 4, 4, 3 ).WithMips( 3 ).WithData( new byte[3 * (16 + 4 + 1) * 4] ).Finish()),
			("volume", Texture.CreateVolume( 4, 4, 3 ).WithData( new byte[4 * 4 * 3 * 4] ).Finish()),
			("cube", Texture.CreateCube( 8, 8 ).Finish()),
		};

		try
		{
			foreach ( var (what, texture) in made )
			{
				Assert.IsNotNull( texture, what );
				AssertSame( Native( texture.native ), texture.Desc, what );
				Assert.AreEqual( 0, texture.SequenceCount, what );
			}
		}
		finally
		{
			foreach ( var (_, texture) in made ) texture?.Dispose();
		}
	}
}
