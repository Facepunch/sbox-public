namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// A single color channel of an 8 bit per channel bitmap.
	/// </summary>
	internal enum ColorChannel
	{
		Red = 0,
		Green = 1,
		Blue = 2,
		Alpha = 3
	}

	/// <summary>
	/// Copy single channels out of other bitmaps into one new bitmap - packing a set of
	/// grayscale maps into one texture, for example. The result is as big as the largest
	/// source, and smaller sources are scaled up to match. Channels that nothing is copied
	/// into are left at zero.
	/// </summary>
	internal static Bitmap PackChannels( params (Bitmap Source, ColorChannel From, ColorChannel To)[] channels )
	{
		ArgumentNullException.ThrowIfNull( channels );

		if ( channels.Length == 0 )
			throw new ArgumentException( "Need at least one channel to pack", nameof( channels ) );

		foreach ( var (source, _, _) in channels )
		{
			if ( source is null || !source.IsValid )
				throw new ArgumentException( "Can't pack an invalid bitmap", nameof( channels ) );

			if ( source.IsFloatingPoint )
				throw new ArgumentException( "Channel packing works on 8 bit per channel bitmaps", nameof( channels ) );
		}

		var width = channels.Max( x => x.Source.Width );
		var height = channels.Max( x => x.Source.Height );

		var packed = new Bitmap( width, height );
		var destination = packed.GetBuffer();

		// A source can feed more than one channel, so only scale it once
		foreach ( var group in channels.GroupBy( x => x.Source ) )
		{
			var source = group.Key;
			Bitmap resized = null;

			try
			{
				if ( source.Width != width || source.Height != height )
					resized = source.Resize( width, height );

				var pixels = (resized ?? source).GetBuffer();

				foreach ( var (_, from, to) in group )
				{
					for ( int i = 0; i < width * height; i++ )
					{
						destination[i * 4 + (int)to] = pixels[i * 4 + (int)from];
					}
				}
			}
			finally
			{
				resized?.Dispose();
			}
		}

		return packed;
	}
}
