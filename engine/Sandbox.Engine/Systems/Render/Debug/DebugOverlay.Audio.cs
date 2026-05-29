using Sandbox.Audio;

namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_audio", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Draws an audio debug overlay with HUD stats and world-space sound markers." )]
	internal static int overlay_audio { get; set; } = 0;

	public partial class Audio
	{
		static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };
		static readonly TextRendering.Outline _statusOutline = new() { Color = Color.Black, Size = 3, Enabled = true };
		static readonly List<SoundHandle> _handles = new();

		internal static void Draw( ref Vector2 pos )
		{
			var scene = Application.GetActiveScene();
			var drawPos = new Vector2( pos.x + 24, pos.y );
			var startY = drawPos.y;

			_handles.Clear();
			SoundHandle.GetActive( _handles );
			FilterToScene( _handles, scene );
			_handles.Sort( static ( a, b ) => a._CreatedTime.CompareTo( b._CreatedTime ) );

			var listener = GetPrimaryListener( scene );

			Header( ref drawPos, "Audio" );

			Header( ref drawPos, "Mixers" );
			if ( Mixer.Master is { } master )
				MixerRow( ref drawPos, master, 0 );
			drawPos.y += 6;

			Header( ref drawPos, "Performance" );
			RowStr( ref drawPos, "Sim update", $"{SoundSimulationSystem.LastSimUpdateMs:F2} ms" );
			RowStr( ref drawPos, "Mix thread", $"{MixingThread.AverageMixTimeMs:F2} ms avg" );
			RowStr( ref drawPos, "Occ avg wait", $"{SoundSimulationSystem.AvgOccWaitFrames:F1} frames" );
			RowStr( ref drawPos, "Room avg wait", $"{SoundSimulationSystem.AvgRoomWaitFrames:F1} frames" );
			drawPos.y += 6;

			Header( ref drawPos, "Sounds" );
			CountRows( ref drawPos );
			drawPos.y += 6;

			ReverbSection( ref drawPos );
			SurfaceProbeSection( ref drawPos, scene, listener );
			SoundTable( ref drawPos, listener );

			DrawStatusOverlay();

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static void FilterToScene( List<SoundHandle> handles, Scene scene )
		{
			if ( scene is null ) return;

			for ( int i = handles.Count - 1; i >= 0; i-- )
			{
				var h = handles[i];
				if ( h.Scene is not null && h.Scene != scene )
					handles.RemoveAt( i );
			}
		}

		static Listener GetPrimaryListener( Scene scene )
		{
			foreach ( var l in Listener.ActiveList )
				if ( l.Scene == scene ) return l;
			return null;
		}

		static void MixerRow( ref Vector2 pos, Mixer mixer, int depth )
		{
			var frame = mixer.Meter.Current;
			var indent = depth * 14;
			var rect = new Rect( pos, new Vector2( 900, 16 ) );
			var scope = new TextRendering.Scope( mixer.Name ?? "?", Color.White.WithAlpha( 0.85f ), 13, "Roboto Mono", 600 ) { Outline = _outline };

			Hud.DrawText( scope, rect with { Left = rect.Left + indent, Width = 140 - indent }, TextFlag.LeftCenter );

			scope.Text = $"{frame.VoiceCount} voices";
			scope.TextColor = frame.VoiceCount > 0 ? Color.White : Color.White.WithAlpha( 0.4f );
			Hud.DrawText( scope, rect with { Left = rect.Left + 148, Width = 80 }, TextFlag.LeftCenter );

			scope.Text = LevelBar( frame.MaxLevel, 10 );
			scope.TextColor = LevelColor( frame.MaxLevel );
			Hud.DrawText( scope, rect with { Left = rect.Left + 236, Width = 120 }, TextFlag.LeftCenter );

			void Prop( float x, string label, float value, bool colorCode = false )
			{
				scope.Text = $"{label} {value:F2}";
				scope.TextColor = colorCode
					? OcclusionColor( value ).WithAlpha( 0.85f )
					: Color.White.WithAlpha( value < 1f ? 0.9f : 0.5f );
				Hud.DrawText( scope, rect with { Left = rect.Left + x, Width = 96 }, TextFlag.LeftCenter );
			}

			Prop( 368, "Vol", mixer.Volume );
			Prop( 472, "Spa", mixer.Spacializing );
			Prop( 576, "Dst", mixer.DistanceAttenuation );
			Prop( 680, "Occ", mixer.Occlusion, colorCode: true );
			Prop( 784, "Air", mixer.AirAbsorption );

			pos.y += rect.Height;

			foreach ( var child in mixer.GetChildren() )
				MixerRow( ref pos, child, depth + 1 );
		}

		static void CountRows( ref Vector2 pos )
		{
			int total = 0, local = 0, occluded = 0, airAbs = 0;

			foreach ( var h in _handles )
			{
				total++;
				if ( h.ListenLocal ) local++;
				if ( h.Occlusion ) occluded++;
				if ( h.AirAbsorption ) airAbs++;
			}

			Row( ref pos, "Total", total );
			Row( ref pos, "Listen Local", local );
			Row( ref pos, "Occlusion", occluded );
			Row( ref pos, "Air Absorption", airAbs );
		}

		static void ReverbSection( ref Vector2 pos )
		{
			var sys = SoundSimulationSystem.Current;
			if ( sys is null ) return;
			var snap = sys.ListenerRoom;
			if ( snap.MfpMeters <= 0f ) return;

			pos.y += 6;
			Header( ref pos, "Listener Room" );

			var mt = snap.MaterialTone;
			float dc = snap.MfpMeters * MathF.Sqrt( snap.MfpMeters / MathF.Max( 90f * snap.DecayTime, 0.001f ) );
			RowStr( ref pos, "T60 L / M / H", $"{snap.DecayTimeLow:F2}s  /  {snap.DecayTime:F2}s  /  {snap.DecayTimeHigh:F2}s" );
			RowStr( ref pos, "Critical Distance", $"{dc:F2}m" );
			RowStr( ref pos, "Mean Free Path", $"{snap.MfpMeters:F2}m" );
			RowStr( ref pos, "Openness", $"{snap.Openness:F2}" );
			RowStr( ref pos, "Material Tone L/M/H", $"{mt.Low:F2} / {mt.Mid:F2} / {mt.High:F2}" );
			pos.y += 6;
		}

		internal static void DrawStatusOverlay()
		{
			float x = Screen.Width - 440f;
			float y = 20f;
			const float LineH = 44f;
			const int FontSize = 32;

			StatusLine( ref y, x, LineH, FontSize, "Reverb",
				SoundSimulationSystem.snd_reverb_enable );
			StatusLine( ref y, x, LineH, FontSize, "Occlusion",
				SoundSimulationSystem.snd_occlusion_enable );
			StatusLine( ref y, x, LineH, FontSize, "Diffraction",
				SoundSimulationSystem.snd_diffraction_enable );
		}

		static void StatusLine( ref float y, float x, float lineH, int fontSize, string label, bool on )
		{
			var rect = new Rect( x, y, 420f, lineH );
			var color = on ? new Color( 0.35f, 1f, 0.45f ) : new Color( 1f, 0.35f, 0.35f );
			var scope = new TextRendering.Scope( $"{label}:  {(on ? "ON" : "OFF")}", color, fontSize, "Roboto Mono", 700 ) { Outline = _statusOutline };
			Hud.DrawText( scope, rect, TextFlag.RightCenter );
			y += lineH;
		}

		static void RowStr( ref Vector2 pos, string label, string value )
		{
			var rect = new Rect( pos, new Vector2( 560, 15 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 13, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 160 }, TextFlag.RightCenter );
			scope.TextColor = Color.White;
			scope.Text = value;
			Hud.DrawText( scope, rect with { Left = rect.Left + 168, Width = 120 }, TextFlag.LeftCenter );
			pos.y += rect.Height;
		}

		static void SoundTable( ref Vector2 pos, Listener listener )
		{
			Header( ref pos, "Active Sounds" );

			int count = Math.Min( _handles.Count, 24 );
			for ( int i = 0; i < count; i++ )
				SoundRow( ref pos, i + 1, _handles[i], listener );
		}

		static void SoundRow( ref Vector2 pos, int index, SoundHandle handle, Listener listener )
		{
			var mixerName = handle.GetEffectiveMixer()?.Name ?? "—";
			var name = string.IsNullOrEmpty( handle.Name ) ? "—" : handle.Name;
			if ( name.Length > 20 ) name = name[..20];

			var model = listener is not null ? handle.GetDirectSoundModel( listener ) : null;
			var tx = handle.Occlusion && model is not null ? model.SmoothedTransmission : null;
			var diff = handle.Occlusion && model is not null ? model.SmoothedDiffraction : null;

			TableRow( ref pos, index.ToString(), name, $"[{mixerName}]", Flags( handle, model ), 12, 400 );

			if ( tx.HasValue )
			{
				SubRow( ref pos, "Occlusion",
						$"Low {tx.Value.Low:F2}  Mid {tx.Value.Mid:F2}  High {tx.Value.High:F2}  Walls {model.AvgWalls:F1}",
					OcclusionColor( tx.Value.Mid ) );
			}

			if ( diff.HasValue )
			{
				SubRow( ref pos, "Diffraction",
						$"Low {diff.Value.Low:F2}  Mid {diff.Value.Mid:F2}  High {diff.Value.High:F2}  Probes {model.LastDiffractionProbes}/{model.LastDiffractionRays}",
					OcclusionColor( diff.Value.Mid ) );
			}

			var sourceRoom = handle.SourceRoom;
			if ( sourceRoom.MfpMeters > 0f && !handle.ListenLocal )
			{
				float decayT = (sourceRoom.DecayTime / 3f).Clamp( 0f, 1f );
				var reverbColor = Color.Lerp( new Color( 0.4f, 1f, 1f ), new Color( 1f, 0.45f, 1f ), decayT );
				float dc = sourceRoom.MfpMeters * MathF.Sqrt( sourceRoom.MfpMeters / MathF.Max( 90f * sourceRoom.DecayTime, 0.001f ) );
				float distMeters = listener is not null ? handle.Position.Distance( listener.Position ) / 39.37f : 0f;
				SubRow( ref pos, "Reverb",
					$"T60 {sourceRoom.DecayTimeLow:F2}/{sourceRoom.DecayTime:F2}/{sourceRoom.DecayTimeHigh:F2}s    Dc {dc:F2}m  dist {distMeters:F2}m    Mix {sourceRoom.Mix:F2}",
					reverbColor );
			}
		}

		static void SubRow( ref Vector2 pos, string label, string value, Color color )
		{
			var subRect = new Rect( pos, new Vector2( 660, 14 ) );
			var sub = new TextRendering.Scope( label, color, 12, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( sub, subRect with { Left = subRect.Left + 24, Width = 100 }, TextFlag.LeftCenter );
			sub.FontWeight = 400;
			sub.Text = value;
			Hud.DrawText( sub, subRect with { Left = subRect.Left + 132, Width = 512 }, TextFlag.LeftCenter );
			pos.y += 14;
		}

		static void TableRow( ref Vector2 pos, string col0, string col1, string col2, string col3,
			int fontSize, int fontWeight )
		{
			var rect = new Rect( pos, new Vector2( 660, 15 ) );
			var scope = new TextRendering.Scope( "", Color.White.WithAlpha( 0.8f ), fontSize, "Roboto Mono", fontWeight ) { Outline = _outline };

			scope.Text = col0; Hud.DrawText( scope, rect with { Width = 20 }, TextFlag.LeftCenter );
			scope.Text = col1; Hud.DrawText( scope, rect with { Left = rect.Left + 24, Width = 160 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.5f );
			scope.Text = col2; Hud.DrawText( scope, rect with { Left = rect.Left + 192, Width = 80 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.6f );
			scope.Text = col3; Hud.DrawText( scope, rect with { Left = rect.Left + 280, Width = 380 }, TextFlag.LeftCenter );

			pos.y += rect.Height;
		}

		static string Flags( SoundHandle h, DirectSoundModel model )
		{
			var sb = new System.Text.StringBuilder();
			void Add( string s ) { if ( sb.Length > 0 ) sb.Append( ' ' ); sb.Append( s ); }
			if ( h.Occlusion ) Add( "Occluded" );
			if ( h.AirAbsorption ) Add( "Air Absorption" );
			if ( h.ListenLocal ) Add( "Listen Local" );
			if ( h.Paused ) Add( "Paused" );
			return sb.Length > 0 ? sb.ToString() : "—";
		}

		static string LevelBar( float level, int segments )
		{
			// dB scale: -60 dB (very quiet) → 0 dB (full).
			float db = level > 1e-6f ? 20f * MathF.Log10( level ) : -80f;
			float t = MathX.Remap( db, -60f, 0f, 0f, 1f ).Clamp( 0f, 1f );
			int filled = (int)(t * segments);
			return new string( '█', filled ) + new string( '░', segments - filled );
		}

		static Color LevelColor( float level ) => level switch
		{
			> 0.5f => new Color( 1f, 0.3f, 0.3f ),  // > -6 dB
			> 0.1f => new Color( 1f, 0.7f, 0.2f ),  // > -20 dB
			> 0f => new Color( 0.4f, 0.9f, 0.4f ),
			_ => Color.White.WithAlpha( 0.3f )
		};

		internal static Color OcclusionColor( float occ ) => occ switch
		{
			> 0.8f => Color.White,
			> 0.4f => new Color( 1f, 0.7f, 0.2f ),
			_ => new Color( 1f, 0.35f, 0.35f )
		};

		static void Header( ref Vector2 pos, string label )
		{
			var rect = new Rect( pos, new Vector2( 560, 18 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.9f ), 13, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			pos.y += 18;
		}

		static void Row( ref Vector2 pos, string label, int value )
		{
			var rect = new Rect( pos, new Vector2( 560, 15 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 13, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 160 }, TextFlag.RightCenter );
			scope.TextColor = value > 0 ? Color.White : Color.White.WithAlpha( 0.5f );
			scope.Text = value.ToString( "N0" );
			Hud.DrawText( scope, rect with { Left = rect.Left + 168, Width = 80 }, TextFlag.LeftCenter );
			pos.y += rect.Height;
		}

		static void SurfaceProbeSection( ref Vector2 pos, Scene scene, Listener listener )
		{
			if ( listener is null ) return;

			var world = scene?.PhysicsWorld;
			if ( world is null || !world.IsValid() ) return;

			var origin = listener.Transform.Position;
			var forward = listener.Transform.Rotation.Forward;

			var tr = world.Trace.FromTo( origin, origin + forward * 8192f )
				.WithoutTags( "player", "trigger" )
				.Run();

			pos.y += 6;
			Header( ref pos, "Looking At" );

			if ( !tr.Hit )
			{
				RowStr( ref pos, "Surface", "(nothing)" );
				return;
			}

			var surface = tr.Surface?.AudioSurface ?? AudioSurface.Generic;
			var surfName = tr.Surface?.ResourceName ?? "(none)";
			var tx = AcousticMaterial.GetTransmission( surface );
			var refl = AcousticMaterial.GetReflectivity( surface );
#pragma warning disable CS0618
			var tags = tr.Tags is { Length: > 0 } ? string.Join( ", ", tr.Tags ) : "(none)";
#pragma warning restore CS0618
			var dist = tr.HitPosition.Distance( origin );

			RowStr( ref pos, "Distance", $"{dist / 39.37f:F2} m" );
			RowStr( ref pos, "Physics surf", surfName );
			RowStr( ref pos, "Audio surface", $"{surface}" );
			RowStr( ref pos, "Tags", tags );
			RowStr( ref pos, "Transmission", $"Low:{tx.Low:F2}  Mid:{tx.Mid:F2}  High:{tx.High:F2}" );
			RowStr( ref pos, "Reflectivity", $"Low:{refl.Low:F2}  Mid:{refl.Mid:F2}  High:{refl.High:F2}" );
		}
	}
}

// Draws 3D world-space markers for active sounds and handles demo-cycle convars.
internal sealed class AudioDebugWorldSystem : GameObjectSystem<AudioDebugWorldSystem>
{
	static readonly List<SoundHandle> _handles = new();

	public AudioDebugWorldSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, DrawWorldMarkers, "AudioDebugWorldMarkers" );
	}

	void DrawWorldMarkers()
	{
		if ( DebugOverlay.overlay_audio == 0 ) return;

		var dbg = Scene.GetSystem<DebugOverlaySystem>();
		if ( dbg is null ) return;

		Sandbox.Audio.Listener listener = null;
		foreach ( var l in Sandbox.Audio.Listener.ActiveList )
			if ( l.Scene == Scene ) { listener = l; break; }

		_handles.Clear();
		SoundHandle.GetActive( _handles );

		foreach ( var handle in _handles )
		{
			if ( handle.Scene is null || handle.Scene != Scene ) continue;
			if ( handle.ListenLocal ) continue;

			var model = handle.Occlusion && listener is not null ? handle.GetDirectSoundModel( listener ) : null;
			var tx = model?.SmoothedTransmission;
			var diff = model?.SmoothedDiffraction;

			float mid = tx?.Mid ?? 1f;
			// Spectral color: R=low-freq, G=mid, B=high-freq transmission.
			// White = all bands open. Orange/red = only bass passes (thick wall). Black = fully blocked.
			var sphereColor = tx.HasValue
				? new Color( tx.Value.Low, tx.Value.Mid, tx.Value.High )
				: Color.White;

			// Text uses severity color (always readable) — spectral color is for the sphere only.
			var textColor = DebugOverlay.Audio.OcclusionColor( mid );

			// Skip sounds very close to the listener (footsteps, etc.) to avoid clutter.
			bool atListener = listener is not null && Vector3.DistanceBetween( handle.Position, listener.Position ) < 80f;

			// Sounds that are nearly fully blocked: draw a small red X instead of all the sphere visuals.
			bool fullyOccluded = tx.HasValue && mid < 0.05f;
			if ( fullyOccluded && !atListener )
			{
				const float XHalf = 6f;
				var xColor = new Color( 1f, 0.15f, 0.15f );
				var p = handle.Position;
				dbg.Line( p + new Vector3( -XHalf, -XHalf, 0 ), p + new Vector3( XHalf, XHalf, 0 ), xColor, overlay: true );
				dbg.Line( p + new Vector3( XHalf, -XHalf, 0 ), p + new Vector3( -XHalf, XHalf, 0 ), xColor, overlay: true );
				continue;
			}

			if ( !atListener )
				dbg.Sphere( new Sphere( handle.Position, 8f ), sphereColor, overlay: true );

			// Occlusion cage: a second sphere that grows outward with blockage — like walls closing in.
			// Faint when barely occluded, large red ring when heavily blocked.
			if ( tx.HasValue && !atListener && mid < 0.95f )
			{
				float blockage = 1f - mid;
				float cageRadius = 8f + blockage * 20f;
				var cageColor = Color.Lerp( new Color( 1f, 0.55f, 0.1f ), new Color( 1f, 0.1f, 0.1f ), blockage ).WithAlpha( blockage * 0.7f );
				dbg.Sphere( new Sphere( handle.Position, cageRadius ), cageColor, overlay: true );
			}

			// Reverb room sphere
			var room = handle.SourceRoom;
			Color reverbSphereColor = default;
			if ( room.Mix > 0f && !atListener )
			{
				const float UnitsPerMeter = 39.37f;
				float reverbRadius = room.MfpMeters * UnitsPerMeter;
				float reverbAlpha = (room.Mix * 2f).Clamp( 0.06f, 0.45f );
				float decayT = (room.DecayTime / 3f).Clamp( 0f, 1f );
				reverbSphereColor = Color.Lerp( new Color( 0.4f, 1f, 1f ), new Color( 1f, 0.45f, 1f ), decayT );
				dbg.Sphere( new Sphere( handle.Position, reverbRadius ), reverbSphereColor.WithAlpha( reverbAlpha ), overlay: true );
			}

			// 3D text — one line per feature, each colored to match its world-space visual.
			float textDist = listener is not null ? Vector3.DistanceBetween( handle.Position, listener.Position ) : 0f;
			if ( !atListener && textDist < 2048f )
			{
				const float LineH = 9f;
				var p = handle.Position + Vector3.Up * 14f;
				int line = 0;

				if ( !string.IsNullOrEmpty( handle.Name ) )
					dbg.Text( p + Vector3.Up * (LineH * line++), handle.Name, 18, color: textColor, overlay: true );

				if ( tx.HasValue )
					dbg.Text( p + Vector3.Up * (LineH * line++), $"Occ  Low:{tx.Value.Low:F2}  Mid:{tx.Value.Mid:F2}  High:{tx.Value.High:F2}  Walls:{model.AvgWalls:F1}", 18, color: textColor, overlay: true );

				if ( diff.HasValue )
					dbg.Text( p + Vector3.Up * (LineH * line++), $"Diff Low:{diff.Value.Low:F2}  Mid:{diff.Value.Mid:F2}  High:{diff.Value.High:F2}", 18, color: textColor, overlay: true );

				if ( room.MfpMeters > 0f )
					dbg.Text( p + Vector3.Up * (LineH * line), $"Reverb  Decay:{room.DecayTime:F2}s  Mix:{room.Mix:F2}  Openness:{room.Openness:F2}", 18, color: reverbSphereColor, overlay: true );
			}
		}
	}
}
