namespace Sandbox.UI;

/// <summary>
/// This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of
/// this class and do the specific things to that.
/// </summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	public override void OpenElement<T>( int sequence ) => OpenElement<T>( sequence, null );

	/// <summary>
	/// Create a panel of type T
	/// </summary>
	public override void OpenElement<T>( int sequence, object key )
	{
		FlushContent();

		var parentElement = CurrentScope.Element ?? Parent;
		var childIndex = CurrentScope.ChildIndex;
		CurrentScope.ChildIndex++;

		PushScope( sequence, key );

		//Log.Info( $"OpenElement {CurrentScope.Sequence}.{CurrentScope.Loop} [{CurrentScope.Element}] [{elementName}]" );

		var block = GetBlock( CurrentScope.Hash );

		var element = block.FindOrCreateElement<T>( parentElement );
		element.SourceFile = sourceFile;
		element.SourceLine = sourceLine;
		//element.SourecColumn = column;
		CurrentScope.Block = block;

		CurrentScope.Element.Parent.SetChildIndex( element, childIndex );
	}

	/// <summary>
	/// Called to set attributes on a panel directly
	/// </summary>
	public void AddAttributeWithSetter<T>( int sequence, object value, Action<T> setter )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		if ( scope.Block.CheckCacheValue( HashCode.Combine( scope.Element, sequence ), value?.GetHashCode() ?? 0 ) )
			return;

		var t = (T)(object)scope.Element;
		setter.Invoke( t );

		if ( t is Panel p )
		{
			p.ParametersChanged( false );
		}
	}

	/// <summary>
	/// Called to set attributes on a panel directly
	/// </summary>
	public override void AddBind<T>( int sequence, string propertyName, Func<T> get, Action<T> set )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		var element = scope.Element;

		if ( scope.Block.CheckCacheValue( HashCode.Combine( element, sequence, propertyName ), 1 ) )
			return;

		var tl = Game.TypeLibrary.GetType( element.GetType() );
		if ( tl == null ) return;
		var theirProperty = tl.GetProperty( propertyName );
		if ( theirProperty == null )
		{
			Log.Warning( $"{element} does not have property '{propertyName}'" );
			return;
		}

		int theirOldValue = -1;
		int ourOldValue = -1;

		Func<bool> check = () =>
		{
			if ( !element.IsValid() )
				return false;

			try
			{

				//
				// If our value changed, update their value
				//
				{
					var val = get.Invoke();
					var valHash = val?.GetHashCode() ?? -1;

					if ( valHash != ourOldValue )
					{
						//Log.Info( $"Our value changed (now {val}) [{ourOldValue}] - updating them" );
						ourOldValue = valHash;

						if ( Translation.TryConvert( val, theirProperty.PropertyType, out var convertedValue ) )
						{
							theirProperty.SetValue( element, convertedValue );
						}
						else
						{
							Log.Warning( $"Couldn't convert {val?.GetType()} to {theirProperty.PropertyType}" );
						}

						// update their hash 
						var tVal = theirProperty.GetValue( element );
						theirOldValue = tVal?.GetHashCode() ?? -1;
						return true;
					}
				}

				//
				// If their value changed, update our value
				//

				{
					var val = theirProperty.GetValue( element );
					var valHash = val?.GetHashCode() ?? -1;

					if ( valHash != theirOldValue )
					{
						//Log.Info( $"Their value changed (now {val})] - updating us" );
						theirOldValue = valHash;

						if ( Translation.TryConvert( val, typeof( T ), out var convertedValue ) )
						{
							set( (T)convertedValue );
						}
						else
						{
							Log.Warning( $"Couldn't convert {val?.GetType()} to {typeof( T )}" );
						}

						ourOldValue = get.Invoke()?.GetHashCode() ?? -1;
						return true;
					}
				}
			}
			catch ( System.Exception )
			{
				// ignore exceptiuons by design
			}

			return false;
		};

		scope.Block.Binds ??= new();
		scope.Block.Binds.Add( check );
		scope.Block.MarkHasBinds();

		// prime it
		check();
	}

	/// <summary>
	/// Update bound variables and return true if any of them changed
	/// </summary>
	internal bool UpdateBinds()
	{
		return RootBlock.UpdateBinds();
	}
}
