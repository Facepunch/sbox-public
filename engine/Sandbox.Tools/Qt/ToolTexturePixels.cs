using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Native tools reading a texture's pixels (<c>ToolsReadTexturePixels</c>: the material editor's compiled texture widget):
/// on Borealis.Gpu its device holds the texture, so it's read from there by the texture's resource name.
/// </summary>
internal static unsafe class ToolTexturePixels
{
	/// <summary><c>ToolTexturePixels_t</c> (src/public/toolutils2/toolrenderutils.h).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct Request
	{
		public IntPtr ResourceName;
		public int Width;
		public int Height;
		public byte* Pixels;
		public int Handled;
	}

	/// <summary>
	/// Read the mip of the size asked for as BGRA8 into the request's pixels, and mark it handled; left clear (Source 2
	/// reads it) without Borealis.Gpu, or for a texture it can't find.
	/// </summary>
	internal static void Read( IntPtr requestPointer )
	{
		if ( requestPointer == IntPtr.Zero ) return;
		ref var request = ref *(Request*)requestPointer;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend ) return;
		if ( request.Pixels == null || request.Width < 1 || request.Height < 1 || request.ResourceName == IntPtr.Zero ) return;

		var name = Marshal.PtrToStringUTF8( request.ResourceName );
		if ( string.IsNullOrEmpty( name ) ) return;
		if ( name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) name = name[..^2];

		try
		{
			var texture = Texture.Load( name, false );
			if ( texture is null || !texture.IsValid ) return;

			for ( int mip = 0; mip < texture.Mips; mip++ )
			{
				var (width, height) = texture.ReadSize( mip );
				if ( width != request.Width || height != request.Height ) continue;

				texture.GetPixels<byte>( (0, 0, width, height), 0, mip, new Span<byte>( request.Pixels, width * height * 4 ), ImageFormat.BGRA8888 );
				request.Handled = 1;
				return;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Reading {name}'s pixels on Borealis.Gpu: {e.Message}" );
		}
	}
}
