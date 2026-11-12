using System;
using System.Globalization;

namespace Sandbox.UI
{
	public abstract partial class BaseStyles
	{
		internal Lazy<Texture> _backgroundImage;
		internal Lazy<Texture> _maskImage;
		internal Lazy<Texture> _borderImageSource;
		internal bool? _backgroundPlaybackPaused;

		public Texture BackgroundImage
		{
			get
			{
				if ( _backgroundImage == null ) return null;

				return _backgroundImage?.Value;
			}

			set
			{
				OnPropertySet( "background-image" );
				if ( _backgroundImage?.Value == value )
					return;

				_backgroundImage = new Lazy<Texture>( value );
				Dirty();
			}
		}

		public Texture MaskImage
		{
			get
			{
				if ( _maskImage == null ) return null;

				return _maskImage?.Value;
			}

			set
			{
				OnPropertySet( "mask-image" );
				if ( _maskImage?.Value == value )
					return;

				_maskImage = new Lazy<Texture>( value );
				Dirty();
			}
		}

		public Texture BorderImageSource
		{
			get
			{
				if ( _borderImageSource == null ) return null;

				return _borderImageSource?.Value;
			}

			set
			{
				OnPropertySet( "border-image-source" );
				if ( _borderImageSource?.Value == value )
					return;

				_borderImageSource = new Lazy<Texture>( value );
				Dirty();
			}
		}

		/// <summary>
		/// Controls whether the background video is paused. Mirrors <c>animation-play-state</c>.
		/// Maps to the CSS property <c>background-playback-state: paused | running</c>.
		/// </summary>
		public bool? BackgroundPlaybackPaused
		{
			get => _backgroundPlaybackPaused;
			set
			{
				OnPropertySet( "background-playback-state" );
				if ( _backgroundPlaybackPaused == value ) return;
				_backgroundPlaybackPaused = value;
				Dirty();
			}
		}

	}
}
