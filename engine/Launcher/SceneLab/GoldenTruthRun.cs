using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sandbox.SceneLab;

/// <summary>
/// <c>-golden-truth folder</c>: check each capture against committed golden truth images instead of native. The golden
/// truth images live in a bucket per platform, API and device, one per preset,
/// <c>folder/Windows.Vulkan/Lavapipe/AllShadows.png</c>, with optional alternates (<c>AllShadows.variant2.png</c>, ...)
/// for renderers whose output differs between machines; a capture passes when it matches any of them. What counts as a
/// match is a histogram of each pixel's largest channel difference, checked against the most specific rule in
/// <c>thresholds.jsonc</c> (the root folder's, then the bucket's): by default, no pixel may be 3 or more off. Run with
/// <c>-vulkan_enable_validation</c> and any validation message fails the run too, unless <c>validation.jsonc</c> in the
/// root folder allows it. Each capture gets a <c>.results.json</c> beside it, and a <c>.diff.png</c> against the
/// closest golden truth image. <c>-golden-truth-update</c> writes the capture into its bucket as the golden truth
/// image; <c>-golden-truth-device name</c> overrides the device bucket.
/// </summary>
internal static class GoldenTruthRun
{
	/// <summary>
	/// The golden truth folder, or null when not checking against golden truth images.
	/// </summary>
	public static string Folder { get; } = Args.String( "-golden-truth" ) is { } folder ? System.IO.Path.GetFullPath( folder ) : null;

	public static bool Enabled => Folder is not null;

	static readonly List<string> validationMessages = new();
	static bool watching;

	/// <summary>
	/// Start collecting validation messages. Called before the engine starts, so the device's creation is covered too.
	/// </summary>
	public static void WatchValidation()
	{
		if ( watching ) return;
		watching = true;
		Sandbox.Diagnostics.Logging.OnMessage += OnLog;
	}

	// The render system logs each validation message as "VK ERROR : VALIDATION - Message Id Number: ...", or WARNING
	static readonly Regex ValidationMessage = new( @"^VK (ERROR|WARNING) : VALIDATION", RegexOptions.CultureInvariant );

	// Set when the render system loads the validation layer: -vulkan_enable_validation without the Vulkan SDK carries on
	// without it, and a run that validated nothing mustn't pass as one that found nothing
	static bool validationLayer;

	static void OnLog( LogEvent e )
	{
		if ( e.Message is not { } text ) return;
		if ( text.StartsWith( "Enabling validation layer:", StringComparison.Ordinal ) ) validationLayer = true;
		if ( !ValidationMessage.IsMatch( text ) ) return;
		lock ( validationMessages ) validationMessages.Add( text.Trim() );
	}

	/// <summary>
	/// The device bucket: a CPU rasterizer by its family, so a driver update doesn't move its golden truth images,
	/// anything else by its name.
	/// </summary>
	public static string Device
	{
		get
		{
			if ( Args.String( "-golden-truth-device" ) is { } device ) return device;

			var gpu = Gpu ?? "";
			if ( gpu.Contains( "llvmpipe", StringComparison.OrdinalIgnoreCase ) ) return "Lavapipe";
			if ( gpu.StartsWith( "SwiftShader", StringComparison.OrdinalIgnoreCase ) ) return "SwiftShader";
			return Regex.Replace( gpu, "[^A-Za-z0-9]+", "-" ).Trim( '-' ) is { Length: > 0 } name ? name : "Unknown";
		}
	}

	/// <summary>
	/// The adapter's name, e.g. <c>llvmpipe (LLVM 21.1.0, 256 bits)</c>.
	/// </summary>
	static string Gpu => NativeEngine.RenderDeviceManager.GetAdapterName();

	/// <summary>
	/// The vector instruction sets a CPU rasterizer picks its code paths from, so they say which variant a run matches.
	/// </summary>
	static string CpuFeatures => string.Join( " ", new[]
	{
		(System.Runtime.Intrinsics.X86.Avx2.IsSupported, "AVX2"),
		(System.Runtime.Intrinsics.X86.Fma.IsSupported, "FMA"),
		(System.Runtime.Intrinsics.X86.Avx512F.IsSupported, "AVX512F"),
		(System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported, "NEON"),
	}.Where( x => x.Item1 ).Select( x => x.Item2 ) );

