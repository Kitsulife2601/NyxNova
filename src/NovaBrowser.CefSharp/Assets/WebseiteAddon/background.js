const extensionApi = globalThis.browser || globalThis.chrome;
const runtimeApi = extensionApi?.runtime;
const tabsApi = extensionApi?.tabs;
const actionApi = extensionApi?.action || extensionApi?.browserAction;
const storageApi = extensionApi?.storage?.local;
const webNavigationApi = extensionApi?.webNavigation;
const translationSettingsStorageKey = "translation-settings";
const validTranslateLanguages = new Set(["de", "en", "fr", "es", "it", "nl", "pl", "tr", "ja", "ko", "zh-CN"]);
const translationDefaults = {
  enabled: false,
  targetLanguage: "de"
};

actionApi?.onClicked?.addListener(() => {
  tabsApi?.create({
    url: runtimeApi.getURL("popup.html")
  });
});

const normalizeTranslationSettings = (settings = {}) => ({
  enabled: Boolean(settings.enabled),
  targetLanguage: validTranslateLanguages.has(settings.targetLanguage)
    ? settings.targetLanguage
    : translationDefaults.targetLanguage
});

const readTranslationSettings = () => new Promise((resolve) => {
  if (!storageApi?.get) {
    resolve({ ...translationDefaults });
    return;
  }

  try {
    const request = storageApi.get(translationSettingsStorageKey, (result) => {
      resolve(normalizeTranslationSettings(result?.[translationSettingsStorageKey]));
    });
    if (request?.then) {
      request
        .then((result) => resolve(normalizeTranslationSettings(result?.[translationSettingsStorageKey])))
        .catch(() => resolve({ ...translationDefaults }));
    }
  } catch {
    resolve({ ...translationDefaults });
  }
});

const isLocalAddress = (hostname) => (
  hostname === "localhost"
  || hostname === "127.0.0.1"
  || hostname === "::1"
  || hostname.endsWith(".local")
);

const isGoogleTranslationBlockedHost = (hostname) => {
  const normalizedHost = hostname.toLowerCase();

  return normalizedHost === "translate.goog"
    || normalizedHost.endsWith(".translate.goog")
    || normalizedHost === "google.com"
    || normalizedHost.endsWith(".google.com")
    || /^(.+\.)?google\.[a-z.]+$/.test(normalizedHost);
};

const shouldTranslateUrl = (rawUrl) => {
  try {
    const url = new URL(rawUrl);

    if (!["http:", "https:"].includes(url.protocol)) {
      return false;
    }
    if (isLocalAddress(url.hostname)) {
      return false;
    }
    if (isGoogleTranslationBlockedHost(url.hostname)) {
      return false;
    }
    if (url.searchParams.has("novaNoTranslate")) {
      return false;
    }

    return true;
  } catch {
    return false;
  }
};

const translateUrl = (rawUrl, targetLanguage) => (
  `https://translate.google.com/translate?sl=auto&tl=${encodeURIComponent(targetLanguage)}&u=${encodeURIComponent(rawUrl)}`
);

const handleNavigationForTranslation = async (details) => {
  if (!tabsApi?.update || details.frameId !== 0 || !shouldTranslateUrl(details.url)) {
    return;
  }

  const settings = await readTranslationSettings();
  if (!settings.enabled) {
    return;
  }

  try {
    tabsApi.update(details.tabId, {
      url: translateUrl(details.url, settings.targetLanguage)
    });
  } catch {
    // Navigation may disappear before the translation redirect can be applied.
  }
};

webNavigationApi?.onCommitted?.addListener((details) => {
  handleNavigationForTranslation(details);
}, {
  url: [{ schemes: ["http", "https"] }]
});

runtimeApi?.onUpdateAvailable?.addListener(() => {
  runtimeApi.reload();
});
