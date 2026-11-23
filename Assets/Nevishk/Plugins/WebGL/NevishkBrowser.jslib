// Browser helpers for the web build: deep links via the URL hash, and prefers-reduced-motion.
mergeInto(LibraryManager.library, {

  NevishkGetHash: function () {
    var h = (window.location.hash || "").replace(/^#/, "");
    try { h = decodeURIComponent(h); } catch (e) {}
    var size = lengthBytesUTF8(h) + 1;
    var buf = _malloc(size);
    stringToUTF8(h, buf, size);
    return buf;
  },

  NevishkSetHash: function (ptr, push) {
    var h = UTF8ToString(ptr);
    var url = window.location.pathname + window.location.search + (h ? "#" + encodeURIComponent(h) : "");
    if (push) window.history.pushState(null, "", url);
    else window.history.replaceState(null, "", url);
  },

  NevishkPrefersReducedMotion: function () {
    return (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) ? 1 : 0;
  },

  // Calls NevishkMap.OnBrowserHashChanged(hash) on the named GameObject when the user presses Back/Forward.
  NevishkListenHash: function (goPtr) {
    var go = UTF8ToString(goPtr);
    window.addEventListener("popstate", function () {
      var h = (window.location.hash || "").replace(/^#/, "");
      try { h = decodeURIComponent(h); } catch (e) {}
      // SendMessage is defined in the Unity framework scope that .jslib code runs in.
      if (typeof SendMessage === "function") SendMessage(go, "OnBrowserHashChanged", h);
      else if (typeof unityInstance !== "undefined" && unityInstance) unityInstance.SendMessage(go, "OnBrowserHashChanged", h);
    });
  }
});
