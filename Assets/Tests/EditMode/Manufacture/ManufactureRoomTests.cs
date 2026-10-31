using System.Collections.Generic;
using NUnit.Framework;
using Promuse.Manufacture;
using Tools;
using UnityEngine;

// 房间的规则 / Who stands in the Manufacture room and what they say: the choice and its defaults, the
// store kept per server, the lines, and the shared measure that sizes them.
public class ManufactureRoomTests
{
    private static readonly string[] Owned = { "AMIYA", "NOVA", "ECHO", "PULSE" };

    // ------------------------------------------------------------------ who stands

    [Test]
    public void NeverChosen_TheOwnedStand_InTheSavesOrder_FiveAtMost()
    {
        CollectionAssert.AreEqual(Owned, RoomSelection.Default(Owned));
        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E" },
                                  RoomSelection.Default(new[] { "A", "B", "", "B", "C", "D", "E", "F" }));
        CollectionAssert.AreEqual(Owned, RoomSelection.Resolve(null, Owned), "nothing saved is the default");
    }

    [Test]
    public void AChoice_KeepsItsOrder_AndDropsWhoIsNoLongerOwned()
    {
        // NOBODY was chosen on this device once and is not owned any more, or unknown to this build.
        List<string> room = RoomSelection.Resolve(new[] { "PULSE", "NOBODY", "AMIYA" }, Owned);
        CollectionAssert.AreEqual(new[] { "PULSE", "AMIYA" }, room);

        CollectionAssert.IsEmpty(RoomSelection.Resolve(new string[0], Owned), "an empty room is a choice, not the default");
    }

    [Test]
    public void TheRoomHoldsFive_ASixthIsRefused()
    {
        var chosen = new List<string> { "A", "B", "C", "D", "E" };

        Assert.IsFalse(RoomSelection.Toggle(chosen, "F"));
        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E" }, chosen, "nothing changed");

        Assert.IsTrue(RoomSelection.Toggle(chosen, "B"), "pressing a chosen one takes them out");
        Assert.IsTrue(RoomSelection.Toggle(chosen, "F"), "and makes room for another, at the end");
        CollectionAssert.AreEqual(new[] { "A", "C", "D", "E", "F" }, chosen);
    }

    // ------------------------------------------------------------------ kept per server

    [Test]
    public void TheStore_KeepsAChoice_EmptyIncluded_AndKnowsWhenNothingWasKept()
    {
        const string key = "Promuse.Manufacture.Room.Tests";
        PlayerPrefs.DeleteKey(key);
        try
        {
            var store = new RoomStore(key);
            Assert.IsNull(store.Load(), "never chosen");

            store.Save(new[] { "ECHO", "AMIYA" });
            CollectionAssert.AreEqual(new[] { "ECHO", "AMIYA" }, store.Load());

            store.Save(new string[0]);
            List<string> empty = store.Load();
            Assert.IsNotNull(empty, "an empty room was chosen");
            CollectionAssert.IsEmpty(empty);
        }
        finally
        {
            PlayerPrefs.DeleteKey(key);
        }
    }

    // ------------------------------------------------------------------ lines

    [Test]
    public void ALine_IsNeverTheOneJustSaid_WhileThereIsAnother()
    {
        var lines = new[] { "one", "two", "three" };

        // Whatever the roll, "two" just said is not said again.
        for (int roll = 0; roll < 3; roll++)
        {
            int r = roll;
            Assert.AreNotEqual("two", ManufactureLines.Pick(lines, "two", n => Mathf.Min(r, n - 1)));
        }

        Assert.AreEqual("only", ManufactureLines.Pick(new[] { "only" }, "only", n => 0), "one line is said again");
        Assert.AreEqual("same", ManufactureLines.Pick(new[] { "same", "same" }, "same", n => 0));
        Assert.IsNull(ManufactureLines.Pick(new[] { "", " " }, null, n => 0), "blank lines are nothing to say");
        Assert.IsNull(ManufactureLines.Pick(null, null, n => 0));
    }

    [Test]
    public void AnOperatorWithNoLines_SaysTheFallback()
    {
        ManufactureLines lines = ScriptableObject.CreateInstance<ManufactureLines>();
        try
        {
            lines.fallback = new[] { "Need something, Doctor?" };
            lines.entries.Add(new ManufactureLines.Entry { charId = "AMIYA", lines = new[] { "Hello." } });
            lines.entries.Add(new ManufactureLines.Entry { charId = "NOVA", lines = new string[0] });

            CollectionAssert.AreEqual(new[] { "Hello." }, lines.For("amiya"), "ids match whatever their case");
            CollectionAssert.AreEqual(lines.fallback, lines.For("NOVA"), "an entry with no lines");
            CollectionAssert.AreEqual(lines.fallback, lines.For("ECHO"), "no entry at all");
        }
        finally
        {
            Object.DestroyImmediate(lines);
        }
    }

    // ------------------------------------------------------------------ the shared measure

    [Test]
    public void AnOperatorIsScaledToTheTargetHeight_TimesTheirOwn()
    {
        // A figure 3.84 units tall, made 3.8 tall on the stage: the stage's own case.
        Assert.AreEqual(3.8f / 3.84f, RigHeight.ScaleFor(3.84f, 3.8f, 1f), 1e-6f);
        Assert.AreEqual(3.8f * 1.1f / 3.84f, RigHeight.ScaleFor(3.84f, 3.8f, 1.1f), 1e-6f, "a taller operator");
        Assert.AreEqual(3.8f / 3.84f, RigHeight.ScaleFor(3.84f, 3.8f, 0f), 1e-6f, "an unset heightScale is 1");
        Assert.AreEqual(1f, RigHeight.ScaleFor(0f, 3.8f, 1f), "nothing to measure leaves the rig as drawn");

        // In the room the same measure runs in canvas pixels: 2.3 units at 100 pixels a unit, made 230 tall.
        Assert.AreEqual(1f, RigHeight.ScaleFor(2.3f * 100f, 230f, 1f), 1e-5f);
    }
}
