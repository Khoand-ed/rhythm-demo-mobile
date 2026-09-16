using System;
using Manager;
using Scripts.Game;
using Tools;
using UI;
using UnityEngine;

namespace Scripts {
    public class GameStart : MonoBehaviour {
        private void Awake() {
            GameManager.Inst();
            LuaEnvManager.Inst();

            UIManager uiManager = UIManager.Inst();

            // 从歌曲场景返回时界面相机是关着的 / The UI camera and canvas are switched off
            // while the gameplay scene runs, because they are DontDestroyOnLoad and
            // would otherwise draw the front-end over the song. They must come back
            // before anything is shown: a screen instantiated under an inactive
            // canvas never runs Awake, and UIBase.Init would then read a null
            // transform.
            GameObject uiCamera = uiManager.GetCamera();
            if (uiCamera != null && !uiCamera.activeSelf) uiCamera.SetActive(true);

            string next = BootIntent.Take();

            // 先开主界面, 再把目标界面叠上去 / Anything other than login opens over the
            // home screen. Without this, returning from a song leaves the song
            // list floating over nothing, and its Back button drops the player
            // onto an empty screen with no music.
            if (next != BootIntent.Login && next != BootIntent.Home) {
                // 主界面出错不能连累目标界面 / HomeUI is Lua-driven and throws if it is
                // reached without a logged-in player. Letting that escape would
                // skip the line below and leave the player staring at nothing,
                // which is the whole failure this branch exists to prevent.
                try {
                    uiManager.Show(BootIntent.Home);
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }

            uiManager.Show(next);
            Destroy(gameObject);
        }
    }
}
