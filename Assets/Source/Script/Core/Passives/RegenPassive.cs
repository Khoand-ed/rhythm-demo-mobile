using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 定时回血 / Recovers HP on a timer.
///
/// PULSE's passive. Replaces the "no special effect" it shipped with: a baseline operator that
/// changes nothing proves nothing about the Character-Driven Gameplay pillar, which is exactly
/// what a prototype is meant to demonstrate.
///
/// 计时器存在 RunState / The accumulator lives on RunState. Kept here it would carry a part-
/// filled timer into the next run, and in the Editor across Play Mode entirely.
/// </summary>
[CreateAssetMenu(menuName = "Rhythm/Passive/Regen", fileName = "PassiveRegen")]
public class RegenPassive : PassiveSO
{
    [Tooltip("Seconds between ticks.")]
    public float interval = 10f;

    [Tooltip("HP restored each tick. Capped at max HP by RunState.Heal.")]
    public int amount = 5;

    public override void BeginRun(RunState run)
    {
        run.passiveTimer = 0f;
    }

    public override void Tick(float deltaSeconds, RunState run)
    {
        if (interval <= 0f) return;

        run.passiveTimer += deltaSeconds;

        // while 而不是 if / A loop rather than a single check, so a long frame - or a seek -
        // still pays out every interval it crossed instead of silently dropping all but one.
        while (run.passiveTimer >= interval)
        {
            run.passiveTimer -= interval;
            run.Heal(amount);
        }
    }

    public override void WriteRules(List<RuleParam> into)
    {
        into.Add(new RuleParam("interval", interval));
        into.Add(new RuleParam("amount", amount));
    }

    public override void ReadRules(IReadOnlyList<RuleParam> from)
    {
        interval = RuleParam.Find(from, "interval", interval);
        amount = (int)RuleParam.Find(from, "amount", amount);
    }
}
