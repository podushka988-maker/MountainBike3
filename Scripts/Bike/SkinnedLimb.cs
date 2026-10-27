using System.Collections.Generic;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Нога или рука, которая плавно гнётся в суставах (2D-скиннинг без пакетов; превью той же математики —
    /// Tools/art/skin2d.py). Картинка нарисована прямой (Tools/art/rider_v8.py): s — расстояние вдоль конечности от таза/плеча,
    /// y — поперёк; центр картинки — таз/плечо. Картинка натянута на сетку (столбцы через Step по s, три ряда по y).
    ///
    /// Сетку двигают три кости: корпус (root — только поворот), бедро/плечо (b1), голень/предплечье (b2).
    /// Столбец поворачивается целиком на угол a1 + w0·(a0 + rest − a1) + w2·(a2 − a1):
    ///   • у колена/локтя (s от len1 − Joint до len1 + Joint) — плавно от бедра к голени, вокруг колена: колено круглое,
    ///     а не стык двух палок;
    ///   • у таза/плеча (s от RootS0 до RootS1) — наполовину к корпусу, вокруг таза/плеча: ягодица и дельта идут с телом;
    ///   • внутренняя сторона сгиба (под коленом, в локте) отодвигается от оси в 1/cos(сгиб/2) раз — икра и бедро
    ///     сходятся складкой, без клина пустоты.
    /// Сегменты — части картинки со своим порядком отрисовки (бедро под корпусом, голень поверх). В начале сегмента — плавное
    /// появление: голень поверх бедра — это та же картинка в том же месте, поэтому шва не видно.
    /// Свет солнца (_lu/_ld) считается по углу каждого столбца — тоже плавно через сустав.
    /// </summary>
    public sealed class SkinnedLimb
    {
        public const float Step = 0.0125f;
        public float joint = 0.075f, miterMax = 2.4f;
        public float rootS0 = -0.1f, rootS1 = 0.1f, rootMax = 0.5f;
        /// <summary>Угол b1 относительно корпуса в нейтральной стойке, ° (в ней у таза/плеча нет изгиба).</summary>
        public float rest;
        public Color tint = Color.white, paint = Color.white;
        /// <summary>Множитель света солнца (дальняя сторона темнее).</summary>
        public float lightK = 1f;

        private readonly Transform _host, _root, _b1, _b2;
        private readonly float _len1, _inner;
        private readonly BikeSkin.Layered _layers;
        private readonly Material _baseMaterial;
        private readonly List<Segment> _segs = new List<Segment>();
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<Texture, Material> _mats = new Dictionary<Texture, Material>();
        private readonly float _sMin, _sMax, _yMin, _yMax;

        private enum Kind { Base, Paint, Top, LightUp, LightDown }

        private sealed class Layer
        {
            public Kind kind;
            public Mesh mesh;
            public Color[] colors;
        }

        private sealed class Segment
        {
            public float[] s;
            public float fade0, fade1;
            public Vector3[] verts;
            public readonly List<Layer> layers = new List<Layer>();
        }

        /// <param name="host">Кому принадлежат меши (рама байка): вершины считаются в его системе.</param>
        /// <param name="innerSign">Обычная внутренняя сторона сгиба (по y картинки): колено −1, локоть +1. Во время движения
        /// она берётся по тому, куда на самом деле согнут сустав (у куклы при падении — как получится).</param>
        public SkinnedLimb(Transform host, string name, BikeSkin.Layered layers, Material material,
                           Transform root, Transform b1, Transform b2, float len1, float innerSign)
        {
            _host = new GameObject(name).transform;
            _host.SetParent(host, false);
            _root = root; _b1 = b1; _b2 = b2; _len1 = len1; _inner = innerSign;
            _layers = layers; _baseMaterial = material;
            Sprite r = Valid(layers.paintLayer) ? layers.paintLayer : Valid(layers.baseLayer) ? layers.baseLayer : layers.topLayer;
            if (!Valid(r)) return;
            float ppu = r.pixelsPerUnit;
            _sMin = -r.pivot.x / ppu; _sMax = (r.rect.width - r.pivot.x) / ppu;
            _yMin = -r.pivot.y / ppu; _yMax = (r.rect.height - r.pivot.y) / ppu;
        }

        public bool IsValid => _sMax > _sMin;

        private static bool Valid(Sprite s) => s != null && s.rect.width > 8f;   // пустой слой — картинка 4×4

        /// <summary>Часть картинки [s0, s1] со своим порядком отрисовки; от fade0 до fade1 — плавное появление.</summary>
        public void AddSegment(float s0, float s1, float fade0, float fade1, ref int order)
        {
            s0 = Mathf.Max(s0, _sMin); s1 = Mathf.Min(s1, _sMax);
            if (!IsValid || s1 - s0 < Step) { order += 5; return; }      // нет картинки — порядок всё равно занимаем
            var seg = new Segment { fade0 = fade0, fade1 = fade1 };
            var cols = new List<float>();
            for (float s = s0; s < s1 - 1e-4f; s += Step) cols.Add(s);
            cols.Add(s1);
            seg.s = cols.ToArray();
            seg.verts = new Vector3[seg.s.Length * 3];
            var tris = new int[(seg.s.Length - 1) * 12];
            for (int i = 0, t = 0; i < seg.s.Length - 1; i++)
                for (int j = 0; j < 2; j++)
                {
                    int a = i * 3 + j, b = (i + 1) * 3 + j, c = (i + 1) * 3 + j + 1, d = i * 3 + j + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                }

            AddLayer(seg, Kind.Base, _layers.baseLayer, tris, ref order);
            AddLayer(seg, Kind.Paint, _layers.paintLayer, tris, ref order);
            AddLayer(seg, Kind.Top, _layers.topLayer, tris, ref order);
            AddLayer(seg, Kind.LightUp, _layers.lightUp, tris, ref order);
            AddLayer(seg, Kind.LightDown, _layers.lightDown, tris, ref order);
            _segs.Add(seg);
        }

        private void AddLayer(Segment seg, Kind kind, Sprite sprite, int[] tris, ref int order)
        {
            int myOrder = order++;                                   // порядок занимаем и для пустых слоёв (как у спрайтов)
            if (!Valid(sprite)) return;
            var go = new GameObject(sprite.name);
            go.transform.SetParent(_host, false);
            var mesh = new Mesh { name = sprite.name };
            mesh.MarkDynamic();
            float ppu = sprite.pixelsPerUnit;
            Rect tr = sprite.textureRect; Texture2D tex = sprite.texture;
            var uv = new Vector2[seg.verts.Length];
            float[] ys = { _yMin, 0f, _yMax };
            for (int i = 0; i < seg.s.Length; i++)
                for (int j = 0; j < 3; j++)
                {
                    float px = seg.s[i] * ppu + sprite.pivot.x, py = ys[j] * ppu + sprite.pivot.y;
                    uv[i * 3 + j] = new Vector2((tr.x + px * tr.width / sprite.rect.width) / tex.width,
                                                (tr.y + py * tr.height / sprite.rect.height) / tex.height);
                }
            mesh.vertices = seg.verts;
            mesh.uv = uv;
            var layer = new Layer { kind = kind, mesh = mesh, colors = new Color[seg.verts.Length] };
            mesh.colors = layer.colors;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(8f, 8f, 1f));     // вершины всегда около байка — без пересчёта
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFor(tex);
            mr.sortingOrder = myOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _owned.Add(mesh);
            seg.layers.Add(layer);
        }

        private Material MaterialFor(Texture2D tex)
        {
            if (_mats.TryGetValue(tex, out Material m)) return m;
            m = _baseMaterial != null ? new Material(_baseMaterial) : new Material(Shader.Find("Sprites/Default"));
            m.name = "Limb " + tex.name;
            m.mainTexture = tex;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            _mats[tex] = m; _owned.Add(m);
            return m;
        }

        /// <summary>Пересчитать сетку по костям и свет. sun — направление на солнце в мире.</summary>
        private readonly float[] _ys = new float[3];

        public void Update(Vector2 sun, float sunStrength)
        {
            if (_segs.Count == 0 || _b1 == null) return;
            Vector2 P1 = _b1.position;
            float a1 = Angle(_b1.right);
            Vector2 P2 = _b2 != null ? (Vector2)_b2.position : P1 + Dir(a1) * _len1;
            float a2 = _b2 != null ? Angle(_b2.right) : a1;
            float a0 = _root != null ? Angle(_root.right) : a1 - rest;
            float d0 = Mathf.DeltaAngle(a1, a0 + rest), d2 = Mathf.DeltaAngle(a1, a2);
            // внутренняя сторона сгиба (куда повернулась голень/предплечье): во сколько раз отодвинуть от оси в самом суставе
            float inner = d2 > 0f ? 1f : (d2 < 0f ? -1f : _inner);
            float bend = Mathf.Min(Mathf.Abs(d2), 170f);
            float miter = Mathf.Min(miterMax, 1f / Mathf.Max(1e-3f, Mathf.Cos(bend * 0.5f * Mathf.Deg2Rad)));
            Vector2 kneeFromB1 = P1 + Dir(a1) * _len1;
            Matrix4x4 toLocal = _host.worldToLocalMatrix;
            _ys[0] = _yMin; _ys[1] = 0f; _ys[2] = _yMax;
            float[] ys = _ys;                                   // без нового массива каждый кадр

            foreach (Segment seg in _segs)
            {
                int n = seg.s.Length;
                for (int i = 0; i < n; i++)
                {
                    float s = seg.s[i];
                    float w2 = SmoothStep((s - (_len1 - joint)) / (2f * joint));
                    float w0 = rootMax * SmoothStep((rootS1 - s) / (rootS1 - rootS0));
                    float th = (a1 + w0 * d0 + w2 * d2) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(th), sn = Mathf.Sin(th);
                    bool nearRoot = s < _len1 * 0.5f;
                    Vector2 pivot = nearRoot ? P1 : Vector2.Lerp(kneeFromB1, P2, w2);
                    float ss = nearRoot ? s : s - _len1;
                    float bump = 1f - SmoothStep(Mathf.Abs(s - _len1) / joint);
                    for (int j = 0; j < 3; j++)
                    {
                        float y = ys[j];
                        if (y * inner > 0f) y *= 1f + (miter - 1f) * bump;
                        Vector2 p = pivot + new Vector2(c * ss - sn * y, sn * ss + c * y);
                        seg.verts[i * 3 + j] = toLocal.MultiplyPoint3x4(p);
                    }
                    // цвета столбца: появление сегмента и свет солнца по углу столбца
                    float fade = seg.fade1 > seg.fade0 ? Mathf.Clamp01((s - seg.fade0) / (seg.fade1 - seg.fade0)) : 1f;
                    float lit = (-sn * sun.x + c * sun.y) * sunStrength * lightK;
                    foreach (Layer l in seg.layers)
                    {
                        Color col;
                        switch (l.kind)
                        {
                            case Kind.Paint: col = paint; break;
                            case Kind.LightUp: col = new Color(1f, 1f, 1f, Mathf.Clamp01(lit)); break;
                            case Kind.LightDown: col = new Color(1f, 1f, 1f, Mathf.Clamp01(-lit)); break;
                            default: col = tint; break;
                        }
                        col.a *= fade;
                        l.colors[i * 3] = l.colors[i * 3 + 1] = l.colors[i * 3 + 2] = col;
                    }
                }
                foreach (Layer l in seg.layers)
                {
                    l.mesh.vertices = seg.verts;
                    l.mesh.colors = l.colors;
                }
            }
        }

        /// <summary>Удалить созданные меши и материалы.</summary>
        public void Dispose()
        {
            foreach (Object o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear(); _mats.Clear();
        }

        private static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        private static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        private static float SmoothStep(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
