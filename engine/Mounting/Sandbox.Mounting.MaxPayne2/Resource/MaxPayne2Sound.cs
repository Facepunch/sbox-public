using RasLib;
using System;

class MaxPayne2Sound( string fileName ) : ResourceLoader<MaxPayne2Mount>
{
	public string FileName { get; set; } = fileName;

	protected override object Load()
	{
		var data = Host.GetFileBytes( FileName );
		if ( data is null )
			return null;

		var loop = FileName.Contains( "_loop", StringComparison.OrdinalIgnoreCase );

		var adpcm = MaxPayneWav.DecodeIfAdpcm( data );
		if ( adpcm is not null )
		{
			return SoundFile.FromPcm( Path, adpcm.Data, new SoundFile.PcmOptions
			{
				Channels = adpcm.Channels,
				Rate = (uint)adpcm.SampleRate,
				Bits = adpcm.BitsPerSample,
				Loop = loop,
			} );
		}

		return SoundFile.FromWav( Path, data, new SoundFile.LoadOptions { Loop = loop } );
	}
}