	static string Platform => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown";

	const string Api = "Vulkan";

	/// <summary>
	/// A preset's golden truth image: its name in Pascal case, <c>All Shadows</c> as <c>AllShadows.png</c>.
	/// </summary>
	public static string ImageName( string preset ) => string.Concat( Regex.Split( preset, "[^A-Za-z0-9]+" ).Where( x => x.Length > 0 ).Select( x => char.ToUpperInvariant( x[0] ) + x[1..] ) ) + ".png";

	/// <summary>
	/// The bucket golden truth images go in on this machine.
	/// </summary>
	public static string Bucket => System.IO.Path.Combine( Folder, $"{Platform}.{Api}", Device );

	public readonly record struct Result( bool Passed, string Text );

	/// <summary>
	/// Check the capture at <paramref name="capturePath"/> of <paramref name="preset"/> against its golden truth
	/// images, along with the validation messages since the last check. Writes the results beside the capture.
	/// <paramref name="image"/> names the golden truth image when it isn't the preset's own, e.g. a GameObject preset's
	/// frame after its change, <c>PostProcessingChanged.png</c>.
	/// </summary>
	public static Result Check( string preset, string capturePath, string image = null )
	{
		var bucket = Bucket;
		var name = image ?? ImageName( preset );
		var pixels = Pixels( capturePath, out var width, out var height );

		if ( Args.Has( "-golden-truth-update" ) )
		{
			System.IO.Directory.CreateDirectory( bucket );
			System.IO.File.Copy( capturePath, System.IO.Path.Combine( bucket, name ), overwrite: true );
			Log.Info( $"Golden truth: wrote {System.IO.Path.Combine( bucket, name )}" );
		}

		var rules = LoadRules( System.IO.Path.Combine( Folder, "thresholds.jsonc" ) ).Concat( LoadRules( System.IO.Path.Combine( bucket, "thresholds.jsonc" ) ) ).ToArray();
		var buckets = Thresholds.Resolve( rules, name, Platform, Api, Device );

		var attempts = new List<Attempt>();
		Attempt closest = null;
		Color32[] closestPixels = null;
		foreach ( var golden in Variants( bucket, name ) )
		{
			var goldenPixels = Pixels( golden, out var goldenWidth, out var goldenHeight );
			var attempt = goldenWidth == width && goldenHeight == height ? Compare( pixels, goldenPixels, buckets ) : Attempt.SizeMismatch( goldenWidth, goldenHeight );
			attempt.GoldenTruth = System.IO.Path.GetRelativePath( Folder, golden ).Replace( '\\', '/' );
			attempts.Add( attempt );

			if ( closest is null || attempt.Score < closest.Score )
			{
				closest = attempt;
				closestPixels = attempt.Compared ? goldenPixels : null;
			}
		}

		var matched = attempts.FirstOrDefault( x => x.Passed );
		var unexpected = UnexpectedValidation( out var allowed );
		var requested = Environment.GetCommandLineArgs().Any( x => x.Equals( "-vulkan_enable_validation", StringComparison.OrdinalIgnoreCase ) );
		var validated = requested && validationLayer;

		var outcome = attempts.Count == 0 ? "missing" : matched is null ? "fail" : unexpected.Length > 0 || requested != validated ? "validation" : "pass";
		var passed = outcome == "pass";

		if ( closestPixels is not null )
			Save( Sandbox.SceneRenderer.FrameComparison.Difference( pixels, closestPixels ), width, height, System.IO.Path.ChangeExtension( capturePath, ".diff.png" ) );

		var sidecar = new Sidecar
		{
			Outcome = outcome,
			Preset = preset,
			Image = name,
			Bucket = System.IO.Path.GetRelativePath( Folder, bucket ).Replace( '\\', '/' ),
			Gpu = Gpu,
			CpuFeatures = CpuFeatures,
			Validation = validated,
			Matched = matched?.GoldenTruth,
			Attempts = attempts,
			ValidationMessages = unexpected,
			AllowedValidationMessages = allowed,
		};
		System.IO.File.WriteAllText( System.IO.Path.ChangeExtension( capturePath, ".results.json" ), JsonSerializer.Serialize( sidecar, JsonOptions ) );

		var text = outcome switch
		{
			"missing" => $"golden truth MISSING: nothing in {bucket} for {name} (run with -golden-truth-update to write it)",
			"fail" => $"golden truth FAILED: {closest}",
			"validation" when unexpected.Length > 0 => $"golden truth FAILED: {unexpected.Length} validation message(s), first: {unexpected[0]}",
			"validation" => "golden truth FAILED: -vulkan_enable_validation didn't load VK_LAYER_KHRONOS_validation - install the Vulkan SDK",
			_ => $"golden truth OK: {matched.GoldenTruth}, {matched}",
		};
		if ( !requested ) text += " (validation off: pass -vulkan_enable_validation)";

		Log.Info( $"Golden truth: {preset}{(image is null ? "" : $" ({name})")}: {text}" );
		foreach ( var message in unexpected.Take( 20 ) ) Log.Warning( $"Golden truth: validation: {message}" );
		return new Result( passed, text );
	}

