#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Promuse.Scoring.Rules;

/// <summary>
/// Reads server/data/ruleset.json, and builds the game's own objects from it.
///
/// 拒绝而不是猜 / Refuses a file it cannot fully stand behind rather than filling gaps. A score
/// ceiling computed from a half-read ruleset would reject honest players or wave cheaters
/// through, and the server finding that out at startup is far better than a player finding it
/// out on a results screen.
/// </summary>
public static class RulesetLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // The model is plain public fields so Unity's JsonUtility can read the same file.
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Ruleset Parse(string json)
    {
        Ruleset rules = JsonSerializer.Deserialize<Ruleset>(json, Options)
            ?? throw new InvalidDataException("The ruleset is empty.");

        Validate(rules);
        return rules;
    }

    public static Ruleset Load(string path) => Parse(File.ReadAllText(path));

    private static void Validate(Ruleset rules)
    {
        var problems = new List<string>();

        if (rules.version != Ruleset.CurrentVersion)
        {
            problems.Add($"version is {rules.version}, this server reads {Ruleset.CurrentVersion}");
        }

        ScoreRules score = rules.score;
        if (score is null) problems.Add("score is missing");
        else
        {
            if (score.hit <= 0 || score.great < score.hit || score.perfect < score.great)
            {
                problems.Add("score awards must be positive and ascend hit <= great <= perfect");
            }

            if (score.holdTick < 0) problems.Add("score.holdTick is negative");
            if (score.holdTickInterval <= 0f) problems.Add("score.holdTickInterval must be positive");

            if (score.multiplierThresholds is null) problems.Add("score.multiplierThresholds is missing");
            else if (Array.Exists(score.multiplierThresholds, t => t <= 0))
            {
                problems.Add("score.multiplierThresholds must all be positive");
            }
        }

        JudgeRules judge = rules.judge;
        if (judge is null) problems.Add("judge is missing");
        else if (!(0f < judge.tapPerfect && judge.tapPerfect <= judge.tapGreat && judge.tapGreat <= judge.tapHit)
                 || !(0f < judge.holdPerfect && judge.holdPerfect <= judge.holdGreat))
        {
            problems.Add("judge windows must be positive and widen Perfect -> Great -> Hit");
        }

        if (rules.health is null || rules.health.maxHp <= 0) problems.Add("health.maxHp must be positive");

        FeverRules fever = rules.fever;
        if (fever is null) problems.Add("fever is missing");
        else
        {
            if (fever.maxFever <= 0f) problems.Add("fever.maxFever must be positive");
            if (fever.feverDuration < 0f) problems.Add("fever.feverDuration is negative");
            if (fever.feverScoreMultiplier < 1) problems.Add("fever.feverScoreMultiplier must be at least 1");
        }

        if (rules.operators is null || rules.operators.Count == 0) problems.Add("no operators");
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (OperatorRules op in rules.operators)
            {
                if (op is null || string.IsNullOrEmpty(op.id))
                {
                    problems.Add("an operator has no id");
                    continue;
                }

                if (!seen.Add(op.id)) problems.Add($"operator {op.id} appears twice");

                if (op.scoreModifier <= 0f || op.feverModifier <= 0f)
                {
                    problems.Add($"operator {op.id}: modifiers are stored as the effective values and must be positive");
                }

                try
                {
                    CreatePassive(op.passive);
                }
                catch (InvalidDataException e)
                {
                    problems.Add($"operator {op.id}: {e.Message}");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("The ruleset is not usable: " + string.Join("; ", problems) + ".");
        }
    }

    // --------------------------------------------------------------- factories

    public static JudgeSettings CreateJudge(this Ruleset rules)
    {
        var settings = new JudgeSettings();
        rules.judge.ApplyTo(settings);
        return settings;
    }

    public static HealthSettings CreateHealth(this Ruleset rules)
    {
        var settings = new HealthSettings();
        rules.health.ApplyTo(settings);
        return settings;
    }

    public static FeverSettings CreateFever(this Ruleset rules)
    {
        var settings = new FeverSettings();
        rules.fever.ApplyTo(settings);
        return settings;
    }

    /// <summary>
    /// The same PassiveSO subclass the device holds, built from its type name.
    ///
    /// 不用开关语句 / Looked up by name rather than through a switch, so a new passive needs only
    /// its own file with WriteRules and ReadRules - the rule PassiveSO was designed around.
    /// </summary>
    public static PassiveSO? CreatePassive(PassiveRules? rules)
    {
        if (rules is null || rules.IsNone) return null;

        Type? type = typeof(PassiveSO).Assembly.GetType(rules.type, throwOnError: false);

        if (type is null || type.IsAbstract || !typeof(PassiveSO).IsAssignableFrom(type))
        {
            throw new InvalidDataException($"unknown passive type '{rules.type}'");
        }

        var passive = (PassiveSO)Activator.CreateInstance(type)!;
        passive.ReadRules(rules.parameters);
        return passive;
    }

    /// <summary>
    /// A RunState set up the way GameManager.SyncTuning sets one up for this operator, then
    /// Reset - so it starts exactly where a run on the device starts.
    /// </summary>
    public static RunState CreateRun(this Ruleset rules, OperatorRules? op, long seed = 0)
    {
        var run = new RunState
        {
            health = rules.CreateHealth(),
            fever = rules.CreateFever(),
            multiplierThresholds = (int[])rules.score.multiplierThresholds.Clone(),
            scoreModifier = op?.scoreModifier ?? 1f,
            feverModifier = op?.feverModifier ?? 1f,
            operatorMaxHp = op?.maxHp ?? 0,
            passive = CreatePassive(op?.passive),
            seed = seed,
        };

        run.Reset();
        return run;
    }
}
