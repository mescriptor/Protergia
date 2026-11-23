using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    /// <summary>Repeating trains of energy packets: PV, battery arrival, then the selected branch.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ReferenceHouseEnergyFlow : MaskableGraphic
    {
        public enum Stage { Off, PanelsToBattery, BatteryArrival, BatteryToEquipment, Arrival }

        const int PulseCount = 6;
        const float PulseInterval = .18f;
        const float Speed = 780f; // Native wall pixels per second; twice the original speed.
        const float Tail = 105f;
        const float BatteryPause = .18f;
        const float EndPause = .25f;
        const float ArrivalSeconds = .45f;
        static readonly Vector2 BatteryCentre = new Vector2(782, 1298);
        static readonly ReferenceHouseCablePath Solar = new ReferenceHouseCablePath(ReferenceHouseDiagram.CableRuns[0]);
        static readonly ReferenceHouseCablePath Meter = new ReferenceHouseCablePath(ReferenceHouseDiagram.CableRuns[2]);
        static readonly ReferenceHouseCablePath Pump = Reverse(ReferenceHouseDiagram.CableRuns[1]);
        // Follow the battery-to-meter trunk only up to the charger junction; never visit the meter.
        static readonly ReferenceHouseCablePath Charger = new ReferenceHouseCablePath(new[]
        {
            ReferenceHouseDiagram.CableRuns[2][0], ReferenceHouseDiagram.CableRuns[2][1],
            ReferenceHouseDiagram.CableRuns[3][0], ReferenceHouseDiagram.CableRuns[3][1],
            ReferenceHouseDiagram.CableRuns[3][2], ReferenceHouseDiagram.CableRuns[3][3]
        });

        Hotspot? selectedProduct;
        ReferenceHouseCablePath delivery;
        double startedAt;
        Vector2 topLeft, pixelScale;

        public Hotspot? SelectedProduct => selectedProduct;
        public Hotspot? Destination => !selectedProduct.HasValue ? (Hotspot?)null :
            delivery == null ? Hotspot.Battery : selectedProduct;
        public float SourceSeconds => Solar.Length / Speed;
        public float DeliveryStartSeconds => SourceSeconds + BatteryPause;
        public float DeliverySeconds => delivery == null ? 0 : delivery.Length / Speed;
        public float TrainSeconds => (PulseCount - 1) * PulseInterval;
        // Leave enough time for the last packet to arrive before restarting the train.
        public float CycleSeconds => (delivery == null ? SourceSeconds : DeliveryStartSeconds + DeliverySeconds)
            + TrainSeconds + ArrivalSeconds + EndPause;
        public float ElapsedSeconds => (float)(Time.unscaledTimeAsDouble - startedAt);
        public float LoopSeconds => Mathf.Repeat(ElapsedSeconds, CycleSeconds);
        public Stage CurrentStage // Stage of the leading packet; following packets use the same route and delay.
        {
            get
            {
                if (!selectedProduct.HasValue) return Stage.Off;
                float t = LoopSeconds;
                if (t < SourceSeconds) return Stage.PanelsToBattery;
                if (delivery == null) return Stage.Arrival;
                if (t < DeliveryStartSeconds) return Stage.BatteryArrival;
                return t < DeliveryStartSeconds + delivery.Length / Speed ? Stage.BatteryToEquipment : Stage.Arrival;
            }
        }

        public void Select(Hotspot? product)
        {
            selectedProduct = product;
            delivery = product == Hotspot.Meter ? Meter : product == Hotspot.Charger ? Charger :
                product == Hotspot.HeatPump ? Pump : null;
            startedAt = Time.unscaledTimeAsDouble;
            // Disabled Graphics clear their CanvasRenderer; no mesh rebuilds while idle or in videos.
            enabled = product.HasValue;
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            useLegacyMeshGeneration = false;
            raycastTarget = false;
        }

        void Update()
        {
            if (selectedProduct.HasValue) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!selectedProduct.HasValue) return;
            Rect rect = rectTransform.rect;
            topLeft = new Vector2(rect.xMin, rect.yMax);
            pixelScale = new Vector2(rect.width / ReferenceHouseLedWall.Width, -rect.height / ReferenceHouseLedWall.Height);
            float t = LoopSeconds;
            for (int i = 0; i < PulseCount; i++)
            {
                float packetTime = t - i * PulseInterval;
                if (packetTime < 0) continue;
                Packet(vh, Solar, packetTime * Speed);
                Arrival(vh, BatteryCentre, packetTime - SourceSeconds);
                if (delivery == null || packetTime < DeliveryStartSeconds) continue;
                float distance = (packetTime - DeliveryStartSeconds) * Speed;
                Packet(vh, delivery, distance);
                Arrival(vh, delivery.End, (distance - delivery.Length) / Speed);
            }
        }

        void Packet(VertexHelper vh, ReferenceHouseCablePath path, float head)
        {
            float from = Mathf.Max(0, head - Tail), to = Mathf.Min(path.Length, head);
            if (to <= from) return;
            // Layered, alpha-blended UI ribbons provide bloom without a post-processing dependency.
            Ribbon(vh, path, from, to, head, 34, new Color32(255, 155, 22, 55));
            Ribbon(vh, path, from, to, head, 21, new Color32(255, 183, 40, 130));
            Ribbon(vh, path, from, to, head, 10, new Color32(255, 226, 126, 255));
            Ribbon(vh, path, Mathf.Max(from, head - 60), to, head, 5.5f, new Color32(255, 253, 236, 255));
            if (head <= path.Length)
            {
                Vector2 point = path.Point(head);
                Disc(vh, point, 17, new Color32(255, 170, 25, 65));
                Disc(vh, point, 10, new Color32(255, 215, 105, 170));
                Disc(vh, point, 5.5f, new Color32(255, 255, 242, 255));
            }
        }

        void Ribbon(VertexHelper vh, ReferenceHouseCablePath path, float from, float to, float head, float width, Color32 tint)
        {
            if (to <= from) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) / 3));
            for (int i = 0; i <= steps; i++)
            {
                float distance = Mathf.Lerp(from, to, i / (float)steps);
                Vector2 point = path.Point(distance), direction = path.Direction(distance);
                var normal = new Vector2(direction.y, -direction.x);
                float strength = Mathf.Clamp01(1 - (head - distance) / Tail);
                strength *= strength;
                Color32 color = tint;
                color.a = (byte)(color.a * strength);
                Color32 clear = color;
                clear.a = 0;
                float half = width * Mathf.Lerp(.18f, .5f, strength);
                int row = Vertex(vh, point + normal * (half + 1), clear);
                Vertex(vh, point + normal * half, color);
                Vertex(vh, point - normal * half, color);
                Vertex(vh, point - normal * (half + 1), clear);
                if (i == 0) continue;
                for (int col = 0; col < 3; col++) Quad(vh, row - 4 + col, row + col, row + col + 1, row - 3 + col);
            }
        }

        void Arrival(VertexHelper vh, Vector2 centre, float elapsed)
        {
            if (elapsed < 0 || elapsed >= ArrivalSeconds) return;
            float t = elapsed / ArrivalSeconds;
            float radius = Mathf.Lerp(10, 34, t);
            Color32 tint = new Color32(255, 226, 135, (byte)(210 * (1 - t)));
            const int sides = 48;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Color32 clear = tint;
                clear.a = 0;
                int row = Vertex(vh, centre + normal * (radius - 3), clear);
                Vertex(vh, centre + normal * (radius - 1), tint);
                Vertex(vh, centre + normal * (radius + 1), tint);
                Vertex(vh, centre + normal * (radius + 4), clear);
                if (i == 0) continue;
                for (int col = 0; col < 3; col++) Quad(vh, row - 4 + col, row + col, row + col + 1, row - 3 + col);
            }
        }

        void Disc(VertexHelper vh, Vector2 point, float radius, Color32 tint)
        {
            int centre = Vertex(vh, point, tint);
            Color32 edge = tint;
            edge.a = 0;
            for (int i = 0; i <= 20; i++)
            {
                float a = i * Mathf.PI / 10;
                int next = Vertex(vh, point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, edge);
                if (i > 0) vh.AddTriangle(centre, next - 1, next);
            }
        }

        int Vertex(VertexHelper vh, Vector2 point, Color32 tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(topLeft + Vector2.Scale(point, pixelScale), tint, Vector2.zero);
            return index;
        }

        static void Quad(VertexHelper vh, int a, int b, int c, int d)
        {
            vh.AddTriangle(a, b, c);
            vh.AddTriangle(a, c, d);
        }

        static ReferenceHouseCablePath Reverse(Vector2[] run)
        {
            var reversed = (Vector2[])run.Clone();
            Array.Reverse(reversed);
            return new ReferenceHouseCablePath(reversed);
        }
    }

    /// <summary>Shared rounded cable geometry, sampled by arc length for constant pulse speed.</summary>
    internal sealed class ReferenceHouseCablePath
    {
        readonly Vector2[] points;
        readonly float[] distances;
        public float Length => distances[distances.Length - 1];
        public Vector2 End => points[points.Length - 1];

        public ReferenceHouseCablePath(Vector2[] path)
        {
            var sampled = new List<Vector2>();
            SampleRounded(path, 16, sampled);
            points = sampled.ToArray();
            distances = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
                distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }

        int Segment(float distance)
        {
            int found = Array.BinarySearch(distances, Mathf.Clamp(distance, 0, Length));
            return Mathf.Clamp(found >= 0 ? found : ~found, 1, points.Length - 1);
        }

        public Vector2 Point(float distance)
        {
            int i = Segment(distance);
            return Vector2.Lerp(points[i - 1], points[i], Mathf.InverseLerp(distances[i - 1], distances[i], distance));
        }

        public Vector2 Direction(float distance)
        {
            return (Point(Mathf.Min(Length, distance + 1)) - Point(Mathf.Max(0, distance - 1))).normalized;
        }

        internal static void SampleRounded(Vector2[] path, float bend, List<Vector2> result)
        {
            result.Clear();
            result.Add(path[0]);
            for (int i = 1; i < path.Length - 1; i++)
            {
                Vector2 corner = path[i], incoming = path[i - 1] - corner, outgoing = path[i + 1] - corner;
                float inset = Mathf.Min(bend, Mathf.Min(incoming.magnitude, outgoing.magnitude) * .5f);
                Vector2 entry = corner + incoming.normalized * inset, exit = corner + outgoing.normalized * inset;
                for (int j = 0; j <= 8; j++)
                {
                    float t = j / 8f;
                    result.Add(Vector2.Lerp(Vector2.Lerp(entry, corner, t), Vector2.Lerp(corner, exit, t), t));
                }
            }
            result.Add(path[path.Length - 1]);
        }
    }
}
