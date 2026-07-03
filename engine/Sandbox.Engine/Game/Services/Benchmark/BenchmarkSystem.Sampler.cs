namespace Sandbox.Services;

public partial class BenchmarkSystem
{
	internal class Sampler
	{
		public struct Result
		{
			public string Name { get; set; }
			public double Min { get; set; }
			public double Max { get; set; }
			public double Avg { get; set; }
			public double Sum { get; set; }
			/// <summary>
			/// The value below which 5% of the samples fall (the 5th percentile).
			/// </summary>
			public double P5 { get; set; }
			/// <summary>
			/// The value below which 50% of the samples fall (the median). Robust to the
			/// hitch tail — prefer this over <see cref="Avg"/> when comparing the typical frame.
			/// </summary>
			public double P50 { get; set; }
			/// <summary>
			/// The value below which 95% of the samples fall (the 95th percentile).
			/// </summary>
			public double P95 { get; set; }
			/// <summary>
			/// The value below which 99% of the samples fall (the 99th percentile).
			/// </summary>
			public double P99 { get; set; }
			/// <summary>
			/// The value below which 99.9% of the samples fall (the 99.9th percentile).
			/// </summary>
			public double P99_9 { get; set; }
			/// <summary>
			/// This is the sum of all samples that exceeded the stuttering threshold.
			/// May not be very useful for non time based samples.
			/// </summary>
			public double Stuttering { get; set; }
			public double Count { get; set; }
		}

		public string Name { get; private set; }

		private List<double> samples;

		public delegate double GetSampleValueDelegate();
		private GetSampleValueDelegate getSampleValue;

		public delegate uint GetSampleKeyDelegate();
		private GetSampleKeyDelegate getSampleKey;

		private uint lastSampleKey;

		public Sampler( string name, GetSampleValueDelegate getSampleValue, GetSampleKeyDelegate getSampleKey = null )
		{
			//Debug.Assert( getSampleValue != null, "No action provided to sampler" );
			this.getSampleValue = getSampleValue;
			this.getSampleKey = getSampleKey;

			samples = new List<double>();
			Name = name;
		}

		public void Update()
		{
			// for samplers that are a value tied to a key, skip the sample if the key has not changed
			// (e.g. don't record duplicate GPU frametimes for a frame, where frame # is the key)
			if ( getSampleKey != null )
			{
				uint key = getSampleKey.Invoke();
				uint lastKey = lastSampleKey;

				lastSampleKey = key;
				if ( key == lastKey )
					return;
			}

			if ( getSampleValue != null )
				AddSample( getSampleValue.Invoke() );
		}

		public void Clear()
		{
			samples.Clear();
		}

		public void AddSample( double value )
		{
			samples.Add( value );
		}

		public bool HasValues => samples is not null;

		public Result GetResults()
		{
			return new Result()
			{
				Name = Name,
				Min = samples.Count() > 0 ? samples.Min() : 0,
				Max = samples.Count() > 0 ? samples.Max() : 0,
				Avg = samples.Count() > 0 ? samples.Average() : 0,
				Sum = samples.Count() > 0 ? samples.Sum() : 0,
				P5 = Percentile( 5 ),
				P50 = Percentile( 50 ),
				P95 = Percentile( 95 ),
				P99 = Percentile( 99 ),
				P99_9 = Percentile( 99.9 ),
				Stuttering = Stuttering( stutteringFactor: 2.5 ),
				Count = samples.Count()
			};
		}

		public string GetDisplayName()
		{
			return Name;
		}

		/// <summary>
		/// Calculates the specified percentile value from the collected samples.
		/// </summary>
		/// <param name="percentile">
		/// The percentile to compute (a value between 0 and 100).
		/// For example, passing 95 computes the 95th percentile.
		/// </param>
		/// <returns>
		/// The value below which the given percentage of samples fall.
		/// </returns>
		private double Percentile( double percentile )
		{
			var sortedSequence = samples.OrderBy( n => n ).ToList();
			double position = (sortedSequence.Count + 1) * percentile / 100.0;
			int index = (int)position - 1;
			double fraction = position - Math.Floor( position );

			if ( index + 1 < sortedSequence.Count )
			{
				return sortedSequence[index] + fraction * (sortedSequence[index + 1] - sortedSequence[index]);
			}
			else
			{
				return sortedSequence[index];
			}
		}

		/// <summary>
		/// Calculates the sum of all samples that exceeded the stuttering threshold.
		/// A sample is considered stuttering if it exceeds the moving average multiplied by the stuttering factor.
		/// </summary>
		/// <param name="stutteringFactor">
		/// The multiplier used to determine the stuttering threshold.
		/// A higher factor makes it less sensitive to spikes (default is typically 2.5).
		/// </param>
		private double Stuttering( double stutteringFactor )
		{
			var averageSequence = GetMovingAverage();
			double stuttering = 0.0;

			for ( int i = 0; i < samples.Count; i++ )
			{
				if ( samples[i] > stutteringFactor * averageSequence[i] )
				{
					stuttering += samples[i];
				}
			}

			return stuttering;
		}

		private IList<double> GetMovingAverage()
		{
			int windowSize = Math.Max( 1, (int)(Math.Sqrt( samples.Count ) * 10) );
			var movingAverage = new List<double>( samples.Count );

			for ( int i = 0; i < samples.Count; i++ )
			{
				int start = Math.Max( 0, i - windowSize / 2 );
				int end = Math.Min( samples.Count - 1, i + windowSize / 2 );

				double sum = 0.0;

				for ( int j = start; j <= end; j++ )
				{
					sum += samples[j];
				}

				double average = sum / (end - start + 1);
				movingAverage.Add( average );
			}

			return movingAverage;
		}
	}
}

