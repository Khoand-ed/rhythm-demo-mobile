using NUnit.Framework;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 开始键 / The login screen's title while the device holds a session: one START where ACCOUNT LOGIN and
// REGISTER stand, and LOG OUT in the corner (Arknights/Login/Build Start And Log Out Buttons). Read from
// the prefab, so a rebuild that drops or moves them fails here instead of on a phone. That LoginUI still
// has no exit button is BackKeyTests' to check.
public class LoginStartTests
{
    private const string LoginPrefabPath = "Assets/Arknights/Resources/Prefab/UI/LoginUI.prefab";

    private static LoginUI Login()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoginPrefabPath);
        Assert.IsNotNull(prefab, LoginPrefabPath);
        LoginUI ui = prefab.GetComponent<LoginUI>();
        Assert.IsNotNull(ui, "LoginUI on the prefab");
        return ui;
    }

    private static RectTransform RectOf(Component c) => (RectTransform)c.transform;

    [Test]
    public void BothButtons_AreLinked_InsideTheTitle()
    {
        LoginUI ui = Login();
        Assert.IsNotNull(ui.home_start, "LoginUI.home_start - run Arknights/Login/Build Start And Log Out Buttons");
        Assert.IsNotNull(ui.home_logout, "LoginUI.home_logout - run Arknights/Login/Build Start And Log Out Buttons");

        Assert.AreSame(ui.homePanel.transform, ui.home_start.transform.parent, "START is on the title's panel");
        Assert.AreSame(ui.homePanel.transform, ui.home_logout.transform.parent, "LOG OUT is on the title's panel");
    }

    [Test]
    public void Start_StandsCentredWhereTheSignInButtonsStand()
    {
        LoginUI ui = Login();
        Assert.IsNotNull(ui.home_start, "LoginUI.home_start");

        RectTransform start = RectOf(ui.home_start);
        RectTransform login = RectOf(ui.home_login);
        RectTransform register = RectOf(ui.home_register);

        Assert.AreEqual(login.anchorMin, start.anchorMin, "anchored as the pair is");
        Assert.AreEqual(login.anchorMax, start.anchorMax, "anchored as the pair is");
        Assert.AreEqual(login.pivot, start.pivot, "pivoted as the pair is");
        Assert.AreEqual((login.anchoredPosition.x + register.anchoredPosition.x) / 2f, start.anchoredPosition.x, 0.5f,
            "halfway between ACCOUNT LOGIN and REGISTER");
        Assert.AreEqual(login.anchoredPosition.y, start.anchoredPosition.y, 0.5f, "at their height");
        Assert.Greater(start.sizeDelta.x, login.sizeDelta.x, "wider than either of the two it replaces");

        Text caption = ui.home_start.GetComponentInChildren<Text>(true);
        Assert.IsNotNull(caption, "START has a caption");
        Assert.AreEqual("START", caption.text);
    }

    [Test]
    public void LogOut_WiresItsClickInCode_AndIsNotABackButton()
    {
        LoginUI ui = Login();
        Assert.IsNotNull(ui.home_logout, "LoginUI.home_logout");

        // 不能在预制体里接 Hide / A click set in the prefab - a Hide above all - would make it the screen's
        // way out, the one Android Back presses; LoginUI adds its listener in Init.
        Assert.AreEqual(0, ui.home_logout.onClick.GetPersistentEventCount(), "LOG OUT has no click set in the prefab");
        Assert.AreEqual(0, ui.home_start.onClick.GetPersistentEventCount(), "START has no click set in the prefab");
        Assert.AreNotEqual("BackButton", ui.home_logout.name, "not named as the convention's back button");
    }

    [Test]
    public void LogOut_SitsInTheTopLeftCorner_BelowTheTopBar()
    {
        LoginUI ui = Login();
        Assert.IsNotNull(ui.home_logout, "LoginUI.home_logout");

        RectTransform logOut = RectOf(ui.home_logout);
        Assert.AreEqual(new Vector2(0f, 1f), logOut.anchorMin, "anchored top-left");
        Assert.AreEqual(new Vector2(0f, 1f), logOut.anchorMax, "anchored top-left");

        // 黑条会吃掉点击 / The top bar's image catches taps, so the button's top edge must sit below it.
        Transform bar = ((Component)ui).transform.Find("TopBar");
        Assert.IsNotNull(bar, "LoginUI has its TopBar");
        float barHeight = ((RectTransform)bar).sizeDelta.y;
        float top = logOut.anchoredPosition.y + (1f - logOut.pivot.y) * logOut.sizeDelta.y;
        Assert.LessOrEqual(top, -barHeight, "LOG OUT starts below the top bar, never under it");
    }
}
