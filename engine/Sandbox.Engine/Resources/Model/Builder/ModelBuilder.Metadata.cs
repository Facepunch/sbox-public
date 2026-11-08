using System.Text.Json;

namespace Sandbox;

public sealed partial class ModelBuilder
{
	private BBox hullBounds;
	private BBox viewBounds;
	private Vector3 eyePosition;
	private float maxEyeDeflection;
	private Dictionary<string, object> modelData;

	private void ApplyMetadata( NativeEngine.ModelBuilder modelBuilder )
	{
		modelBuilder.SetHullBounds( hullBounds.Mins, hullBounds.Maxs );
		modelBuilder.SetViewBounds( viewBounds.Mins, viewBounds.Maxs );
		modelBuilder.SetEyePosition( eyePosition );
		modelBuilder.SetMaxEyeDeflection( maxEyeDeflection );

		if ( modelData is null )
			return;

		var json = JsonSerializer.Serialize( modelData, Model.DataJsonOptions );
		var data = NativeEngine.EngineGlue.JsonToKeyValues3( json );
		if ( !data.IsValid )
			throw new InvalidOperationException( "Could not convert model game data to KeyValues3." );

		try
		{
			if ( !modelBuilder.SetModelKeyValues( data ) )
				throw new InvalidOperationException( "Could not serialize model game data." );
		}
		finally
		{
			data.DeleteThis();
		}
	}

	/// <summary>
	/// Sets game data read by <see cref="Model.GetData{T}"/>. Replaces any data for the same node.
	/// Use an array for types whose GameData attribute allows multiple entries.
	/// </summary>
	public ModelBuilder WithData<T>( T data )
	{
		ArgumentNullException.ThrowIfNull( data );
		var key = Model.DeduceKeyName( typeof( T ) );
		modelData ??= new();
		modelData[key] = data;
		return this;
	}

	/// <summary>
	/// Sets the bounds used for physics and gameplay queries.
	/// If not set, the bounds are calculated from the model's physics data.
	/// </summary>
	/// <param name="bounds">The model-space hull bounds.</param>
	public ModelBuilder WithHullBounds( BBox bounds )
	{
		hullBounds = bounds;
		return this;
	}

	/// <summary>
	/// Sets the bounds used for rendering and visibility.
	/// If not set, the bounds are calculated from the model's meshes.
	/// </summary>
	/// <param name="bounds">The model-space visibility bounds.</param>
	public ModelBuilder WithViewBounds( BBox bounds )
	{
		viewBounds = bounds;
		return this;
	}

	/// <summary>
	/// Sets the model-space position used as the model's eye position.
	/// </summary>
	/// <param name="position">The eye position.</param>
	public ModelBuilder WithEyePosition( Vector3 position )
	{
		eyePosition = position;
		return this;
	}

	/// <summary>
	/// Sets the maximum angle, in degrees, that the model's eyes can turn away from forward.
	/// </summary>
	/// <param name="degrees">The maximum eye deflection angle.</param>
	public ModelBuilder WithMaxEyeDeflection( float degrees )
	{
		if ( !float.IsFinite( degrees ) || degrees is < 0.0f or > 180.0f )
			throw new ArgumentOutOfRangeException( nameof( degrees ), "Eye deflection must be between 0 and 180 degrees." );

		maxEyeDeflection = degrees;
		return this;
	}
}
