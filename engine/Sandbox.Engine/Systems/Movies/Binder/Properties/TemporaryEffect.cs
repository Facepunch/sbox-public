using static Sandbox.Component;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Special handling for <see cref="Component.ITemporaryEffect.IsActive"/>.
/// </summary>
file sealed record TemporaryEffectIsActiveProperty( ITrackReference Parent ) : ITrackProperty<bool>
{
	public const string PropertyName = nameof( ITemporaryEffect.IsActive );

	public string Name => PropertyName;

	public bool Value
	{
		get => (Parent.Value as ITemporaryEffect)?.IsActive ?? false;
		set
		{
			// We can only set IsActive to false by calling DisableLooping(), and we can't set it to true

			if ( value ) return;
			if ( Parent.Value is not ITemporaryEffect effect ) return;
			if ( !effect.IsActive ) return;

			effect.DisableLooping();
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class TemporaryEffectIsActivePropertyFactory : ITrackPropertyFactory<ITrackReference>
{
	public IEnumerable<string> GetPropertyNames( ITrackReference parent )
	{
		if ( !parent.TargetType.IsAssignableTo( typeof( ITemporaryEffect ) ) ) return [];

		return [TemporaryEffectIsActiveProperty.PropertyName];
	}

	public Type? GetTargetType( ITrackReference parent, string name )
	{
		if ( name != TemporaryEffectIsActiveProperty.PropertyName ) return null;
		if ( !parent.TargetType.IsAssignableTo( typeof( ITemporaryEffect ) ) ) return null;

		return typeof( bool );
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackReference parent, string name )
	{
		Assert.AreEqual( TemporaryEffectIsActiveProperty.PropertyName, name );
		Assert.AreEqual( typeof( bool ), typeof( T ) );
		Assert.True( parent.TargetType.IsAssignableTo( typeof( ITemporaryEffect ) ) );

		return (ITrackProperty<T>)(ITrackProperty)new TemporaryEffectIsActiveProperty( parent );
	}
}
