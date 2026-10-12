using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.UI;

// 立绘取景 / How SplashFrame sizes and places a whole splash: never stretched, the figure as tall as
// the frame asks, a card always covered - and an Image with no frame still gets its sprite the old way.
public class SplashFrameTests
{
    // A wide splash, so a width/height mix-up shows: 400 x 300.
    private const int ArtW = 400;
    private const int ArtH = 300;

    // The figure in the middle of the splash, the face near its top.
    private static readonly Rect Figure = new Rect(0.40f, 0.10f, 0.20f, 0.70f);
    private static readonly Rect Face = new Rect(0.45f, 0.66f, 0.10f, 0.12f);

    private GameObject root;
    private Texture2D texture;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(ArtW, ArtH, TextureFormat.RGBA32, false);
        sprite = Sprite.Create(texture, new Rect(0, 0, ArtW, ArtH), new Vector2(0.5f, 0.5f));
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
    }

    private (Image slot, SplashFrame frame, RectTransform splash) Frame(SplashFrame.Mode mode, float w, float h)
    {
        root = new GameObject("Slot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(w, h);

        var child = new GameObject("Splash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(root.transform, false);

        SplashFrame frame = root.AddComponent<SplashFrame>();
        frame.mode = mode;
        frame.splash = child.GetComponent<Image>();
        return (root.GetComponent<Image>(), frame, (RectTransform)child.transform);
    }

    [Test]
    public void Figure_IsAsTallAsTheFrameAsks_AndKeepsTheSplashsProportions()
    {
        var (slot, _, splash) = Frame(SplashFrame.Mode.Figure, 820f, 1000f);

        SplashFrame.Show(slot, sprite, Figure, Face);

        Vector2 size = splash.sizeDelta;
        Assert.AreEqual(1000f * SplashFrame.DefaultFigureHeight, size.y * Figure.height, 0.01f, "figure height");
        Assert.AreEqual((float)ArtW / ArtH, size.x / size.y, 0.0001f, "never stretched");
    }

    [Test]
    public void Figure_PutsTheFiguresCentreOnTheFramesCentre()
    {
        var (slot, _, splash) = Frame(SplashFrame.Mode.Figure, 820f, 1000f);

        SplashFrame.Show(slot, sprite, Figure, Face);

        // The figure's centre, measured from the splash's own centre, lands at the frame's centre.
        Vector2 size = splash.sizeDelta;
        Vector2 centre = splash.anchoredPosition + new Vector2((Figure.center.x - 0.5f) * size.x, (Figure.center.y - 0.5f) * size.y);
        Assert.AreEqual(0f, centre.x, 0.01f);
        Assert.AreEqual(0f, centre.y, 0.01f);
    }

    [Test]
    public void Figure_WithNoRectangles_ShowsTheWholeSpriteCentred()
    {
        var (slot, _, splash) = Frame(SplashFrame.Mode.Figure, 820f, 1000f);

        SplashFrame.Show(slot, sprite, default, default);

        Assert.AreEqual(1000f * SplashFrame.DefaultFigureHeight, splash.sizeDelta.y, 0.01f);
        Assert.AreEqual(Vector2.zero, splash.anchoredPosition);
    }

    [Test]
    public void Cover_FillsTheCard_WithNoEdgeShowing()
    {
        var (slot, _, splash) = Frame(SplashFrame.Mode.Cover, 200f, 344f);

        SplashFrame.Show(slot, sprite, Figure, Face);

        Vector2 size = splash.sizeDelta;
        Vector2 at = splash.anchoredPosition;
        Assert.GreaterOrEqual(size.x + 0.001f, 200f);
        Assert.GreaterOrEqual(size.y + 0.001f, 344f);
        Assert.LessOrEqual(Mathf.Abs(at.x), (size.x - 200f) / 2f + 0.001f, "left or right edge would show");
        Assert.LessOrEqual(Mathf.Abs(at.y), (size.y - 344f) / 2f + 0.001f, "top or bottom edge would show");
    }

    [Test]
    public void Cover_FramesOnTheFace()
    {
        var (slot, _, splash) = Frame(SplashFrame.Mode.Cover, 200f, 344f);

        SplashFrame.Show(slot, sprite, Figure, Face);

        Vector2 size = splash.sizeDelta;
        Assert.AreEqual(200f * SplashFrame.DefaultFaceWidth, Face.width * size.x, 0.01f, "face width");

        // Face centred across; its top a set gap below the card's top (both measured from the centre).
        float faceX = splash.anchoredPosition.x + (Face.center.x - 0.5f) * size.x;
        float faceTop = splash.anchoredPosition.y + (Face.yMax - 0.5f) * size.y;
        Assert.AreEqual(0f, faceX, 0.01f);
        Assert.AreEqual(344f / 2f - 344f * SplashFrame.DefaultFaceTop, faceTop, 0.01f);
    }

    [Test]
    public void AFramedSlot_DrawsNothingItself()
    {
        var (slot, frame, _) = Frame(SplashFrame.Mode.Figure, 820f, 1000f);
        slot.color = new Color(0.3f, 0.31f, 0.34f, 1f);

        SplashFrame.Show(slot, sprite, Figure, Face);

        Assert.IsNull(slot.sprite);
        Assert.AreEqual(Color.clear, slot.color, "the grey tint that darkened the art is gone");
        Assert.AreSame(sprite, frame.splash.sprite);
        Assert.IsTrue(frame.splash.enabled);
    }

    [Test]
    public void NoSprite_HidesTheSplash()
    {
        var (slot, frame, _) = Frame(SplashFrame.Mode.Figure, 820f, 1000f);

        SplashFrame.Show(slot, null, default, default);

        Assert.IsFalse(frame.splash.enabled);
    }

    [Test]
    public void AnImageWithNoFrame_GetsTheSpriteTheOldWay()
    {
        root = new GameObject("Plain", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Image plain = root.GetComponent<Image>();

        SplashFrame.Show(plain, sprite, Figure, Face);

        Assert.AreSame(sprite, plain.sprite);
        Assert.AreEqual(Color.white, plain.color);
    }

    // ---------------------------------------------------------------- shade

    private sealed class ShadeProbe : SplashShade
    {
        public void Fill(VertexHelper vh) => OnPopulateMesh(vh);
    }

    [TestCase(SplashShade.Edge.Left)]
    [TestCase(SplashShade.Edge.Right)]
    [TestCase(SplashShade.Edge.Top)]
    [TestCase(SplashShade.Edge.Bottom)]
    public void Shade_IsDarkestAtItsEdge_AndClearAtTheOther(SplashShade.Edge edge)
    {
        root = new GameObject("Shade", typeof(RectTransform), typeof(CanvasRenderer));
        ((RectTransform)root.transform).sizeDelta = new Vector2(300f, 100f);
        ShadeProbe shade = root.AddComponent<ShadeProbe>();
        shade.color = Color.black;
        shade.darkest = edge;
        shade.strength = 0.6f;
        shade.hold = 0.25f;

        var vh = new VertexHelper();
        shade.Fill(vh);

        float darkCoord = float.NaN, clearCoord = float.NaN;
        byte darkAlpha = 0, clearAlpha = 255;
        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float coord = edge == SplashShade.Edge.Left || edge == SplashShade.Edge.Right ? v.position.x : v.position.y;
            bool atDarkSide = edge == SplashShade.Edge.Left || edge == SplashShade.Edge.Bottom;
            if (float.IsNaN(darkCoord) || (atDarkSide ? coord < darkCoord : coord > darkCoord)) { darkCoord = coord; darkAlpha = v.color.a; }
            if (float.IsNaN(clearCoord) || (atDarkSide ? coord > clearCoord : coord < clearCoord)) { clearCoord = coord; clearAlpha = v.color.a; }
        }

        vh.Dispose();
        Assert.AreEqual(Mathf.RoundToInt(255 * 0.6f), darkAlpha, "at the dark edge");
        Assert.AreEqual(0, clearAlpha, "at the far edge");
    }
}
