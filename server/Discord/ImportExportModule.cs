using System.Text;
using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Canal manuel, sans app compagnon : le bot fournit la chaîne à coller en
/// jeu (/gp → Importer) et ingère celle produite en jeu (/gp → Exporter).
/// </summary>
public sealed class ImportExportModule(ExportBuilder exportBuilder, RosterService roster, CraftOrderService craftOrders, IConfiguration config)
	: InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("export-addon", "Obtenir la chaîne d'import pour l'addon (à coller en jeu : /gp → Importer)")]
	public async Task ExportAddonAsync()
	{
		var export = await exportBuilder.BuildAsync();
		var encoded = StringCodec.Encode(export);
		// En pièce jointe : pas de limite des 2000 caractères d'un message.
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(encoded));
		await RespondWithFileAsync(
			new FileAttachment(stream, "guildprofessions-import.txt"),
			$"Données de la guilde (version {export.Version}, {export.Characters.Count} personnages). " +
			"Ouvre le fichier, copie tout, puis en jeu : `/gp` → **Importer**.",
			ephemeral: true);
	}

	[SlashCommand("import", "Importer la chaîne produite en jeu (/gp → Exporter)")]
	public async Task ImportAsync()
	{
		await RespondWithModalAsync<ImportModal>("gp_import_modal");
	}

	[ModalInteraction("gp_import_modal")]
	public async Task HandleImportAsync(ImportModal modal)
	{
		GuildProfessions.Server.Contracts.UploadPayload? payload;
		try
		{
			payload = StringCodec.Decode<GuildProfessions.Server.Contracts.UploadPayload>(modal.Payload);
		}
		catch (Exception exception)
		{
			await RespondAsync($"Import impossible : {exception.Message}", ephemeral: true);
			return;
		}
		if (payload is null)
		{
			await RespondAsync("Import impossible : chaîne vide.", ephemeral: true);
			return;
		}

		var (filtered, guildWarnings) = GuildFilter.Apply(payload, config["Guild:Name"]);
		var member = await roster.GetOrCreateMemberAsync(Context.User.Id.ToString(), Context.User.Username);
		var result = await roster.ApplyUploadAsync(member, filtered);
		var orderWarnings = await craftOrders.ApplyUploadAsync(member, filtered);

		var summary = new StringBuilder($"Import appliqué : {filtered.Characters.Count} personnage(s)");
		if (filtered.Orders is { Count: > 0 })
		{
			summary.Append($", {filtered.Orders.Count} commande(s)");
		}
		summary.Append('.');
		foreach (var warning in guildWarnings.Concat(result.Warnings).Concat(orderWarnings))
		{
			summary.Append("\n⚠ ").Append(warning);
		}
		await RespondAsync(summary.ToString(), ephemeral: true);
	}

	public sealed class ImportModal : IModal
	{
		public string Title => "Import GuildProfessions";

		[InputLabel("Chaîne exportée depuis le jeu")]
		[ModalTextInput("payload", TextInputStyle.Paragraph, placeholder: "GP1!...", maxLength: 4000)]
		public string Payload { get; set; } = "";
	}
}
