using Sandbox.TextureLoader;

namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_video", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Draws a video player debug overlay with decode load and queue depths." )]
	internal static int overlay_video { get; set; } = 0;

	public partial class Video
	{
		static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };
		static readonly List<VideoTextureLoader.ActivePlayer> _players = new();

		// Last totals per player, turned into a rate. Only a rate can be summed across decoders.
		class Sample
		{
			public double Time;
			public double DecodeSeconds;
			public int Frames;

			public float Busy;       // cores' worth of CPU
			public float MsPerFrame;
			public float Fps;
		}

		static readonly Dictionary<VideoPlayer, Sample> _samples = new();

		const float SampleInterval = 0.25f;

		internal static void Draw( ref Vector2 pos )
		{
			var drawPos = new Vector2( pos.x + 24, pos.y );
			var startY = drawPos.y;

			_players.Clear();
			VideoTextureLoader.GetActive( _players );

			Update();

			_players.Sort( static ( a, b ) => b.Presenting.CompareTo( a.Presenting ) );

			var presenting = 0;
			var busy = 0f;
			var idleBusy = 0f;

			foreach ( var entry in _players )
			{
				if ( !_samples.TryGetValue( entry.Player, out var sample ) )
					continue;

				busy += sample.Busy;

				if ( entry.Presenting ) presenting++;
				else idleBusy += sample.Busy;
			}

			var cores = Math.Max( 1, Environment.ProcessorCount );
			var wastedShare = busy > 0f ? idleBusy / busy * 100f : 0f;

			Header( ref drawPos, "Video" );
			RowStr( ref drawPos, "Players", $"{_players.Count} ({presenting} presenting)" );
			RowStr( ref drawPos, "Threads", $"{_players.Count * 3}" );

			// CPU-milliseconds burnt per second of wall time, and what that is out of the box.
			RowStr( ref drawPos, "Decode CPU", $"{busy * 1000f:F0} ms/s  ({busy:F2} cores)" );
			RowStr( ref drawPos, "Share of CPU", $"{busy / cores * 100f:F1}% of {cores} logical" );

			// Decoding for players nothing is drawing.
			RowStr( ref drawPos, "Wasted", $"{idleBusy * 1000f:F0} ms/s  ({wastedShare:F0}% of decode)" );
			drawPos.y += 6;

			Header( ref drawPos, "Per Player" );

			foreach ( var entry in _players )
			{
				PlayerRow( ref drawPos, entry );
			}

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static void Update()
		{
			var now = RealTime.Now;

			foreach ( var entry in _players )
			{
				var player = entry.Player;
				var decodeSeconds = player.VideoDecodeSeconds + player.AudioDecodeSeconds;
				var frames = player.DecodedFrames;

				if ( !_samples.TryGetValue( player, out var sample ) )
				{
					_samples[player] = new Sample { Time = now, DecodeSeconds = decodeSeconds, Frames = frames };
					continue;
				}

				var elapsed = now - sample.Time;
				if ( elapsed < SampleInterval )
					continue;

				var decoded = decodeSeconds - sample.DecodeSeconds;
				var frameCount = frames - sample.Frames;

				sample.Busy = (float)(decoded / elapsed);
				sample.Fps = (float)(frameCount / elapsed);
				sample.MsPerFrame = frameCount > 0 ? (float)(decoded / frameCount * 1000.0) : 0f;

				sample.Time = now;
				sample.DecodeSeconds = decodeSeconds;
				sample.Frames = frames;
			}

			if ( _samples.Count > _players.Count )
			{
				foreach ( var player in _samples.Keys.Where( x => !_players.Any( y => y.Player == x ) ).ToArray() )
					_samples.Remove( player );
			}
		}

		static void PlayerRow( ref Vector2 pos, VideoTextureLoader.ActivePlayer entry )
		{
			var player = entry.Player;

			_samples.TryGetValue( player, out var sample );
			sample ??= new Sample();

			var value = $"{player.Width}x{player.Height}  " +
						$"{sample.Busy * 100f,3:F0}%  " +
						$"{sample.MsPerFrame:F1}ms/f  " +
						$"{sample.Fps:F0}fps  " +
						$"pkt {player.PacketQueueDepth}  " +
						$"frm {player.FrameQueueDepth}";

			var rect = new Rect( pos, new Vector2( 760, 15 ) );
			var scope = new TextRendering.Scope( ShortName( entry.Url ), Color.White.WithAlpha( 0.8f ), 13, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 220 }, TextFlag.RightCenter );

			scope.TextColor = entry.Presenting ? Color.White : new Color( 1f, 0.55f, 0.35f );
			scope.Text = value;
			Hud.DrawText( scope, rect with { Left = rect.Left + 228, Width = 520 }, TextFlag.LeftCenter );

			pos.y += rect.Height;
		}

		static string ShortName( string url )
		{
			if ( string.IsNullOrEmpty( url ) )
				return "?";

			var parts = url.Split( '?' )[0].Split( '/' );

			return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : parts[^1];
		}

		static void Header( ref Vector2 pos, string label )
		{
			var rect = new Rect( pos, new Vector2( 560, 18 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.9f ), 13, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			pos.y += 18;
		}

		static void RowStr( ref Vector2 pos, string label, string value )
		{
			var rect = new Rect( pos, new Vector2( 560, 15 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 13, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 160 }, TextFlag.RightCenter );
			scope.TextColor = Color.White;
			scope.Text = value;
			Hud.DrawText( scope, rect with { Left = rect.Left + 168, Width = 300 }, TextFlag.LeftCenter );
			pos.y += rect.Height;
		}
	}
}
