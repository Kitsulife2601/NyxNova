const form = document.querySelector("#searchForm");
const extensionApi = globalThis.browser || globalThis.chrome;
const input = document.querySelector("#searchInput");
const searchLetterLayer = document.querySelector("#searchLetterLayer");
const mainClock = document.querySelector("#mainClock");
const analogClock = document.querySelector("#analogClock");
const hourHand = document.querySelector(".hour-hand");
const minuteHand = document.querySelector(".minute-hand");
const secondHand = document.querySelector(".second-hand");
const clockModeButtons = [...document.querySelectorAll("[data-clock-mode]")];
const clockFontButtons = [...document.querySelectorAll("[data-clock-font]")];
const clockSettingsToggle = document.querySelector("#clockSettingsToggle");
const settingsPanel = document.querySelector("#settingsPanel");
const clockSettingsEntry = document.querySelector("#clockSettingsEntry");
const clockSettingsPanel = document.querySelector("#clockSettingsPanel");
const translationSettingsEntry = document.querySelector("#translationSettingsEntry");
const translationSettingsPanel = document.querySelector("#translationSettingsPanel");
const autoTranslateToggle = document.querySelector("#autoTranslateToggle");
const translateLanguageSelect = document.querySelector("#translateLanguageSelect");
const translationStatus = document.querySelector("#translationStatus");
const backgroundVideo = document.querySelector(".video-bg");
const backgroundVideoSources = [...document.querySelectorAll(".video-bg source")];
const backgroundImage = document.querySelector(".image-bg");
const backgroundUploadInput = document.querySelector("#backgroundUpload");
const openMenu = document.querySelector("#openMenu");
const closeMenu = document.querySelector("#closeMenu");
const menuOverlay = document.querySelector("#menuOverlay");
const heartSecret = document.querySelector("#heartSecret");
const heartPanel = document.querySelector("#heartPanel");
const discordIdInput = document.querySelector("#discordIdInput");
const saveDiscordId = document.querySelector("#saveDiscordId");
const closeHeartPanel = document.querySelector("#closeHeartPanel");
const heartRemoveConfirm = document.querySelector("#heartRemoveConfirm");
const confirmRemoveDiscordId = document.querySelector("#confirmRemoveDiscordId");
const keepDiscordId = document.querySelector("#keepDiscordId");
const showHeartPanelButton = document.querySelector("#showHeartPanel");
const heartStatus = document.querySelector("#heartStatus");
const heartRecipients = document.querySelector(".heart-recipients");
const sidebar = document.querySelector("#sidebar");
const sidebarMenu = document.querySelector(".sidebar-menu");
const sidebarLinks = [...document.querySelectorAll(".sidebar-menu a")];
const heartEndpoint = "http://127.0.0.1:8787/send-heart";
const verifyEndpoint = "http://127.0.0.1:8787/verify-id";
const backgroundDbName = "nova-start-background";
const backgroundStoreName = "backgrounds";
const backgroundRecordKey = "active-background";
const clockModeStorageKey = "clock-mode";
const clockFontStorageKey = "clock-font";
const translationSettingsStorageKey = "translation-settings";
const customBookmarksStorageKey = "custom-bookmarks";
const discordIdStorageKey = "heart-discord-id";
const discordNameStorageKey = "heart-discord-name";
const discordTargetNameStorageKey = "heart-discord-target-name";
const heartPanelClosedStorageKey = "heart-panel-closed";
const heartReopenOnTapStorageKey = "heart-reopen-on-tap";
const allowedDiscordIds = new Set([
  "1312104318006071328",
  "548009850328711181"
]);
let typingTimer;
let heartTimer;
let searchMessageTimer;
let customBackgroundObjectUrl = "";
let isSendingHeart = false;
let isCheckingHeart = false;

input.focus();

const extensionUrl = (path) => {
  if (extensionApi?.runtime?.getURL) {
    return extensionApi.runtime.getURL(path);
  }

  return path;
};

const setSearchMessage = (message) => {
  const defaultPlaceholder = "Was suchst du?";

  input.placeholder = message;
  clearTimeout(searchMessageTimer);
  searchMessageTimer = setTimeout(() => {
    input.placeholder = defaultPlaceholder;
  }, 2400);
};

const clearSearchInput = () => {
  input.value = "";
  form.classList.remove("has-text", "is-typing");
  renderSearchLetters();
};

const renderSearchLetters = () => {
  if (!searchLetterLayer) {
    return;
  }

  const letters = [...input.value].map((letter, index) => {
    const span = document.createElement("span");
    span.className = letter === " " ? "search-letter is-space" : "search-letter";
    span.style.setProperty("--letter-index", index);
    span.textContent = letter === " " ? "\u00a0" : letter;
    return span;
  });

  searchLetterLayer.replaceChildren(...letters);
};

