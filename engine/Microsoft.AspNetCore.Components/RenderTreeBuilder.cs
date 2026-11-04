using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components.Rendering;

public abstract partial class RenderTreeBuilder
{
	public abstract void AddLocation( string filename, int line, int column );
	public abstract void OpenElement( int sequence, string elementName );
	public abstract void OpenElement( int sequence, string elementName, object key = null );
	public abstract void AddStyleDefinitions( int sequence, string styles );
	public abstract void AddAttribute<T>( int sequence, Action<T> value ) where T : IComponent;
	public abstract void CloseElement();
	public abstract void AddContent<T>( int sequence, T content );
	public abstract void AddReferenceCapture<T>( int sequence, T current, Action<T> value ) where T : IComponent;
	public abstract void SetRenderFragment<T>( Action<T, RenderFragment> setter, RenderFragment builder ) where T : IComponent;
	public abstract void SetRenderFragmentWithContext<T, U>( Func<T, RenderFragment<U>> getter, Action<T, RenderFragment<U>> setter, RenderFragment<U> builder ) where T : IComponent;

	public abstract void AddMarkupContent( int sequence, string markupContent );
	public abstract void OpenElement<T>( int sequence ) where T : IComponent, new();
	public abstract void OpenElement<T>( int sequence, object key ) where T : IComponent, new();
	public abstract void AddBind<T>( int sequence, string propertyName, Func<T> get, Action<T> set );

	//
	// The stock razor compiler emits these instead of the typed calls our own razor emits.
	// They bridge into the same machinery, so components compiled either way render.
	//
	public void OpenComponent<T>( int sequence ) where T : IComponent, new() => OpenElement<T>( sequence, null );
	public void CloseComponent() => CloseElement();
	public void AddComponentParameter( int sequence, string parameterName, object value ) => SetComponentParameter( sequence, parameterName, value );
	public void AddComponentParameter( int sequence, string parameterName, Action value ) => SetComponentParameter( sequence, parameterName, value );
	public void AddComponentParameter( int sequence, string parameterName, Func<Task> value ) => SetComponentParameter( sequence, parameterName, value );
	public void SetKey( object value ) { }

	/// <summary>
	/// A component parameter set by name rather than through a typed setter - how the stock
	/// razor compiler does it.
	/// </summary>
	protected virtual void SetComponentParameter( int sequence, string parameterName, object value ) { }
}
