using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    /// <summary>
    /// Builds editable LED-wall cards from the supplied Home Corner artwork.
    /// Coordinates and font sizes are native 1872 x 1560 wall pixels.
    /// </summary>
    public static class ReferenceHouseProductCards
    {
        const float Width = 390f;
        const float Padding = 20f;
        const float HeadingSize = 20f;
        const float BodySize = 17.5f;
        const float SectionGap = 15f;
        const float ParagraphGap = 4f;
        const float Edge = 12f;
        const float LabelGap = 16f;

        readonly struct Section
        {
            public readonly string Heading, Body;
            public Section(string heading, string body) { Heading = heading; Body = body; }
        }

        sealed class Copy
        {
            public string Title;
            public Section[] Sections;
            public string Footnote;
        }

        /// <summary>Creates one inactive card. The meter uses its existing supplied artwork instead.</summary>
        public static RectTransform Build(Transform parent, ReferenceHouseDiagram.Label label, Font bold, Font regular)
        {
            if (!parent || !bold || !regular) throw new ArgumentException("Card parent and fonts are required.");
            var copy = Content(label.Id);
            var panel = Box(parent, "Card | " + label.Id, new Rect(0, 0, Width, 1));
            var background = panel.gameObject.AddComponent<ReferenceHouseDiagram>();
            background.cardBackground = true;
            background.raycastTarget = false;

            float y = Padding;
            y += PutText(panel, "Heading", copy.Title, bold, HeadingSize, ReferenceHouseDiagram.InkColor, y);
            y += SectionGap;
            for (int i = 0; i < copy.Sections.Length; ++i)
            {
                var section = copy.Sections[i];
                if (!string.IsNullOrEmpty(section.Heading))
                {
                    y += PutText(panel, "Benefit " + (i + 1) + " | Heading", section.Heading,
                        bold, HeadingSize, new Color32(16, 15, 13, 255), y);
                    y += ParagraphGap;
                }
                if (!string.IsNullOrEmpty(section.Body))
                    y += PutText(panel, "Benefit " + (i + 1) + " | Description", section.Body,
                        regular, BodySize, new Color32(16, 15, 13, 255), y);
                if (i + 1 < copy.Sections.Length) y += SectionGap;
            }
            if (!string.IsNullOrEmpty(copy.Footnote))
            {
                y += SectionGap;
                y += PutText(panel, "Terms", copy.Footnote, regular, BodySize,
                    new Color32(16, 15, 13, 255), y);
            }

            var bounds = Place(label, new Vector2(Width, Mathf.Ceil(y + Padding)));
            panel.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            panel.sizeDelta = bounds.size;
            panel.gameObject.SetActive(false);
            return panel;
        }

        static Copy Content(Hotspot id)
        {
            switch (id)
            {
                case Hotspot.Photovoltaics:
                    return new Copy
                    {
                        Title = "Τι κερδίζω με το\nProtergia Helios Value;",
                        Sections = new[]
                        {
                            new Section("Δωρεάν αυτοψία από την Protergia και ανάληψη όλης της διαδικασίας*.",
                                "Από τη μελέτη, τη χρηματοδότηση, έως την προμήθεια του εξοπλισμού και την εγκατάσταση, με το «κλειδί στο χέρι»!"),
                            new Section("Πλήρη χρηματοδότηση από την Protergia",
                                "για την προμήθεια εξοπλισμού και την εγκατάσταση φωτοβολταϊκού και μπαταρίας."),
                            new Section("Ευέλικτη αποπληρωμή",
                                "σε έως 120 μηνιαίες δόσεις, μέσω καταναλωτικού δανείου από συνεργαζόμενη τράπεζα.")
                        },
                        Footnote = "*υπό την προϋπόθεση προμήθειας και εγκατάστασης από την Protergia"
                    };
                case Hotspot.HeatPump:
                    return new Copy
                    {
                        Title = "Γιατί να βάλω\nαντλία θερμότητας",
                        Sections = new[]
                        {
                            new Section("Εξοικονόμηση Κόστους",
                                "Μειώνεις το κόστος θέρμανσης έως και 75%, με χαμηλότερα έξοδα λειτουργίας και συντήρησης σε σχέση με τα παραδοσιακά συστήματα."),
                            new Section("Απόλυτη Ευελιξία",
                                "Απολαμβάνεις θέρμανση, ψύξη και ζεστό νερό από μία ολοκληρωμένη ενεργειακή λύση."),
                            new Section("Ενεργειακή Αναβάθμιση Ακινήτου",
                                "Αναβαθμίζεις την απόδοση του σπιτιού σου, αυξάνοντας τη μακροπρόθεσμη αξία του."),
                            new Section("Ενεργειακή Βιωσιμότητα",
                                "Περιορίζεις το αποτύπωμα άνθρακα, χωρίς τοπικές εκπομπές καυσαερίων.")
                        }
                    };
                case Hotspot.Charger:
                    return new Copy
                    {
                        Title = "Τι κερδίζω με το Protergia\nChargespot Home;",
                        Sections = new[]
                        {
                            new Section("Προμήθεια, μελέτη & εγκατάσταση οικιακού φορτιστή",
                                "από ποιοτικά & πιστοποιημένα μοντέλα."),
                            new Section("Δωρεάν αυτοψία 100€",
                                "υπό την προϋπόθεση ολοκλήρωσης του έργου από την Protergia."),
                            new Section("Δώρο 100€ Φόρτιση",
                                "στο πανελλαδικό δημόσιο δίκτυο Chargespot της Protergia."),
                            new Section("Δωρεάν εφαρμογή Chargespot",
                                "για 1 έτος, για τη διαχείριση και παρακολούθηση της οικιακής φόρτισης."),
                            new Section("After Sales Support",
                                "από την εξειδικευμένη τεχνική ομάδα της Protergia.")
                        }
                    };
                case Hotspot.Battery:
                    return new Copy
                    {
                        Title = "Μπαταρία αποθήκευσης\nενέργειας",
                        Sections = new[]
                        {
                            new Section(null, "Αξιοποίησε την ενέργεια του ήλιου και μετά τη δύση του."),
                            new Section(null, "Η μπαταρία αποθηκεύει την περίσσεια ενέργειας από τα φωτοβολταϊκά, ώστε να τη χρησιμοποιείς αργότερα για τις ανάγκες του σπιτιού σου.")
                        }
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "The meter card uses MeterCard.png.");
            }
        }

        static Rect Place(ReferenceHouseDiagram.Label label, Vector2 size)
        {
            float left = Mathf.Clamp(label.TextBounds.x - 12, Edge, ReferenceHouseLedWall.Width - size.x - Edge);
            float rightAligned = Mathf.Clamp(label.HitBounds.xMax - size.x, Edge, ReferenceHouseLedWall.Width - size.x - Edge);
            float above = label.HitBounds.yMin - size.y - LabelGap;
            float below = label.HitBounds.yMax + LabelGap;
            var candidates = new List<Vector2>();
            if (label.Id == Hotspot.Photovoltaics) candidates.Add(new Vector2(left, below));
            if (label.Id == Hotspot.Battery) candidates.Add(new Vector2(rightAligned, above));
            candidates.Add(new Vector2(left, above));
            candidates.Add(new Vector2(rightAligned, above));
            candidates.Add(new Vector2(left, below));
            candidates.Add(new Vector2(rightAligned, below));
            candidates.Add(new Vector2(label.HitBounds.xMax + LabelGap, label.HitBounds.yMin));
            candidates.Add(new Vector2(label.HitBounds.xMin - size.x - LabelGap, label.HitBounds.yMin));

            foreach (var position in candidates)
            {
                var bounds = new Rect(position, size);
                if (Fits(bounds)) return bounds;
            }
            throw new InvalidOperationException("No unobstructed native card position for " + label.Id + " (" + size + ").");
        }

        static bool Fits(Rect bounds)
        {
            if (bounds.xMin < Edge || bounds.yMin < Edge || bounds.xMax > ReferenceHouseLedWall.Width - Edge ||
                bounds.yMax > ReferenceHouseLedWall.Height - Edge) return false;
            if (bounds.Overlaps(ReferenceHouseDiagram.MeterCardBounds) ||
                bounds.Overlaps(ReferenceHouseDiagram.TitleTextBounds)) return false;
            foreach (var label in ReferenceHouseDiagram.Labels)
            {
                var avoid = label.HitBounds;
                avoid.xMin -= 8;
                avoid.xMax += 8;
                avoid.yMin -= 8;
                avoid.yMax += 8;
                if (bounds.Overlaps(avoid)) return false;
            }
            return true;
        }

        static float PutText(Transform parent, string name, string value, Font font, float size, Color ink, float y)
        {
            float density = size % 1 == 0 ? 1 : 2;
            var rect = Box(parent, name, new Rect(Padding, y, Width - Padding * 2, 1));
            rect.localScale = Vector3.one / density;
            rect.sizeDelta = new Vector2((Width - Padding * 2) * density, 1);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = Mathf.RoundToInt(size * density);
            text.text = value;
            text.alignment = TextAnchor.UpperLeft;
            text.color = ink;
            text.lineSpacing = 1.05f;
            text.resizeTextForBestFit = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            float height = Mathf.Ceil(text.preferredHeight / density) + 2;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height * density);
            return height;
        }

        static RectTransform Box(Transform parent, string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            return rect;
        }
    }
}
