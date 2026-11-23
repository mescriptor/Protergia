using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ReferenceHouseTools
{
    // Positions are pixels from the top left of the native LED frame, calibrated to
    // the fixed Reference House camera. Rendering and hit testing share this layout.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ReferenceHouseDiagram : MaskableGraphic
    {
        public enum Hotspot { Photovoltaics, HeatPump, Battery, Charger, Meter }

        public readonly struct Label
        {
            public readonly Hotspot Id;
            public readonly string Text;
            public readonly Rect TextBounds, HitBounds;
            internal readonly Rect Pill;
            internal Vector2 Sun => new Vector2(Pill.xMin - 29, Pill.center.y);

            internal Label(Hotspot id, string text, Rect pill, float hitLeft = 51)
            {
                Id = id;
                Text = text;
                Pill = pill;
                TextBounds = new Rect(pill.x + 12, pill.y, pill.width - 24, pill.height);
                HitBounds = Rect.MinMaxRect(pill.xMin - hitLeft, pill.center.y - Mathf.Max(24, pill.height / 2),
                    pill.xMax, pill.center.y + Mathf.Max(24, pill.height / 2));
            }
        }

        public const string Title = "Ενεργειακή τομή κατοικίας";
        static readonly Rect TitlePill = new Rect(141, 66, 302, 58);
        public static readonly Rect TitleTextBounds = new Rect(155, 66, 274, 58);
        public static readonly Rect MeterCardBounds = new Rect(1252, 854, 356, 672);
        public static readonly Color32 TitleColor = new Color32(0x50, 0x09, 0x83, 255);
        public static readonly Color32 LabelColor = new Color32(0xE4, 0x98, 0xEF, 255);
        public static readonly Color32 InkColor = TitleColor;
        public static readonly Color32 LineColor = new Color32(0xE2, 0xAE, 0x62, 255);
        public bool cardBackground;
        int selectedIndex = -1;
        public int SelectedIndex => selectedIndex;
        public ReferenceHouseEnergyFlow EnergyFlow { get; private set; }

        [Serializable]
        public sealed class EquipmentOutline
        {
            public Hotspot product;
            public Vector2[] points;
        }

        // Projected from the actual equipment meshes using the scene's fixed LED-wall camera.
        public EquipmentOutline[] equipmentOutlines = Array.Empty<EquipmentOutline>();
        public Hotspot? HighlightedObject => selectedIndex >= 0 && selectedIndex < Labels.Count &&
            HasObjectOutline(Labels[selectedIndex].Id) ? Labels[selectedIndex].Id : (Hotspot?)null;

        public bool HasObjectOutline(Hotspot product)
        {
            if (equipmentOutlines == null) return false;
            foreach (var shape in equipmentOutlines)
                if (shape != null && shape.product == product && shape.points != null && shape.points.Length >= 3)
                    return true;
            return false;
        }

        public void SetSelected(int index)
        {
            if (index < 0 || index >= Labels.Count) index = -1;
            if (selectedIndex == index) return;
            selectedIndex = index;
            if (!cardBackground && index >= 0 && !EnergyFlow)
            {
                var layer = new GameObject("Energy flow | panels to battery to equipment",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(ReferenceHouseEnergyFlow));
                layer.layer = gameObject.layer;
                layer.transform.SetParent(transform, false);
                layer.transform.SetAsFirstSibling(); // Keep labels and cards above the light trails.
                var bounds = (RectTransform)layer.transform;
                bounds.anchorMin = Vector2.zero;
                bounds.anchorMax = Vector2.one;
                bounds.offsetMin = bounds.offsetMax = Vector2.zero;
                EnergyFlow = layer.GetComponent<ReferenceHouseEnergyFlow>();
                EnergyFlow.raycastTarget = false;
                EnergyFlow.maskable = maskable;
            }
            if (EnergyFlow) EnergyFlow.Select(index >= 0 ? Labels[index].Id : (Hotspot?)null);
            SetVerticesDirty();
        }

        public static IReadOnlyList<Label> Labels { get; } = new[]
        {
            new Label(Hotspot.Photovoltaics, "Φωτοβολταϊκά", new Rect(519, 245, 179, 45)),
            new Label(Hotspot.HeatPump, "Αντλία θερμότητας", new Rect(138, 1173, 212, 46)),
            new Label(Hotspot.Battery, "Μπαταρία αποθήκευσης\nενέργειας", new Rect(919, 1264, 267, 59)),
            new Label(Hotspot.Charger, "Φορτιστής\nαυτοκινήτου", new Rect(1470, 784, 161, 57)),
            new Label(Hotspot.Meter, "Μετρητής", new Rect(1133, 906, 110, 46), 90),
        };

        // Branches follow the roof edge, front slabs and basement equipment.
        internal static readonly Vector2[][] CableRuns =
        {
            new[] { P(513, 375), P(389, 392), P(389, 1117), P(779, 1138), P(779, 1228) },
            new[] { P(543, 1348), P(607, 1355), P(660, 1332), P(722, 1335) },
            new[] { P(839, 1342), P(864, 1338), P(864, 950), P(1068, 929) },
            new[] { P(864, 1148), P(1340, 1174), P(1399, 1138), P(1399, 989) }
        };
        static readonly Vector2[][] FloorLoops =
        {
            new[] { P(1319, 776), P(873, 767), P(873, 774), P(1319, 784) },
            new[] { P(1318, 1164), P(874, 1136), P(874, 1143), P(1318, 1172) },
        };

        static Vector2 P(float x, float y) => new Vector2(x, y);
        readonly List<Vector2> outline = new List<Vector2>(96);
        readonly List<Vector2> centreLine = new List<Vector2>(72);
        Vector2 topLeft, pixelScale;

        protected override void Awake()
        {
            base.Awake();
            useLegacyMeshGeneration = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            topLeft = P(rect.xMin, rect.yMax);
            if (cardBackground)
            {
                pixelScale = P(1, -1);
                float radius = Mathf.Min(20, Mathf.Min(rect.width, rect.height) / 2);
                RoundedRect(vh, new Rect(Vector2.zero, rect.size), radius, new Color32(0xFC, 0xEA, 0xC3, 255));
                return;
            }
            pixelScale = P(rect.width / ReferenceHouseLedWall.Width, -rect.height / ReferenceHouseLedWall.Height);
            foreach (var path in FloorLoops)
                Stroke(vh, path, 3.5f, 5, new Color32(236, 151, 120, 255));
            foreach (var path in CableRuns)
            {
                Stroke(vh, path, 11, 16, new Color32(235, 159, 55, 57));
                Stroke(vh, path, 5, 16, LineColor);
            }

            DrawEquipmentHighlight(vh);
            Sunburst(vh, P(99, 95), 26);
            RoundedRect(vh, TitlePill, TitlePill.height / 2, TitleColor);
            for (int i = 0; i < Labels.Count; i++)
            {
                var label = Labels[i];
                Sunburst(vh, label.Sun, 18);
                RoundedRect(vh, label.Pill, label.Pill.height / 2,
                    i == selectedIndex ? new Color32(255, 222, 137, 255) : LabelColor);
            }
        }

        void DrawEquipmentHighlight(VertexHelper vh)
        {
            var selected = HighlightedObject;
            if (!selected.HasValue) return;
            foreach (var shape in equipmentOutlines)
            {
                if (shape == null || shape.product != selected.Value || shape.points == null || shape.points.Length < 3)
                    continue;
                Vector2 centre = Vector2.zero;
                outline.Clear();
                foreach (var point in shape.points) { outline.Add(point); centre += point; }
                ConvexFill(vh, centre / shape.points.Length, new Color32(255, 181, 35, 28));
                var closed = new Vector2[shape.points.Length + 1];
                Array.Copy(shape.points, closed, shape.points.Length);
                closed[closed.Length - 1] = closed[0];
                Stroke(vh, closed, 17, 3, new Color32(255, 160, 24, 32));
                Stroke(vh, closed, 8, 3, new Color32(255, 171, 30, 100));
                Stroke(vh, closed, 2.5f, 2, new Color32(255, 217, 94, 255));
            }
        }

        void Sunburst(VertexHelper vh, Vector2 centre, float radius)
        {
            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                var ray = P(Mathf.Cos(angle), Mathf.Sin(angle));
                var dot = centre + ray * radius;
                // Short, slightly elongated rays leave the house visible in the centre.
                outline.Clear();
                for (int j = 0; j < 12; j++)
                {
                    float t = j * Mathf.PI / 6;
                    var tangent = P(-ray.y, ray.x);
                    outline.Add(dot + ray * (Mathf.Cos(t) * radius * .154f)
                                    + tangent * (Mathf.Sin(t) * radius * .095f));
                }
                ConvexFill(vh, dot, LineColor);
            }
        }

        void RoundedRect(VertexHelper vh, Rect rect, float radius, Color32 fill)
        {
            outline.Clear();
            Arc(P(rect.xMax - radius, rect.yMin + radius), radius, -90, 0);
            Arc(P(rect.xMax - radius, rect.yMax - radius), radius, 0, 90);
            Arc(P(rect.xMin + radius, rect.yMax - radius), radius, 90, 180);
            Arc(P(rect.xMin + radius, rect.yMin + radius), radius, 180, 270);
            ConvexFill(vh, rect.center, fill);
        }

        void Arc(Vector2 centre, float radius, float start, float end)
        {
            for (int step = 0; step <= 10; step++)
            {
                float a = Mathf.Lerp(start, end, step / 10f) * Mathf.Deg2Rad;
                var next = centre + P(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                // Semicircular pill ends join two quarter circles at the same point.
                if (outline.Count == 0 || (outline[outline.Count - 1] - next).sqrMagnitude > .0001f)
                    outline.Add(next);
            }
        }

        void ConvexFill(VertexHelper vh, Vector2 centre, Color32 fill)
        {
            int count = outline.Count;
            int origin = Vertex(vh, centre, fill);
            Color32 clear = fill;
            clear.a = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 curr = outline[i];
                Vector2 prev = outline[(i + count - 1) % count], next = outline[(i + 1) % count];
                Vector2 outside = RightNormal(((curr - prev).normalized + (next - curr).normalized).normalized);
                Vertex(vh, curr, fill);
                Vertex(vh, curr + outside, clear);
            }
            for (int i = 0; i < count; i++)
            {
                int curr = origin + 1 + i * 2, next = origin + 1 + ((i + 1) % count) * 2;
                vh.AddTriangle(origin, curr, next);
                Quad(vh, curr, curr + 1, next + 1, next);
            }
        }

        // Sample rounded bends as a continuous ribbon. Transparent edge vertices
        // antialias the wire even when the native canvas has multisampling disabled.
        void Stroke(VertexHelper vh, Vector2[] path, float width, float bend, Color32 fill)
        {
            ReferenceHouseCablePath.SampleRounded(path, bend, centreLine);
            float half = width / 2;
            Color32 clear = fill;
            clear.a = 0;
            for (int i = 0; i < centreLine.Count; i++)
            {
                Vector2 curr = centreLine[i], prev = centreLine[Mathf.Max(i - 1, 0)];
                Vector2 next = centreLine[Mathf.Min(i + 1, centreLine.Count - 1)];
                Vector2 offset = RightNormal(((curr - prev).normalized + (next - curr).normalized).normalized);
                int row = Vertex(vh, curr + offset * (half + 1), clear);
                Vertex(vh, curr + offset * half, fill);
                Vertex(vh, curr - offset * half, fill);
                Vertex(vh, curr - offset * (half + 1), clear);
                if (i == 0) continue;
                for (int col = 0; col < 3; col++)
                    Quad(vh, row - 4 + col, row + col, row + col + 1, row - 3 + col);
            }
        }

        static Vector2 RightNormal(Vector2 direction) => P(direction.y, -direction.x);

        static void Quad(VertexHelper vh, int a, int b, int c, int d)
        {
            vh.AddTriangle(a, b, c);
            vh.AddTriangle(a, c, d);
        }

        int Vertex(VertexHelper vh, Vector2 pixel, Color32 tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(topLeft + Vector2.Scale(pixel, pixelScale), tint, Vector2.zero);
            return index;
        }
    }
}
