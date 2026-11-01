using System.Collections.Generic;
using Data.Char;
using NUnit.Framework;
using Promuse.Manufacture;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 房间和选人面板 / The room and its picker, as Arknights/Manufacture/Build Manufacture UI leaves them: bound,
// layered, standing the operators in the middle, and choosing at most five. Run on copies of the built
// screen, with ids that are no operator, so nothing here needs the local art.
public class ManufactureRoomScreenTests
{
    private const string ScreenPath = "Assets/Arknights/Resources/Prefab/UI/ManufactureUI.prefab";
    private const string LinesPath = "Assets/Manufacture/Resources/Manufacture/ManufactureLines.asset";

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

    [Test]
    public void TheRoomAndItsPicker_AreBound()
    {
        ManufactureUI ui = Ui;
        Assert.IsNotNull(ui.roomButton);
        Assert.AreSame(screen.transform.Find("TopBar/RoomButton").GetComponent<Button>(), ui.roomButton, "ROOM is on the top bar");

        Assert.IsNotNull(ui.room);
        Assert.AreSame(screen.transform.Find("Room").GetComponent<ManufactureRoom>(), ui.room, "on the room's own canvas");
        Assert.IsNotNull(ui.room.graphicMaterial, "the shared SkeletonGraphic material, nothing local-only");
        Assert.AreEqual(5, ui.room.slots.Length);

        foreach (ManufactureChibi slot in ui.room.slots)
        {
            Assert.IsNotNull(slot);
            Assert.IsNotNull(slot.figure);
            Assert.IsNotNull(slot.hitArea);
            Assert.IsNotNull(slot.bubble);
            Assert.IsNotNull(slot.bubbleText);
            Assert.IsFalse(slot.hitArea.raycastTarget, "an empty slot takes no tap");
            Assert.IsFalse(slot.bubble.activeSelf);
            Assert.IsFalse(slot.bubbleText.richText);
        }

        RoomPicker picker = ui.roomPicker;
        Assert.IsNotNull(picker);
        foreach (Object field in new Object[] { picker.grid, picker.tileTemplate, picker.count, picker.hint, picker.confirmButton, picker.cancelButton })
        {
            Assert.IsNotNull(field);
        }
        Assert.IsNotNull(picker.tileTemplate.Find("Mask/Portrait"));
        Assert.IsNotNull(picker.tileTemplate.Find("Selected/Order"));
    }

    [Test]
    public void ThePicker_StartsHidden_OverEverything()
    {
        Assert.IsFalse(Ui.roomPicker.gameObject.activeSelf);
        Assert.AreEqual(screen.transform.childCount - 1, Ui.roomPicker.transform.GetSiblingIndex(),
                        "last, so it covers the top bar and the upgrade panel");
    }

    [Test]
    public void TheOperators_StandInTheMiddle_InTheOrderChosen()
    {
        ManufactureRoom room = Ui.room;

        CollectionAssert.AreEqual(new[] { 0f }, Xs(room.Middle(1)));
        CollectionAssert.AreEqual(new[] { -150f, 0f, 150f }, Xs(room.Middle(3)));
        CollectionAssert.AreEqual(new[] { -150f, 0f }, Xs(room.Middle(2)), "of two as near the middle, the left one");
        CollectionAssert.AreEqual(new[] { -300f, -150f, 0f, 150f, 300f }, Xs(room.Middle(9)));
    }

    [Test]
    public void AnOperatorWithNoRig_LeavesTheRoomEmpty_AndUntappable()
    {
        ManufactureRoom room = Ui.room;
        room.Show(new List<string> { "NOBODY" });

        CollectionAssert.IsEmpty(room.Standing());
        foreach (ManufactureChibi slot in room.slots) Assert.IsFalse(slot.hitArea.raycastTarget);

        // A meta without a battle rig - every operator, on a clone without the local art.
        CharMeta bare = ScriptableObject.CreateInstance<CharMeta>();
        try
        {
            Assert.IsFalse(room.slots[2].Stand("BARE", bare, room.graphicMaterial, 230f, false, null));
            Assert.IsFalse(room.slots[2].IsStanding);
            Assert.IsFalse(room.slots[2].hitArea.raycastTarget);
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void ThePicker_TakesFiveAtMost_SaysSoAtTheSixth_AndConfirmsTheChoice()
    {
        RoomPicker picker = Ui.roomPicker;
        IReadOnlyList<string> confirmed = null;
        picker.Confirm += ids => confirmed = ids;

        picker.Open(new[] { "A", "B", "C", "D", "E", "F" }, new[] { "E", "A", "B", "C", "D" });
        Assert.IsTrue(picker.IsOpen);
        Assert.AreEqual("5 / 5", picker.count.text);
        Assert.AreEqual("2", Tile(picker, "A").Find("Selected/Order").GetComponent<TMPro.TMP_Text>().text, "A stands second from the left");

        picker.Press("F");
        Assert.IsTrue(picker.hint.gameObject.activeSelf, "a sixth is refused, and the panel says why");
        CollectionAssert.AreEqual(new[] { "E", "A", "B", "C", "D" }, picker.Chosen);
        Assert.IsFalse(Tile(picker, "F").Find("Selected").gameObject.activeSelf);

        picker.Press("A");
        picker.Press("F");
        Assert.IsFalse(picker.hint.gameObject.activeSelf);
        Assert.AreEqual("5 / 5", picker.count.text);

        picker.confirmButton.onClick.Invoke();
        CollectionAssert.AreEqual(new[] { "E", "B", "C", "D", "F" }, confirmed);
    }

    [Test]
    public void TheLines_AreThere_ThreeForEachOperator()
    {
        ManufactureLines lines = AssetDatabase.LoadAssetAtPath<ManufactureLines>(LinesPath);
        Assert.IsNotNull(lines, "run Arknights/Manufacture/Build Manufacture UI");

        foreach (string id in new[] { "AMIYA", "NOVA", "ECHO", "PULSE" })
        {
            Assert.AreEqual(3, lines.For(id).Count, id);
        }
        CollectionAssert.IsNotEmpty(lines.fallback);
    }

    private static float[] Xs(List<ManufactureChibi> slots)
    {
        var xs = new float[slots.Count];
        for (int i = 0; i < slots.Count; i++) xs[i] = ((RectTransform)slots[i].transform).anchoredPosition.x;
        return xs;
    }

    private static Transform Tile(RoomPicker picker, string id)
    {
        Transform tile = picker.grid.Find("Tile (" + id + ")");
        Assert.IsNotNull(tile, id);
        return tile;
    }
}
