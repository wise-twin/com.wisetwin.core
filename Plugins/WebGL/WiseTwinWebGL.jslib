mergeInto(LibraryManager.library, {
    // === Host-provided metadata URL (package 1.10.0) ===
    // The HOST PAGE decides where the training metadata lives — the build no
    // longer needs a baked containerId / apiBaseUrl. Before loading Unity, the
    // host sets:
    //   window.wisetwinHost = { metadataUrl: "/api/unity/metadata?..." }   (SaaS player / SCORM embed)
    //   window.wisetwinHost = { metadataUrl: "./metadata.json", mode: "scorm" } (standalone SCORM package)
    // Relative URLs are resolved here against the page (UnityWebRequest needs
    // an absolute URL). Returns "" when the host says nothing → MetadataLoader
    // falls back to its legacy local / production modes.
    GetHostMetadataUrl: function () {
        var url = "";
        try {
            var host = window.wisetwinHost;
            if (host && typeof host.metadataUrl === "string" && host.metadataUrl) {
                url = new URL(host.metadataUrl, document.baseURI).href;
            }
        } catch (e) {
            console.warn('[WiseTwin] Invalid window.wisetwinHost.metadataUrl', e);
            url = "";
        }
        var size = lengthBytesUTF8(url) + 1;
        var buffer = _malloc(size);
        stringToUTF8(url, buffer, size);
        return buffer;
    },

    // === VERSION SIMPLIFIÉE - Une seule méthode de communication ===

    SendTrainingCompleted: function(jsonPtr) {
        var jsonData = UTF8ToString(jsonPtr);

        try {
            // Utiliser uniquement la méthode officielle react-unity-webgl
            if (typeof window.dispatchReactUnityEvent === 'function') {
                // Envoyer l'événement avec toutes les données
                window.dispatchReactUnityEvent("TrainingCompleted", jsonData);
                console.log('[WiseTwin] Training completion sent successfully');
            } else {
                console.error('[WiseTwin] dispatchReactUnityEvent not available - ensure react-unity-webgl is properly initialized');
                console.log('[WiseTwin] Training data that would have been sent:', JSON.parse(jsonData));
            }
        } catch (error) {
            console.error('[WiseTwin] Failed to send training completion:', error);
            console.log('[WiseTwin] Raw JSON:', jsonData);
        }
    }
});