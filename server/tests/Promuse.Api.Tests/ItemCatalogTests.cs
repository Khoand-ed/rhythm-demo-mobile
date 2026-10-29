using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Manufacture;
using Promuse.Api.Runs;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Events;
using Promuse.Contracts.Items;
using Promuse.Contracts.Manufacture;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// 物品目录 / server/data/items.json - the game's ItemMetas by id and name - served to the admin
/// page, and holding every item the server itself pays out: an item missing from it would show on
/// the admin page as "unknown", and in the game as "Item #id" with no icon.
/// </summary>
[Collection(ApiCollection.Name)]
public class ItemCatalogTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    [Fact]
    public async Task The_admin_page_gets_every_item_by_id_and_name()
    {
        var (admin, _) = await PlayerAsync(admin: true);
        var (player, _) = await PlayerAsync();

        HttpResponseMessage response = await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/items", admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ItemCatalogList list = await ReadAsync<ItemCatalogList>(response);

        Assert.Contains(new CatalogItem(1, "Orundum", 4), list.Items);
        Assert.Contains(new CatalogItem(0, "Originite Prime", 5), list.Items);
        Assert.Equal(list.Items.Select(i => i.ItemId).Order(), list.Items.Select(i => i.ItemId));

        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/items", player))).StatusCode);
    }

    [Fact]
    public async Task Every_item_the_server_seeds_or_grants_is_in_the_catalog()
    {
        HashSet<int> known = [.. Factory.Services.GetRequiredService<GameData>().Items.Select(i => i.ItemId)];
        var paid = new List<(string Where, int ItemId)>();

        // 表里种的 / Every seeded row of every table, by its columns named ...ItemId: the shop, the
        // headhunting banners and pool, mission rewards - and whatever is seeded next.
        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            IModel model = scope.ServiceProvider.GetRequiredService<PromuseDbContext>().GetService<IDesignTimeModel>().Model;
            foreach (IEntityType type in model.GetEntityTypes())
            foreach (IDictionary<string, object?> row in type.GetSeedData())
            foreach (IProperty property in type.GetProperties().Where(p => p.Name.EndsWith("ItemId", StringComparison.Ordinal)))
            {
                if (row.TryGetValue(property.Name, out object? value) && value is int id) paid.Add((type.ClrType.Name + "." + property.Name, id));
            }
        }

        // JSON 里种的 / The seeded documents.
        EventDefinition dawn = SeedEvents.ResonantDawn();
        if (dawn.CurrencyItemId is int currency) paid.Add(("event currency", currency));
        paid.AddRange(dawn.CheckIn.Select(d => ("event check-in", d.Reward.ItemId)));
        paid.AddRange(dawn.Milestones.Select(m => ("event milestone", m.Reward.ItemId)));
        paid.AddRange(dawn.Shop.Select(o => ("event shop", o.Sell.ItemId)));

        CampaignDefinition main = SeedCampaign.Main();
        paid.AddRange(main.Chapters.SelectMany(c => c.Stages).SelectMany(s => s.Drops).Select(d => ("campaign drop", d.ItemId)));
        paid.Add(("campaign first clear", SeedCampaign.OrundumItemId));
        paid.Add(("level-up", SeedCampaign.OriginitePrimeItemId));

        paid.AddRange(SeedSignIn.Default().Select(r => ("sign-in", r.ItemId)));

        // 制造站 / What Manufacture pays: its blueprints, and what each station makes at every level.
        paid.AddRange(ManufactureRules.Blueprints.Select(b => ("manufacture blueprint", b.ItemId)));
        foreach (string station in ManufactureStations.All)
        {
            for (int level = 1; level <= ManufactureRules.MaxLevel; level++)
            {
                paid.Add(($"manufacture {station} level {level}", ManufactureRules.LevelOf(station, level).ItemId));
            }
        }

        // 新账号 / What a new account starts with.
        var (session, _) = await PlayerAsync();
        using (IServiceScope scope = Factory.Services.CreateScope())
        {
            List<int> starting = await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().PlayerItems
                .Where(i => i.AccountId == session.PlayerId).Select(i => i.ItemId).ToListAsync();
            Assert.NotEmpty(starting);
            paid.AddRange(starting.Select(id => ("starting items", id)));
        }

        Assert.Contains(paid, p => p.Where.StartsWith("ShopOffer.", StringComparison.Ordinal));
        string[] missing = [.. paid.Where(p => !known.Contains(p.ItemId)).Select(p => $"{p.Where}: item {p.ItemId}").Distinct()];
        Assert.True(missing.Length == 0,
            "Paid by the server but not in server/data/items.json - add an ItemMeta and run Arknights/Depot/Export Item Catalog:\n" +
            string.Join("\n", missing));
    }
}
