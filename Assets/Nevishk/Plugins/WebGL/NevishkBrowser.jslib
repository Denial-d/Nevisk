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

  NevishkDownload: function (namePtr, textPtr) {
    var name = UTF8ToString(namePtr), text = UTF8ToString(textPtr);
    var blob = new Blob([text], { type: "application/json" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob); a.download = name;
    document.body.appendChild(a); a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  },

  // Opens the browser's file picker; sends the chosen file's text to gameObject.method.
  NevishkOpenFile: function (goPtr, methodPtr) {
    var go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
    var input = document.createElement("input");
    input.type = "file"; input.accept = ".json,application/json";
    input.onchange = function () {
      var f = input.files && input.files[0];
      if (!f) return;
      var r = new FileReader();
      r.onload = function () {
        if (typeof SendMessage === "function") SendMessage(go, method, String(r.result));
        else if (typeof unityInstance !== "undefined" && unityInstance) unityInstance.SendMessage(go, method, String(r.result));
      };
      r.readAsText(f);
    };
    input.click();
  },

  NevishkCopyLink: function (hashPtr) {
    var hash = UTF8ToString(hashPtr);
    var url = window.location.origin + window.location.pathname + window.location.search + "#" + hash;
    function fallback() { window.prompt("Скопируйте ссылку на сюжет:", url); }
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(url).catch(fallback);
    else fallback();
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
