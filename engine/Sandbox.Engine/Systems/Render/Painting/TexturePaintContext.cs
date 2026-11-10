using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

internal sealed class TexturePaintContext : Painter.Context
{
	Texture _texture;

	internal TexturePaintContext( Texture texture ) : base( new CommandList( "Painter.Texture" ) )
	{
		_texture = texture;
	}

	internal Painter Begin()
	{
		ThreadSafe.AssertIsMainThread();

		if ( !_texture.IsValid )
			throw new ObjectDisposedException( nameof( Texture ) );

		if ( !_texture.IsRenderTarget )
			throw new InvalidOperationException( "Create the destination with Texture.CreateRenderTarget() before painting." );

		var desc = _texture.Desc;
		if ( desc.IsArray || desc.IsCube || desc.m_nFlags.HasFlag( RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE )
			|| _texture.MultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE || _texture.Mips != 1 || _texture.ImageFormat.IsDepthFormat() )
		{
			throw new InvalidOperationException( "Painting requires a 2D color render target with one mip and no MSAA." );
		}

		return base.Begin( new Rect( 0, 0, _texture.Width, _texture.Height ) );
	}

	protected override void OnEnd()
	{
		try
		{
			if ( !_texture.IsValid )
				throw new ObjectDisposedException( nameof( Texture ) );

			base.OnEnd();
			if ( CommandList.GetCheckpoint() == 0 ) return;

			Submit();
		}
		finally
		{
			CommandList.Reset();
			Batcher.Dispose();
			_texture = null;
			State = default;
		}
	}

	void Submit()
	{
		using var scope = Graphics.Scope.Create();
		Graphics.RenderTarget = RenderTarget.From( _texture );
		_texture.Flags |= TextureFlags.PremultipliedAlpha;
		scope.Attributes.Set( "UIGammaOutput", true );
		scope.Attributes.Set( "UIFrameGrabEncoded", true );
		CommandList.Execute();
	}
}
