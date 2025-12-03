namespace Editor;

public partial class SoundPlayer
{
	public class WaveForm : GraphicsItem
	{
		private struct Column
		{
			public float top;
			public float bottom;
		}

		private readonly TimelineView TimelineView;
		private short[] Samples;
		private float Duration;
		private readonly List<Column> Columns = new();
		private short MinSample = short.MaxValue;
		private short MaxSample = short.MinValue;

		private int SamplesPerColumn;

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

			if ( Columns.Count > 0 )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary );

				var height = LocalRect.Height;

				int start = (int)(TimelineView.VisibleRect.Left / LineSize);
				int end = (int)(TimelineView.VisibleRect.Right / LineSize);
				for ( int i = start; i <= end && i <= Columns.Count - 1; ++i )
				{
					var line = Columns[i];
					float lo = height * line.top;
					float hi = height * line.bottom;

					var r = new Rect( new Vector2( i * LineSize, hi ), new Vector2( LineWidth, Math.Max( 1, lo - hi ) ) );
					Paint.DrawRect( r );
				}
			}
		}

		public void SetSamples( short[] samples, float duration )
		{
			Samples = samples;
			Duration = duration;
			Width = Duration * LineSize;
			isDirty = true;
		}

		public void Analyse()
		{
			isDirty = false;

			MinSample = short.MaxValue;
			MaxSample = short.MinValue;

			Columns.Clear();

			if ( Samples == null || Samples.Length == 0 )
				return;

			var sampleCount = Samples.Length;

			for ( int i = 0; i < sampleCount; i++ )
			{
				var sample = Samples[i];
				MinSample = Math.Min( sample, MinSample );
				MaxSample = Math.Max( sample, MaxSample );
			}

			int minVal = Math.Max( Math.Abs( (int)MinSample ), Math.Abs( (int)MaxSample ) );
			int maxVal = -minVal;

			float fRange = maxVal - minVal;

			int columns = MathX.FloorToInt( TimelineView.PositionFromTime( TimelineView.Duration ) / LineSize );
			SamplesPerColumn = Math.Max( 1, sampleCount / columns );

			for ( int i = 0; i < columns - 1; i++ )
			{
				int start = i * SamplesPerColumn;
				int end = (i + 1) * SamplesPerColumn;

				float posAvg, negAvg;
				averages( Samples, start, end, out posAvg, out negAvg );

				Columns.Add( new Column
				{
					top = fRange != 0.0f ? (negAvg - minVal) / fRange : 0.5f,
					bottom = fRange != 0.0f ? (posAvg - minVal) / fRange : 0.5f
				} );
			}

			Update();
		}

		private static void averages( short[] data, int startIndex, int endIndex, out float posAvg, out float negAvg )
		{
			posAvg = 0.0f;
			negAvg = 0.0f;

			int posCount = 0, negCount = 0;

			for ( int i = startIndex; i < endIndex && i < data.Length; i++ )
			{
				if ( data[i] > 0 )
				{
					posCount++;
					posAvg += data[i];
				}
				else
				{
					negCount++;
					negAvg += data[i];
				}
			}

			if ( posCount > 0 )
				posAvg /= posCount;
			if ( negCount > 0 )
				negAvg /= negCount;
		}
	}

}
