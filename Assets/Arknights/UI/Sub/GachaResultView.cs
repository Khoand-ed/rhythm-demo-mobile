using System;
using System.Collections.Generic;
using System.Globalization;
using Data.Char;
using DG.Tweening;
using Promuse.Contracts.Gacha;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 寻访结果 / What a pull gave: the cards dealt face down, then turned one after another, each
    /// in its rarity's colour - white for 3-star, violet for 4-star, gold for 5-star.
    ///
    /// 点一下全翻 / A tap while cards are still turning turns the rest at once; a tap once they are
    /// all up closes the screen. Nobody should have to sit through ten flips to see a result.
    ///
    /// 只画服务端给的 / It draws exactly what the server answered. Nothing here decides a card; by the
    /// time this opens the pull has already been paid for, rolled and saved.
    ///
    /// Built by Arknights/Gacha/Build Gacha UI. Node names under the card template are the
    /// contract with the builder: Back, Face/Glow, Face/Portrait, Face/Band, Face/Name, Face/Stars,
    /// Face/Badge.
    /// </summary>
    public class GachaResultView : MonoBehaviour {
        public static readonly Color ThreeStar = new Color(0.86f, 0.88f, 0.91f, 1f);
        public static readonly Color FourStar = new Color(0.66f, 0.45f, 0.95f, 1f);
        public static readonly Color FiveStar = new Color(1.00f, 0.78f, 0.25f, 1f);

        private static readonly Color NewBadge = new Color(1f, 0.42f, 0.30f, 1f);
        private static readonly Color CopyBadge = new Color(1f, 1f, 1f, 0.75f);

        public CanvasGroup group;
        public Button tapArea;
        public RectTransform grid;
        public RectTransform cardTemplate;
        public TextMeshProUGUI hint;
        public TextMeshProUGUI summary;

        [Header("节奏 / Timing")]
        public float dealGap = 0.06f;
        public float flipGap = 0.16f;
        public float flipHalf = 0.11f;

        private const int Columns = 5;
        private static readonly Vector2 Spacing = new Vector2(236f, 360f);

        private readonly List<Card> cards = new List<Card>();
        private Sequence running;
        private bool revealed;

        /// <summary>Raised once the player dismisses the results.</summary>
        public event Action Closed;

        private void Awake() {
            cardTemplate.gameObject.SetActive(false);
            tapArea.onClick.AddListener(OnTap);
        }

        public void Show(GachaPullResult result, string footer) {
            gameObject.SetActive(true);
            running?.Kill();

            Layout(result.Pulls);

            revealed = false;
            hint.text = "TAP TO REVEAL ALL";
            summary.text = footer;

            group.alpha = 0f;
            group.DOKill();
            group.DOFade(1f, 0.2f);

            // 先发牌, 再一张张翻 / Deal face down, then turn them in order.
            running = DOTween.Sequence();
            for (int i = 0; i < cards.Count; i++) {
                Card card = cards[i];
                card.Root.localScale = Vector3.one * 0.6f;
                card.Group.alpha = 0f;
                running.Insert(i * dealGap, card.Root.DOScale(1f, 0.18f).SetEase(Ease.OutBack));
                running.Insert(i * dealGap, card.Group.DOFade(1f, 0.15f));
            }

            float start = cards.Count * dealGap + 0.25f;
            for (int i = 0; i < cards.Count; i++) {
                running.Insert(start + i * flipGap, Flip(cards[i]));
            }

            running.OnComplete(Finish);
        }

        private void OnTap() {
            if (!revealed) {
                // 跳过动画 / Skip: every card straight to its face.
                running?.Kill();
                foreach (Card card in cards) card.ShowFace(true);
                Finish();
                return;
            }

            group.DOKill();
            group.DOFade(0f, 0.15f).OnComplete(() => {
                gameObject.SetActive(false);
                Closed?.Invoke();
            });
        }

        private void Finish() {
            revealed = true;
            hint.text = "TAP TO CONTINUE";
        }

        // -------------------------------------------------------------------- layout

        /// <summary>
        /// One card in the middle for a single pull, two rows of five for a ten-pull - in the order
        /// the server rolled them, so a guarantee lands where it happened.
        /// </summary>
        private void Layout(IReadOnlyList<GachaPullOutcome> pulls) {
            while (cards.Count < pulls.Count) {
                cards.Add(new Card((RectTransform)Instantiate(cardTemplate, grid)));
            }

            int rows = (pulls.Count + Columns - 1) / Columns;

            for (int i = 0; i < cards.Count; i++) {
                bool used = i < pulls.Count;
                cards[i].Root.gameObject.SetActive(used);
                if (!used) continue;

                int row = i / Columns;
                int inRow = Mathf.Min(Columns, pulls.Count - row * Columns);
                int column = i % Columns;

                cards[i].Root.anchoredPosition = new Vector2(
                    (column - (inRow - 1) * 0.5f) * Spacing.x,
                    ((rows - 1) * 0.5f - row) * Spacing.y);

                cards[i].Set(pulls[i]);
            }
        }

        private Tween Flip(Card card) {
            Sequence flip = DOTween.Sequence();
            flip.Append(card.Root.DOScaleX(0f, flipHalf).SetEase(Ease.InSine));
            flip.AppendCallback(() => card.ShowFace(false));
            flip.Append(card.Root.DOScaleX(1f, flipHalf).SetEase(Ease.OutSine));

            // 稀有度越高越热闹 / The rarer, the louder: a 5-star punches and its glow pulses on.
            if (card.Rarity >= 5) {
                flip.Append(card.Root.DOPunchScale(Vector3.one * 0.12f, 0.35f, 6, 0.6f));
            }

            return flip;
        }

        public static Color RarityColor(int rarity) {
            return rarity >= 5 ? FiveStar : rarity == 4 ? FourStar : ThreeStar;
        }

        // ---------------------------------------------------------------------- card

        private sealed class Card {
            public readonly RectTransform Root;
            public readonly CanvasGroup Group;
            public int Rarity { get; private set; }

            private readonly GameObject back;
            private readonly GameObject face;
            private readonly Image glow;
            private readonly Image portrait;
            private readonly Image band;
            private readonly TextMeshProUGUI name;
            private readonly Image stars;
            private readonly TextMeshProUGUI badge;

            public Card(RectTransform root) {
                Root = root;
                Group = root.GetComponent<CanvasGroup>();
                back = root.Find("Back").gameObject;
                face = root.Find("Face").gameObject;
                glow = root.Find("Face/Glow").GetComponent<Image>();
                portrait = root.Find("Face/Portrait").GetComponent<Image>();
                band = root.Find("Face/Band").GetComponent<Image>();
                name = root.Find("Face/Name").GetComponent<TextMeshProUGUI>();
                stars = root.Find("Face/Stars").GetComponent<Image>();
                badge = root.Find("Face/Badge").GetComponent<TextMeshProUGUI>();
            }

            public void Set(GachaPullOutcome pull) {
                Rarity = pull.Rarity;
                Root.localScale = Vector3.one;

                CharMeta meta = GachaUI.SafeMeta(pull.CharacterId);
                Sprite art = meta != null ? GachaUI.SafeSprite(meta.GetImage) : null;

                portrait.sprite = art;
                portrait.enabled = art != null;

                Color colour = RarityColor(pull.Rarity);
                band.color = colour;
                // 三星不发光 / A 3-star gets no glow at all: most of a ten-pull is 3-stars, and a rim on
                // every card turns the rare ones' glow into background.
                glow.color = new Color(colour.r, colour.g, colour.b, pull.Rarity >= 5 ? 0.60f : pull.Rarity == 4 ? 0.35f : 0f);
                // 星星用干员界面同一张图 / The same star strip the operator screens use, rather than
                // a glyph the default font may not carry.
                stars.sprite = GachaUI.SafeSprite(() => CharManager.Inst().GetStarImage("info_" + pull.Rarity));
                stars.enabled = stars.sprite != null;

                name.text = meta != null ? meta.GetEnglishName() : pull.CharacterId;

                if (pull.IsNew) {
                    badge.text = "NEW";
                    badge.color = NewBadge;
                } else if (pull.Converted != null) {
                    int amount = pull.Converted.Amount;
                    badge.text = "+" + amount.ToString(CultureInfo.InvariantCulture) + (amount == 1 ? " CERT" : " CERTS");
                    badge.color = CopyBadge;
                } else {
                    badge.text = "";
                }

                back.SetActive(true);
                face.SetActive(false);
            }

            public void ShowFace(bool instant) {
                back.SetActive(false);
                face.SetActive(true);
                if (instant) Root.localScale = Vector3.one;
            }
        }
    }
}
