using Data.HomeContent;
using UnityEngine;

namespace UI {
    /// <summary>
    /// 小红点 / The dot on one of Home's top-left icons: new mail, an unread notice, today's sign-in
    /// waiting. It only draws what the managers hold; HomeDaily keeps them fresh.
    ///
    /// Placed by Arknights/Home/Build Mail, Notice and Calendar UI.
    /// </summary>
    public class HomeBadge : MonoBehaviour {
        public enum Kind { Mail, Notice, SignIn }

        public Kind kind;
        public GameObject dot;

        private void OnEnable() {
            MailManager.Inst().Changed += Draw;
            NoticeManager.Inst().Changed += Draw;
            SignInManager.Inst().Changed += Draw;
            Draw();
        }

        private void OnDisable() {
            MailManager.Inst().Changed -= Draw;
            NoticeManager.Inst().Changed -= Draw;
            SignInManager.Inst().Changed -= Draw;
        }

        private void Draw() {
            if (this == null || dot == null) return;

            dot.SetActive(kind switch {
                Kind.Mail => MailManager.Inst().Unopened > 0,
                Kind.Notice => NoticeManager.Inst().Unread > 0,
                _ => SignInManager.Inst().CanSign,
            });
        }
    }
}
