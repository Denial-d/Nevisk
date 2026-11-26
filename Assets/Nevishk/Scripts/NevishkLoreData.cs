using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nevishk
{
    // Mirrors Resources/nevishk_lore.json. Field names must match the JSON keys.

    [Serializable]
    public class CityInfo
    {
        public string name;
        public string tagline;
        public string mapImage;
        public int[] mapPixelSize;
    }

    [Serializable]
    public class CategoryInfo
    {
        public string id;
        public string name;
        public string color;
    }

    [Serializable]
    public class DistrictInfo
    {
        public string id;
        public string name;
        public int number;
        public string color;
        public string owner;
        public string status;
        public string summary;
        public string text;
        public string[] docs;
        public float[] uv;      // 0..1 on the base map image, origin top-left
        public ImageInfo[] images;
        public string kicker;   // overrides "РАЙОН N" (e.g. a district whose official language isn't Russian)
        public LoreTranslation ru;
    }

    /// <summary>
    /// A Russian version of an entry whose official text is in another language (Loom of Echoes: Ouroboros
    /// banned Russian). The panel offers it behind a switch, with an in-world warning.
    /// </summary>
    [Serializable]
    public class LoreTranslation
    {
        public string name, summary, text;
        public static bool Has(LoreTranslation t) => t != null && !string.IsNullOrEmpty(t.text);
    }

    [Serializable]
    public class ImageInfo
    {
        public string file;     // Resources/Images/<file>
        public string caption;
    }

    [Serializable]
    public class LandmarkInfo
    {
        public string id;
        public string name;
        public string category;
        public string district;
        public float[] uv;
        public string text;
        public string[] docs;
        public ImageInfo[] images;
        public string model;     // building type, overriding the guess from the name (NevishkBuildings types)
        public float[] size;     // its footprint in base-map px (w, d)
        public LoreTranslation ru;
    }

    [Serializable]
    public class DocumentInfo
    {
        public string id;
        public string title;
        public string kind;
        public string author;
        public string text;
    }

    [Serializable]
    public class LoreDatabase
    {
        public CityInfo city;
        public CategoryInfo[] categories;
        public DistrictInfo[] districts;
        public LandmarkInfo[] landmarks;
        public DocumentInfo[] documents;

        [NonSerialized] Dictionary<string, DistrictInfo> _districts;
        [NonSerialized] Dictionary<string, LandmarkInfo> _landmarks;
        [NonSerialized] Dictionary<string, DocumentInfo> _documents;
        [NonSerialized] Dictionary<string, CategoryInfo> _categories;

        public static LoreDatabase Load(string resourceName = "nevishk_lore")
        {
            var asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.LogError($"[Nevishk] Resources/{resourceName}.json not found.");
                return null;
            }
            var db = JsonUtility.FromJson<LoreDatabase>(asset.text);
            db.Index();
            return db;
        }

        void Index()
        {
            _districts = new Dictionary<string, DistrictInfo>();
            foreach (var d in districts) _districts[d.id] = d;
            _landmarks = new Dictionary<string, LandmarkInfo>();
            foreach (var l in landmarks) _landmarks[l.id] = l;
            _documents = new Dictionary<string, DocumentInfo>();
            foreach (var d in documents) _documents[d.id] = d;
            _categories = new Dictionary<string, CategoryInfo>();
            foreach (var c in categories) _categories[c.id] = c;
        }

        public DistrictInfo District(string id) => id != null && _districts.TryGetValue(id, out var v) ? v : null;
        public LandmarkInfo Landmark(string id) => id != null && _landmarks.TryGetValue(id, out var v) ? v : null;
        public DocumentInfo Document(string id) => id != null && _documents.TryGetValue(id, out var v) ? v : null;
        public CategoryInfo Category(string id) => id != null && _categories.TryGetValue(id, out var v) ? v : null;

        public IEnumerable<LandmarkInfo> LandmarksIn(string districtId)
        {
            foreach (var l in landmarks)
                if (l.district == districtId) yield return l;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }
    }
}
