using Sandbox;

/// <summary>
/// Groups the clothing categories into a nice groups
/// </summary>
[AssetType( Name = "Avatar Clothing Category", Extension = "clthgrp", Category = "Citizen" )]
public class AvatarClothingCategory : GameResource
{
	[Property] public string Name { get; set; }

	[Property] public Category[] Categories { get; set; }


	public class Category
	{
		[KeyProperty]
		public string Name { get; set; }
		public string Title { get; set; }
		public Texture Icon { get; set; }
		public bool ShowAll { get; set; } = true;
		public SubCategory[] SubCategories { get; set; }
	}

	public class SubCategory
	{
		[KeyProperty]
		public string Name { get; set; } = "Name";
		public string Title { get; set; } = "Title";
		public Texture Icon { get; set; }
		public Clothing.ClothingCategory[] Categories { get; set; }
	}
}

