using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Shared;

/// <summary>
/// Version des données de la guilde courante : le compagnon ne réécrit
/// Data.lua que si elle change. Service partagé par tous les modules, pour
/// qu'aucun ne dépende d'un autre juste pour signaler un changement.
/// </summary>
public interface IDataVersion
{
	Task<long> CurrentAsync(CancellationToken ct = default);

	/// <summary>Incrémente la version en une seule instruction SQL (deux envois simultanés ne se perdent pas).</summary>
	Task<long> BumpAsync(CancellationToken ct = default);

	/// <summary>Enregistre les changements en cours puis incrémente la version, dans la même transaction.</summary>
	Task<long> SaveAndBumpAsync(CancellationToken ct = default);
}

public sealed class DataVersion(AppDbContext db, GuildContext guildContext) : IDataVersion
{
	public async Task<long> CurrentAsync(CancellationToken ct = default) =>
		await db.Guilds.AsNoTracking()
			.Where(g => g.Id == guildContext.Required.Id)
			.Select(g => g.DataVersion)
			.SingleAsync(ct);

	public async Task<long> BumpAsync(CancellationToken ct = default)
	{
		await db.Guilds
			.Where(g => g.Id == guildContext.Required.Id)
			.ExecuteUpdateAsync(s => s.SetProperty(g => g.DataVersion, g => g.DataVersion + 1), ct);
		return await CurrentAsync(ct);
	}

	public async Task<long> SaveAndBumpAsync(CancellationToken ct = default)
	{
		if (db.Database.CurrentTransaction is not null)
		{
			// Déjà dans l'unité de travail d'un appelant (ingestion d'un envoi) : il valide lui-même.
			await db.SaveChangesAsync(ct);
			return await BumpAsync(ct);
		}
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		await db.SaveChangesAsync(ct);
		var version = await BumpAsync(ct);
		await transaction.CommitAsync(ct);
		return version;
	}
}
