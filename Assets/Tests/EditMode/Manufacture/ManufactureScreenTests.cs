using NUnit.Framework;
using Promuse.Manufacture;
using UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 预制体是契约 / The Manufacture screen as Arknights/Manufacture/Build Manufacture UI leaves it: every
// field the script reads is bound, the two layers that tick have canvases of their own, the overlays
// start hidden, Back has its way out - and Home's MANUFACTURE tile opens it.
public class ManufactureScreenTests
{
    private const string ScreenPath = "Assets/Arknights/Resources/Prefab/UI/ManufactureUI.prefab";
    private const string HomePath = "Assets/Arknights/Resources/Prefab/UI/HomeUI.prefab";

    private static ManufactureUI Screen()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath);
        Assert.IsNotNull(prefab, "run Arknights/Manufacture/Build Manufacture UI");
        ManufactureUI ui = prefab.GetComponent<ManufactureUI>();
        Assert.IsNotNull(ui);
        return ui;
    }

    [Test]
    public void EveryFieldTheScreenReads_IsBound()
    {
        ManufactureUI ui = Screen();

        Assert.IsNotNull(ui.lmdIcon);
        Assert.IsNotNull(ui.lmdText);
        Assert.IsNotNull(ui.collectAllButton);
        Assert.IsNotNull(ui.stationLayer);
        Assert.IsNotNull(ui.upgradePanel);
        Assert.IsNotNull(ui.lockOverlay);
        Assert.IsNotNull(ui.lockText);
        Assert.IsNotNull(ui.retryButton);

        foreach (StationCard card in new[] { ui.lmdCard, ui.expCard })
        {
            Assert.IsNotNull(card);
            Assert.IsNotNull(card.title);
            Assert.IsNotNull(card.level);
            Assert.IsNotNull(card.productIcon);
            Assert.IsNotNull(card.productName);
            Assert.IsNotNull(card.stored);
            Assert.IsNotNull(card.progressFill);
            Assert.IsNotNull(card.next);
            Assert.IsNotNull(card.full);
            Assert.IsNotNull(card.collectButton);
            Assert.IsNotNull(card.upgradeButton);
        }

        Assert.AreEqual(("lmd", "exp"), (ui.lmdCard.stationId, ui.expCard.stationId));

        UpgradePanel panel = ui.upgradePanel;
        foreach (Object field in new Object[]
                 {
                     panel.title, panel.product, panel.pace, panel.holds, panel.blueprint, panel.cost, panel.note,
                     panel.confirmButton, panel.cancelButton,
                 })
        {
            Assert.IsNotNull(field);
        }
    }

    [Test]
    public void TheRoomAndTheStations_EachHaveACanvasThatCanBePressed()
    {
        Component ui = Screen();

        foreach (string layer in new[] { "Room", "Hud" })
        {
            Transform child = ui.transform.Find(layer);
            Assert.IsNotNull(child, layer);
            Assert.IsNotNull(child.GetComponent<Canvas>(), layer + " has its own canvas");
            Assert.IsNotNull(child.GetComponent<GraphicRaycaster>(), layer + " can be pressed");
        }

        Assert.AreSame(ui.transform.Find("Hud").GetComponent<CanvasGroup>(), Screen().stationLayer);
    }

    [Test]
    public void ATapOnTheEmptyRoom_StopsThere()
    {
        // Home stays open under the screen: a room that let a tap through would press a tile behind it.
        Component ui = Screen();
        Image wall = ui.transform.Find("Room/Wall").GetComponent<Image>();

        Assert.IsTrue(wall.raycastTarget);
        Assert.AreEqual((Vector2.zero, Vector2.one), (wall.rectTransform.anchorMin, wall.rectTransform.anchorMax), "it covers the screen");
    }

    [Test]
    public void TheOverlaysStartHidden_AndTheUpgradePanelDrawsOverTheTopBar()
    {
        ManufactureUI ui = Screen();
        Component root = ui;

        Assert.IsFalse(ui.lockOverlay.activeSelf);
        Assert.IsFalse(ui.upgradePanel.gameObject.activeSelf);

        int topBar = root.transform.Find("TopBar").GetSiblingIndex();
        Assert.Greater(ui.upgradePanel.transform.GetSiblingIndex(), topBar, "the panel covers Back until it is closed");
        Assert.Less(ui.lockOverlay.transform.GetSiblingIndex(), topBar, "Back stays usable while the screen is locked");
    }

    [Test]
    public void BackLeavesThroughTheTopBarButton()
    {
        ManufactureUI ui = Screen();
        Component root = ui;

        Button exit = UIBase.FindExitButton(ui);
        Assert.IsNotNull(exit);
        Assert.AreSame(root.transform.Find("TopBar/BackButton").GetComponent<Button>(), exit);
    }

    [Test]
    public void HomesManufactureTile_OpensTheScreen()
    {
        GameObject home = AssetDatabase.LoadAssetAtPath<GameObject>(HomePath);
        Assert.IsNotNull(home);

        Transform tile = FindDeep(home.transform, "ManufactureTile");
        Assert.IsNotNull(tile);

        // OpenUIButton lives in the default assembly, which no assembly definition can see: read it by name.
        Component open = tile.GetComponent("OpenUIButton");
        Assert.IsNotNull(open, "run Arknights/Manufacture/Build Manufacture UI");
        Assert.AreEqual(ManufactureUI.UIName, new SerializedObject(open).FindProperty("uiName").stringValue);
        Assert.IsNotNull(tile.GetComponent<Button>());
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
