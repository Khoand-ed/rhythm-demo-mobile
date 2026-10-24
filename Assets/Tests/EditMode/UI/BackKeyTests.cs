using System.Collections.Generic;
using NUnit.Framework;
using UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 返回键要有去处 / Android's Back presses the top screen's own way out (UIBase.OnBack). These read every
// screen prefab and check that each has one - the button a tap would press, found the way the game
// finds it at run time - so a prefab rebuilt without its back button, or a new screen, fails here
// instead of on a phone where Back does nothing.
public class BackKeyTests
{
    private const string Screens = "Assets/Arknights/Resources/Prefab/UI";

    private static IEnumerable<UIBase> AllScreens()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Screens }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            UIBase screen = prefab != null ? prefab.GetComponent<UIBase>() : null;
            if (screen != null) yield return screen;
        }
    }

    private static UIBase ScreenNamed(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Screens + "/" + name + ".prefab");
        Assert.IsNotNull(prefab, name + ".prefab");
        return prefab.GetComponent<UIBase>();
    }

    private static bool HasOwnOnBack(UIBase screen) =>
        screen.GetType().GetMethod(nameof(UIBase.OnBack)).DeclaringType != typeof(UIBase);

    private static string PathOf(Button button, UIBase screen) =>
        AnimationUtility.CalculateTransformPath(button.transform, ((Component)screen).transform);

    [Test]
    public void EveryScreen_HasAWayBack()
    {
        var lost = new List<string>();
        int screens = 0;
        foreach (UIBase screen in AllScreens())
        {
            screens++;
            if (screen.ExitButton() == null && !HasOwnOnBack(screen)) lost.Add(screen.name);
        }

        Assert.Greater(screens, 20, "the screen prefabs were found");
        Assert.IsEmpty(lost, "no button to leave by, and no OnBack of their own");
    }

    // 约定找到的按钮 / The screens that never name their back button in code: the prefab wires it, and
    // Back finds it by that wiring or by TopBar/BackButton. Pinned, because a convention is what
    // breaks quietly.
    [TestCase("DepotUI", "TopBar/BackButton")]
    [TestCase("MissionUI", "TopBar/BackButton")]
    [TestCase("SettingUI", "TopBar/back")]
    [TestCase("ShopUI", "TopBar/back")]
    [TestCase("CharInfoUI", "back")]
    [TestCase("CharUI", "TopBar/back")]
    [TestCase("SongSelectUI", "TopBar/BackButton")]
    [TestCase("ItemInfoUI", "Blur")]
    [TestCase("MissionRewardUI", "Blur")]
    public void APrefabWiredScreen_FindsItsOwnBackButton(string name, string path)
    {
        UIBase screen = ScreenNamed(name);
        Button exit = UIBase.FindExitButton(screen);

        Assert.IsNotNull(exit, name);
        Assert.AreEqual(path, PathOf(exit, screen));
    }

    [Test]
    public void HomeAndTheSignInScreen_HaveNothingFurtherBack_AndAskToLeave()
    {
        foreach (string name in new[] { "HomeUI", "LoginUI" })
        {
            UIBase screen = ScreenNamed(name);
            Assert.IsNull(screen.ExitButton(), name + " has no button that leaves it");
            Assert.IsTrue(HasOwnOnBack(screen), name + " answers Back itself, with the question");
        }
    }
}
