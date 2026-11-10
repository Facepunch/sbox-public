using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

internal sealed class TexturePaintContext : Painter.Context
{
	Texture _texture;
	readonly RenderBackend _backend;
	RenderBackend _recording;
	bool _recordingSet;

	internal TexturePaintContext( Texture texture, RenderBackend backend = null ) : base( new CommandList( "Painter.Texture" ) )
	{
		_texture = texture;
		_backend = backend ?? RenderBackend.Default;
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

		var painter = base.Begin( new Rect( 0, 0, _texture.Width, _texture.Height ) );

		// Record against the device it will draw on
		if ( _backend != RenderBackend.Default )
		{
			_recording = Graphics.SetRecordingBackend( _backend );
			_recordingSet = true;
		}

		return painter;
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

			if ( _recordingSet )
			{
				Graphics.SetRecordingBackend( _recording );
				_recordingSet = false;
			}
		}
	}

	void Submit()
	{
		using var scope = Graphics.Scope.CreateOn( _backend );
		Graphics.RenderTarget = RenderTarget.From( _texture );
		_texture.Flags |= TextureFlags.PremultipliedAlpha;
		scope.Attributes.Set( "UIGammaOutput", true );
		scope.Attributes.Set( "UIFrameGrabEncoded", true );
		CommandList.Execute();
	}
}
