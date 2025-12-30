using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot;
using System;
using System.Collections.Generic;
using Sandbox.Test;

namespace MathTest;

#nullable enable

[TestClass]
public sealed class SpringDamperTest
{
	private static IReadOnlyList<InitialCondition> InitialConditions { get; } =
	[
		// At rest
		new ( 0f, 0f, 0f ),

		// Displaced 1u above target
		new ( 1f, 0f, 0f ),
		
		// Displaced 1u below target
		new ( -1f, 0f, 0f ),

		// High initial velocity
		new ( 0f, 0f, 100f ),
		
		// High initial velocity, displaced 1u above target
		new ( 1f, 0f, 100f ),
		
		// High initial velocity, displaced 1u below target
		new ( -1f, 0f, 100f ),

		// Displaced 1u below non-zero target
		new ( 0f, 1f, 0f ),
		
		// At rest at non-zero target
		new ( 1f, 1f, 0f ),

		// Displaced 2u below non-zero target
		new ( -1f, 1f, 0f )
	];

	/// <summary>
	/// When comparing to the legacy implementation, what update rate should we use for it.
	/// </summary>
	private const float ReferenceUpdateRate = 1_000f;

	private static IReadOnlyList<float> UpdateRates { get; } =
	[
		240f, 60f, 30f, 15f, 5f
	];

