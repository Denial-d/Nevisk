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
        [DllImport("__Internal")] static extern void NevishkDownload(string fileName, string text);
        [DllImport("__Internal")] static extern void NevishkOpenFile(string gameObjectName, string method);
        [DllImport("__Internal")] static extern void NevishkCopyLink(string hash);

        public static string GetHash() => NevishkGetHash();
        public static void SetHash(string hash, bool push) => NevishkSetHash(hash ?? "", push ? 1 : 0);
        public static bool PrefersReducedMotion => NevishkPrefersReducedMotion() == 1;
        public static void ListenForBackButton(string gameObjectName) => NevishkListenHash(gameObjectName);
        /// Saves text as a downloaded file.
        public static void Download(string fileName, string text) => NevishkDownload(fileName, text);
        /// Lets the visitor pick a text file; its contents arrive via SendMessage(gameObjectName, method, text).
        public static void OpenTextFile(string gameObjectName, string method) => NevishkOpenFile(gameObjectName, method);
        /// Copies this page's address with the given hash to the clipboard.
        public static void CopyLinkWithHash(string hash) => NevishkCopyLink(hash);
#else
        public static string GetHash() => "";
        public static void SetHash(string hash, bool push) { }
        public static bool PrefersReducedMotion => false;
        public static void ListenForBackButton(string gameObjectName) { }
        public static void Download(string fileName, string text) { }
        public static void OpenTextFile(string gameObjectName, string method) { }
        public static void CopyLinkWithHash(string hash) { }
#endif
    }
}