	/// <summary>
	/// The golden truth image <c>name.png</c> and its variants <c>name.variantN.png</c>, in order.
	/// </summary>
	static IEnumerable<string> Variants( string bucket, string name )
	{
		if ( !System.IO.Directory.Exists( bucket ) ) return [];

		var stem = System.IO.Path.GetFileNameWithoutExtension( name );
		var variant = new Regex( $@"^{Regex.Escape( stem )}(\.variant([2-9]|[1-9][0-9]{{1,8}}))?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );
		return System.IO.Directory.EnumerateFiles( bucket, $"{stem}*.png" )
			.Select( path => (path, match: variant.Match( System.IO.Path.GetFileName( path ) )) )
			.Where( x => x.match.Success )
			.OrderBy( x => x.match.Groups[2].Success ? int.Parse( x.match.Groups[2].Value ) : 1 )
			.Select( x => x.path );
	}

	/// <summary>
	/// The last check: validation messages logged after the last capture was checked, as its frame finished and the window
	/// closed. Called once the window is gone. False, with the messages logged, when any aren't allowed.
	/// </summary>
	public static bool Finish()
	{
		var unexpected = UnexpectedValidation( out _ );
		if ( unexpected.Length == 0 ) return true;

		Log.Warning( $"Golden truth: FAILED: {unexpected.Length} validation message(s) after the last check" );
		foreach ( var message in unexpected.Take( 20 ) ) Log.Warning( $"Golden truth: validation: {message}" );
		return false;
	}

	/// <summary>
	/// The validation messages since the last check that <c>validation.jsonc</c> doesn't allow, and those it does. Each
	/// check takes them, so in a parity run a preset answers for what it and its loading logged.
	/// </summary>
	static string[] UnexpectedValidation( out string[] allowed )
	{
		// The render system logs from its own threads too, which the log queues until the main thread's next frame
		Sandbox.Diagnostics.Logging.PushQueuedMessages();

		string[] messages;
		lock ( validationMessages )
		{
			messages = validationMessages.Distinct().ToArray();
			validationMessages.Clear();
		}

		var path = System.IO.Path.Combine( Folder, "validation.jsonc" );
		var rules = System.IO.File.Exists( path ) ? JsonSerializer.Deserialize<AllowedValidation[]>( System.IO.File.ReadAllText( path ), RuleOptions ) ?? [] : [];
		var applying = rules.Where( x => x.Device is null || x.Device.Equals( Device, StringComparison.OrdinalIgnoreCase ) ).ToArray();

		bool IsAllowed( string message ) => applying.Any( x => !string.IsNullOrEmpty( x.Message ) && message.Contains( x.Message, StringComparison.Ordinal ) );
		allowed = messages.Where( IsAllowed ).ToArray();
		return messages.Where( x => !IsAllowed( x ) ).ToArray();
	}

	static Attempt Compare( Color32[] image, Color32[] golden, Thresholds.Bucket[] buckets )
	{
		// Pixels by their largest channel difference, alpha left out: the swap chain's alpha isn't part of the picture
		var histogram = new int[256];
		long squared = 0;
		for ( int i = 0; i < image.Length; i++ )
		{
			var a = image[i];
			var b = golden[i];
			int r = Math.Abs( a.r - b.r ), g = Math.Abs( a.g - b.g ), bl = Math.Abs( a.b - b.b );
			histogram[Math.Max( r, Math.Max( g, bl ) )]++;
			squared += r * r + g * g + bl * bl;
		}

		var mse = image.Length > 0 ? (double)squared / (image.Length * 3) : 0;
		return new Attempt
		{
			Compared = true,
			Passed = Thresholds.Check( histogram, buckets ),
			MaxDiff = Array.FindLastIndex( histogram, x => x > 0 ),
			Psnr = mse > 0 ? Math.Round( 10.0 * Math.Log10( 255.0 * 255.0 / mse ), 2 ) : null,
			Different = image.Length - histogram[0],
			Diffs = Thresholds.Bands( histogram ),
			Thresholds = buckets.ToDictionary( x => x.Key, x => x.Limit ),
			Counts = string.Join( ", ", buckets.Select( x => $"[{x.Key}] {Thresholds.Count( histogram, x )}/{x.Limit}" ) ),
		};
	}

	static Color32[] Pixels( string path, out int width, out int height )
	{
		using var bitmap = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( path ) );
		width = bitmap.Width;
		height = bitmap.Height;
		return bitmap.GetPixels32();
	}

