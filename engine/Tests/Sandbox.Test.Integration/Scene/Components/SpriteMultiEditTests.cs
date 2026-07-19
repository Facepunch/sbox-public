namespace SceneTests.Components;

/// <summary>
/// Reproduces what the inspector does when several sprites are selected at once, to settle whether
/// selecting them changes their values or merely displays one of them.
///
/// Built the same way <c>ComponentListWidget</c> builds it: a <see cref="MultiSerializedObject"/>
/// holding each component's serialized form, rebuilt into shared properties.
/// </summary>
[TestClass]
public class SpriteMultiEditTests
{
	static MultiSerializedObject SelectBoth( params Component[] components )
	{
		var mso = new MultiSerializedObject();

		foreach ( var component in components )
		{
			mso.Add( component.GetSerialized() );
		}

		mso.Rebuild();

		return mso;
	}

	static (SpriteRenderer First, SpriteRenderer Second, Sprite FirstSprite, Sprite SecondSprite) TwoDistinctSprites( Scene scene )
	{
		var firstSprite = new Sprite();
		var secondSprite = new Sprite();

		var first = scene.CreateObject().Components.Create<SpriteRenderer>();
		var second = scene.CreateObject().Components.Create<SpriteRenderer>();

		first.Sprite = firstSprite;
		second.Sprite = secondSprite;

		return (first, second, firstSprite, secondSprite);
	}

	/// <summary>
	/// The question that matters: does selecting two sprites overwrite one with the other?
	///
	/// Displaying a shared value is survivable. Silently reassigning artwork because someone
	/// clicked two objects is not, so this is worth pinning down on its own.
	/// </summary>
	[TestMethod]
	public void SelectingTwoSpritesDoesNotChangeEither()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (first, second, firstSprite, secondSprite) = TwoDistinctSprites( scene );

		var mso = SelectBoth( first, second );

		// Read every property, the way an inspector populating its widgets would.
		foreach ( var property in mso )
		{
			_ = property.GetValue<object>();
		}

		Assert.AreSame( firstSprite, first.Sprite, "the first sprite must be left alone" );
		Assert.AreSame( secondSprite, second.Sprite, "the second sprite must be left alone" );
	}

	/// <summary>
	/// What a multi-selection reports for a property the two sprites disagree on. This is display
	/// behaviour, not corruption - but it is why both rows appear to show the same artwork.
	/// </summary>
	[TestMethod]
	public void MultiSelectionReportsTheFirstValueForDifferingProperties()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (first, second, firstSprite, secondSprite) = TwoDistinctSprites( scene );

		var mso = SelectBoth( first, second );

		Assert.IsTrue( mso.TryGetProperty( nameof( SpriteRenderer.Sprite ), out var property ) );

		Assert.IsTrue( property.IsMultipleDifferentValues,
			"the two sprites differ, and the property knows it - nothing acts on that yet" );

		Assert.AreSame( firstSprite, property.GetValue<Sprite>(),
			"GetValue reports the first selected object, which is why both rows look identical" );

		Assert.AreNotSame( secondSprite, property.GetValue<Sprite>() );
	}

	/// <summary>
	/// Editing through a multi-selection writes to every selected object. Expected, and the reason
	/// one edit appears to set the other.
	/// </summary>
	[TestMethod]
	public void EditingThroughAMultiSelectionWritesToEverySelectedSprite()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (first, second, _, _) = TwoDistinctSprites( scene );

		var mso = SelectBoth( first, second );

		Assert.IsTrue( mso.TryGetProperty( nameof( SpriteRenderer.SortOrder ), out var property ) );

		property.SetValue( 7 );

		Assert.AreEqual( 7, first.SortOrder );
		Assert.AreEqual( 7, second.SortOrder );
	}

	/// <summary>
	/// The same reporting behaviour on a component that has nothing to do with sprites, which is
	/// what makes this a property of the inspector rather than of sprite sorting.
	/// </summary>
	[TestMethod]
	public void TheSameHappensForAnyComponent()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var first = scene.CreateObject().Components.Create<ModelRenderer>();
		var second = scene.CreateObject().Components.Create<ModelRenderer>();

		first.Tint = Color.Red;
		second.Tint = Color.Blue;

		var mso = SelectBoth( first, second );

		Assert.IsTrue( mso.TryGetProperty( nameof( ModelRenderer.Tint ), out var property ) );

		Assert.IsTrue( property.IsMultipleDifferentValues );
		Assert.AreEqual( Color.Red, property.GetValue<Color>(), "the first selected object again" );

		// And the values themselves are untouched by having been selected.
		Assert.AreEqual( Color.Red, first.Tint );
		Assert.AreEqual( Color.Blue, second.Tint );
	}
}
