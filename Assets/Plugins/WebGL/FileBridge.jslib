// Browser file download / upload / wheel handling for the Creature Generator.
// Auto-included by Unity for WebGL builds (Assets/Plugins/WebGL). Paired with
// Assets/Scripts/WebFileBridge.cs.
mergeInto(LibraryManager.library, {

  // Hand the given text to the browser as a file download.
  BMeshDownloadFile: function (namePtr, textPtr, mimePtr) {
    var name = UTF8ToString(namePtr);
    var mime = UTF8ToString(mimePtr) || 'application/octet-stream';
    try {
      var blob = new Blob([UTF8ToString(textPtr)], { type: mime });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url; a.download = name; a.style.display = 'none';
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { if (a.parentNode) { a.parentNode.removeChild(a); } URL.revokeObjectURL(url); }, 0);
    } catch (e) { console.error('BMeshDownloadFile failed', e); }
  },

  // Hand raw bytes (e.g. a .glb) to the browser as a file download.
  BMeshDownloadBytes: function (namePtr, dataPtr, length, mimePtr) {
    var name = UTF8ToString(namePtr);
    var mime = UTF8ToString(mimePtr) || 'application/octet-stream';
    try {
      // Copy out of the wasm heap -- the view can be detached before the Blob reads it.
      var bytes = new Uint8Array(HEAPU8.subarray(dataPtr, dataPtr + length));
      var blob = new Blob([bytes], { type: mime });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url; a.download = name; a.style.display = 'none';
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { if (a.parentNode) { a.parentNode.removeChild(a); } URL.revokeObjectURL(url); }, 0);
    } catch (e) { console.error('BMeshDownloadBytes failed', e); }
  },

  // Open the browser file picker; SendMessage(go, cb, <text>) once a file is
  // read (empty string if the user picked nothing).
  BMeshUploadFile: function (goPtr, cbPtr, acceptPtr) {
    var go = UTF8ToString(goPtr);
    var cb = UTF8ToString(cbPtr);
    var accept = UTF8ToString(acceptPtr);

    var input = document.createElement('input');
    input.type = 'file';
    if (accept) { input.accept = accept; }
    input.style.display = 'none';
    document.body.appendChild(input);

    var send = (typeof SendMessage !== 'undefined')
      ? SendMessage
      : (typeof Module !== 'undefined' && Module.SendMessage) ? Module.SendMessage : null;

    var finish = function (text) {
      if (input.parentNode) { input.parentNode.removeChild(input); }
      if (send) { send(go, cb, text || ''); }
      else { console.error('BMeshUploadFile: SendMessage unavailable'); }
    };

    input.addEventListener('change', function (evt) {
      var file = evt.target.files && evt.target.files[0];
      if (!file) { finish(''); return; }
      var reader = new FileReader();
      reader.onload = function () { finish(reader.result); };
      reader.onerror = function () { finish(''); };
      reader.readAsText(file);
    });

    input.click();
  },

  // Stop the browser page/canvas from scrolling when the wheel is used over the
  // Unity view (Unity still receives the wheel through its own listener).
  BMeshPreventCanvasScroll: function () {
    try {
      var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
      if (!canvas || canvas.__bmeshNoScroll) { return; }
      canvas.__bmeshNoScroll = true;
      canvas.addEventListener('wheel', function (e) { e.preventDefault(); }, { passive: false });
    } catch (e) {
      console.error('BMeshPreventCanvasScroll failed', e);
    }
  }
});