	static void Save( Color32[] pixels, int width, int height, string path )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	}

	static Thresholds.Rule[] LoadRules( string path )
	{
		if ( !System.IO.File.Exists( path ) ) return [];
		return JsonSerializer.Deserialize<Thresholds.Rule[]>( System.IO.File.ReadAllText( path ), RuleOptions ) ?? [];
	}

	static readonly JsonSerializerOptions RuleOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	sealed class AllowedValidation
	{
		/// <summary>
		/// Part of the message, e.g. its VUID.
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// Only on this device bucket; any when null.
		/// </summary>
		public string Device { get; set; }

		/// <summary>
		/// Why it's allowed.
		/// </summary>
		public string Reason { get; set; }
	}

	sealed class Sidecar
	{
		public string Outcome { get; set; }
		public string Preset { get; set; }
		public string Image { get; set; }
		public string Bucket { get; set; }
		public string Gpu { get; set; }
		public string CpuFeatures { get; set; }
		public bool Validation { get; set; }
		public string Matched { get; set; }
		public List<Attempt> Attempts { get; set; }
		public string[] ValidationMessages { get; set; }
		public string[] AllowedValidationMessages { get; set; }
	}

	sealed class Attempt
	{
		public string GoldenTruth { get; set; }
		public bool Passed { get; set; }
		public int MaxDiff { get; set; }

		/// <summary>
		/// Null when the images are identical.
		/// </summary>
		public double? Psnr { get; set; }

		public int Different { get; set; }
		public string Error { get; set; }

		/// <summary>
		/// Pixels by largest channel difference: 1 to 15 each, then bands.
		/// </summary>
		public Dictionary<string, int> Diffs { get; set; }

		public Dictionary<string, int> Thresholds { get; set; }

		[JsonIgnore] public bool Compared { get; set; }
		[JsonIgnore] public string Counts { get; set; }

		// The closest golden truth image is the one the diff image is made against
		[JsonIgnore] public double Score => Compared ? -(Psnr ?? double.MaxValue) : double.MaxValue;

		public static Attempt SizeMismatch( int width, int height ) => new() { MaxDiff = 255, Error = $"golden truth image is {width}x{height}" };

		public override string ToString() => Error ?? $"max diff {MaxDiff}, {Different} pixels differ, PSNR {(Psnr is { } p ? $"{p:0.0} dB" : "inf")}, {Counts}";
	}
}

