using static Editor.Inspectors.AssetInspector;

namespace Editor.Inspectors;

[CanEdit( "asset:vsnd" )]
public class SoundFileCompileSettings : Widget, IAssetInspector
{
	public class Settings
	{
		[Title( "Looping Enabled" ), Header( "Looping" )]
		public bool Loop { get; set; }

		[ShowIf( nameof( Loop ), true )]
		[Description( "Start Time" )]
		public float Start { get; set; }

		[ShowIf( nameof( Loop ), true )]
		[Description( "End Time, 0 is end of sound" )]
		public float End { get; set; }

		[Title( "Sample Rate" ), Header( "Resampling" )]
		public SamplingRate Rate { get; set; } = SamplingRate.Rate44100;

		[Title( "Enabled" ), Header( "Compression" )]
		public bool Compress { get; set; }

		[Title( "Bitrate" ), MinMax( 128, 256 )]
		public int Bitrate { get; set; } = 256;

		public enum SamplingRate
		{
			[Title( "8000" )] Rate8000 = 8000,
			[Title( "11025" )] Rate11025 = 11025,
			[Title( "12000" )] Rate12000 = 12000,

			[Title( "16000" )] Rate16000 = 16000,
			[Title( "22050" )] Rate22050 = 22050,
			[Title( "24000" )] Rate24000 = 24000,

			[Title( "32000" )] Rate32000 = 32000,
			[Title( "44100" )] Rate44100 = 44100
		}
	}

	/// <summary>
	/// Each selected asset paired with the Settings object bound to it in the sheet. One entry for a single
	/// selection, many for multi-select.
	/// </summary>
	private readonly List<(Asset Asset, Settings Settings)> _targets = new();

	public SoundFileCompileSettings( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		if ( asset?.MetaData is null )
			return;

		_targets.Clear();

		var settings = Load( asset );
		_targets.Add( (asset, settings) );

		var so = EditorTypeLibrary.GetSerializedObject( settings );
		so.OnPropertyChanged += ValuesChanged;

		Layout = ControlSheet.Create( so );
	}

	public bool SetAssets( Asset[] assets )
	{
		_targets.Clear();

		var mso = new MultiSerializedObject();

		foreach ( var asset in assets )
		{
			if ( asset?.MetaData is null )
				continue;

			var settings = Load( asset );
			_targets.Add( (asset, settings) );

			mso.Add( EditorTypeLibrary.GetSerializedObject( settings ) );
		}

		if ( _targets.Count == 0 )
			return false;

		mso.Rebuild();
		mso.OnPropertyChanged += ValuesChanged;

		Layout = ControlSheet.Create( mso );

		return true;
	}

	/// <summary>
	/// Read an asset's compile metadata into a fresh Settings object.
	/// </summary>
	private static Settings Load( Asset asset )
	{
		var meta = asset.MetaData;

		return new Settings
		{
			Loop = meta.Get( "loop", false ),
			Start = meta.Get( "start", 0.0f ),
			End = meta.Get( "end", 0.0f ),
			Rate = meta.Get( "rate", Settings.SamplingRate.Rate44100 ),
			Compress = meta.Get( "compress", false ),
			Bitrate = meta.Get( "bitrate", 256 ),
		};
	}

	/// <summary>
	/// Write a Settings object back to an asset's compile metadata.
	/// </summary>
	private static void Save( Asset asset, Settings settings )
	{
		var meta = asset.MetaData;
		if ( meta is null )
			return;

		meta.Set( "loop", settings.Loop );
		meta.Set( "start", settings.Start );
		meta.Set( "end", settings.End );
		meta.Set( "rate", settings.Rate );
		meta.Set( "compress", settings.Compress );
		meta.Set( "bitrate", settings.Bitrate );
	}

	/// <summary>
	/// A value changed in the sheet. For multi-select the edit has already been propagated to every target's
	/// Settings by the MultiSerializedObject, so we just persist each one - untouched fields keep their own
	/// per-asset values.
	/// </summary>
	private void ValuesChanged( SerializedProperty property )
	{
		foreach ( var (asset, settings) in _targets )
		{
			Save( asset, settings );
		}
	}
}
