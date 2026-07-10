mergeInto(LibraryManager.library, {
    BrowserPersistentStorage_HasKey: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            return localStorage.getItem('sdo_v1/' + key) !== null ? 1 : 0;
        } catch (e) {
            return 0;
        }
    },

    BrowserPersistentStorage_GetString: function (keyPtr, bufferPtr, bufferLen) {
        var key = UTF8ToString(keyPtr);
        var value = '';
        try {
            var stored = localStorage.getItem('sdo_v1/' + key);
            if (stored !== null) {
                value = stored;
            }
        } catch (e) {
        }
        stringToUTF8(value, bufferPtr, bufferLen);
    },

    BrowserPersistentStorage_SetString: function (keyPtr, valuePtr) {
        var key = UTF8ToString(keyPtr);
        var value = UTF8ToString(valuePtr);
        try {
            localStorage.setItem('sdo_v1/' + key, value);
        } catch (e) {
        }
    },

    BrowserPersistentStorage_RemoveKey: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            localStorage.removeItem('sdo_v1/' + key);
        } catch (e) {
        }
    }
});
