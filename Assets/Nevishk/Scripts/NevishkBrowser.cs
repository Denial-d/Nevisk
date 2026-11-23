using System.Runtime.InteropServices;
using UnityEngine;

namespace Nevishk
{
    /// <summary>
    /// Deep links (#district_or_landmark_id in the URL) and the visitor's reduced-motion
    /// preference. Only active in web builds; harmless no-ops in the Editor.
    /// </summary>
    public static class NevishkBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string NevishkGetHash();
        [DllImport("__Internal")] static extern void NevishkSetHash(string hash, int push);
        [DllImport("__Internal")] static extern int NevishkPrefersReducedMotion();
        [DllImport("__Internal")] static extern void NevishkListenHash(string gameObjectName);

        public static string GetHash() => NevishkGetHash();
        public static void SetHash(string hash, bool push) => NevishkSetHash(hash ?? "", push ? 1 : 0);
        public static bool PrefersReducedMotion => NevishkPrefersReducedMotion() == 1;
        public static void ListenForBackButton(string gameObjectName) => NevishkListenHash(gameObjectName);
#else
        public static string GetHash() => "";
        public static void SetHash(string hash, bool push) { }
        public static bool PrefersReducedMotion => false;
        public static void ListenForBackButton(string gameObjectName) { }
#endif
    }
}
