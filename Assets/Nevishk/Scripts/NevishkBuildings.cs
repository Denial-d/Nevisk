using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nevishk
{
    /// <summary>
    /// Low-poly 3D versions of the buildings drawn on the base map (factories, manor, chapel, …),
    /// assembled from boxes, gable roofs, cylinders and spires. One builder per drawn icon style.
    /// Sizes are in world units (1 unit = 1 base-map pixel); the footprint comes from the drawn icon.
    /// </summary>
    public static class NevishkBuildings
    {
        // Palette: muted, so the buildings sit in the dusk grade with the rest of the city.
        static readonly Color Brick     = new Color(0.55f, 0.36f, 0.30f);
        static readonly Color Concrete  = new Color(0.62f, 0.60f, 0.56f);
        static readonly Color DarkConc  = new Color(0.40f, 0.39f, 0.37f);
        static readonly Color RoofDark  = new Color(0.25f, 0.24f, 0.23f);
        static readonly Color RoofRed   = new Color(0.45f, 0.23f, 0.18f);
        static readonly Color Whitewash = new Color(0.82f, 0.79f, 0.72f);
        static readonly Color Cream     = new Color(0.86f, 0.82f, 0.70f);
        static readonly Color Patina    = new Color(0.36f, 0.45f, 0.40f);
        static readonly Color Glass     = new Color(0.40f, 0.48f, 0.55f);
        static readonly Color Rail      = new Color(0.18f, 0.17f, 0.16f);
        static readonly Color Gilt      = new Color(0.85f, 0.70f, 0.30f);

        public struct Result { public GameObject root; public float height; public float radius; }

        /// Terrain access for structures that spread over the ground (parks, streets, embankments):
        /// height at a world point, and whether it is water. Set by NevishkMap before building.
        public static Func<Vector3, float> GroundAt;
        public static Func<Vector3, bool> IsWet;

        /// <param name="mat">Returns a shared material for a colour and smoothness.</param>
        public static Result Build(string type, Transform parent, Vector3 basePos, float w, float d, float yaw,
                                   Func<Color, float, Material> mat, int seed)
        {
            var root = new GameObject("Building · " + type).transform;
            root.SetParent(parent, false);
            root.position = basePos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var kit = new Kit(root, mat, new System.Random(seed));
            float h = type switch
            {
                "factory"  => Factory(kit, w, d),
                "houses"   => Houses(kit, w, d),
                "manor"    => Manor(kit, w, d),
                "pair"     => Pair(kit, w, d),
                "depot"    => Depot(kit, w, d),
                "slabs"    => Slabs(kit, w, d),
                "ruin"     => Ruin(kit, w, d),
                "shed"     => Shed(kit, w, d),
                "spillway" => Spillway(kit, w, d),
                "gallery"  => Gallery(kit, w, d),
                "towers"   => Towers(kit, w, d),
                "chapel"   => Chapel(kit, w, d),
                "checkpoint" => Checkpoint(kit, w, d),
                "bridge"     => Bridge(kit, w, d),
                "fountain"   => Fountain(kit, w, d),
                "alley"      => Alley(kit, w, d, false),
                "street"     => Alley(kit, w, d, true),
                "firepit"    => Firepit(kit, w, d),
                "cave"       => Cave(kit, w, d),
                "watertower" => WaterTower(kit, w, d),
                "tunnel"     => TunnelPortal(kit, w, d),
                "powerplant" => PowerPlant(kit, w, d),
                "nuclear"    => Nuclear(kit, w, d),
                "silos"      => Silos(kit, w, d),
                "concrete"   => ConcretePlant(kit, w, d),
                "brewery"    => Brewery(kit, w, d),
                "mine"       => Mine(kit, w, d),
                "office"     => Office(kit, w, d),
                "memorial"   => Memorial(kit, w, d),
                "barracks"   => Barracks(kit, w, d),
                "fortress"   => Fortress(kit, w, d),
                "panels"     => Panels(kit, w, d),
                "newblocks"  => NewBlocks(kit, w, d),
                "embankment" => Embankment(kit, w, d),
                "market"     => Market(kit, w, d),
                "treatment"  => Treatment(kit, w, d),
                "oldtown"    => OldTown(kit, w, d),
                "university" => University(kit, w, d),
                "bar"        => Bar(kit, w, d),
                "park"       => Park(kit, w, d),
                "statue"     => Statue(kit, w, d),
                "checkpoint_bridge" => CheckpointOnBridge(kit, w, d),
                "depot_through" => DepotThrough(kit, w, d),
                "arch"       => Arch(kit, w, d),
                _          => kit.Box(Vector3.zero, new Vector3(w, 8, d), Concrete),
            };
            // Buildings stand on the highest ground under them; a foundation fills the slope below,
            // so nothing floats and nothing is half-buried.
            if (NeedsFoundation(type))
                kit.Box(new Vector3(0, -14f, 0), new Vector3(w * 0.96f, 14f, d * 0.96f), FoundationStone);
            return new Result { root = root.gameObject, height = h, radius = Mathf.Max(w, d) * 0.6f };
        }

        // ------------------------------------------------------------------ building types
        // Each returns the building's top height above its base.

        static float Factory(Kit k, float w, float d)
        {
            float hallW = w * 0.68f, hallH = 9f;
            k.Box(new Vector3(-w * 0.16f, 0, 0), new Vector3(hallW, hallH, d), Brick);
            // Saw-tooth roof: a row of small gables across the hall.
            int teeth = Mathf.Max(3, Mathf.RoundToInt(hallW / 6f));
            float tw = hallW / teeth;
            for (int i = 0; i < teeth; i++)
                k.Gable(new Vector3(-w * 0.16f - hallW / 2 + tw * (i + 0.5f), hallH, 0), new Vector3(tw, 3.2f, d), RoofDark, alongZ: true);
            k.Box(new Vector3(w * 0.34f, 0, -d * 0.15f), new Vector3(w * 0.3f, 6f, d * 0.6f), Concrete);
            k.Box(new Vector3(w * 0.34f, 6f, -d * 0.15f), new Vector3(w * 0.32f, 0.6f, d * 0.64f), RoofDark);
            // Chimneys, like on the drawing.
            k.Cylinder(new Vector3(w * 0.28f, 0, d * 0.28f), 1.5f, 26f, Brick);
            k.Cylinder(new Vector3(w * 0.40f, 0, d * 0.28f), 1.2f, 21f, Brick);
            return 26f;
        }

        static float Houses(Kit k, float w, float d)
        {
            // Village of small whitewashed houses with red roofs.
            var spots = new List<Vector2>();
            for (int tries = 0; spots.Count < 6 && tries < 60; tries++)
            {
                var p = new Vector2(k.Range(-w / 2 + 3, w / 2 - 3), k.Range(-d / 2 + 3, d / 2 - 3));
                bool clear = true;
                foreach (var q in spots) if ((p - q).sqrMagnitude < 36f) clear = false;
                if (clear) spots.Add(p);
            }
            foreach (var p in spots)
            {
                float hw = k.Range(4f, 6f), hd = k.Range(3.5f, 4.5f), hh = k.Range(3f, 4f);
                bool turn = k.Range(0, 1) > 0.5f;
                var size = turn ? new Vector3(hd, hh, hw) : new Vector3(hw, hh, hd);
                k.Box(new Vector3(p.x, 0, p.y), size, Whitewash);
                k.Gable(new Vector3(p.x, hh, p.y), new Vector3(size.x + 0.6f, 2.4f, size.z + 0.6f), RoofRed, alongZ: turn);
            }
            return 7f;
        }

        static float Manor(Kit k, float w, float d)
        {
            float cw = w * 0.42f, ch = 11f;
            k.Box(Vector3.zero, new Vector3(cw, ch, d * 0.65f), Cream);
            k.Gable(new Vector3(0, ch, 0), new Vector3(cw + 1, 4.5f, d * 0.65f + 1), Patina, alongZ: false);
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(new Vector3(s * (cw / 2 + w * 0.14f), 0, d * 0.08f), new Vector3(w * 0.28f, 7.5f, d * 0.5f), Cream);
                k.Gable(new Vector3(s * (cw / 2 + w * 0.14f), 7.5f, d * 0.08f), new Vector3(w * 0.28f + 0.6f, 3f, d * 0.5f + 0.6f), Patina, alongZ: false);
            }
            // Portico: columns and a pediment on the south front.
            float front = -d * 0.325f - 3f;
            for (int i = 0; i < 4; i++)
                k.Cylinder(new Vector3(-cw * 0.3f + cw * 0.2f * i, 0, front), 0.55f, 8f, Whitewash);
            k.Box(new Vector3(0, 8f, front + 0.5f), new Vector3(cw * 0.75f, 1f, 4f), Whitewash);
            k.Gable(new Vector3(0, 9f, front + 0.5f), new Vector3(cw * 0.75f, 2.5f, 4f), Whitewash, alongZ: false);
            return ch + 4.5f;
        }

        static float Pair(Kit k, float w, float d)
        {
            k.Box(new Vector3(-w * 0.22f, 0, 0), new Vector3(w * 0.42f, 14f, d * 0.8f), Concrete);
            k.Box(new Vector3(-w * 0.22f, 14f, 0), new Vector3(w * 0.44f, 0.6f, d * 0.82f), RoofDark);
            k.Box(new Vector3(w * 0.25f, 0, d * 0.05f), new Vector3(w * 0.38f, 9f, d * 0.9f), Brick);
            k.Gable(new Vector3(w * 0.25f, 9f, d * 0.05f), new Vector3(w * 0.4f, 3f, d * 0.92f), RoofDark, alongZ: true);
            return 14.6f;
        }

        static float Depot(Kit k, float w, float d)
        {
            float hh = 6f;
            k.Box(Vector3.zero, new Vector3(w, hh, d), Brick);
            // Arched roof: a cylinder lying along the shed, half sunk into it.
            k.CylinderLying(new Vector3(0, hh, 0), d * 0.5f, w, DarkConc);
            // Tracks running out of the doors.
            foreach (float s in new[] { -0.25f, 0.25f })
                k.Box(new Vector3(w * 0.5f + 8f, 0, d * s), new Vector3(16f, 0.4f, 1.6f), Rail);
            return hh + d * 0.5f;
        }

        static float Slabs(Kit k, float w, float d)
        {
            // Безмолвные плиты: 5–7 storey panel blocks, abandoned, one broken.
            float[] hs = { 18f, 21f, 15f, 19f };
            for (int i = 0; i < hs.Length; i++)
            {
                float x = -w / 2 + w * (i + 0.5f) / hs.Length;
                float z = (i % 2 == 0 ? -1 : 1) * d * 0.18f;
                if (i == 2)
                {
                    // Collapsed corner: two stepped pieces.
                    k.Box(new Vector3(x, 0, z), new Vector3(w / hs.Length * 0.8f, hs[i], 5f), DarkConc);
                    k.Box(new Vector3(x - 1.5f, hs[i], z), new Vector3(w / hs.Length * 0.45f, 4f, 5f), DarkConc);
                }
                else k.Box(new Vector3(x, 0, z), new Vector3(w / hs.Length * 0.8f, hs[i], 5f), DarkConc);
            }
            return 21f;
        }

        static float Ruin(Kit k, float w, float d)
        {
            // Roofless walls with gaps, and a broken chimney.
            k.Box(new Vector3(0, 0, -d / 2 + 0.6f), new Vector3(w, 6f, 1.2f), DarkConc);
            k.Box(new Vector3(-w / 2 + 0.6f, 0, 0), new Vector3(1.2f, 4.5f, d), DarkConc);
            k.Box(new Vector3(w / 2 - 0.6f, 0, d * 0.2f), new Vector3(1.2f, 7f, d * 0.6f), DarkConc);
            k.Box(new Vector3(-w * 0.15f, 0, d / 2 - 0.6f), new Vector3(w * 0.6f, 3f, 1.2f), DarkConc);
            var chimney = k.Cylinder(new Vector3(w * 0.25f, 0, d * 0.25f), 1.3f, 14f, Brick);
            chimney.transform.localRotation = Quaternion.Euler(6f, 0, 4f);   // leaning
            return 14f;
        }

        static float Shed(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(w, 4f, d), DarkConc);
            k.Box(new Vector3(-w * 0.15f, 4f, 0), new Vector3(w * 0.7f, 0.5f, d + 0.6f), RoofDark);   // roof half fallen in
            return 4.5f;
        }

        static float Spillway(Kit k, float w, float d)
        {
            // Concrete spillway at the ravine's edge, stepped toward the drop.
            for (int i = 0; i < 4; i++)
                k.Box(new Vector3(0, 0, -d / 2 + d * (i + 0.5f) / 4f), new Vector3(w, 10f - i * 2.2f, d / 4f), Concrete);
            k.Box(new Vector3(0, 10f, -d / 2 + d / 8f), new Vector3(w + 2f, 0.8f, 2f), DarkConc);
            return 11f;
        }

        static float Gallery(Kit k, float w, float d)
        {
            float hh = 9f;
            k.Box(new Vector3(0, 0, d * 0.1f), new Vector3(w * 0.85f, hh, d * 0.7f), Cream);
            k.Box(new Vector3(0, hh, d * 0.05f), new Vector3(w * 0.9f, 1.2f, d * 0.9f), Whitewash);   // flat cornice
            float front = -d * 0.25f - 2.5f;
            for (int i = 0; i < 6; i++)
                k.Cylinder(new Vector3(-w * 0.35f + w * 0.14f * i, 0, front), 0.6f, hh, Whitewash);
            k.Gable(new Vector3(0, hh + 1.2f, front + 2f), new Vector3(w * 0.8f, 3f, 5f), Whitewash, alongZ: false);
            return hh + 4.2f;
        }

        static float Towers(Kit k, float w, float d)
        {
            float[] hs = { 44f, 52f, 38f };
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-w * 0.3f + w * 0.3f * i, 0, (i - 1) * d * 0.15f);
                k.Box(p, new Vector3(6.5f, hs[i], 6.5f), Glass, 0.75f);
                k.Box(p + Vector3.up * hs[i], new Vector3(5f, 2f, 5f), DarkConc);
            }
            return 54f;
        }

        static float Chapel(Kit k, float w, float d)
        {
            k.Box(new Vector3(0, 0, d * 0.1f), new Vector3(w * 0.6f, 6f, d * 0.7f), Whitewash);
            k.Gable(new Vector3(0, 6f, d * 0.1f), new Vector3(w * 0.6f + 0.6f, 4f, d * 0.7f + 0.6f), RoofDark, alongZ: true);
            var tower = new Vector3(0, 0, -d * 0.32f);
            k.Box(tower, new Vector3(3.6f, 13f, 3.6f), Whitewash);
            k.Spire(tower + Vector3.up * 13f, 2.6f, 6f, Patina);
            // Cross on top.
            k.Box(tower + Vector3.up * 19f, new Vector3(0.35f, 3f, 0.35f), Gilt, 0.6f);
            k.Box(tower + Vector3.up * 20.6f, new Vector3(1.6f, 0.35f, 0.35f), Gilt, 0.6f);
            return 22f;
        }

        static readonly Color FoundationStone = new Color(0.46f, 0.44f, 0.40f);
        public static bool NeedsFoundation(string type) => type switch
        {
            "bridge" => false, "checkpoint_bridge" => false, "depot_through" => false, "street" => false, "alley" => false, "arch" => false,
            "embankment" => false, "park" => false, "cave" => false, "tunnel" => false, "firepit" => false,
            "spillway" => false, "slabs" => false, "ruin" => false, "shed" => false,
            _ => true,
        };

        /// Types that are placed on the ground as-is (paths, natural features) rather than moved to dry, level ground.
        public static bool IsGroundFeature(string type) => type is "street" or "alley" or "arch" or "embankment" or "park" or "cave" or "firepit";

        // ------------------------------------------------------------------ which structure for which place

        /// Picks a structure for a lore landmark from its name (Russian, as on the board).
        /// Returns null for places that are landscape, not buildings (Степь, Горы).
        public static (string type, float w, float d)? ForLandmark(string name)
        {
            string n = name.ToLowerInvariant();
            bool Has(params string[] keys) { foreach (var k in keys) if (n.Contains(k)) return true; return false; }
            if (Has("степь", "горы", "балаур", "prime city")) return null;
            if (Has("кпп")) return ("checkpoint", 18, 8);
            if (Has("мост")) return ("bridge", 60, 9);
            if (Has("фонтан")) return ("fountain", 12, 12);
            if (Has("аллея «память»", "аллея \"память\"", "бужорилор")) return ("arch", 8, 3);   // the paths themselves come from the terrain data
            if (Has("бульвар", "улица", "туманная", "светлый двор", "аллея")) return ("street", 56, 10);
            if (Has("кострище")) return ("firepit", 8, 8);
            if (Has("пещера")) return ("cave", 18, 14);
            if (Has("насосная")) return ("watertower", 10, 10);
            if (Has("туннель", "тунель")) return ("tunnel", 14, 6);
            if (Has("тэц")) return ("powerplant", 44, 26);
            if (Has("аэс")) return ("nuclear", 40, 30);
            if (Has("элеватор")) return ("silos", 30, 10);
            if (Has("жби")) return ("concrete", 40, 18);
            if (Has("пивзавод")) return ("brewery", 30, 18);
            if (Has("горное производство")) return ("mine", 34, 26);
            if (Has("creer")) return ("office", 24, 20);
            if (Has("мемориал")) return ("memorial", 16, 12);
            if (Has("военная часть")) return ("barracks", 42, 30);
            if (Has("крепост")) return ("fortress", 34, 30);
            if (Has("панельки", "общаги")) return ("panels", 46, 24);
            if (Has("новые жк")) return ("newblocks", 36, 20);
            if (Has("набережная")) return ("embankment", 60, 8);
            if (Has("толкучка")) return ("market", 26, 18);
            if (Has("очистные")) return ("treatment", 40, 30);
            if (Has("старый центр")) return ("oldtown", 40, 30);
            if (Has("универ", "б.л.я", "е.б.н.у")) return ("university", 34, 20);
            if (Has("хромая жаба")) return ("bar", 12, 9);
            if (Has("хутор")) return ("houses", 34, 26);
            if (Has("депо")) return ("depot", 22, 18);
            if (Has("парк")) return ("park", 40, 30);
            if (Has("брынкуш")) return ("statue", 8, 8);
            return ("houses", 24, 18);   // anything else: a few buildings
        }

        // ------------------------------------------------------------------ more building types

        static readonly Color Stone   = new Color(0.58f, 0.56f, 0.52f);
        static readonly Color Canvas1 = new Color(0.62f, 0.32f, 0.24f);
        static readonly Color Canvas2 = new Color(0.30f, 0.42f, 0.50f);
        static readonly Color Canvas3 = new Color(0.66f, 0.58f, 0.32f);
        static readonly Color LampGlow = new Color(1.00f, 0.84f, 0.55f);
        static readonly Color Water   = new Color(0.16f, 0.24f, 0.27f);
        static readonly Color Leaf    = new Color(0.22f, 0.34f, 0.22f);
        static readonly Color Bronze  = new Color(0.45f, 0.34f, 0.20f);
        static readonly Color RedWhite = new Color(0.75f, 0.20f, 0.18f);

        /// Checkpoint standing on a bridge deck: booth at the kerb, barrier across the roadway (local x = across the deck).
        static float CheckpointOnBridge(Kit k, float w, float d)
        {
            float half = w / 2f;
            k.Box(new Vector3(-half + 2f, 0, 0), new Vector3(2.8f, 3f, 3.2f), Whitewash);
            k.Box(new Vector3(-half + 2f, 3f, 0), new Vector3(3.4f, 0.35f, 3.8f), RoofDark);
            k.Box(new Vector3(-half + 3.8f, 0, -1.8f), new Vector3(0.4f, 1.3f, 0.4f), DarkConc);
            k.Box(new Vector3(0.5f, 1.1f, -1.8f), new Vector3(w - 6f, 0.22f, 0.22f), RedWhite);   // barrier arm across the road
            k.Box(new Vector3(half - 1.2f, 0, -1.8f), new Vector3(0.4f, 1.3f, 0.4f), DarkConc);
            return 3.4f;
        }

        /// Engine shed the railway runs straight through (local x = along the track): two side walls and a
        /// barrel roof, open at both ends.
        static float DepotThrough(Kit k, float w, float d)
        {
            float half = d / 2f, wallH = 6f;
            foreach (float s in new[] { -1f, 1f })
                k.Box(new Vector3(0, -2f, s * (half - 0.5f)), new Vector3(w, wallH + 2f, 1f), Brick);
            // Barrel roof from planks around a half circle over the walls.
            const int planks = 9;
            for (int i = 0; i < planks; i++)
            {
                float a = Mathf.PI * (i + 0.5f) / planks;                       // 0..π from one wall to the other
                float seg = Mathf.PI * half / planks;
                var centre = new Vector3(0, wallH + Mathf.Sin(a) * half, Mathf.Cos(a) * half);
                k.BoxRotated(centre, new Vector3(w, 0.45f, seg + 0.15f), Quaternion.Euler(90f - a * Mathf.Rad2Deg, 0, 0), DarkConc);   // plank tangent to the arch
            }
            // Dark interior gable rims at each end, so the openings read as doorways.
            foreach (float s in new[] { -1f, 1f })
                k.BoxRotated(new Vector3(s * (w / 2 - 0.2f), wallH + half * 0.95f, 0), new Vector3(0.4f, 0.6f, d * 0.6f), Quaternion.identity, Rail);
            return wallH + half;
        }

        /// Old rusty arch over a path (Аллея «Память»: «По краям аллеи стояли старые арки»).
        static float Arch(Kit k, float w, float d)
        {
            foreach (float s in new[] { -1f, 1f })
                k.Box(new Vector3(s * w / 2, 0, 0), new Vector3(0.45f, 5.5f, 0.45f), Rust);
            k.Box(new Vector3(0, 5.3f, 0), new Vector3(w + 0.6f, 0.4f, 0.45f), Rust);
            k.Box(new Vector3(0, 4.4f, 0), new Vector3(w * 0.6f, 0.25f, 0.3f), Rust);
            return 6f;
        }
        static readonly Color Rust = new Color(0.45f, 0.25f, 0.15f);

        static float Checkpoint(Kit k, float w, float d)
        {
            k.Box(new Vector3(-3, 0, -2), new Vector3(4, 3.2f, 4), Whitewash);                 // booth
            k.Box(new Vector3(-3, 3.2f, -2), new Vector3(4.6f, 0.4f, 4.6f), RoofDark);
            k.Box(new Vector3(-0.6f, 0, 0), new Vector3(0.5f, 1.4f, 0.5f), DarkConc);          // barrier post
            k.Box(new Vector3(3.5f, 1.2f, 0), new Vector3(8f, 0.25f, 0.25f), RedWhite);        // barrier arm
            k.Box(new Vector3(-w / 2 - 1, 0, d / 2), new Vector3(w * 0.4f, 2.4f, 0.3f), DarkConc);   // fence
            k.Box(new Vector3(w / 2 - 2, 0, d / 2), new Vector3(w * 0.3f, 2.4f, 0.3f), DarkConc);
            return 4f;
        }

        /// Bridge along local x, deck surface at y = 0 (the root sits at deck level), piers to well below.
        static float Bridge(Kit k, float w, float d)
        {
            k.Box(new Vector3(0, -1.4f, 0), new Vector3(w, 1.4f, d), Stone);                    // deck
            foreach (float s in new[] { -1f, 1f })
                k.Box(new Vector3(0, 0, s * (d / 2 - 0.2f)), new Vector3(w, 1.1f, 0.35f), DarkConc);   // parapets
            int piers = Mathf.Max(1, Mathf.RoundToInt(w / 16f) - 1);
            for (int i = 1; i <= piers; i++)
            {
                float x = -w / 2 + w * i / (piers + 1);
                k.Box(new Vector3(x, -60f, 0), new Vector3(2.4f, 58.6f, d * 0.8f), Stone);
            }
            for (int i = 0; i < Mathf.RoundToInt(w / 10f); i++)                                    // lamps along it
                k.Lamp(new Vector3(-w / 2 + 5 + i * 10, 0, d / 2 - 0.4f), 4f);
            return 5f;
        }

        static float Fountain(Kit k, float w, float d)
        {
            k.Cylinder(Vector3.zero, w * 0.45f, 1.0f, Stone);
            k.Cylinder(new Vector3(0, 0.05f, 0), w * 0.40f, 1.0f, Water, 0.85f);
            k.Cylinder(Vector3.zero, 0.6f, 3.6f, Stone);
            k.Cylinder(new Vector3(0, 3.6f, 0), 1.6f, 0.4f, Stone);
            k.Sphere(new Vector3(0, 4.6f, 0), new Vector3(1.2f, 1.6f, 1.2f), DarkConc);           // worn figure
            return 5.5f;
        }

        static float Alley(Kit k, float w, float d, bool street)
        {
            k.Strip(-w / 2, w / 2, 0f, street ? d * 0.7f : 3.2f, 0.15f, street ? DarkConc : Stone);   // path / road
            for (float x = -w / 2 + 3; x <= w / 2 - 3; x += street ? 9f : 7f)
            {
                k.LampOnGround(new Vector3(x, 0, d / 2), 4.5f);
                k.LampOnGround(new Vector3(x + 3.5f, 0, -d / 2), 4.5f);
                var extra = new Vector3(x + 1.7f, 0, street ? d / 2 + 1.8f : d / 2 - 0.9f);
                if (!k.Ground(ref extra)) continue;
                if (!street) k.Box(extra, new Vector3(2.2f, 0.55f, 0.7f), Sleeperish);
                else k.Spire(extra, 1.6f, 5f, Leaf);                                             // street trees
            }
            return 5f;
        }
        static readonly Color Sleeperish = new Color(0.33f, 0.25f, 0.18f);

        static float Firepit(Kit k, float w, float d)
        {
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2;
                k.Box(new Vector3(Mathf.Cos(a) * 2.3f, 0, Mathf.Sin(a) * 2.3f), new Vector3(0.9f, 0.6f, 0.8f), Stone);
            }
            foreach (float a in new[] { 0.3f, 2.4f, 4.4f })
                k.CylinderLying(new Vector3(Mathf.Cos(a) * 4.2f, 0.35f, Mathf.Sin(a) * 4.2f), 0.35f, 3.2f, Sleeperish);
            k.Box(Vector3.zero, new Vector3(1.4f, 0.4f, 1.4f), new Color(1f, 0.45f, 0.15f), 0.2f);
            k.Light(new Vector3(0, 1.2f, 0), new Color(1f, 0.5f, 0.2f), 16f, 2.5f);
            return 2f;
        }

        static float Cave(Kit k, float w, float d)
        {
            k.Sphere(new Vector3(0, 2f, 0), new Vector3(w, 11f, d), Rock);
            k.Box(new Vector3(0, 0, -d * 0.42f), new Vector3(5.5f, 4.5f, 3f), Black);
            return 8f;
        }
        static readonly Color Rock  = new Color(0.44f, 0.41f, 0.36f);
        static readonly Color Black = new Color(0.03f, 0.03f, 0.03f);

        static float WaterTower(Kit k, float w, float d)
        {
            foreach (var (x, z) in new[] { (-2.4f, -2.4f), (2.4f, -2.4f), (-2.4f, 2.4f), (2.4f, 2.4f) })
                k.Box(new Vector3(x, 0, z), new Vector3(0.6f, 11f, 0.6f), DarkConc);
            k.Cylinder(new Vector3(0, 11f, 0), 4f, 6f, Brick);
            k.Spire(new Vector3(0, 17f, 0), 4.3f, 2.5f, RoofDark);
            return 19.5f;
        }

        static float TunnelPortal(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(w, 9f, d), Rock);
            k.Box(new Vector3(0, 0, -d / 2 - 0.1f), new Vector3(w * 0.45f, 5.5f, 1.5f), Concrete);
            k.Box(new Vector3(0, 0, -d / 2 - 0.3f), new Vector3(w * 0.3f, 4.6f, 1.6f), Black);
            return 9f;
        }

        static float PowerPlant(Kit k, float w, float d)
        {
            k.Box(new Vector3(-w * 0.18f, 0, -d * 0.2f), new Vector3(w * 0.55f, 14f, d * 0.5f), Brick);
            k.Gable(new Vector3(-w * 0.18f, 14f, -d * 0.2f), new Vector3(w * 0.56f, 3f, d * 0.52f), RoofDark, alongZ: false);
            // Cooling towers: a wide base and a narrower waist.
            foreach (float x in new[] { w * 0.15f, w * 0.38f })
            {
                k.Cylinder(new Vector3(x, 0, d * 0.22f), 8f, 12f, Concrete);
                k.Cylinder(new Vector3(x, 12f, d * 0.22f), 6.6f, 10f, Concrete);
                k.Cylinder(new Vector3(x, 22f, d * 0.22f), 7.2f, 6f, Concrete);
            }
            k.Cylinder(new Vector3(-w * 0.4f, 0, d * 0.3f), 1.6f, 44f, RedWhite);
            return 44f;
        }

        static float Nuclear(Kit k, float w, float d)
        {
            // Unfinished: reactor drum with a half-built dome, scaffolding and a tower crane.
            k.Cylinder(Vector3.zero, 10f, 18f, Concrete);
            k.Sphere(new Vector3(0, 18f, 0), new Vector3(18f, 10f, 18f), DarkConc);
            k.Box(new Vector3(-6f, 18f, 0), new Vector3(8f, 6f, 20f), Black);                     // the dome's open gap
            k.Box(new Vector3(w * 0.36f, 0, 0), new Vector3(1.2f, 52f, 1.2f), Gilt);                 // crane mast
            k.Box(new Vector3(w * 0.16f, 51f, 0), new Vector3(30f, 1.2f, 1.2f), Gilt);               // jib
            k.Box(new Vector3(-w * 0.32f, 0, d * 0.25f), new Vector3(10f, 9f, 12f), Concrete);
            return 53f;
        }

        static float Silos(Kit k, float w, float d)
        {
            for (int i = 0; i < 6; i++)
                k.Cylinder(new Vector3(-w / 2 + 3 + i * (w - 10) / 5f, 0, 0), 2.6f, 24f, Concrete);
            k.Box(new Vector3(w / 2 - 3, 0, 0), new Vector3(6f, 32f, 7f), Concrete);
            k.Box(new Vector3(0, 24f, 0), new Vector3(w - 4, 1.6f, 2.4f), DarkConc);                // conveyor gallery
            return 32f;
        }

        static float ConcretePlant(Kit k, float w, float d)
        {
            k.Box(new Vector3(0, 0, -d * 0.2f), new Vector3(w, 10f, d * 0.55f), Concrete);
            k.Box(new Vector3(0, 10f, -d * 0.2f), new Vector3(w + 0.5f, 0.6f, d * 0.57f), RoofDark);
            // Gantry crane over the slab yard.
            foreach (float x in new[] { -w * 0.3f, w * 0.3f })
            {
                k.Box(new Vector3(x, 0, d * 0.3f - 4), new Vector3(1f, 12f, 1f), Gilt);
                k.Box(new Vector3(x, 0, d * 0.3f + 4), new Vector3(1f, 12f, 1f), Gilt);
            }
            k.Box(new Vector3(0, 12f, d * 0.3f - 4), new Vector3(w * 0.62f, 1.2f, 1f), Gilt);
            k.Box(new Vector3(0, 12f, d * 0.3f + 4), new Vector3(w * 0.62f, 1.2f, 1f), Gilt);
            for (int i = 0; i < 4; i++) k.Box(new Vector3(-w * 0.15f + i * 4, 0, d * 0.32f), new Vector3(3f, 1.4f + i * 0.6f, 6f), DarkConc);
            return 13f;
        }

        static float Brewery(Kit k, float w, float d)
        {
            k.Box(new Vector3(-w * 0.12f, 0, 0), new Vector3(w * 0.7f, 9f, d * 0.8f), Brick);
            k.Gable(new Vector3(-w * 0.12f, 9f, 0), new Vector3(w * 0.72f, 3.5f, d * 0.82f), RoofDark, alongZ: false);
            for (int i = 0; i < 3; i++) k.Cylinder(new Vector3(w * 0.36f, 0, -d * 0.3f + i * d * 0.3f), 2f, 11f, Glass);
            k.Cylinder(new Vector3(-w * 0.4f, 0, d * 0.3f), 1.2f, 20f, Brick);
            return 20f;
        }

        static float Mine(Kit k, float w, float d)
        {
            // Abandoned mine: headframe with a wheel, a spoil heap, a roofless shed.
            foreach (var (x, z) in new[] { (-2f, -2f), (2f, -2f), (-2f, 2f), (2f, 2f) })
                k.Box(new Vector3(x - w * 0.2f, 0, z), new Vector3(0.7f, 22f, 0.7f), Rail);
            k.CylinderLying(new Vector3(-w * 0.2f, 22.5f, 0), 2.4f, 0.8f, Rail);
            k.Spire(new Vector3(w * 0.22f, 0, d * 0.1f), 12f, 14f, Rock);
            k.Box(new Vector3(-w * 0.2f, 0, d * 0.32f), new Vector3(12f, 4f, 7f), DarkConc);
            return 25f;
        }

        static float Office(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(w, 6f, d), DarkConc);
            k.Box(new Vector3(-w * 0.15f, 6f, 0), new Vector3(w * 0.55f, 34f, d * 0.55f), Glass, 0.85f);
            k.Box(new Vector3(-w * 0.15f, 40f, 0), new Vector3(w * 0.45f, 1.5f, d * 0.45f), DarkConc);
            return 41.5f;
        }

        static float Memorial(Kit k, float w, float d)
        {
            for (int i = 0; i < 3; i++) k.Box(new Vector3(0, i * 0.6f, 0), new Vector3(w - i * 3, 0.6f, d - i * 3), Stone);
            k.Box(new Vector3(0, 1.8f, -d * 0.15f), new Vector3(w * 0.7f, 3.2f, 0.8f), DarkConc);     // wall of names
            k.Box(new Vector3(0, 1.8f, d * 0.15f), new Vector3(1.8f, 16f, 1.8f), Stone);              // obelisk
            k.Spire(new Vector3(0, 17.8f, d * 0.15f), 1.3f, 3f, Stone);
            return 21f;
        }

        static float Barracks(Kit k, float w, float d)
        {
            for (int i = 0; i < 2; i++)
            {
                k.Box(new Vector3(-w * 0.1f, 0, -d * 0.2f + i * d * 0.35f), new Vector3(w * 0.6f, 5f, 7f), Patina);
                k.Gable(new Vector3(-w * 0.1f, 5f, -d * 0.2f + i * d * 0.35f), new Vector3(w * 0.62f, 2.2f, 7.4f), RoofDark, alongZ: false);
            }
            foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                k.Box(new Vector3(w * 0.38f + x, 0, d * 0.3f + z), new Vector3(0.4f, 10f, 0.4f), Rail);
            k.Box(new Vector3(w * 0.38f, 10f, d * 0.3f), new Vector3(3.4f, 2.6f, 3.4f), Patina);
            k.Box(new Vector3(0, 0, -d / 2), new Vector3(w, 2.4f, 0.3f), DarkConc);
            k.Box(new Vector3(0, 0, d / 2), new Vector3(w, 2.4f, 0.3f), DarkConc);
            k.Box(new Vector3(-w / 2, 0, 0), new Vector3(0.3f, 2.4f, d), DarkConc);
            return 12.6f;
        }

        static float Fortress(Kit k, float w, float d)
        {
            k.Box(new Vector3(-w * 0.1f, 0, -d / 2), new Vector3(w * 0.7f, 8f, 2.4f), Stone);
            k.Box(new Vector3(-w / 2, 0, 0), new Vector3(2.4f, 6f, d * 0.8f), Stone);
            k.Box(new Vector3(w / 2, 0, d * 0.1f), new Vector3(2.4f, 9f, d * 0.5f), Stone);
            k.Box(new Vector3(w * 0.1f, 0, d / 2), new Vector3(w * 0.4f, 4f, 2.4f), Stone);
            k.Cylinder(new Vector3(-w / 2, 0, -d / 2), 4.5f, 13f, Stone);
            k.Cylinder(new Vector3(w / 2, 0, d / 2), 4.5f, 7f, Stone);
            return 13f;
        }

        static float Panels(Kit k, float w, float d)
        {
            float[] hs = { 27f, 15f, 27f };
            for (int i = 0; i < 3; i++)
                k.Box(new Vector3(-w / 2 + w * (i + 0.5f) / 3f, 0, (i - 1) * d * 0.25f), new Vector3(w / 3f * 0.85f, hs[i], 8f), Concrete);
            return 27f;
        }

        static float NewBlocks(Kit k, float w, float d)
        {
            float[] hs = { 42f, 50f, 36f };
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-w / 2 + w * (i + 0.5f) / 3f, 0, (i % 2 == 0 ? -1 : 1) * d * 0.2f);
                k.Box(p, new Vector3(9f, hs[i], 9f), Cream);
                k.Box(p + Vector3.up * hs[i], new Vector3(9.4f, 1f, 9.4f), Patina);
            }
            return 51f;
        }

        static float Embankment(Kit k, float w, float d)
        {
            k.Strip(-w / 2, w / 2, 0f, d * 0.6f, 0.2f, Stone);
            k.Strip(-w / 2, w / 2, -d * 0.3f, 0.3f, 1.3f, Rail);                                       // railing
            for (float x = -w / 2 + 4; x <= w / 2 - 4; x += 8f)
            {
                k.LampOnGround(new Vector3(x, 0, -d * 0.3f + 0.6f), 4.5f);
                var bench = new Vector3(x + 4, 0, d * 0.15f);
                if (k.Ground(ref bench)) k.Box(bench, new Vector3(2.2f, 0.55f, 0.7f), Sleeperish);
            }
            return 5f;
        }

        static float Market(Kit k, float w, float d)
        {
            Color[] tops = { Canvas1, Canvas2, Canvas3 };
            int c = 0;
            for (float x = -w / 2 + 2.5f; x <= w / 2 - 2.5f; x += 5f)
            for (float z = -d / 2 + 2.5f; z <= d / 2 - 2.5f; z += 6f)
            {
                k.Box(new Vector3(x, 0, z), new Vector3(3.6f, 2.4f, 3.6f), Whitewash);
                k.Gable(new Vector3(x, 2.4f, z), new Vector3(4.2f, 1.4f, 4.2f), tops[c++ % 3], alongZ: c % 2 == 0);
            }
            return 4f;
        }

        static float Treatment(Kit k, float w, float d)
        {
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(-w * 0.25f + (i % 2) * w * 0.5f, 0, -d * 0.22f + (i / 2) * d * 0.44f);
                k.Cylinder(p, 7f, 2.5f, Concrete);
                k.Cylinder(p + Vector3.up * 0.1f, 6.4f, 2.5f, Water, 0.85f);
            }
            k.Box(new Vector3(0, 0, 0), new Vector3(8f, 6f, 6f), Brick);
            return 6f;
        }

        static float OldTown(Kit k, float w, float d)
        {
            for (int i = 0; i < 10; i++)
            {
                float x = k.Range(-w / 2 + 4, w / 2 - 4), z = k.Range(-d / 2 + 4, d / 2 - 4);
                float hw = k.Range(6f, 9f), hd = k.Range(5f, 7f), hh = k.Range(6f, 10f);
                k.Box(new Vector3(x, 0, z), new Vector3(hw, hh, hd), i % 3 == 0 ? Cream : Whitewash);
                k.Gable(new Vector3(x, hh, z), new Vector3(hw + 0.6f, 3f, hd + 0.6f), RoofRed, alongZ: false);
            }
            k.Box(Vector3.zero, new Vector3(5f, 18f, 5f), Whitewash);
            k.Spire(new Vector3(0, 18f, 0), 3.4f, 9f, Patina);
            return 27f;
        }

        static float University(Kit k, float w, float d) => Gallery(k, w, d);

        static float Bar(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(w * 0.8f, 5f, d * 0.8f), Brick);
            k.Gable(new Vector3(0, 5f, 0), new Vector3(w * 0.82f, 2.6f, d * 0.82f), RoofDark, alongZ: false);
            k.Box(new Vector3(0, 2.2f, -d * 0.4f - 0.1f), new Vector3(w * 0.5f, 1.6f, 0.2f), LampGlow, 0.4f);   // lit windows
            k.Light(new Vector3(0, 2.5f, -d * 0.4f - 1.5f), new Color(1f, 0.6f, 0.3f), 14f, 1.8f);
            return 7.6f;
        }

        static float Park(Kit k, float w, float d)
        {
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3(k.Range(-w / 2, w / 2), 0, k.Range(-d / 2, d / 2));
                float r = k.Range(2f, 3f), hgt = k.Range(6f, 10f);
                if (k.Ground(ref p)) k.Spire(p, r, hgt, Leaf);                                    // never in the water
            }
            k.Strip(-w / 2, w / 2, 0f, 3f, 0.12f, Stone);
            for (float x = -w / 2 + 6; x < w / 2; x += 12f) k.LampOnGround(new Vector3(x, 0, 2.2f), 4f);
            return 10f;
        }

        static float Statue(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(3.2f, 2.4f, 3.2f), Stone);
            k.Sphere(new Vector3(0, 4.2f, 0), new Vector3(2.2f, 3.4f, 2.6f), Bronze);                 // an ovoid head, like Brâncuși's Muse
            return 6f;
        }

        // ------------------------------------------------------------------ kit

        class Kit
        {
            readonly Transform _root;
            readonly Func<Color, float, Material> _mat;
            readonly System.Random _rng;
            public Kit(Transform root, Func<Color, float, Material> mat, System.Random rng) { _root = root; _mat = mat; _rng = rng; }

            public float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

            /// Box standing on y = bottom.y (bottom centre at the given point).
            public float Box(Vector3 bottom, Vector3 size, Color c, float smooth = 0.1f)
            {
                Part(UnitCube, bottom + Vector3.up * size.y / 2, size, Quaternion.identity, c, smooth);
                return bottom.y + size.y;
            }

            /// Gable roof (triangular prism) sitting on y = bottom.y. alongZ: ridge runs along z.
            public void Gable(Vector3 bottom, Vector3 size, Color c, bool alongZ)
            {
                var rot = alongZ ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                var s = alongZ ? size : new Vector3(size.z, size.y, size.x);
                Part(PrismMesh, bottom, s, rot, c, 0.1f);
            }

            public GameObject Cylinder(Vector3 bottom, float radius, float height, Color c, float smooth = 0.1f)
                => Part(UnitCylinder, bottom + Vector3.up * height / 2, new Vector3(radius * 2, height / 2, radius * 2), Quaternion.identity, c, smooth);

            public void BoxRotated(Vector3 centre, Vector3 size, Quaternion rot, Color c, float smooth = 0.1f)
                => Part(UnitCube, centre, size, rot, c, smooth);

            public void Sphere(Vector3 centre, Vector3 size, Color c)
                => Part(UnitSphere, centre, size, Quaternion.identity, c, 0.1f);

            /// Street lamp: thin post with a small glowing head.
            /// Drop a local point onto the terrain (keeps x, z). False if it lands in water or there is no terrain info.
            public bool Ground(ref Vector3 local)
            {
                if (GroundAt == null) return true;
                Vector3 world = _root.TransformPoint(new Vector3(local.x, 0f, local.z));
                if (IsWet != null && IsWet(world)) return false;
                local.y = GroundAt(world) - _root.position.y;
                return true;
            }

            public void LampOnGround(Vector3 local, float height)
            {
                if (Ground(ref local)) Lamp(local, height);
            }

            /// A path or kerb laid along local x in short segments that follow the ground and stop at water.
            public void Strip(float x0, float x1, float z, float width, float thick, Color c)
            {
                const float seg = 5f;
                for (float x = x0; x < x1 - 0.01f; x += seg)
                {
                    float len = Mathf.Min(seg, x1 - x);
                    var p = new Vector3(x + len / 2, 0, z);
                    if (!Ground(ref p)) continue;
                    Box(p - Vector3.up * 0.05f, new Vector3(len + 0.3f, thick + 0.05f, width), c);
                }
            }

            public void Lamp(Vector3 bottom, float height)
            {
                Part(UnitCylinder, bottom + Vector3.up * height / 2, new Vector3(0.25f, height / 2, 0.25f), Quaternion.identity, Rail, 0.3f);
                Part(UnitCube, bottom + Vector3.up * (height + 0.25f), new Vector3(0.7f, 0.5f, 0.7f), Quaternion.identity, LampGlow, 0.6f);
            }

            public void Light(Vector3 local, Color c, float range, float intensity)
            {
                var l = new GameObject("Light").AddComponent<UnityEngine.Light>();
                l.transform.SetParent(_root, false);
                l.transform.localPosition = local;
                l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
                l.shadows = LightShadows.None;
            }

            /// Cylinder lying along x with its axis at the given point (for arched roofs).
            public void CylinderLying(Vector3 axis, float radius, float length, Color c)
                => Part(UnitCylinder, axis, new Vector3(radius * 2, length / 2, radius * 2), Quaternion.Euler(0, 0, 90), c, 0.1f);

            public void Spire(Vector3 bottom, float radius, float height, Color c)
                => Part(ConeMesh, bottom, new Vector3(radius * 2, height, radius * 2), Quaternion.identity, c, 0.3f);

            GameObject Part(Mesh mesh, Vector3 localPos, Vector3 scale, Quaternion rot, Color c, float smooth)
            {
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(_root, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = rot;
                go.transform.localScale = scale;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = _mat(c, smooth);
                return go;
            }
        }

        // ------------------------------------------------------------------ meshes

        static Mesh _cube, _cyl, _prism, _cone;
        /// Shared meshes for other builders (countryside villages, woods).
        public static Mesh CubeMesh => UnitCube;
        public static Mesh CylinderMesh => UnitCylinder;
        public static Mesh GableMesh => PrismMesh;
        public static Mesh SpireMesh => ConeMesh;
        static Mesh UnitCube => _cube != null ? _cube : (_cube = Primitive(PrimitiveType.Cube));
        static Mesh UnitCylinder => _cyl != null ? _cyl : (_cyl = Primitive(PrimitiveType.Cylinder));
        static Mesh _sphere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _cube = _cyl = _prism = _cone = _sphere = null; GroundAt = null; IsWet = null; }   // rebuilt each Play
        static Mesh UnitSphere => _sphere != null ? _sphere : (_sphere = Primitive(PrimitiveType.Sphere));

        static Mesh Primitive(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(go);
            return m;
        }

        /// Unit gable prism: 1 wide (x), 1 tall (y, apex), 1 long (z), base centred on the origin. Flat-shaded.
        static Mesh PrismMesh
        {
            get
            {
                if (_prism != null) return _prism;
                Vector3 l0 = new Vector3(-0.5f, 0, -0.5f), r0 = new Vector3(0.5f, 0, -0.5f), t0 = new Vector3(0, 1, -0.5f);
                Vector3 l1 = new Vector3(-0.5f, 0, 0.5f), r1 = new Vector3(0.5f, 0, 0.5f), t1 = new Vector3(0, 1, 0.5f);
                var v = new List<Vector3>(); var tri = new List<int>();
                void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); }
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
                Tri(l0, t0, r0);                 // front gable (faces −z)
                Tri(r1, t1, l1);                 // back gable (faces +z)
                Quad(l0, l1, t1, t0);            // left slope
                Quad(r0, t0, t1, r1);            // right slope
                Quad(l0, r0, r1, l1);            // bottom
                _prism = new Mesh { name = "Gable" };
                _prism.SetVertices(v); _prism.SetTriangles(tri, 0);
                _prism.RecalculateNormals(); _prism.RecalculateBounds();
                return _prism;
            }
        }

        /// Unit cone (radius 0.5, height 1, base at the origin), 10 sides, flat-shaded.
        static Mesh ConeMesh
        {
            get
            {
                if (_cone != null) return _cone;
                var v = new List<Vector3>(); var tri = new List<int>();
                const int sides = 10;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                    var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                    var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                    int k = v.Count; v.Add(p0); v.Add(Vector3.up); v.Add(p1);
                    tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
                }
                _cone = new Mesh { name = "Spire" };
                _cone.SetVertices(v); _cone.SetTriangles(tri, 0);
                _cone.RecalculateNormals(); _cone.RecalculateBounds();
                return _cone;
            }
        }
    }
}
