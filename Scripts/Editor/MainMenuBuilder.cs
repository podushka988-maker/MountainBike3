using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BikeGame.EditorTools
{
    /// <summary>
    /// Главное меню (создаётся вместе со сценой игры — меню Bike → Создать сцену Free Ride):
    /// тот же вечерний фон с параллаксом, трасса, птицы и пылинки; по кадру время от времени проезжает гонщик на автопилоте.
    /// Деревянная вывеска с названием и три доски-кнопки: ИГРАТЬ, МАГАЗИН (цвета экипировки), ВЫЙТИ.
    /// </summary>
    public static partial class FreeRideSceneBuilder
    {
        public const string MenuScenePath = Root + "/Scenes/MainMenu.unity";
        private static Sprite MenuSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{EnvArt}/UI/Menu/{name}.png");

        private static void BuildMenuScene(Material mat, BikeConfig config, PhysicsMaterial2D groundPhys, BikeSkin skin)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject camGo = CreateCameraAndBackground(mat);
            var cam = camGo.GetComponent<Camera>();
            cam.orthographicSize = 4.6f;
            camGo.transform.position = new Vector3(0f, 3.5f, -10f);         // земля в нижней части кадра

            // трасса: пологие холмы, длинные ровные участки (вилли), небольшие фигуры и огромные трамплины и дропы в духе Red Bull Rampage
            TerrainGenerator terrain = CreateTerrain(mat, groundPhys);
            terrain.features = true;
            terrain.maxDifficultyDistance = 1e6f;                            // сложность не растёт: обычные фигуры небольшие
            terrain.firstFeature = 45f;
            terrain.featureGapStart = new Vector2(30f, 25f);                 // между фигурами 30–55 м ровного — место для вилли
            terrain.sequentialFeatures = true;                              // локации по порядку (первая — по случайному сиду)
            terrain.rampageWeight = 2.5f; terrain.rampageFrom = 0f;         // и огромные фигуры Rampage (~40% фигур): кикеры, каньоны, дропы со скал
            terrain.doublesWeight = terrain.gapsWeight = terrain.stairsWeight = terrain.stepUpsWeight = terrain.rocksWeight = 0f;
            terrain.tablesWeight = 1.2f; terrain.dropsWeight = 1f; terrain.moundsWeight = 0.6f; terrain.rollersWeight = 0.5f;
            terrain.bigHillsAmplitude = 1.2f;
            terrain.hillsAmplitude = 0.5f;
            terrain.bumpsAmplitude = 0.05f;
            terrain.target = camGo.transform;
            terrain.keepBehind = 55f; terrain.buildAhead = 95f;            // камера отдаляется, чтобы показать огромную фигуру целиком

            // гонщик на автопилоте
            BikeController2D bike = BuildBike(mat, config, skin, new Vector2(-6f, 0f));
            terrain.wheels = new[] { bike.rearWheel.transform, bike.frontWheel.transform };
            var fx = bike.gameObject.AddComponent<BikeEffects>();
            fx.bike = bike;
            fx.dustMaterial = GetOrCreateTexMaterial("Dust", EnvArt + "/FX/dust.png");
            var audio = bike.gameObject.AddComponent<BikeAudio>();
            audio.bike = bike;
            audio.wind = Clip("wind_loop"); audio.roll = Clip("roll_dirt_loop"); audio.freehub = Clip("freehub_loop"); audio.freehubSlow = Clip("freehub_slow_loop"); audio.pedal = Clip("pedal_loop");
            audio.skid = Clip("skid_loop"); audio.crash = Clip("crash"); audio.frameCrack = Clip("frame_crack");
            audio.landings = new[] { Clip("land_1"), Clip("land_2"), Clip("land_3") };
            var rider = new GameObject("MenuRider").AddComponent<MenuRider>();
            rider.bike = bike; rider.cam = cam; rider.terrain = terrain;

            // атмосфера: пылинки, птицы, лес
            var motes = camGo.AddComponent<AmbientMotes>();
            motes.moteMaterial = GetOrCreateTexMaterial("Mote", EnvArt + "/FX/mote.png");
            WireLocations(camGo, terrain, motes, mat, BackgroundThemes.Pick.Random);       // в меню — случайная локация (витрина)
            var birds = camGo.AddComponent<BirdFlock>();
            birds.material = mat;
            birds.interval = new Vector2(5f, 14f);                           // в меню птицы чаще
            birds.frames = new[] { 0, 1, 2 }.Select(f => AssetDatabase.LoadAssetAtPath<Sprite>($"{EnvArt}/FX/bird_{f}.png")).ToArray();
            var ambient = camGo.AddComponent<AmbientAudio>();
            ambient.forest = Clip("forest_loop");
            ambient.volume = 0.5f;

            // ---------- интерфейс ----------
            var canvasGo = new GameObject("Canvas") { layer = 5 };
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
            RectTransform root = canvasGo.GetComponent<RectTransform>();
            AddLightTint(root);
            var vignette = MakeRect(root, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            vignette.sprite = UiSprite("vignette");
            vignette.color = vignette.sprite != null ? new Color(1f, 1f, 1f, 0.85f) : Color.clear;
            vignette.raycastTarget = false;
            var safe = MakeRect(root, "SafeArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeArea>();

            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.previewRider = bike.GetComponent<BikeVisual>();
            menu.swatchSprite = MenuSprite("menu_swatch");
            menu.swatchSelectedSprite = MenuSprite("menu_swatch_sel");

            // вывеска с названием (покачивается на верёвках) и рекорд
            var title = MenuImage(safe, "Title", MenuSprite("menu_title"), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(900, 252));
            var sway = title.gameObject.AddComponent<ButtonPress>(); sway.hoverScale = 1f; sway.pressScale = 1f; sway.sway = 1.2f;
            title.raycastTarget = false;
            menu.hideInShop = new[] { title.gameObject };      // в магазине — только фон с ботом на трейле и карточки
            menu.bestText = MakeText(safe, "Best", new Vector2(0.5f, 1f), new Vector2(0, -320), new Vector2(700, 50), 34, TextAnchor.MiddleCenter, "");
            menu.bestText.color = new Color(1f, 0.93f, 0.8f);

            // доски-кнопки
            menu.playButton = MenuButton(safe, "Play", MenuSprite("menu_btn_play"), new Vector2(0.5f, 0.62f), new Vector2(560, 131));
            menu.shopButton = MenuButton(safe, "Shop", MenuSprite("menu_btn_shop"), new Vector2(0.5f, 0.495f), new Vector2(520, 121));
            menu.exitButton = MenuButton(safe, "Exit", MenuSprite("menu_btn_exit"), new Vector2(0.5f, 0.375f), new Vector2(480, 112));

            // монеты: деревянная табличка в правом верхнем углу (видна и в меню, и в магазине)
            var plate = MenuImage(safe, "Coins", MenuSprite("loc_label"), new Vector2(1f, 1f), new Vector2(-200f, -62f), new Vector2(340f, 80f));
            plate.preserveAspect = false;
            plate.raycastTarget = false;
            var plateCoin = MenuImage(plate.rectTransform, "Coin", MenuSprite("coin"), new Vector2(0f, 0.5f), new Vector2(52f, 3f), new Vector2(62f, 62f));
            plateCoin.raycastTarget = false;
            var coinsLabel = PanelText(plate.rectTransform, "Amount", new Vector2(1f, 0.5f), new Vector2(-30f, 4f), new Vector2(240f, 70f), 42,
                                       TextAnchor.MiddleRight, "0", new Color(1f, 0.85f, 0.38f));
            coinsLabel.rectTransform.pivot = new Vector2(1f, 0.5f);
            menu.coinsText = coinsLabel.gameObject.AddComponent<CountUpText>();
            menu.coinsText.text = coinsLabel;
            menu.coinsPlate = plate.rectTransform;
            menu.buySound = Clip("buy"); menu.deniedSound = Clip("denied");

            // магазин: на фоне главного меню деревянные карточки байков — лента листается вбок (пальцем, мышью, колёсиком);
            // на карточке — картинка байка, цена на верхней панели и доска КУПИТЬ / ВЫБРАТЬ / ВЫБРАНО
            menu.cardSprite = MenuSprite("menu_card"); menu.cardSelectedSprite = MenuSprite("menu_card_sel");
            menu.selectSprite = MenuSprite("menu_btn_select"); menu.selectedSprite = MenuSprite("menu_btn_selected");
            menu.buySprite = MenuSprite("menu_btn_buy");
            var panel = MakeRect(safe, "Shop", new Vector2(0f, 0.52f), new Vector2(1f, 0.52f), Vector2.zero, new Vector2(0f, 600f));
            menu.shopPanel = panel.gameObject;
            var models = _models ?? new BikeChoice.Model[0];
            const float gap = 470f, cardW = 420f, margin = 70f;
            float contentW = margin * 2f + cardW + gap * Mathf.Max(0, models.Length - 1);
            var shopRect = panel.gameObject.AddComponent<ScrollRect>();
            shopRect.horizontal = true; shopRect.vertical = false;
            shopRect.movementType = ScrollRect.MovementType.Elastic; shopRect.elasticity = 0.12f;
            shopRect.inertia = true; shopRect.decelerationRate = 0.12f;
            shopRect.scrollSensitivity = 45f;
            var shopHit = panel.gameObject.AddComponent<Image>();                // ловит перетаскивание между карточками
            shopHit.color = Color.clear;
            var shopContent = MakeRect(panel, "Content", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(contentW, 600f));
            shopContent.pivot = new Vector2(0f, 0.5f);
            shopRect.viewport = panel; shopRect.content = shopContent;
            menu.shopScroll = shopRect;
            for (int i = 0; i < models.Length; i++) BikeCardUI(shopContent, menu, models[i], i, new Vector2(margin + cardW / 2f + i * gap, -20f));
            menu.closeShopButton = MenuButton(panel, "Close", MenuSprite("menu_close"), new Vector2(1f, 1f), new Vector2(84, 84), new Vector2(-70, 10));
            panel.gameObject.SetActive(false);

            // выбор локации (после ИГРАТЬ): лента на весь экран — картинка локации во весь экран, название на табличке в левом
            // нижнем углу; листается вверх-вниз и встаёт на локацию; нажатие — заезд; справа — точки, сверху справа — закрыть
            var locs = MakeRect(root, "Locations", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            menu.locationPanel = locs.gameObject;
            var locBg = locs.gameObject.AddComponent<Image>();
            locBg.color = new Color(0.02f, 0.02f, 0.03f, 1f);
            var scroll = locs.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic; scroll.elasticity = 0.12f;
            scroll.inertia = true; scroll.decelerationRate = 0.06f;
            scroll.scrollSensitivity = 0f;                                   // колёсико листает по одной локации (LocationFeed)
            var viewport = MakeRect(locs, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = MakeRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 1080f * Locations.Ids.Length));
            content.pivot = new Vector2(0.5f, 1f);
            scroll.viewport = viewport; scroll.content = content;
            var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Inter-Black.otf");
            var pages = new List<RectTransform>();
            for (int i = 0; i < Locations.Ids.Length; i++)
                pages.Add(LocationPageUI(content, menu, Locations.Ids[i], Locations.Titles[i], i, font));
            var feed = locs.gameObject.AddComponent<LocationFeed>();
            feed.scroll = scroll; feed.pages = pages.ToArray();
            // точки справа — на какой локации лента
            var dotSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var dots = new List<Image>();
            for (int i = 0; i < Locations.Ids.Length; i++)
            {
                var d = MakeRect(locs, "Dot " + i, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                 new Vector2(-56f, (Locations.Ids.Length - 1) * 22f - i * 44f), new Vector2(20f, 20f)).gameObject.AddComponent<Image>();
                d.sprite = dotSprite; d.raycastTarget = false;
                dots.Add(d);
            }
            feed.dots = dots.ToArray();
            menu.locationFeed = feed;
            menu.closeLocationsButton = MenuButton(locs, "Close", MenuSprite("menu_close"), new Vector2(1f, 1f), new Vector2(96, 96), new Vector2(-76, -76));
            locs.gameObject.SetActive(false);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }


        /// <summary>
        /// Карточка байка: доска (кнопка), окно с картинкой байка, цена на верхней полупрозрачной панели окна (монета + число,
        /// у бесплатного — FREE) и доска КУПИТЬ / ВЫБРАТЬ / ВЫБРАНО.
        /// </summary>
        private static void BikeCardUI(RectTransform panel, MainMenu menu, BikeChoice.Model m, int index, Vector2 pos)
        {
            var cardImg = MenuImage(panel, "Card " + index, MenuSprite("menu_card"), new Vector2(0f, 0.5f), pos, new Vector2(420, 465));
            cardImg.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var rt = cardImg.rectTransform;
            // картинка байка в окне карточки (окно — верхние 3/4 доски)
            var pic = MenuImage(rt, "Bike", m.preview, new Vector2(0.5f, 1f), new Vector2(0, -192), new Vector2(320, 178));
            pic.raycastTarget = false;
            // цена — на верхней полупрозрачной панели окна (в картинке доски: y 73–126 из 620 → центр ~75 px от верха карточки)
            var price = MakeRect(rt, "Price", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -75f), new Vector2(318f, 42f));
            var coin = MenuImage(price, "Coin", MenuSprite("coin"), new Vector2(0.5f, 0.5f), new Vector2(-40f, 1f), new Vector2(38f, 38f));
            coin.raycastTarget = false;
            var priceText = PanelText(price, "Amount", new Vector2(0.5f, 0.5f), new Vector2(20f, 1f), new Vector2(240f, 44f), 32, TextAnchor.MiddleCenter,
                                      m.price > 0 ? PlayerWallet.Format(m.price) : "FREE",
                                      m.price > 0 ? new Color(1f, 0.85f, 0.38f) : new Color(1f, 0.93f, 0.82f));
            foreach (Shadow fx in priceText.GetComponents<Shadow>())        // контур (Outline) и тень — потоньше для мелкой цены
                fx.effectDistance = fx is Outline ? new Vector2(2f, -2f) : new Vector2(0f, -3f);
            var btn = MenuButton(rt, "Select", MenuSprite(m.price > 0 ? "menu_btn_buy" : "menu_btn_select"), new Vector2(0.5f, 0f), new Vector2(300, 70), new Vector2(0, 62));
            var card = cardImg.gameObject.AddComponent<BikeCard>();
            card.index = index; card.id = m.id; card.price = m.price;
            card.card = cardImg; card.button = btn.GetComponent<Image>(); card.menu = menu;
            card.priceText = priceText; card.priceCoin = coin;
        }

        /// <summary>
        /// Панель локации в ленте: картинка на весь экран (заполняет экран при любом соотношении сторон, лишнее обрезается)
        /// и деревянная табличка с названием в левом нижнем углу. Название — текст Label (шрифт Inter Black): поменять —
        /// в Locations.Titles или прямо в сцене (Canvas → Locations → Viewport → Content → Location … → Board → Label).
        /// </summary>
        private static RectTransform LocationPageUI(RectTransform content, MainMenu menu, string id, string title, int index, Font font)
        {
            var page = MakeRect(content, "Location " + id, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1080f * index), new Vector2(1920f, 1080f));
            page.pivot = new Vector2(0.5f, 1f);
            page.gameObject.AddComponent<RectMask2D>();
            var sp = MenuSprite("loc_full_" + id);
            if (sp == null) Debug.LogWarning("[Bike] Нет картинки локации loc_full_" + id + " (Tools/art/ui/make_locations.py)");
            var pic = MakeRect(page, "Picture", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            pic.sprite = sp;
            if (sp == null) pic.color = new Color(0.25f, 0.22f, 0.2f);
            var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = sp != null ? sp.rect.width / sp.rect.height : 16f / 9f;
            pic.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            // табличка с названием — левый нижний угол
            var board = MenuImage(page, "Board", MenuSprite("loc_label"), new Vector2(0f, 0f), new Vector2(70f + 300f, 60f + 70f), new Vector2(600f, 140f));
            board.raycastTarget = false;
            board.gameObject.AddComponent<ButtonPress>().hoverScale = 1f;
            var label = MakeText(board.rectTransform, "Label", new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(520f, 100f), 78, TextAnchor.MiddleCenter, title);
            if (font != null) { label.font = font; label.fontStyle = FontStyle.Normal; }
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 28; label.resizeTextMaxSize = 78;
            label.color = new Color(1f, 0.93f, 0.82f);
            var o = label.GetComponent<Outline>();
            if (o != null) { o.effectColor = new Color(0.16f, 0.09f, 0.05f, 0.95f); o.effectDistance = new Vector2(3f, -3f); }
            var sh = label.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.5f); sh.effectDistance = new Vector2(0f, -6f);
            var card = page.gameObject.AddComponent<LocationCard>();
            card.id = id; card.card = pic; card.label = label; card.menu = menu;
            return page;
        }

        private static Image MenuImage(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = MakeRect(parent, name, anchor, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            if (sprite == null) img.color = new Color(0.45f, 0.3f, 0.2f, 0.9f);
            return img;
        }

        private static Button MenuButton(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 size, Vector2 pos = default)
        {
            var img = MenuImage(parent, name, sprite, anchor, pos, size);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f); b.colors = colors;
            img.gameObject.AddComponent<ButtonPress>();
            return b;
        }

        private static void SwatchRow(RectTransform panel, string label, string key, Color[] colors, float y)
        {
            var t = MakeText(panel, label + "Label", new Vector2(0f, 1f), new Vector2(150, y), new Vector2(200, 60), 34, TextAnchor.MiddleLeft, label);
            t.rectTransform.pivot = new Vector2(0f, 0.5f);
            t.color = new Color(1f, 0.93f, 0.8f);
            for (int i = 0; i < colors.Length; i++)
            {
                var rt = MakeRect(panel, $"{label} {i}", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330 + i * 66, y), new Vector2(60, 60));
                var fill = rt.gameObject.AddComponent<Image>();
                fill.sprite = MenuSprite("menu_swatch");
                fill.color = Color.white;
                // цвет — во внутреннем окне рамки (отдельная картинка поверх белого окна)
                var inner = MakeRect(rt, "Color", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(40, 38));
                var ci = inner.gameObject.AddComponent<Image>();
                ci.color = colors[i];
                ci.raycastTarget = false;
                rt.gameObject.AddComponent<Button>();
                var sw = rt.gameObject.AddComponent<ColorSwatch>();
                sw.key = key; sw.color = colors[i]; sw.frame = fill;
            }
        }

        /// <summary>Сцены в сборке: меню первым (с него стартует игра), затем игра.</summary>
        private static void SetBuildScenes(params string[] first)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => first.Contains(s.path));
            scenes.InsertRange(0, first.Select(p => new EditorBuildSettingsScene(p, true)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
