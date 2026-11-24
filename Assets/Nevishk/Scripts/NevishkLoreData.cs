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
