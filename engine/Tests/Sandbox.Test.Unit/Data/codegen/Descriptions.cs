using Sandbox;

/// <summary>
/// This class has a description!
/// </summary>
public class DescriptionTest
{
    /// <summary>
    /// This property has a description!
    /// </summary>
    public int Foo { get; set; }

    /// <summary>
    /// This method has a description!
    /// </summary>
    /// <param name="baz">
    /// This parameter has a description!
    /// </param>
    /// <returns>
    /// Here's a description of the return value!
    /// </returns>
    public int Bar( int baz )
    {
        return baz;
    }

    public int Boo( int bep )
    {
        return bep;
    }


	/// <summary>
	/// <para>Method description. See also: <see cref="Bar"/>!</para>
	/// <para>Here's a parameter reference: <paramref name="arg"/>.</para>
	/// <para>
	/// How about some code: <c>Console.WriteLine("Hello, World!");</c>
	/// </para>
    /// <para>
    /// Here's some more refs:
    /// <see cref="GenericTest{T}"/>,
    /// <see cref="GenericTest{T}.SomeProperty"/>,
    /// <see cref="NestedType"/>.
    /// </para>
	/// </summary>
	/// <param name="arg">A useful argument.</param>
    public int ComplexDescription( int arg )
    {
        return arg;
    }

    public class NestedType
    {

    }
}

public class GenericTest<T>
{
    public int SomeProperty { get; set; }
}