	/// <summary>
	/// <see cref="SpringDamper.FromSmoothingTime"/>, at any update rate, should closely match the behaviour of the legacy
	/// SmoothDamp method when it was running at a high update rate.
	/// </summary>
	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.5f )]
	[DataRow( 1f )]
	[DataRow( 10f )]
	public void SmoothDampTest( float smoothTime )
	{
		var model = SpringDamper.FromSmoothingTime( smoothTime );
		var legacyModel = new LegacySmoothDamperModel( smoothTime );

		var plotModels = DampTest( WithTarget( model.Simulate ), WithTarget( legacyModel.Simulate ) );

		if ( !plotModels.All( x => x.Pass ) )
		{
			TestContext.AddResultPlot( $"smooth_{smoothTime}.svg",
				plotModels.Select( x => x.PlotModel ).ToArray(), maxCols: 3 );
		}

		foreach ( var (plotModel, pass) in plotModels )
		{
			Assert.IsTrue( pass, plotModel.Title );
		}
	}

	/// <summary>
	/// <see cref="SpringDamper.FromDamping"/>, at any update rate, should closely match the behaviour of the legacy
	/// SpringDamp method when it was running at a high update rate.
	/// </summary>
	[TestMethod]
	[DataRow( 0f, 0.5f )]
	[DataRow( 2f, 0.5f )]
	[DataRow( 2f, 0f )]
	[DataRow( 12f, 0f )]
	[DataRow( 12f, 0.125f )]
	public void SpringDampTest( float frequency, float damping )
	{
		var model = SpringDamper.FromDamping( frequency, damping );
		var legacyModel = new LegacySpringDamperModel( frequency, damping );

		var plotModels = DampTest( WithTarget( model.Simulate ), WithTarget( legacyModel.Simulate ) );

		if ( !plotModels.All( x => x.Pass ) )
		{
			TestContext.AddResultPlot( $"spring_{frequency}Hz_{damping}.svg",
				plotModels.Select( x => x.PlotModel ).ToArray(), maxCols: 3 );
		}

		Assert.IsTrue( plotModels.All( x => x.Pass ) );
	}

	#region Plumbing

	public TestContext TestContext { get; set; } = null!;

	/// <summary>
	/// Old implementation of SpringDamp to compare with. This fell apart at high deltaTimes,
	/// and could explode to infinity if particularly unlucky with deltaTime.
	/// </summary>
	private readonly record struct LegacySpringDamperModel( float Frequency, float Damping )
	{
		public (float Position, float Velocity) Simulate( float position, float velocity, float deltaTime )
		{
			if ( deltaTime <= 0.0f ) return (position, velocity);

			// Angular frequency (how fast the spring oscillates)
			var omega = Frequency * MathF.PI * 2.0f;

			// Damping factor to control how much oscillation decays over time
			var dampingFactor = Damping * omega;

			// Compute the velocity using spring physics
			var force = omega * omega * -position - 2.0f * dampingFactor * velocity;
			velocity += force * deltaTime;

			// Update position
			return (position + velocity * deltaTime, velocity);
		}
	}

	/// <summary>
	/// Old implementation of SmoothDamp to compare with. This fell apart at high deltaTimes.
	/// </summary>
	private readonly record struct LegacySmoothDamperModel( float SmoothTime )
	{
		public (float Position, float Velocity) Simulate( float position, float velocity, float deltaTime )
		{
			// If smoothing time is zero, directly jump to target (independent of timestep)
			if ( SmoothTime <= 0.0f )
			{
				return (0f, velocity);
			}

			// If timestep is zero, stay at current position
			if ( deltaTime <= 0.0f )
			{
				return (position, velocity);
			}

			// Implicit integration of critically damped spring
			var omega = MathF.PI * 2.0f / SmoothTime;
			velocity = (velocity - (omega * omega) * deltaTime * position) / ((1.0f + omega * deltaTime) * (1.0f + omega * deltaTime));

			return (position + velocity * deltaTime, velocity);
		}
	}

	private delegate (float Position, float Velocity) DampUpdateFunc( float position, float target, float velocity, float deltaTime );

	/// <summary>
	/// Spring state to test.
	/// </summary>
	private record struct InitialCondition( float Position, float Target, float Velocity );

	/// <summary>
	/// Wraps a damping update function without a target parameter into one with such a parameter.
	/// </summary>
	private DampUpdateFunc WithTarget( Func<float, float, float, (float, float)> updateFunc ) =>
		( x, g, v, dt ) => { (x, v) = updateFunc( x - g, v, dt ); return (x + g, v); };

	/// <summary>
	/// Tests <paramref name="updateFunc"/>, comparing it to <paramref name="legacyUpdateFunc"/>, against all the test cases in
	/// <see cref="InitialConditions"/> and with each rate in <see cref="UpdateRates"/>.
	/// </summary>
	private IReadOnlyList<(PlotModel PlotModel, bool Pass)> DampTest( DampUpdateFunc updateFunc, DampUpdateFunc legacyUpdateFunc ) =>
		InitialConditions.Select( config => DampTest( config, updateFunc, legacyUpdateFunc ) ).ToArray();

	/// <summary>
	/// Compare a spring damper <paramref name="updateFunc"/> to a <paramref name="legacyUpdateFunc"/> with a particular initial condition
	/// of the spring, at all the update rates in <see cref="UpdateRates"/>. Returns a plot highlighting any differences, and whether
	/// the new function is within an error margin of the old function.
	/// </summary>
	private (PlotModel PlotModel, bool Pass) DampTest( InitialCondition config, DampUpdateFunc updateFunc, DampUpdateFunc legacyUpdateFunc )
	{
		var (position, target, velocity) = config;

		// Set up plot title, axes

		var plotModel = new PlotModel
		{
			Title = $"x₀: {position}, x₁: {target}, v₀: {velocity}"
		};

		plotModel.Axes.Add( new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0d, Maximum = 1d, Title = "Time (seconds)" } );
		plotModel.Axes.Add( new LinearAxis { Position = AxisPosition.Left, Title = "Position", Minimum = -3f, Maximum = 3f } );

		// Add a horizontal line for the target value

		plotModel.Series.Add( new LineSeries
		{
			Title = "Target",
			LegendKey = "general",
			Color = OxyColor.FromHsv( 0f, 0f, 0.5f ),
			LineStyle = LineStyle.Dash,
			StrokeThickness = 0.5f,
			Points =
			{
				new DataPoint( 0f, target ),
				new DataPoint( 1f, target )
			}
		} );

		// Run legacyUpdateFunc at ReferenceUpdateRate, plotting it as a grey line

		IReadOnlyList<Vector2> referenceData;

		{
			var series = new LineSeries
			{
				Title = "Legacy",
				LegendKey = "general",
				Color = OxyColor.FromHsv( 0f, 0f, 0.75f )
			};

			referenceData = DampTest( config, legacyUpdateFunc, ReferenceUpdateRate ).ToArray();

			series.Points.AddRange( referenceData.Select( p => new DataPoint( p.x, p.y ) ) );

			plotModel.Series.Add( series );
		}

		// Find Y axis min / max from reference values

		var min = referenceData.Min( p => p.y );
		var max = referenceData.Max( p => p.y );

		{
			var margin = System.Math.Max( (max - min) * 0.125f, 0.1f );

			plotModel.Axes[1].Minimum = min - margin;
			plotModel.Axes[1].Maximum = max + margin;
		}

		// 2% error margin

		const float errorMargin = 0.02f;
		const float timeErrorMargin = errorMargin;

		var valueErrorMargin = System.Math.Max( (max - min) * errorMargin, errorMargin );

		// We'll draw circles around any values outside the error margin

		var errorColor = OxyColor.FromHsv( 0f, 1f, 0.75f );

		var errors = new ScatterSeries
		{
			Title = "Errors",
			LegendKey = "general",
			MarkerType = MarkerType.Circle,
			MarkerSize = 8f,
			MarkerStroke = OxyColor.FromArgb( 0, 255, 255, 255 ),
			MarkerFill = errorColor
		};

		// Draw legacyUpdateFunc at a low update rate, so we can see how much it diverged

		{
			const float failingUpdateRate = 15f;

			var series = new ScatterSeries
			{
				Title = $"Legacy {failingUpdateRate}Hz",
				LegendKey = "update-rates",
				MarkerType = MarkerType.Plus,
				MarkerSize = 4f,
				MarkerStrokeThickness = 2f,
				MarkerStroke = OxyColor.FromHsv( 0f, 0f, 0.5f )
			};

			series.Points.AddRange( DampTest( config, legacyUpdateFunc, failingUpdateRate ).Select( p => new ScatterPoint( p.x, p.y ) ) );

			plotModel.Series.Add( series );
		}

		var pass = true;

		// Run updateFunc at each update rate, compare to referenceData

		for ( var i = 0; i < UpdateRates.Count; ++i )
		{
			var updateRate = UpdateRates[i];
			var data = DampTest( config, updateFunc, updateRate ).ToArray();
			var colorFraction = i / System.Math.Max( UpdateRates.Count - 1d, 1d );

			var series = new ScatterSeries
			{
				Title = $"{updateRate}Hz",
				LegendKey = "update-rates",
				MarkerType = MarkerType.Cross,
				MarkerSize = 2f + colorFraction * 2f,
				MarkerStrokeThickness = 2f,
				MarkerStroke = OxyColor.FromHsv( (1f - colorFraction) * 0.667f, 0.75, 0.9f )
			};

			series.Points.AddRange( data.Select( p => new ScatterPoint( p.x, p.y ) ) );

			plotModel.Series.Add( series );

			var outsideMargin = FindOutsideMargin( data, referenceData, ReferenceUpdateRate, timeErrorMargin, valueErrorMargin ).ToArray();

			if ( outsideMargin.Length <= 0 ) continue;

			pass = false;

			series.Title += " (Fail)";

			errors.Points.AddRange( outsideMargin.Select( p => new ScatterPoint( p.x, p.y ) ) );
		}

		// If there's any errors, add the error scatter series to the plot

		if ( !pass )
		{
			plotModel.Series.Insert( 0, errors );
			plotModel.TitleColor = errorColor;
		}

		// Set up the legends

		plotModel.Legends.Add( new Legend
		{
			Key = "general",
			LegendTitle = "Legend",
			LegendPosition = LegendPosition.RightTop,
			LegendBackground = OxyColor.FromArgb( 191, 255, 255, 255 ),
			LegendBorder = OxyColor.FromRgb( 0, 0, 0 ),
			AllowUseFullExtent = true
		} );

		plotModel.Legends.Add( new Legend
		{
			Key = "update-rates",
			LegendTitle = "Update Rates",
			LegendPosition = LegendPosition.BottomRight,
			LegendBackground = OxyColor.FromArgb( 191, 255, 255, 255 ),
			LegendBorder = OxyColor.FromRgb( 0, 0, 0 ),
			AllowUseFullExtent = true
		} );

		return (plotModel, pass);
	}

	/// <summary>
	/// Run the given <paramref name="updateFunc"/> at the given <paramref name="updateRate"/>, with a spring initial
	/// condition of <paramref name="config"/>.
	/// </summary>
	private IEnumerable<Vector2> DampTest( InitialCondition config, DampUpdateFunc updateFunc, float updateRate )
	{
		var (position, target, velocity) = config;

		var deltaTime = 1f / updateRate;

		yield return new( 0f, position );

		for ( var t = 0f; t <= 1f - deltaTime; t += deltaTime )
		{
			(position, velocity) = updateFunc( position, target, velocity, deltaTime );
			yield return new Vector2( t + deltaTime, position );
		}
	}

	/// <summary>
	/// For each point in <paramref name="data"/>, try to find a matching point in <paramref name="referenceData"/> that's within
	/// <paramref name="timeMargin"/> (x-axis) and <paramref name="valueMargin"/> (y-axis). For any points outside that margin,
	/// return them.
	/// </summary>
	private static IEnumerable<Vector2> FindOutsideMargin( IReadOnlyList<Vector2> data, IReadOnlyList<Vector2> referenceData, float referenceUpdateRate, float timeMargin, float valueMargin )
	{
		var refIndex = 0;
		var refDelta = 1f / referenceUpdateRate;

		var indexMargin = (int)(timeMargin * referenceUpdateRate + 1);

		foreach ( var point in data )
		{
			// Find index of reference point closest in time to data point

			while ( refIndex < referenceData.Count && referenceData[refIndex].x < point.x - refDelta )
			{
				++refIndex;
			}

			// Look for a nearby reference point within margin

			var matchFound = false;
			var closestDist = float.PositiveInfinity;

			for ( var i = System.Math.Max( refIndex - indexMargin, 0 ); i <= refIndex + indexMargin && i < referenceData.Count; ++i )
			{
				// Check if we're in between two neighboring reference points

				var j = System.Math.Min( referenceData.Count - 1, i + 1 );

				var min = System.Math.Min( referenceData[i].y, referenceData[j].y );
				var max = System.Math.Max( referenceData[i].y, referenceData[j].y );

				var dist = System.Math.Max( min - point.y, point.y - max );

				closestDist = System.Math.Min( dist, closestDist );

				if ( dist <= valueMargin )
				{
					matchFound = true;
					break;
				}
			}

			if ( matchFound )
			{
				continue;
			}

			yield return point;
		}
	}

	#endregion
}
