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

	/// <summary>
	/// Sort layers have to survive a multi-selection like anything else. This is what
	/// <c>SortLayerControlWidget</c> reads to decide between showing a layer and showing
	/// "Multiple Values" - showing the first sprite's layer while the others disagree would claim
	/// they all share it.
	/// </summary>
	[TestMethod]
	public void SortLayersThatDifferAreReportedAsMultipleValues()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var settings = ProjectSettings.Sorting;
		var background = settings.AddLayer( "MultiEditBackground" );
		var foreground = settings.AddLayer( "MultiEditForeground" );

		var (first, second, _, _) = TwoDistinctSprites( scene );

		first.SortLayer = new SortLayerHandle( background );
		second.SortLayer = new SortLayerHandle( foreground );

		var mso = SelectBoth( first, second );

		Assert.IsTrue( mso.TryGetProperty( nameof( SpriteRenderer.SortLayer ), out var property ) );
		Assert.IsTrue( property.IsMultipleDifferentValues );

		// Selecting them must not have merged the layers.
		Assert.AreEqual( background.Id, first.SortLayer.Id );
		Assert.AreEqual( foreground.Id, second.SortLayer.Id );
	}

	/// <summary>
	/// Picking a layer with several sprites selected applies it to all of them. Unlike the sprite
	/// texture, fanning out here is the whole point - the value written is the one that was picked,
	/// not one read back off the first selection.
	/// </summary>
	[TestMethod]
	public void PickingASortLayerAppliesItToEverySelectedSprite()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var settings = ProjectSettings.Sorting;
		var target = settings.AddLayer( "MultiEditTarget" );

		var (first, second, firstSprite, secondSprite) = TwoDistinctSprites( scene );

		var mso = SelectBoth( first, second );

		Assert.IsTrue( mso.TryGetProperty( nameof( SpriteRenderer.SortLayer ), out var property ) );

		property.SetValue( new SortLayerHandle( target ) );

		Assert.AreEqual( target.Id, first.SortLayer.Id );
		Assert.AreEqual( target.Id, second.SortLayer.Id );

		// The two features have to coexist: setting a sort layer across the selection must leave
		// each sprite holding its own Sprite, which is exactly what the embedded sprite widget was
		// getting wrong.
		Assert.AreSame( firstSprite, first.Sprite );
		Assert.AreSame( secondSprite, second.Sprite );
		Assert.AreNotSame( first.Sprite, second.Sprite );
	}

	/// <summary>
	/// The other direction of the same requirement: editing the sprites must not flatten sorting
	/// state that was deliberately different between them.
	/// </summary>
	[TestMethod]
	public void EditingSpritesLeavesDifferingSortOrdersAlone()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (first, second, _, _) = TwoDistinctSprites( scene );

		first.SortOrder = 3;
		second.SortOrder = 9;

		var mso = SelectBoth( first, second );

		// Whatever the inspector reads while building rows must not disturb anything.
		foreach ( var property in mso )
		{
			_ = property.GetValue<object>();
		}

		Assert.IsTrue( mso.TryGetProperty( nameof( SpriteRenderer.Sprite ), out var spriteProperty ) );

		// Write each sprite through its own property, the way the fixed widget does.
		foreach ( var target in spriteProperty.MultipleProperties )
		{
			target.SetValue( target.GetValue<Sprite>( null ) ?? new Sprite() );
		}

		Assert.AreEqual( 3, first.SortOrder );
		Assert.AreEqual( 9, second.SortOrder );
	}
}