/// <summary>
/// Rules from <c>thresholds.jsonc</c>: how many pixels may be how far off. Each rule can name an image (with <c>*</c>
/// and <c>?</c> wildcards), platform, API and device; the most specific match wins, and with none, no pixel may be 3
/// or more off. <c>"allow": { "1-2": 500, "3+": 0 }</c> lets 500 pixels be 1 or 2 off and none more.
/// </summary>
internal static class Thresholds
{
	public sealed class Rule
	{
		public string Image { get; set; }
		public string Platform { get; set; }
		public string Api { get; set; }
		public string Device { get; set; }
		public Dictionary<string, int> Allow { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>
	/// At most <see cref="Limit"/> pixels whose largest channel difference is between <see cref="Min"/> and <see cref="Max"/>.
	/// </summary>
	public readonly record struct Bucket( int Min, int Max, int Limit )
	{
		public string Key => Max >= 255 ? $"{Min}+" : Min == Max ? $"{Min}" : $"{Min}-{Max}";

		public static Bucket Parse( string key, int limit )
		{
			if ( key.EndsWith( '+' ) ) return new Bucket( int.Parse( key[..^1] ), 255, limit );
			var parts = key.Split( '-' );
			return parts.Length == 2 ? new Bucket( int.Parse( parts[0] ), int.Parse( parts[1] ), limit ) : new Bucket( int.Parse( key ), int.Parse( key ), limit );
		}
	}

	public static readonly Bucket[] Default = [new Bucket( 3, 255, 0 )];

	public static Bucket[] Resolve( Rule[] rules, string image, string platform, string api, string device )
	{
		Rule best = null;
		var bestScore = -1;

		foreach ( var rule in rules )
		{
			var isDefault = rule.Image == "default";
			if ( rule.Image is not null && !isDefault && !Matches( rule.Image, image ) ) continue;
			if ( rule.Platform is not null && !rule.Platform.Equals( platform, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( rule.Api is not null && !rule.Api.Equals( api, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( rule.Device is not null && !rule.Device.Equals( device, StringComparison.OrdinalIgnoreCase ) ) continue;

			var score = isDefault ? 0 : (rule.Image is not null ? 1 : 0) + (rule.Platform is not null ? 2 : 0) + (rule.Api is not null ? 4 : 0) + (rule.Device is not null ? 8 : 0);

			// Later rules win a tie, so a bucket's own thresholds.jsonc beats the root's
			if ( score >= bestScore )
			{
				best = rule;
				bestScore = score;
			}
		}

		return best?.Allow is { Count: > 0 } allow ? allow.Select( x => Bucket.Parse( x.Key, x.Value ) ).ToArray() : Default;
	}

	static bool Matches( string pattern, string image )
	{
		if ( pattern.IndexOfAny( ['*', '?'] ) < 0 ) return pattern.Equals( image, StringComparison.OrdinalIgnoreCase );
		return Regex.IsMatch( image, "^" + Regex.Escape( pattern ).Replace( "\\*", ".*" ).Replace( "\\?", "." ) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );
	}

	public static bool Check( int[] histogram, Bucket[] buckets ) => buckets.All( x => Count( histogram, x ) <= x.Limit );

	/// <summary>
	/// The pixels in a bucket's range.
	/// </summary>
	public static int Count( int[] histogram, Bucket bucket )
	{
		var count = 0;
		for ( int d = Math.Max( bucket.Min, 0 ); d <= Math.Min( bucket.Max, histogram.Length - 1 ); d++ ) count += histogram[d];
		return count;
	}

	/// <summary>
	/// The histogram for the sidecar: 1 to 15 each, then 16-31, 32-63, 64-127 and 128+, only those that occur.
	/// </summary>
	public static Dictionary<string, int> Bands( int[] histogram )
	{
		var bands = new Dictionary<string, int>();
		void Add( int min, int max )
		{
			var count = 0;
			for ( int d = min; d <= max; d++ ) count += histogram[d];
			if ( count > 0 ) bands[new Bucket( min, max, 0 ).Key] = count;
		}

		for ( int d = 1; d < 16; d++ ) Add( d, d );
		Add( 16, 31 );
		Add( 32, 63 );
		Add( 64, 127 );
		Add( 128, 255 );
		return bands;
	}
}
