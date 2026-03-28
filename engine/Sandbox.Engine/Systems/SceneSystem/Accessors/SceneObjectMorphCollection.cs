using Sandbox;

/// <summary>
/// Hidden class. addon code should only ever access MorphCollection.
/// </summary>
internal sealed class SceneObjectMorphCollection( SceneModel sceneObject ) : MorphCollection
{
	const float FadeTime = 0.05f;

	public override void ResetAll()
	{
		ResetAll( FadeTime );
	}

	public override void ResetAll( float fadeTime )
	{
		sceneObject.animNative.SBox_ClearFlexOverride( fadeTime );
	}

	public override void Reset( int i )
	{
		Reset( i, FadeTime );
	}

	public override void Reset( string name )
	{
		Reset( name, FadeTime );
	}

	public override void Reset( int i, float fadeTime )
	{
		sceneObject.animNative.SBox_ClearFlexOverride( i, fadeTime );
	}

	public override void Reset( string name, float fadeTime )
	{
		sceneObject.animNative.SBox_ClearFlexOverride( name, fadeTime );
	}

	public override void Set( int i, float weight )
	{
		Set( i, weight, FadeTime );
	}

	public override void Set( string name, float weight )
	{
		Set( name, weight, FadeTime );
	}

	public override void Set( int i, float weight, float fadeTime )
	{
		sceneObject.animNative.SBox_SetFlexOverride( i, MathF.Max( 0.0f, weight ), fadeTime );
	}

	public override void Set( string name, float weight, float fadeTime )
	{
		sceneObject.animNative.SBox_SetFlexOverride( name, MathF.Max( 0.0f, weight ), fadeTime );
	}

	public override float Get( int i )
	{
		return sceneObject.animNative.SBox_GetFlexOverride( i );
	}

	public override float Get( string name )
	{
		return sceneObject.animNative.SBox_GetFlexOverride( name );
	}

	public override string GetName( int index )
	{
		return sceneObject.Model.GetMorphName( index );
	}

	public override int Count => sceneObject.Model.MorphCount;
}
