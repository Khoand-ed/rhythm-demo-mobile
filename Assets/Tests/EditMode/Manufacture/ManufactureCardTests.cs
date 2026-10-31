using System;
using NUnit.Framework;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;
using Promuse.Manufacture;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 卡片和面板怎么画 / A station card and the upgrade panel drawing real stations, on a copy of the
// screen as Arknights/Manufacture/Build Manufacture UI leaves it - so the bindings are the prefab's own.
public class ManufactureCardTests
{
    private const string ScreenPath = "Assets/Arknights/Resources/Prefab/UI/ManufactureUI.prefab";

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly StationLevel Lv1 = new StationLevel(1, 2, 50, 1800, 16);
    private static readonly StationUpgrade ToLv2 =
        new StationUpgrade(new StationLevel(2, 2, 50, 1440, 25), new[] { new ItemStack(2, 8000) }, 31, "1-3");

    private GameObject screen;

    [SetUp]
    public void CopyTheScreen()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath);
        Assert.IsNotNull(prefab, "run Arknights/Manufacture/Build Manufacture UI");
        screen = Object.Instantiate(prefab);
    }

    [TearDown]
    public void DropTheCopy() => Object.DestroyImmediate(screen);

    private ManufactureUI Ui => screen.GetComponent<ManufactureUI>();

    private static StationState Lmd(DateTimeOffset anchor) =>
        new StationState(ManufactureStations.Lmd, 1, 3, Lv1, 0, anchor, null, null, ToLv2);

    [Test]
    public void AFullStore_MakesCollectStandOut_UntilItIsCollected()
    {
        StationCard card = Ui.lmdCard;
        Graphic face = card.collectButton.targetGraphic;
        Color usual = face.color;

        // Nine hours on an eight-hour store: full, and stopped.
        StationState full = Lmd(Now.AddHours(-9));
        card.Bind(full, ManufactureManager.Read(full, Now), Now, busy: false);

        Assert.AreEqual(StationCard.ReadyFace, face.color);
        StringAssert.StartsWith("FULL", card.next.text);
        Assert.IsTrue(card.collectButton.interactable);

        // Collected: the clock starts again from now, with nothing in the store.
        StationState collected = full with { AnchorAt = Now };
        card.Bind(collected, ManufactureManager.Read(collected, Now), Now, busy: false);

        Assert.AreEqual(usual, face.color, "back to the prefab's own colour");
        StringAssert.StartsWith("+50 in", card.next.text);
        Assert.IsFalse(card.collectButton.interactable, "nothing to collect");
    }

    [Test]
    public void AFullLv2Store_FitsItsCard_OnOneLine()
    {
        // "1,250 / 1,250" is wider than the card at full size; it once wrapped over the product's name.
        StationCard card = Ui.lmdCard;
        var lv2 = new StationLevel(2, 2, 50, 1440, 25);
        StationState full = new StationState(ManufactureStations.Lmd, 2, 3, lv2, 0, Now.AddHours(-11), null, null, null);
        card.Bind(full, ManufactureManager.Read(full, Now), Now, busy: false);
        card.stored.ForceMeshUpdate();

        Assert.AreEqual("1,250 / 1,250", card.stored.text);
        Assert.AreEqual(TMPro.TextWrappingModes.NoWrap, card.stored.textWrappingMode);
        Assert.AreEqual(1, card.stored.textInfo.lineCount);
        Assert.IsFalse(card.stored.isTextOverflowing, "it shrinks to fit rather than spilling out");
    }

    [Test]
    public void ARequestOut_LocksBothButtons()
    {
        StationCard card = Ui.lmdCard;
        StationState station = Lmd(Now.AddHours(-2));
        card.Bind(station, ManufactureManager.Read(station, Now), Now, busy: true);

        Assert.IsFalse(card.collectButton.interactable);
        Assert.IsFalse(card.upgradeButton.interactable);
    }

    [Test]
    public void TheUpgradePanel_SaysWhatIsMissing_AndKeepsConfirmOff()
    {
        UpgradePanel panel = Ui.upgradePanel;

        // 70 minutes in: two units, 100 LMD, in the store.
        StationState station = Lmd(Now.AddMinutes(-70));
        StationReading reading = ManufactureManager.Read(station, Now);
        Assert.AreEqual(2, reading.StoredUnits);

        // No blueprint, and 4,700 + the station's 100 against 8,000: 3,200 short.
        panel.Open(station, reading, id => id == 2 ? 4700 : 0);
        StringAssert.Contains("clear 1-3", panel.blueprint.text);
        StringAssert.EndsWith("3,200 short", panel.cost.text);
        Assert.IsFalse(panel.confirmButton.interactable);

        // The blueprint held, and the station's 100 LMD makes up the last of the price.
        panel.Open(station, reading, id => id == 31 ? 1 : id == 2 ? 7900 : 0);
        StringAssert.Contains("held, not spent", panel.blueprint.text);
        StringAssert.DoesNotContain("short", panel.cost.text);
        Assert.IsTrue(panel.confirmButton.interactable);
    }
}
