using Data.Story;
using NUnit.Framework;
using Promuse.Contracts.Stories;

// 剧情脚本 / How a .story file is read, and how the admin page's corrections land on it. The same
// rules are in ci/check_stories.py and the admin page; these pin the game's copy.
public class StoryScriptTests
{
    private const string Sample =
        "// a comment\n" +
        "@title Resonant Dawn - RD-1\n" +
        "@bg rhodes_deck\n" +
        "\n" +
        "AMIYA: Doctor, the signal is back.   #a1f3\r\n" +
        "Radio: ...anyone receiving? Over: out.   #b2c4\n" +
        "@bg none\n" +
        "> The wind carries a faint melody.   #c9e1\n";

    [Test]
    public void AScript_ReadsItsLinesTitleAndBackgrounds()
    {
        StoryScript script = StoryScript.Parse("resonant_dawn_1_before", Sample);

        Assert.IsEmpty(script.Errors);
        Assert.IsEmpty(script.Warnings);
        Assert.AreEqual("Resonant Dawn - RD-1", script.Title);
        Assert.AreEqual(3, script.Lines.Count);

        StoryLine first = script.Lines[0];
        Assert.AreEqual(StoryLineKind.Dialogue, first.Kind);
        Assert.AreEqual(("AMIYA", "Doctor, the signal is back.", "a1f3", "rhodes_deck"),
            (first.Speaker, first.Text, first.LineId, first.Background));

        Assert.AreEqual("...anyone receiving? Over: out.", script.Lines[1].Text, "only the first colon names the speaker");

        StoryLine narration = script.Lines[2];
        Assert.AreEqual(StoryLineKind.Narration, narration.Kind);
        Assert.IsNull(narration.Speaker);
        Assert.IsNull(narration.Background, "@bg none clears it");
        Assert.AreEqual(8, narration.SourceLine);
    }

    [TestCase("AMIYA: hi #AB12", "not a line id")]
    [TestCase("AMIYA: hi #a1f3\nNOVA: yo #a1f3", "used twice")]
    [TestCase("@music theme", "unknown directive")]
    [TestCase("@title One\n@title Two\nAMIYA: hi #a1f3", "one @title")]
    [TestCase("@bg Not A Name\nAMIYA: hi #a1f3", "@bg takes")]
    [TestCase("just some words #a1f3", "not a line")]
    [TestCase("AMIYA:    #a1f3", "says nothing")]
    [TestCase("// only a comment", "no lines")]
    public void ABrokenScript_SaysWhatIsWrong(string source, string expected)
    {
        StoryScript script = StoryScript.Parse("broken", source);

        Assert.IsTrue(script.Errors.Exists(e => e.Contains(expected)),
            $"expected an error containing '{expected}', got: {string.Join(" | ", script.Errors)}");
    }

    [Test]
    public void ALineWithoutAnId_StillPlaysButIsFlagged()
    {
        StoryScript script = StoryScript.Parse("ok", "AMIYA: no id here");

        Assert.IsEmpty(script.Errors);
        Assert.AreEqual(1, script.Lines.Count);
        Assert.IsNull(script.Lines[0].LineId);
        Assert.AreEqual(1, script.Warnings.Count);
    }

    [Test]
    public void ACorrection_AppliesOnlyWhileTheLineStillReadsWhatItWasWrittenAgainst()
    {
        StoryScript script = StoryScript.Parse("resonant_dawn_1_before", Sample);

        int applied = script.Apply(new[]
        {
            new StoryPatch("resonant_dawn_1_before", "a1f3", "Doctor, the signal is back.", "Doctor - the signal is back!"),
            // 已经改过的行 / A line a later build already rewrote: the old correction must not win.
            new StoryPatch("resonant_dawn_1_before", "b2c4", "...anyone recieving?", "...anyone receiving?"),
            // Another story's line with the same id.
            new StoryPatch("someone_else", "c9e1", "The wind carries a faint melody.", "Wrong story."),
        });

        Assert.AreEqual(1, applied);
        Assert.AreEqual("Doctor - the signal is back!", script.Lines[0].Text);
        Assert.AreEqual("...anyone receiving? Over: out.", script.Lines[1].Text);
        Assert.AreEqual("The wind carries a faint melody.", script.Lines[2].Text);
    }
}
