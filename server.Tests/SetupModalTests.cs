using Discord;
using GuildProfessions.Server.Discord;
using GuildProfessions.Server.Services;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// /gkeep-setup ouvrait une fenêtre que Discord refusait (erreur 50035,
/// TEXT_INPUT_COMPONENT_LABEL_IN_LABEL_COMPONENT) : depuis Discord.Net 3.18 le champ
/// est enveloppé dans un composant Label, et le libellé traduit était posé sur le champ.
/// </summary>
public sealed class SetupModalTests
{
	// Ce que Discord.Net construit pour SetupModal : le champ dans un Label.
	private static ModalBuilder LibraryModal() =>
		new ModalBuilder()
			.WithCustomId("gk_setup_modal")
			.WithTitle("Guildkeep")
			.AddLabel(new LabelBuilder()
				.WithLabel("Guild name")
				.WithComponent(new TextInputBuilder()
					.WithCustomId("guild_name")
					.WithStyle(TextInputStyle.Short)
					.WithPlaceholder("FORMER HOPE")
					.WithMinLength(2)
					.WithMaxLength(32)));

	[Theory]
	[InlineData(BotLanguage.French, "Nom exact de la guilde en jeu", "ex. FORMER HOPE")]
	[InlineData(BotLanguage.English, "Exact in-game guild name", "e.g. FORMER HOPE")]
	public void Libelle_traduit_sur_le_Label_jamais_sur_le_champ(BotLanguage language, string label, string placeholder)
	{
		var modal = LibraryModal();
		SetupModule.TranslateNameStep(modal, new BotLocalizer { Language = language });

		var wrapper = Assert.Single(modal.Components.OfType<LabelBuilder>());
		var input = Assert.IsType<TextInputBuilder>(wrapper.Component);
		Assert.Equal(label, wrapper.Label);
		Assert.Null(input.Label);
		Assert.Equal(placeholder, input.Placeholder);
		modal.Build();
	}
}
