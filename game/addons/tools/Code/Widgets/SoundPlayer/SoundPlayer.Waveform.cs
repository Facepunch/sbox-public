namespace Editor;

public partial class SoundPlayer
{
	public class WaveForm : GraphicsItem
	{
		private struct Column
		{
			public float top;
			public float bottom;
			public bool clipping;
		}

		private readonly TimelineView TimelineView;
		private short[] Samples;
		private int Channels = 1;
		private float Duration;

		/// <summary>
		/// One column list per channel, analysed from the interleaved samples. Mono sounds have a
		/// single list and draw exactly as before.
		/// </summary>
		private List<Column>[] ChannelColumns;

		private int FramesPerColumn;

		const float LineWidth = 1;
		const float LineSpacing = 0;
		float LineSize => LineWidth + LineSpacing;

		public WaveForm( TimelineView view )
		{
			TimelineView = view;
			ZIndex = -1;
		}

		bool isDirty;

		protected override void OnPaint()
		{
			base.OnPaint();

			if ( isDirty )
			{
				Analyse();
			}

			Paint.Antialiasing = false;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( LocalRect );

			if ( ChannelColumns == null || ChannelColumns.Length == 0 )
				return;

			var height = LocalRect.Height;

			// mono draws solid - multi-channel overlays each channel translucently, so where the
			// channels agree the wave reads dense, and where they differ you see each channel's
			// own envelope
			float alpha = ChannelColumns.Length > 1 ? 0.55f : 1.0f;

			int start = (int)(TimelineView.VisibleRect.Left / LineSize);
			int end = (int)(TimelineView.VisibleRect.Right / LineSize);

			foreach ( var columns in ChannelColumns )
			{
				for ( int i = start; i <= end && i <= columns.Count - 1; ++i )
				{
					var line = columns[i];
					float lo = height * line.top;
					float hi = height * line.bottom;

					// columns that hit the rails draw red so clipping is obvious at a glance
					Paint.SetBrush( (line.clipping ? Theme.Red : Theme.Primary).WithAlpha( alpha ) );

					var r = new Rect( new Vector2( i * LineSize, hi ), new Vector2( LineWidth, Math.Max( 1, lo - hi ) ) );
					Paint.DrawRect( r );
				}
			}
		}

		/// <summary>
		/// Set the samples to analyse. Multi-channel samples are interleaved (L,R,L,R.. for stereo).
		/// </summary>
		public void SetSamples( short[] samples, float duration, int channels = 1 )
		{
			Samples = samples;
			Duration = duration;
			Channels = Math.Max( 1, channels );
			Width = Duration * LineSize;
			isDirty = true;
		}

		public void Analyse()
		{
			isDirty = false;

			ChannelColumns = null;

			if ( Samples == null || Samples.Length == 0 )
				return;

			int channelCount = Channels;
			int frameCount = Samples.Length / channelCount;
			if ( frameCount <= 0 )
				return;

			// normalize against full scale rather than the sound's own peak, so quiet sounds
			// draw small and loud sounds fill the view
			const int minVal = short.MaxValue;
			const int maxVal = -minVal;
			const float fRange = maxVal - minVal;

			int columns = MathX.FloorToInt( TimelineView.PositionFromTime( TimelineView.Duration ) / LineSize );
			if ( columns <= 1 )
				return;

			FramesPerColumn = Math.Max( 1, frameCount / columns );

			// a sample within 1% of full scale is treated as clipping
			int clipThreshold = (int)(short.MaxValue * 0.99f);

			ChannelColumns = new List<Column>[channelCount];

			for ( int ch = 0; ch < channelCount; ch++ )
			{
				var list = new List<Column>( columns );

				for ( int i = 0; i < columns - 1; i++ )
				{
					int start = i * FramesPerColumn;
					int end = (i + 1) * FramesPerColumn;

					float posAvg, negAvg;
					int columnPeak;
					averages( Samples, ch, channelCount, start, end, out posAvg, out negAvg, out columnPeak );

					list.Add( new Column
					{
						top = (negAvg - minVal) / fRange,
						bottom = (posAvg - minVal) / fRange,
						clipping = columnPeak >= clipThreshold
					} );
				}

				ChannelColumns[ch] = list;
			}

			Update();
		}

		private static void averages( short[] data, int channel, int channelCount, int startFrame, int endFrame, out float posAvg, out float negAvg, out int peak )
		{
			posAvg = 0.0f;
			negAvg = 0.0f;
			peak = 0;

			int posCount = 0, negCount = 0;

			for ( int f = startFrame; f < endFrame; f++ )
			{
				int i = f * channelCount + channel;
				if ( i >= data.Length )
					break;

				var sample = data[i];

				int abs = Math.Abs( (int)sample );
				if ( abs > peak )
					peak = abs;

				if ( sample > 0 )
				{
					posCount++;
					posAvg += sample;
				}
				else
				{
					negCount++;
					negAvg += sample;
				}
			}

			if ( posCount > 0 )
				posAvg /= posCount;
			if ( negCount > 0 )
				negAvg /= negCount;
		}
	}
}