const readCustomBookmarks = () => {
  try {
    const saved = JSON.parse(localStorage.getItem(customBookmarksStorageKey) || "[]");
    return Array.isArray(saved)
      ? saved.filter((bookmark) => bookmark?.url && bookmark?.title).slice(0, 20)
      : [];
  } catch {
    return [];
  }
};

const saveCustomBookmarks = (bookmarks) => {
  localStorage.setItem(customBookmarksStorageKey, JSON.stringify(bookmarks.slice(0, 20)));
};

const titleFromUrl = (url) => {
  const host = url.hostname.replace(/^www\./, "");
  const firstPart = host.split(".")[0] || host;
  return firstPart.charAt(0).toUpperCase() + firstPart.slice(1);
};

const faviconUrlFor = (url) => `${url.origin}/favicon.ico`;

const normalizeBookmarkUrl = (value) => {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  try {
    const url = new URL(/^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`);
    return ["http:", "https:"].includes(url.protocol) ? url : null;
  } catch {
    return null;
  }
};

const buildCustomBookmark = (bookmark) => {
  const link = document.createElement("a");
  link.href = bookmark.url;
  link.target = "_blank";
  link.rel = "noreferrer";
  link.className = "custom-bookmark";

  const icon = document.createElement("span");
  icon.className = "menu-icon custom-favicon";
  icon.setAttribute("aria-hidden", "true");

  const image = document.createElement("img");
  image.src = bookmark.icon;
  image.alt = "";
  image.loading = "lazy";
  image.addEventListener("error", () => {
    icon.textContent = bookmark.title.slice(0, 1).toUpperCase();
    icon.classList.add("fallback-icon");
  }, { once: true });
  icon.append(image);

  const copy = document.createElement("span");
  copy.className = "bookmark-copy";

  const title = document.createElement("span");
  title.textContent = bookmark.title;

  const subtitle = document.createElement("small");
  subtitle.textContent = new URL(bookmark.url).hostname.replace(/^www\./, "");

  copy.append(title, subtitle);
  link.append(icon, copy);

  return link;
};

const renderCustomBookmarks = () => {
  if (!sidebarMenu) {
    return;
  }

  sidebarMenu.querySelectorAll(".custom-bookmark").forEach((bookmark) => bookmark.remove());
  readCustomBookmarks().forEach((bookmark) => {
    sidebarMenu.append(buildCustomBookmark(bookmark));
  });
};

const addCustomBookmark = (rawValue) => {
  const parts = rawValue.trim().split(/\s+/);
  const url = normalizeBookmarkUrl(parts.shift() || "");

  if (!url) {
    setSearchMessage("Nutze: !lese https://seite.de");
    return false;
  }

  const title = parts.join(" ").trim() || titleFromUrl(url);
  const bookmark = {
    title: title.slice(0, 28),
    url: url.href,
    icon: faviconUrlFor(url)
  };
  const bookmarks = readCustomBookmarks().filter((saved) => saved.url !== bookmark.url);

  bookmarks.unshift(bookmark);
  saveCustomBookmarks(bookmarks);
  renderCustomBookmarks();
  setSearchMessage(`${bookmark.title} als Lesezeichen gespeichert.`);
  return true;
};

const backgroundFileType = (file) => {
  if (file.type) {
    return file.type;
  }

  const fileName = file.name.toLowerCase();
  if (fileName.endsWith(".png")) return "image/png";
  if (fileName.endsWith(".jpg") || fileName.endsWith(".jpeg")) return "image/jpeg";
  if (fileName.endsWith(".webp")) return "image/webp";
  if (fileName.endsWith(".gif")) return "image/gif";
  if (fileName.endsWith(".mp4")) return "video/mp4";
  if (fileName.endsWith(".webm")) return "video/webm";

  return "";
};

const isSupportedBackgroundType = (type) => type.startsWith("image/") || type.startsWith("video/");

const openBackgroundDatabase = () => new Promise((resolve, reject) => {
  if (!globalThis.indexedDB) {
    reject(new Error("Hintergrund-Speicher ist in diesem Browser nicht verfügbar."));
    return;
  }

  const request = indexedDB.open(backgroundDbName, 1);

  request.onupgradeneeded = () => {
    const database = request.result;
    if (!database.objectStoreNames.contains(backgroundStoreName)) {
      database.createObjectStore(backgroundStoreName, { keyPath: "id" });
    }
  };

  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error || new Error("Hintergrund-Speicher konnte nicht geöffnet werden."));
});

const useBackgroundStore = async (mode, action) => {
  const database = await openBackgroundDatabase();

  return new Promise((resolve, reject) => {
    const transaction = database.transaction(backgroundStoreName, mode);
    const store = transaction.objectStore(backgroundStoreName);
    const request = action(store);

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error || new Error("Hintergrund konnte nicht gespeichert werden."));
    transaction.oncomplete = () => database.close();
    transaction.onerror = () => reject(transaction.error || new Error("Hintergrund-Speicher ist fehlgeschlagen."));
  });
};

const getSavedBackground = () => useBackgroundStore("readonly", (store) => store.get(backgroundRecordKey));

const saveBackground = (file) => useBackgroundStore("readwrite", (store) => store.put({
  id: backgroundRecordKey,
  blob: file,
  name: file.name,
  type: backgroundFileType(file),
  updatedAt: Date.now()
}));

const deleteSavedBackground = () => useBackgroundStore("readwrite", (store) => store.delete(backgroundRecordKey));

const revokeCustomBackgroundUrl = () => {
  if (customBackgroundObjectUrl) {
    URL.revokeObjectURL(customBackgroundObjectUrl);
    customBackgroundObjectUrl = "";
  }
};

const playBackgroundVideo = () => {
  if (!backgroundVideo?.play) {
    return;
  }

  backgroundVideo.play()
    .then(() => {
      document.body.classList.add("video-ready");
      document.body.classList.remove("video-error");
    })
    .catch(() => {
      if (backgroundVideo.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
        document.body.classList.add("video-ready");
        document.body.classList.remove("video-error");
      }
    });
};

const applySavedBackground = (record) => {
  const blob = record?.blob;
  const type = record?.type || blob?.type || "";

  if (!blob || !type) {
    return false;
  }

  revokeCustomBackgroundUrl();
  customBackgroundObjectUrl = URL.createObjectURL(blob);
  document.body.classList.add("custom-background");
  document.body.classList.toggle("custom-image", type.startsWith("image/"));
  document.body.classList.toggle("custom-video", type.startsWith("video/"));
  document.body.classList.remove("video-error");

  if (type.startsWith("image/")) {
    backgroundVideo?.pause?.();
    if (backgroundVideo) {
      backgroundVideo.hidden = true;
    }
    if (backgroundImage) {
      backgroundImage.src = customBackgroundObjectUrl;
      backgroundImage.hidden = false;
    }
    return true;
  }

  if (type.startsWith("video/")) {
    if (backgroundImage) {
      backgroundImage.hidden = true;
      backgroundImage.removeAttribute("src");
    }
    if (backgroundVideo) {
      backgroundVideo.hidden = false;
      backgroundVideo.src = customBackgroundObjectUrl;
      backgroundVideo.load();
      backgroundVideo.addEventListener("loadeddata", () => {
        document.body.classList.add("video-ready");
      }, { once: true });
      playBackgroundVideo();
    }
    return true;
  }

  return false;
};

const resetBackground = async () => {
  await deleteSavedBackground();
  revokeCustomBackgroundUrl();
  document.body.classList.remove("custom-background", "custom-image", "custom-video", "video-error");

  if (backgroundImage) {
    backgroundImage.hidden = true;
    backgroundImage.removeAttribute("src");
  }

  if (backgroundVideo) {
    backgroundVideo.hidden = false;
    backgroundVideo.pause?.();
    backgroundVideo.removeAttribute("src");
    backgroundVideo.load();
    playBackgroundVideo();
  }
};

const restoreSavedBackground = async () => {
  try {
    const record = await getSavedBackground();
    if (record) {
      applySavedBackground(record);
    }
  } catch {
    setSearchMessage("Eigener Hintergrund konnte nicht geladen werden.");
  }
};

const validClockModes = new Set(["digital", "analog"]);
const validClockFonts = new Set(["neon", "clean", "mono", "elegant"]);
const validTranslateLanguages = new Set(["de", "en", "fr", "es", "it", "nl", "pl", "tr", "ja", "ko", "zh-CN"]);
const translationDefaults = {
  enabled: false,
  targetLanguage: "de"
};
const translationHostPermission = {
  origins: ["http://*/*", "https://*/*"]
};

const savedClockMode = () => {
  const mode = localStorage.getItem(clockModeStorageKey) || "digital";
  return validClockModes.has(mode) ? mode : "digital";
};

const savedClockFont = () => {
  const font = localStorage.getItem(clockFontStorageKey) || "neon";
  return validClockFonts.has(font) ? font : "neon";
};

const setSelectedButton = (buttons, value, dataName) => {
  buttons.forEach((button) => {
    const isSelected = button.dataset[dataName] === value;
    button.classList.toggle("is-selected", isSelected);
    button.setAttribute("aria-pressed", String(isSelected));
  });
};

const applyClockMode = (mode, shouldSave = false) => {
  const nextMode = validClockModes.has(mode) ? mode : "digital";

  mainClock.hidden = nextMode === "analog";
  analogClock.hidden = nextMode !== "analog";
  document.body.dataset.clockMode = nextMode;
  setSelectedButton(clockModeButtons, nextMode, "clockMode");

  if (shouldSave) {
    localStorage.setItem(clockModeStorageKey, nextMode);
  }
};

const applyClockFont = (font, shouldSave = false) => {
  const nextFont = validClockFonts.has(font) ? font : "neon";

  document.body.dataset.clockFont = nextFont;
  setSelectedButton(clockFontButtons, nextFont, "clockFont");

  if (shouldSave) {
    localStorage.setItem(clockFontStorageKey, nextFont);
  }
};

const normalizeTranslationSettings = (settings = {}) => ({
  enabled: Boolean(settings.enabled),
  targetLanguage: validTranslateLanguages.has(settings.targetLanguage)
    ? settings.targetLanguage
    : translationDefaults.targetLanguage
});

const readTranslationSettings = async () => {
  const storage = extensionApi?.storage?.local;

  if (storage?.get) {
    try {
      const result = await new Promise((resolve) => {
        const request = storage.get(translationSettingsStorageKey, resolve);
        if (request?.then) {
          request.then(resolve).catch(() => resolve({}));
        }
      });
      return normalizeTranslationSettings(result?.[translationSettingsStorageKey]);
    } catch {
      // Fall back to localStorage when the extension storage API is not available.
    }
  }

  try {
    return normalizeTranslationSettings(JSON.parse(localStorage.getItem(translationSettingsStorageKey) || "{}"));
  } catch {
    return { ...translationDefaults };
  }
};

const saveTranslationSettings = async (settings) => {
  const nextSettings = normalizeTranslationSettings(settings);
  localStorage.setItem(translationSettingsStorageKey, JSON.stringify(nextSettings));

  const storage = extensionApi?.storage?.local;
  if (storage?.set) {
    await new Promise((resolve) => {
      const request = storage.set({ [translationSettingsStorageKey]: nextSettings }, resolve);
      if (request?.then) {
        request.then(resolve).catch(resolve);
      }
    });
  }

  return nextSettings;
};

const hasTranslationHostPermission = () => new Promise((resolve) => {
  const permissionsApi = extensionApi?.permissions;

  if (!permissionsApi?.contains) {
    resolve(true);
    return;
  }

  try {
    const request = permissionsApi.contains(translationHostPermission, resolve);
    if (request?.then) {
      request.then(resolve).catch(() => resolve(false));
    }
  } catch {
    resolve(false);
  }
});

const requestTranslationHostPermission = () => new Promise((resolve) => {
  const permissionsApi = extensionApi?.permissions;

  if (!permissionsApi?.request) {
    resolve(true);
    return;
  }

  try {
    const request = permissionsApi.request(translationHostPermission, resolve);
    if (request?.then) {
      request.then(resolve).catch(() => resolve(false));
    }
  } catch {
    resolve(false);
  }
});

const applyTranslationSettings = (settings) => {
  const nextSettings = normalizeTranslationSettings(settings);

  if (autoTranslateToggle) {
    autoTranslateToggle.checked = nextSettings.enabled;
  }
  if (translateLanguageSelect) {
    translateLanguageSelect.value = nextSettings.targetLanguage;
  }

  const languageLabel = translateLanguageSelect
    ? translateLanguageSelect.options[translateLanguageSelect.selectedIndex]?.text || "Deutsch"
    : "Deutsch";

  if (translationStatus) {
    translationStatus.textContent = nextSettings.enabled
      ? `An. Neue Webseiten öffnen übersetzt in ${languageLabel}.`
      : "Aus. Webseiten öffnen normal.";
  }
};

const syncTranslationSettings = async () => {
  const settings = await readTranslationSettings();

  if (settings.enabled && !(await hasTranslationHostPermission())) {
    const disabledSettings = await saveTranslationSettings({
      ...settings,
      enabled: false
    });
    applyTranslationSettings(disabledSettings);
    if (translationStatus) {
      translationStatus.textContent = "Aus. Bitte Übersetzung neu aktivieren und die Webseiten-Berechtigung erlauben.";
    }
    return;
  }

  applyTranslationSettings(settings);
};

const updateTranslationSettings = async () => {
  const shouldEnable = Boolean(autoTranslateToggle?.checked);

  if (shouldEnable && !(await hasTranslationHostPermission())) {
    const isAllowed = await requestTranslationHostPermission();

    if (!isAllowed) {
      const disabledSettings = await saveTranslationSettings({
        enabled: false,
        targetLanguage: translateLanguageSelect?.value || translationDefaults.targetLanguage
      });
      applyTranslationSettings(disabledSettings);
      if (translationStatus) {
        translationStatus.textContent = "Berechtigung abgelehnt. Übersetzung bleibt aus.";
      }
      return;
    }
  }

  const nextSettings = await saveTranslationSettings({
    enabled: shouldEnable,
    targetLanguage: translateLanguageSelect?.value || translationDefaults.targetLanguage
  });
  applyTranslationSettings(nextSettings);
};

const setClockSettingsOpen = (isOpen) => {
  if (!clockSettingsPanel) {
    return;
  }

  clockSettingsPanel.hidden = !isOpen;
  clockSettingsEntry?.classList.toggle("is-active", isOpen);
  clockSettingsEntry?.setAttribute("aria-expanded", String(isOpen));
};

const setTranslationSettingsOpen = (isOpen) => {
  if (!translationSettingsPanel) {
    return;
  }

  translationSettingsPanel.hidden = !isOpen;
  translationSettingsEntry?.classList.toggle("is-active", isOpen);
  translationSettingsEntry?.setAttribute("aria-expanded", String(isOpen));
};

const setSettingsPanelOpen = (isOpen) => {
  if (!clockSettingsToggle || !settingsPanel) {
    return;
  }

  settingsPanel.hidden = !isOpen;
  clockSettingsToggle.classList.toggle("is-active", isOpen);
  clockSettingsToggle.setAttribute("aria-expanded", String(isOpen));

  if (!isOpen) {
    setClockSettingsOpen(false);
    setTranslationSettingsOpen(false);
  }
};

const updateAnalogClock = (date) => {
  const seconds = date.getSeconds();
  const minutes = date.getMinutes();
  const hours = date.getHours() % 12;

  hourHand?.style.setProperty("--rotation", `${((hours + minutes / 60) * 30).toFixed(2)}deg`);
  minuteHand?.style.setProperty("--rotation", `${((minutes + seconds / 60) * 6).toFixed(2)}deg`);
  secondHand?.style.setProperty("--rotation", `${seconds * 6}deg`);
};

const prepareBackgroundVideo = () => {
  if (!backgroundVideo) {
    return;
  }

  backgroundVideo.muted = true;
  backgroundVideo.defaultMuted = true;
  backgroundVideo.loop = true;
  backgroundVideo.playsInline = true;
  backgroundVideo.setAttribute("muted", "");
  backgroundVideo.setAttribute("playsinline", "");

  let sourceChanged = false;
  backgroundVideoSources.forEach((source) => {
    const sourcePath = source.getAttribute("src");
    if (!sourcePath || sourcePath.startsWith("http") || sourcePath.startsWith("moz-extension:") || sourcePath.startsWith("chrome-extension:")) {
      return;
    }

    source.src = extensionUrl(sourcePath);
    sourceChanged = true;
  });

  if (sourceChanged) {
    backgroundVideo.load();
  }

  const markVideoReady = () => {
    document.body.classList.add("video-ready");
    document.body.classList.remove("video-error");
  };

  const markVideoError = () => {
    document.body.classList.add("video-error");
  };

  backgroundVideo.addEventListener("loadeddata", markVideoReady, { once: true });
  backgroundVideo.addEventListener("canplay", playBackgroundVideo, { once: true });
  backgroundVideo.addEventListener("error", markVideoError);
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) {
      playBackgroundVideo();
    }
  });
  window.addEventListener("pageshow", playBackgroundVideo);
  document.addEventListener("pointerdown", playBackgroundVideo, { once: true });
  document.addEventListener("keydown", playBackgroundVideo, { once: true });

  playBackgroundVideo();
};

prepareBackgroundVideo();
restoreSavedBackground();
applyClockMode(savedClockMode());
applyClockFont(savedClockFont());
syncTranslationSettings();
renderCustomBookmarks();

const updateClock = () => {
  const now = new Date();
  const fullTime = now.toLocaleTimeString("de-DE", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });

  mainClock.textContent = fullTime;
  mainClock.dateTime = now.toTimeString().slice(0, 8);
  updateAnalogClock(now);
};

updateClock();
setInterval(updateClock, 1000);

const openUrl = (url) => {
  if (extensionApi?.tabs?.create) {
    extensionApi.tabs.create({ url });
    return;
  }

  window.location.href = url;
};

const setMenuOpen = (isOpen) => {
  document.body.classList.toggle("menu-open", isOpen);
  openMenu.setAttribute("aria-expanded", String(isOpen));
  sidebar.setAttribute("aria-hidden", String(!isOpen));

  if (isOpen) {
    closeMenu.focus();
    return;
  }

  setSettingsPanelOpen(false);
  openMenu.focus();
};

openMenu.addEventListener("click", () => setMenuOpen(true));
closeMenu.addEventListener("click", () => setMenuOpen(false));
menuOverlay.addEventListener("click", () => setMenuOpen(false));

sidebarLinks.forEach((link) => {
  link.addEventListener("click", () => setMenuOpen(false));
});

sidebarMenu?.addEventListener("click", (event) => {
  if (event.target.closest("a")) {
    setMenuOpen(false);
  }
});

clockSettingsToggle?.addEventListener("click", () => {
  setSettingsPanelOpen(settingsPanel?.hidden ?? true);
});

clockSettingsEntry?.addEventListener("click", () => {
  const shouldOpen = clockSettingsPanel?.hidden ?? true;
  setClockSettingsOpen(shouldOpen);
  if (shouldOpen) {
    setTranslationSettingsOpen(false);
  }
});

translationSettingsEntry?.addEventListener("click", () => {
  const shouldOpen = translationSettingsPanel?.hidden ?? true;
  setTranslationSettingsOpen(shouldOpen);
  if (shouldOpen) {
    setClockSettingsOpen(false);
  }
});

autoTranslateToggle?.addEventListener("change", updateTranslationSettings);
translateLanguageSelect?.addEventListener("change", updateTranslationSettings);

clockModeButtons.forEach((button) => {
  button.addEventListener("click", () => {
    applyClockMode(button.dataset.clockMode, true);
  });
});

clockFontButtons.forEach((button) => {
  button.addEventListener("click", () => {
    applyClockFont(button.dataset.clockFont, true);
  });
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && document.body.classList.contains("menu-open")) {
    setMenuOpen(false);
  }
});

input.addEventListener("input", () => {
  form.classList.toggle("has-text", input.value.trim().length > 0);
  form.classList.add("is-typing");
  renderSearchLetters();
  clearTimeout(typingTimer);
  typingTimer = setTimeout(() => form.classList.remove("is-typing"), 700);
});

const savedDiscordId = () => localStorage.getItem(discordIdStorageKey) || "";

const savedDiscordName = () => localStorage.getItem(discordNameStorageKey) || "";

const savedTargetName = () => localStorage.getItem(discordTargetNameStorageKey) || "";

const isDiscordId = (value) => /^\d{17,20}$/.test(value.trim());

const hasHeartAccess = (value = savedDiscordId()) => allowedDiscordIds.has(value.trim());

const hasVerifiedHeartAccess = () => hasHeartAccess();

const isHeartPanelOpen = () => localStorage.getItem(heartPanelClosedStorageKey) === "0";

const setHeartRemoveConfirmOpen = (isOpen) => {
  if (heartRemoveConfirm) {
    heartRemoveConfirm.hidden = !isOpen;
  }
};

const setHeartPanelHidden = (isHidden) => {
  heartPanel.hidden = isHidden;
  if (isHidden) {
    setHeartRemoveConfirmOpen(false);
  }
  if (showHeartPanelButton) {
    showHeartPanelButton.hidden = !isHidden || hasVerifiedHeartAccess();
  }
};

const setHeartStatus = (message, type = "") => {
  heartStatus.textContent = message;
  heartStatus.classList.toggle("is-ok", type === "ok");
  heartStatus.classList.toggle("is-error", type === "error");
};

const setHeartRoute = (senderName = savedDiscordName(), targetName = savedTargetName()) => {
  heartRecipients.textContent = senderName && targetName
    ? `${senderName} -> ${targetName}`
    : "Gespeichert. Herz sendet direkt.";
};

const showHeartPanel = (message = "Speichere deine ID, dann erscheint das Herz.", type = "") => {
  localStorage.setItem(heartPanelClosedStorageKey, "0");
  localStorage.removeItem(heartReopenOnTapStorageKey);
  setHeartRemoveConfirmOpen(false);
  setHeartPanelHidden(false);
  setHeartStatus(message, type);
  setHeartRoute();
  setMenuOpen(true);
  requestAnimationFrame(() => discordIdInput.focus());
};

const hideHeartPanel = () => {
  localStorage.setItem(heartPanelClosedStorageKey, "1");
  setHeartRemoveConfirmOpen(false);
  setHeartPanelHidden(true);
};

const requestCloseHeartPanel = () => {
  if (!savedDiscordId()) {
    hideHeartPanel();
    return;
  }

  setHeartRemoveConfirmOpen(true);
  setHeartStatus("Möchtest du die gespeicherte Discord-ID entfernen?");
  requestAnimationFrame(() => keepDiscordId?.focus());
};

const syncHeartAccess = () => {
  const discordId = savedDiscordId();
  const canSeeHeart = hasVerifiedHeartAccess();

  heartSecret.hidden = !canSeeHeart;
  discordIdInput.value = discordId;
  setHeartRoute();

  if (canSeeHeart) {
    setHeartPanelHidden(true);
    setHeartStatus(savedDiscordName() && savedTargetName()
      ? `${savedDiscordName()} ist geprüft. Ein Klick sendet an ${savedTargetName()}.`
      : "ID gespeichert. Ein Klick aufs Herz sendet direkt.",
      "ok"
    );
    return;
  }

  setHeartStatus(hasHeartAccess(discordId)
    ? "Bitte ID einmal prüfen und speichern."
    : "Speichere deine ID, dann erscheint das Herz."
  );
  setHeartPanelHidden(!isHeartPanelOpen());
};

const markHeart = () => {
  heartSecret.classList.add("is-active");
  clearTimeout(heartTimer);
  heartTimer = setTimeout(() => heartSecret.classList.remove("is-active"), 1800);
};

const sendHeartMessage = async () => {
  if (isSendingHeart) {
    return;
  }

  const senderId = savedDiscordId();

  if (!hasVerifiedHeartAccess(senderId)) {
    showHeartPanel("Bitte zuerst deine Discord-ID prüfen und speichern.", "error");
    heartSecret.hidden = true;
    return;
  }

  isSendingHeart = true;
  setHeartPanelHidden(true);
  markHeart();
  setHeartStatus("Nachricht wird gesendet...");

  const controller = new AbortController();
  const requestTimeout = setTimeout(() => controller.abort(), 3000);

  try {
    const response = await fetch(heartEndpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ senderId }),
      signal: controller.signal
    });

    const data = await response.json().catch(() => ({}));

    if (!response.ok || !data.ok) {
      throw new Error(data.error || "Bot-Server nicht erreichbar.");
    }

    const recipientName = data.recipient?.name || savedTargetName() || "die andere Person";
    if (data.sender?.name) {
      localStorage.setItem(discordNameStorageKey, data.sender.name);
    }
    if (data.recipient?.name) {
      localStorage.setItem(discordTargetNameStorageKey, data.recipient.name);
    }
    setHeartRoute();
    setHeartStatus(`Gesendet an ${recipientName}: ${data.message || "Herz-Nachricht"}`, "ok");
  } catch (error) {
    const message = error.name === "AbortError" || error.message === "Failed to fetch"
      ? "Bot-Server nicht erreichbar."
      : error.message;
    setHeartPanelHidden(false);
    setMenuOpen(true);
    setHeartStatus(`${message} Starte den Heart Bot auf deinem PC.`, "error");
  } finally {
    clearTimeout(requestTimeout);
    isSendingHeart = false;
  }
};

const clearSavedHeartAccess = ({ clearInput = false } = {}) => {
  localStorage.removeItem(discordIdStorageKey);
  localStorage.removeItem(discordNameStorageKey);
  localStorage.removeItem(discordTargetNameStorageKey);
  localStorage.removeItem(heartReopenOnTapStorageKey);
  heartSecret.hidden = true;
  if (clearInput) {
    discordIdInput.value = "";
  }
  setHeartRoute("", "");
};

const removeSavedHeartAccessAndClose = () => {
  clearSavedHeartAccess({ clearInput: true });
  setHeartRemoveConfirmOpen(false);
  setHeartStatus("Discord-ID wurde entfernt.", "ok");
  hideHeartPanel();
  setMenuOpen(false);
};

const keepSavedHeartAccessAndClose = () => {
  setHeartRemoveConfirmOpen(false);
  localStorage.setItem(heartReopenOnTapStorageKey, "1");
  hideHeartPanel();
  setMenuOpen(false);
};

const shouldOpenHeartPanelFromHeart = () => localStorage.getItem(heartReopenOnTapStorageKey) === "1";

const saveHeartAccess = async () => {
  if (isCheckingHeart) {
    return;
  }

  const discordId = discordIdInput.value.trim();

  if (!isDiscordId(discordId)) {
    clearSavedHeartAccess();
    setHeartStatus("Bitte eine echte Discord-ID mit 17 bis 20 Zahlen eingeben.", "error");
    discordIdInput.focus();
    return;
  }

  if (!hasHeartAccess(discordId)) {
    clearSavedHeartAccess();
    setHeartStatus("Diese ID darf die Nachricht nicht senden.", "error");
    discordIdInput.focus();
    return;
  }

  isCheckingHeart = true;
  setHeartStatus("Discord-ID wird geprüft...");

  const controller = new AbortController();
  const requestTimeout = setTimeout(() => controller.abort(), 3000);

  try {
    const response = await fetch(verifyEndpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ senderId: discordId }),
      signal: controller.signal
    });
    const data = await response.json().catch(() => ({}));

    if (!response.ok || !data.ok) {
      throw new Error(data.error || "ID konnte nicht geprüft werden.");
    }

    localStorage.setItem(discordIdStorageKey, data.sender.id);
    localStorage.setItem(discordNameStorageKey, data.sender.name);
    localStorage.setItem(discordTargetNameStorageKey, data.recipient.name);
    discordIdInput.value = data.sender.id;
    heartSecret.hidden = false;
    setHeartRoute(data.sender.name, data.recipient.name);
    setHeartStatus(`${data.sender.name} geprüft. Herz sendet an ${data.recipient.name}.`, "ok");
    localStorage.setItem(heartPanelClosedStorageKey, "1");
    setHeartRemoveConfirmOpen(false);
    setHeartPanelHidden(true);
  } catch (error) {
    clearSavedHeartAccess();
    const message = error.name === "AbortError" || error.message === "Failed to fetch"
      ? "Bot-Server nicht erreichbar."
      : error.message;
    setHeartStatus(`${message} Starte den Heart Bot auf deinem PC.`, "error");
    discordIdInput.focus();
  } finally {
    clearTimeout(requestTimeout);
    isCheckingHeart = false;
  }
};

heartSecret.addEventListener("click", () => {
  if (shouldOpenHeartPanelFromHeart()) {
    showHeartPanel("ID bleibt gespeichert. Herz sendet direkt, wenn du wieder tippst.", "ok");
    return;
  }

  sendHeartMessage();
});

saveDiscordId.addEventListener("click", saveHeartAccess);
closeHeartPanel.addEventListener("click", requestCloseHeartPanel);
confirmRemoveDiscordId?.addEventListener("click", removeSavedHeartAccessAndClose);
keepDiscordId?.addEventListener("click", keepSavedHeartAccessAndClose);
showHeartPanelButton?.addEventListener("click", () => {
  showHeartPanel(hasVerifiedHeartAccess()
    ? "ID gespeichert. Ein Klick aufs Herz sendet direkt."
    : "Speichere deine ID, dann erscheint das Herz.",
    hasVerifiedHeartAccess() ? "ok" : ""
  );
});

discordIdInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    saveHeartAccess();
  }
});

discordIdInput.addEventListener("input", () => {
  setHeartRemoveConfirmOpen(false);
});

backgroundUploadInput?.addEventListener("change", async () => {
  const file = backgroundUploadInput.files?.[0];

  if (!file) {
    return;
  }

  const fileType = backgroundFileType(file);

  if (!isSupportedBackgroundType(fileType)) {
    setSearchMessage("Bitte ein Bild oder Video wählen.");
    backgroundUploadInput.value = "";
    return;
  }

  try {
    await saveBackground(file);
    applySavedBackground({
      blob: file,
      name: file.name,
      type: fileType
    });
    setSearchMessage("Hintergrund gespeichert.");
  } catch {
    setSearchMessage("Hintergrund konnte nicht gespeichert werden.");
  } finally {
    backgroundUploadInput.value = "";
  }
});

const runSearchCommand = () => {
  const query = input.value.trim();
  const command = query.toLowerCase().replace(/\s+/g, " ");

  if (!query) {
    input.focus();
    return;
  }

  if (/^!(background|backround|hintergrund|bg)( upload)?$/.test(command)) {
    clearSearchInput();
    backgroundUploadInput?.click();
    setSearchMessage("Wähle Bild oder Video.");
    return;
  }

  if (/^!(background|backround|hintergrund|bg) reset$/.test(command)) {
    clearSearchInput();
    resetBackground()
      .then(() => setSearchMessage("Hintergrund zurückgesetzt."))
      .catch(() => setSearchMessage("Hintergrund konnte nicht zurückgesetzt werden."));
    return;
  }

  const bookmarkCommand = query.match(/^!(lese|lesezeichen|bookmark)\s+(.+)$/i);
  if (bookmarkCommand) {
    clearSearchInput();
    addCustomBookmark(bookmarkCommand[2]);
    return;
  }

  openUrl(`https://www.google.com/search?q=${encodeURIComponent(query)}`);
};

syncHeartAccess();

input.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    runSearchCommand();
  }
});

form.addEventListener("submit", (event) => {
  event.preventDefault();
  runSearchCommand();
});
