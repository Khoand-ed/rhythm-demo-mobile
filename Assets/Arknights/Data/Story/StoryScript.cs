using System.Collections.Generic;
using Promuse.Contracts.Stories;

namespace Data.Story {
    public enum StoryLineKind { Dialogue, Narration }

    /// <summary>One line the player taps through.</summary>
    public sealed class StoryLine {
        public StoryLineKind Kind;

        /// <summary>As written before the colon - an operator id (AMIYA) or any name. Null for narration.</summary>
        public string Speaker;

        public string Text;

        /// <summary>The id written after the line (#a1f3); null when it has none and so cannot be corrected.</summary>
        public string LineId;

        /// <summary>The background in force for this line, set by the last @bg above it; null for none.</summary>
        public string Background;

        /// <summary>1-based, for messages that point at the file.</summary>
        public int SourceLine;
    }

    /// <summary>
    /// 剧情脚本 / A story script: the dialogue before or after a stage, written as plain text in
    /// <c>Resources/Story/&lt;id&gt;.story</c> with any text editor.
    ///
    /// <code>
    /// // a comment
    /// @title Resonant Dawn - RD-1        the card shown before the first line
    /// @bg rhodes_deck                    background from Resources/Story/Backgrounds; "none" for none
    ///
    /// AMIYA: Doctor, the signal is back.   #a1f3
    /// Radio: ...anyone receiving?          #b2c4
    /// > The wind carries a faint melody.   #c9e1
    /// </code>
    ///
    /// A line is <c>SPEAKER: text</c> or <c>&gt; narration</c>. A speaker that names an operator
    /// (its CharMeta id) shows that operator's name and portrait; any other name is shown as written.
    ///
    /// 行号用来热修 / The <c>#id</c> at the end of a line is what the admin page corrects it by, so
    /// it must not change once a story has shipped. <c>Arknights/Story/Assign Missing Line Ids</c>
    /// writes one on every line that has none.
    ///
    /// Pure - no Unity types - so the importer, the preview window, the game and the tests all read a
    /// script the same way. <c>ci/check_stories.py</c> and the admin page follow the same rules.
    /// </summary>
    public sealed class StoryScript {
        public const string NoBackground = "none";
        public const int MaxSpeaker = 32;

        public string Id;
        public string Title;
        public readonly List<StoryLine> Lines = new List<StoryLine>();

        /// <summary>What makes the script unplayable or uncorrectable, each prefixed with its line number.</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>What still plays but should be fixed - a line with no id cannot be corrected later.</summary>
        public readonly List<string> Warnings = new List<string>();

        public static StoryScript Parse(string id, string source) {
            StoryScript script = new StoryScript { Id = id };
            if (!StoryIds.IsStoryId(id)) {
                script.Errors.Add($"'{id}' is not a story id - name the file with lower-case letters, digits and underscores.");
            }

            HashSet<string> ids = new HashSet<string>();
            string background = null;
            string[] rows = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < rows.Length; i++) {
                int number = i + 1;
                string line = rows[i].Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;

                if (line[0] == '@') {
                    Directive(script, line, number, ref background);
                    continue;
                }

                string lineId = SplitLineId(ref line, out string badId);
                if (badId != null) {
                    script.Errors.Add($"{number}: '#{badId}' is not a line id - 4 to 8 lower-case letters and digits.");
                    continue;
                }

                StoryLine parsed = new StoryLine { LineId = lineId, Background = background, SourceLine = number };

                if (line[0] == '>') {
                    parsed.Kind = StoryLineKind.Narration;
                    parsed.Text = line.Substring(1).Trim();
                } else {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) {
                        script.Errors.Add($"{number}: not a line - write 'SPEAKER: text' or '> narration'.");
                        continue;
                    }

                    parsed.Kind = StoryLineKind.Dialogue;
                    parsed.Speaker = line.Substring(0, colon).Trim();
                    parsed.Text = line.Substring(colon + 1).Trim();

                    if (parsed.Speaker.Length > MaxSpeaker) {
                        script.Errors.Add($"{number}: the speaker's name is longer than {MaxSpeaker} characters.");
                        continue;
                    }
                }

                if (parsed.Text.Length == 0) {
                    script.Errors.Add($"{number}: the line says nothing.");
                    continue;
                }

                if (parsed.Text.Length > StoryIds.MaxLineText) {
                    script.Errors.Add($"{number}: longer than {StoryIds.MaxLineText} characters - split it into two lines.");
                    continue;
                }

                if (lineId == null) {
                    script.Warnings.Add($"{number}: no line id, so it cannot be corrected from the admin page.");
                } else if (!ids.Add(lineId)) {
                    script.Errors.Add($"{number}: the line id #{lineId} is used twice in this script.");
                    continue;
                }

                script.Lines.Add(parsed);
            }

            if (script.Lines.Count == 0 && script.Errors.Count == 0) script.Errors.Add("The script has no lines.");
            return script;
        }

        private static void Directive(StoryScript script, string line, int number, ref string background) {
            int space = line.IndexOf(' ');
            string name = (space < 0 ? line.Substring(1) : line.Substring(1, space - 1)).ToLowerInvariant();
            string value = space < 0 ? "" : line.Substring(space + 1).Trim();

            switch (name) {
                case "title":
                    if (script.Title != null) script.Errors.Add($"{number}: a script has one @title.");
                    else if (value.Length == 0) script.Errors.Add($"{number}: @title needs the text to show.");
                    else script.Title = value;
                    break;

                case "bg":
                    if (value == NoBackground) background = null;
                    else if (StoryIds.IsStoryId(value)) background = value;
                    else script.Errors.Add($"{number}: @bg takes an image name (lower-case, digits, underscores) or 'none'.");
                    break;

                default:
                    script.Errors.Add($"{number}: unknown directive '@{name}' - only @title and @bg exist.");
                    break;
            }
        }

        /// <summary>
        /// Takes the trailing <c>#id</c> off a line. A trailing <c>#word</c> that is not a valid id is
        /// reported rather than left in the text, where it would be shown to the player.
        /// </summary>
        public static string SplitLineId(ref string line, out string badId) {
            badId = null;

            int hash = line.LastIndexOf('#');
            if (hash <= 0 || !char.IsWhiteSpace(line[hash - 1])) return null;

            string token = line.Substring(hash + 1).Trim();
            if (token.Length == 0 || token.IndexOf(' ') >= 0) return null;

            if (!StoryIds.IsLineId(token)) {
                badId = token;
                return null;
            }

            line = line.Substring(0, hash).TrimEnd();
            return token;
        }

        /// <summary>
        /// 热修 / Puts the admin page's corrections in. A correction applies only while the line still
        /// reads what it was written against - once a build carries the fix, or rewrites the line,
        /// the old correction is quietly ignored.
        /// </summary>
        /// <returns>How many lines were corrected.</returns>
        public int Apply(IEnumerable<StoryPatch> patches) {
            if (patches == null) return 0;

            Dictionary<string, StoryLine> byId = new Dictionary<string, StoryLine>();
            foreach (StoryLine line in Lines) {
                if (line.LineId != null) byId[line.LineId] = line;
            }

            int applied = 0;
            foreach (StoryPatch patch in patches) {
                if (patch == null || patch.StoryId != Id) continue;
                if (!byId.TryGetValue(patch.LineId, out StoryLine line)) continue;
                if (line.Text != patch.Original?.Trim()) continue;

                line.Text = patch.Text;
                applied++;
            }

            return applied;
        }
    }
}
