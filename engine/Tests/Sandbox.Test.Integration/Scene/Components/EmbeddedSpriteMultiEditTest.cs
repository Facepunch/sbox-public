using Sandbox.Resources;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace SceneTests.Components;

/// <summary>
/// Multi-editing sprites whose Sprite is an *embedded* resource, which is what dragging an image
/// into the scene actually produces.
///
/// An earlier test used a plain <c>new Sprite()</c> and found nothing wrong. That was the wrong
/// shape: <c>TextureDropObject.CreateSpriteFromTexture</c> stamps
/// <c>EmbeddedResource { ResourceCompiler = "embed" }</c>, and embedded resources take a completely
/// different path through serialization - they are written inline and rebuilt by
/// <c>Activator.CreateInstance</c> on the way back, rather than being referenced by path.
/// </summary>
[TestClass]
public class EmbeddedSpriteMultiEditTest
{
	/// <summary>Builds a sprite the same way the editor's drop handler does.</summary>
	static Sprite CreateEmbeddedSprite( string name )
	{
		var sprite = new Sprite
		{
			Animations =
			[
				new Sprite.Animation
				{
					Name = name,
					Frames = [new Sprite.Frame { Texture = Texture.White }]
				}
			]
		};

		sprite.EmbeddedResource = new EmbeddedResource
		{
			ResourceCompiler = "embed",
			Data = new JsonObject()
		};

		return sprite;
	}

	static (SpriteRenderer A, SpriteRenderer B, Sprite SpriteA, Sprite SpriteB) TwoEmbedded( Scene scene )
	{
		var a = scene.CreateObject().Components.Create<SpriteRenderer>();
		var b = scene.CreateObject().Components.Create<SpriteRenderer>();

		var sa = CreateEmbeddedSprite( "first" );
		var sb = CreateEmbeddedSprite( "second" );

		a.Sprite = sa;
		b.Sprite = sb;

		return (a, b, sa, sb);
	}

	[TestMethod]
	public void TwoEmbeddedSpritesStartDistinct()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, sa, sb) = TwoEmbedded( scene );

		Assert.AreNotSame( sa, sb );
		Assert.AreSame( sa, a.Sprite );
		Assert.AreSame( sb, b.Sprite );
		Assert.AreNotEqual( a.Sprite.Animations[0].Name, b.Sprite.Animations[0].Name );
	}

	/// <summary>
	/// Multi-selecting builds a MultiSerializedObject over both, exactly as the inspector does.
	/// Reading every property must not disturb either sprite.
	/// </summary>
	[TestMethod]
	public void MultiSelectingEmbeddedSpritesDoesNotMergeThem()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, sa, sb) = TwoEmbedded( scene );

		var mso = new MultiSerializedObject();
		mso.Add( a.GetSerialized() );
		mso.Add( b.GetSerialized() );
		mso.Rebuild();

		foreach ( var property in mso )
		{
			_ = property.GetValue<object>();
		}

		var sb2 = new StringBuilder();
		sb2.Append( $"A={a.Sprite?.Animations?[0]?.Name} B={b.Sprite?.Animations?[0]?.Name} " );
		sb2.Append( $"same={ReferenceEquals( a.Sprite, b.Sprite )}" );

		Assert.AreSame( sa, a.Sprite, sb2.ToString() );
		Assert.AreSame( sb, b.Sprite, sb2.ToString() );
	}

	/// <summary>
	/// The undo system serializes whole components when an edit starts. Round-tripping both through
	/// that is where two embedded resources could collapse into one, since neither has a path to
	/// tell them apart.
	/// </summary>
	[TestMethod]
	public void SerializingBothEmbeddedSpritesKeepsThemDistinct()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, _, _) = TwoEmbedded( scene );

		var jsonA = Json.Serialize( a.Sprite );
		var jsonB = Json.Serialize( b.Sprite );

		Assert.AreNotEqual( jsonA, jsonB,
			$"two embedded sprites serialized identically, so nothing can tell them apart: {jsonA}" );
	}

	/// <summary>
	/// Control widgets reach each selected object's own property through <c>MultipleProperties</c>.
	/// Pin that it enumerates every target, and collapses to just itself for a single selection -
	/// widgets use one loop for both cases.
	/// </summary>
	[TestMethod]
	public void MultiplePropertiesEnumeratesEveryTarget()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, _, _) = TwoEmbedded( scene );

		var mso = new MultiSerializedObject();
		mso.Add( a.GetSerialized() );
		mso.Add( b.GetSerialized() );
		mso.Rebuild();

		var multi = mso.GetProperty( nameof( SpriteRenderer.Sprite ) );
		Assert.AreEqual( 2, multi.MultipleProperties.Count() );

		var single = a.GetSerialized().GetProperty( nameof( SpriteRenderer.Sprite ) );
		Assert.AreEqual( 1, single.MultipleProperties.Count() );
		Assert.AreSame( single, single.MultipleProperties.Single() );
	}

	/// <summary>
	/// The trap behind the multi-select merge: GetValue returns only the first selection, while
	/// SetValue writes that one instance to every selected object. So a control widget that reads a
	/// value and writes it back - even completely unchanged - permanently aliases the whole selection
	/// on to the first object's sprite. This pins the engine behaviour widgets have to work around.
	/// </summary>
	[TestMethod]
	public void SetValueOnAMultiSelectionAliasesEveryTarget()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, sa, _) = TwoEmbedded( scene );

		var mso = new MultiSerializedObject();
		mso.Add( a.GetSerialized() );
		mso.Add( b.GetSerialized() );
		mso.Rebuild();

		var property = mso.GetProperty( nameof( SpriteRenderer.Sprite ) );

		Assert.AreSame( sa, property.GetValue<Sprite>( null ), "GetValue should return the first selection" );

		property.SetValue( property.GetValue<Sprite>( null ) );

		Assert.AreSame( sa, a.Sprite );
		Assert.AreSame( sa, b.Sprite,
			"SetValue is expected to fan out - this is why widgets must write to each target instead" );
	}

	/// <summary>
	/// What <c>EmbeddedSpriteControlWidget</c>'s Texture setter does now: update each selected
	/// object's own sprite instead of broadcasting one instance. Both keep their own Sprite, and both
	/// receive the new texture.
	/// </summary>
	[TestMethod]
	public void PerTargetWriteKeepsSpritesDistinct()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var (a, b, sa, sb) = TwoEmbedded( scene );

		var mso = new MultiSerializedObject();
		mso.Add( a.GetSerialized() );
		mso.Add( b.GetSerialized() );
		mso.Rebuild();

		var property = mso.GetProperty( nameof( SpriteRenderer.Sprite ) );
		var newTexture = Texture.Transparent;

		foreach ( var target in property.MultipleProperties )
		{
			var sprite = target.GetValue<Sprite>( null ) ?? new Sprite();
			sprite.Animations[0].Frames[0].Texture = newTexture;
			target.SetValue( sprite );
		}

		Assert.AreSame( sa, a.Sprite );
		Assert.AreSame( sb, b.Sprite );
		Assert.AreNotSame( a.Sprite, b.Sprite );
		Assert.AreSame( newTexture, a.Sprite.Animations[0].Frames[0].Texture );
		Assert.AreSame( newTexture, b.Sprite.Animations[0].Frames[0].Texture );
	}
}
