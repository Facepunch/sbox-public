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

		[Title( "Force Mono" ), Header( "Processing" )]
		[Description( "Downmix every channel into a single mono channel" )]
		public bool ForceMono { get; set; }

		[Title( "Trim Silence" )]
		[Description( "Remove silence from the start and end of the sound" )]
		public bool TrimSilence { get; set; }

		[Title( "Normalize Loudness" )]
		[Description( "Normalize the sound to a standard loudness level" )]
		public bool Normalize { get; set; }

		[Title( "Gain (dB)" ), Range( -24, 24 )]
		[Description( "Fixed volume adjustment applied to the whole sound" )]
		public float Gain { get; set; } = 0.0f;

		[Title( "Sample Rate" ), Header( "Resampling" )]
		public SamplingRate Rate { get; set; } = SamplingRate.Rate44100;

		[Title( "Enabled" ), Header( "Compression" )]
		public bool Compress { get; set; }

		[Title( "Bitrate" ), Range( 128, 256, true, true )]
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
			ForceMono = meta.Get( "forceMono", false ),
			TrimSilence = meta.Get( "trimSilence", false ),
			Normalize = meta.Get( "normalize", false ),
			Gain = meta.Get( "gain", 0.0f ),
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
		meta.Set( "forceMono", settings.ForceMono );
		meta.Set( "trimSilence", settings.TrimSilence );
		meta.Set( "normalize", settings.Normalize );
		meta.Set( "gain", settings.Gain );
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
		// Don't save/compile here directly. For multi-select the MultiSerializedObject fires this
		// once per selected asset as the edit propagates, and sliders fire it every drag tick -
		// acting immediately means compiling assets whose meta is about to change again, and a
		// recompile requested while that stale compile is still in flight can get dropped. Mark
		// dirty and act once things settle.
		_timeSinceChange = 0;
		_dirty = true;
	}

	private bool _dirty;
	private RealTimeSince _timeSinceChange;

	[EditorEvent.Frame]
	private void SaveAndRecompile()
	{
		if ( !_dirty )
			return;

		// let slider drags and multi-select propagation settle first
		if ( _timeSinceChange < 0.2f )
			return;

		Flush();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		// don't lose an edit that was still inside the settle window
		if ( _dirty )
		{
			Flush();
		}
	}

	private void Flush()
	{
		_dirty = false;

		// write every asset's meta first, then compile - a vsnd isn't in the game-resource
		// auto-compile path, so the explicit compile is what rebuilds it (same path as the
		// "Full Recompile" button), which fires the reload that refreshes previews
		foreach ( var (asset, settings) in _targets )
		{
			Save( asset, settings );
		}

		foreach ( var (asset, _) in _targets )
		{
			asset.Compile( false );
		}
	}
}
