(function () {
  const storagePrefix = "nova-addon-storage:";

  const readAllStorage = () => {
    const result = {};
    for (let index = 0; index < localStorage.length; index += 1) {
      const key = localStorage.key(index);
      if (key && key.startsWith(storagePrefix)) {
        const publicKey = key.slice(storagePrefix.length);
        try {
          result[publicKey] = JSON.parse(localStorage.getItem(key) || "null");
        } catch {
          result[publicKey] = localStorage.getItem(key);
        }
      }
    }
    return result;
  };

  const storageLocal = {
    get(keys, callback) {
      const allValues = readAllStorage();
      let result = {};

      if (typeof keys === "string") {
        result[keys] = allValues[keys];
      } else if (Array.isArray(keys)) {
        keys.forEach((key) => {
          result[key] = allValues[key];
        });
      } else if (keys && typeof keys === "object") {
        result = { ...keys };
        Object.keys(keys).forEach((key) => {
          if (Object.prototype.hasOwnProperty.call(allValues, key)) {
            result[key] = allValues[key];
          }
        });
      } else {
        result = allValues;
      }

      callback?.(result);
      return Promise.resolve(result);
    },
    set(values, callback) {
      Object.entries(values || {}).forEach(([key, value]) => {
        localStorage.setItem(`${storagePrefix}${key}`, JSON.stringify(value));
      });
      callback?.();
      return Promise.resolve();
    },
    remove(keys, callback) {
      const normalized = Array.isArray(keys) ? keys : [keys];
      normalized.filter(Boolean).forEach((key) => localStorage.removeItem(`${storagePrefix}${key}`));
      callback?.();
      return Promise.resolve();
    }
  };

  const extensionApi = {
    runtime: {
      getURL(path) {
        return new URL(path, window.location.href).href;
      },
      reload() {
        window.location.reload();
      },
      onUpdateAvailable: {
        addListener() {
          return undefined;
        }
      }
    },
    tabs: {
      create({ url } = {}) {
        if (url) {
          window.location.href = url;
        }
      },
      update(_tabId, { url } = {}) {
        if (url) {
          window.location.href = url;
        }
      }
    },
    action: {
      onClicked: {
        addListener() {
          return undefined;
        }
      }
    },
    storage: {
      local: storageLocal
    },
    permissions: {
      contains(_permission, callback) {
        callback?.(true);
        return Promise.resolve(true);
      },
      request(_permission, callback) {
        callback?.(true);
        return Promise.resolve(true);
      }
    },
    webNavigation: {
      onCommitted: {
        addListener() {
          return undefined;
        }
      }
    }
  };

  window.chrome = window.chrome || {};
  window.browser = window.browser || {};
  window.chrome.runtime = window.chrome.runtime || extensionApi.runtime;
  window.chrome.tabs = window.chrome.tabs || extensionApi.tabs;
  window.chrome.action = window.chrome.action || extensionApi.action;
  window.chrome.storage = window.chrome.storage || extensionApi.storage;
  window.chrome.permissions = window.chrome.permissions || extensionApi.permissions;
  window.chrome.webNavigation = window.chrome.webNavigation || extensionApi.webNavigation;
  window.browser.runtime = window.browser.runtime || extensionApi.runtime;
  window.browser.tabs = window.browser.tabs || extensionApi.tabs;
  window.browser.action = window.browser.action || extensionApi.action;
  window.browser.storage = window.browser.storage || extensionApi.storage;
  window.browser.permissions = window.browser.permissions || extensionApi.permissions;
  window.browser.webNavigation = window.browser.webNavigation || extensionApi.webNavigation;
  document.documentElement.dataset.novaBridge = "ready";
})();
