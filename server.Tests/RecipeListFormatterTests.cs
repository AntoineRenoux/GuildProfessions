using GuildProfessions.Server.Services;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class RecipeListFormatterTests
{
	[Fact]
	public void Layout_Should_SortRecipesAndTitleWithCount()
	{
		var layout = RecipeListFormatter.Layout([("Alchimie", ["Potion de soins", "Élixir de défense", "Huile de pierre"])]);

		var field = Assert.Single(layout.Fields);
		Assert.Equal("Alchimie (3)", field.Title);
		Assert.Equal("• Élixir de défense\n• Huile de pierre\n• Potion de soins", field.Body);
		Assert.Equal(0, layout.HiddenCount);
	}

	[Fact]
	public void Layout_Should_SplitLongProfessionIntoContinuationFields_UnderDiscordLimits()
	{
		var recipes = Enumerable.Range(1, 120).Select(i => $"Recette d'alchimie numéro {i:000}").ToList();

		var layout = RecipeListFormatter.Layout([("Alchimie", recipes)]);

		Assert.True(layout.Fields.Count > 1);
		Assert.Equal("Alchimie (120)", layout.Fields[0].Title);
		Assert.All(layout.Fields.Skip(1), f => Assert.Equal("Alchimie (suite)", f.Title));
		Assert.All(layout.Fields, f => Assert.True(f.Body.Length <= 1024));
		Assert.True(layout.Fields.Sum(f => f.Title.Length + f.Body.Length) <= 6000);
		// Rien n'est perdu en silence : affiché + masqué = total.
		var shown = layout.Fields.Sum(f => f.Body.Split('\n').Length);
		Assert.Equal(120, shown + layout.HiddenCount);
	}

	[Fact]
	public void Layout_Should_CountHiddenRecipes_WhenEmbedBudgetExceeded()
	{
		var huge = Enumerable.Range(1, 400).Select(i => $"Une recette au nom assez long pour remplir vite {i:000}").ToList();

		var layout = RecipeListFormatter.Layout([("Forge", huge), ("Minage", ["Fonte du fer"])]);

		Assert.True(layout.HiddenCount > 0);
		var shown = layout.Fields.Sum(f => f.Body.Split('\n').Length);
		Assert.Equal(401, shown + layout.HiddenCount);
	}
}
