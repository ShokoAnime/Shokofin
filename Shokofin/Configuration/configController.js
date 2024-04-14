const PluginConfig = {
    pluginId: "5216ccbf-d24a-4eb3-8a7e-7da4230b7052"
};

const Messages = {
    ConnectToShoko: "Please establish a connection to a running instance of Shoko Server before you continue.",
    InvalidCredentials: "An error occurred while trying to authenticating the user using the provided credentials.",
    UnableToRender: "There was an error loading the page, please refresh once to see if that will fix it.",
};

/**
 * Filter out duplicate values and sanitize list.
 * @param {string} value - Stringified list of values to filter.
 * @returns {string[]} An array of sanitized and filtered values.
 */
function filterIgnoredFolders(value) {
    // We convert to a set to filter out duplicate values.
    const filteredSet = new Set(
        value
            // Split the values at every comma.
            .split(",")
            // Sanitize inputs.
            .map(str =>  str.trim().toLowerCase())
            .filter(str => str),
    );

    // Convert it back into an array.
    return Array.from(filteredSet);
}

/**
 * Filter out duplicate values and sanitize list.
 * @param {string} value - Stringified list of values to filter.
 * @returns {number[]} An array of sanitized and filtered values.
 */
function filterReconnectIntervals(value) {
    // We convert to a set to filter out duplicate values.
    const filteredSet = new Set(
        value
            // Split the values at every comma.
            .split(",")
            // Sanitize inputs.
            .map(str => parseInt(str.trim().toLowerCase(), 10))
            .filter(int => !Number.isNaN(int)),
    );

    // Convert it back into an array.
    return Array.from(filteredSet).sort((a, b) => a - b);
 }

function adjustSortableListElement(element) {
    const btnSortable = element.querySelector(".btnSortable");
    const inner = btnSortable.querySelector(".material-icons");

    if (element.previousElementSibling) {
        btnSortable.title = "Up";
        btnSortable.classList.add("btnSortableMoveUp");
        inner.classList.add("keyboard_arrow_up");

        btnSortable.classList.remove("btnSortableMoveDown");
        inner.classList.remove("keyboard_arrow_down");
    }
    else {
        btnSortable.title = "Down";
        btnSortable.classList.add("btnSortableMoveDown");
        inner.classList.add("keyboard_arrow_down");

        btnSortable.classList.remove("btnSortableMoveUp");
        inner.classList.remove("keyboard_arrow_up");
    }
}

/** @param {PointerEvent} event */
function onSortableContainerClick(event) {
    const parentWithClass = (element, className) => {
        return (element.parentElement.classList.contains(className)) ? element.parentElement : null;
    }
    const btnSortable = parentWithClass(event.target, "btnSortable");
    if (btnSortable) {
        const listItem = parentWithClass(btnSortable, "sortableOption");
        const list = parentWithClass(listItem, "paperList");
        if (btnSortable.classList.contains("btnSortableMoveDown")) {
            const next = listItem.nextElementSibling;
            if (next) {
                listItem.parentElement.removeChild(listItem);
                next.parentElement.insertBefore(listItem, next.nextSibling);
            }
        }
        else {
            const prev = listItem.previousElementSibling;
            if (prev) {
                listItem.parentElement.removeChild(listItem);
                prev.parentElement.insertBefore(listItem, prev);
            }
        }

        for (const option of list.querySelectorAll(".sortableOption")) {
            adjustSortableListElement(option)
        };
    }
}

async function loadUserConfig(form, userId, config) {
    if (!userId) {
        form.querySelector("#UserSettingsContainer").setAttribute("hidden", "");
        form.querySelector("#UserUsername").removeAttribute("required");
        Dashboard.hideLoadingMsg();
        return;
    }

    Dashboard.showLoadingMsg();

    // Get the configuration to use.
    if (!config) config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId)
    const userConfig = config.UserList.find((c) => userId === c.UserId) || { UserId: userId };

    // Configure the elements within the user container
    form.querySelector("#UserEnableSynchronization").checked = userConfig.EnableSynchronization || false;
    form.querySelector("#SyncUserDataOnImport").checked = userConfig.SyncUserDataOnImport || false;
    form.querySelector("#SyncUserDataAfterPlayback").checked = userConfig.SyncUserDataAfterPlayback || false;
    form.querySelector("#SyncUserDataUnderPlayback").checked = userConfig.SyncUserDataUnderPlayback || false;
    form.querySelector("#SyncUserDataUnderPlaybackLive").checked = userConfig.SyncUserDataUnderPlaybackLive || false;
    form.querySelector("#SyncUserDataInitialSkipEventCount").checked = userConfig.SyncUserDataInitialSkipEventCount === 2;
    form.querySelector("#SyncRestrictedVideos").checked = userConfig.SyncRestrictedVideos || false;
    form.querySelector("#UserUsername").value = userConfig.Username || "";
    // Synchronization settings
    form.querySelector("#UserPassword").value = "";
    if (userConfig.Token) {
        form.querySelector("#UserDeleteContainer").removeAttribute("hidden");
        form.querySelector("#UserUsername").setAttribute("disabled", "");
        form.querySelector("#UserPasswordContainer").setAttribute("hidden", "");
        form.querySelector("#UserUsername").removeAttribute("required");
    }
    else {
        form.querySelector("#UserDeleteContainer").setAttribute("hidden", "");
        form.querySelector("#UserUsername").removeAttribute("disabled");
        form.querySelector("#UserPasswordContainer").removeAttribute("hidden");
        form.querySelector("#UserUsername").setAttribute("required", "");
    }

    // Show the user settings now if it was previously hidden.
    form.querySelector("#UserSettingsContainer").removeAttribute("hidden");

    Dashboard.hideLoadingMsg();
}

async function loadMediaFolderConfig(form, mediaFolderId, config) {
    if (!mediaFolderId) {
        form.querySelector("#MediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        Dashboard.hideLoadingMsg();
        return;
    }

    Dashboard.showLoadingMsg();

    // Get the configuration to use.
    if (!config) config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId)
    const mediaFolderConfig = config.MediaFolders.find((c) => mediaFolderId === c.MediaFolderId);
    if (!mediaFolderConfig) {
        form.querySelector("#MediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        Dashboard.hideLoadingMsg();
        return;
    }

    form.querySelector("#MediaFolderImportFolderName").value = mediaFolderConfig.IsMapped ? `${mediaFolderConfig.ImportFolderName} (${mediaFolderConfig.ImportFolderId}) ${mediaFolderConfig.ImportFolderRelativePath}` : "Not Mapped";

    // Configure the elements within the user container
    form.querySelector("#MediaFolderVirtualFileSystem").checked = mediaFolderConfig.IsVirtualFileSystemEnabled;
    form.querySelector("#MediaFolderLibraryFilteringMode").value = mediaFolderConfig.LibraryFilteringMode;

    // Show the user settings now if it was previously hidden.
    form.querySelector("#MediaFolderDefaultSettingsContainer").setAttribute("hidden", "");
    form.querySelector("#MediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    Dashboard.hideLoadingMsg();
}

async function loadSignalrMediaFolderConfig(form, mediaFolderId, config) {
    if (!mediaFolderId) {
        form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        Dashboard.hideLoadingMsg();
        return;
    }

    Dashboard.showLoadingMsg();

    // Get the configuration to use.
    if (!config) config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId)
    const mediaFolderConfig = config.MediaFolders.find((c) => mediaFolderId === c.MediaFolderId);
    if (!mediaFolderConfig) {
        form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        Dashboard.hideLoadingMsg();
        return;
    }

    form.querySelector("#SignalRMediaFolderImportFolderName").value = mediaFolderConfig.IsMapped ? `${mediaFolderConfig.ImportFolderName} (${mediaFolderConfig.ImportFolderId}) ${mediaFolderConfig.ImportFolderRelativePath}` : "Not Mapped";

    // Configure the elements within the user container
    form.querySelector("#SignalRFileEvents").checked = mediaFolderConfig.IsFileEventsEnabled;
    form.querySelector("#SignalRRefreshEvents").checked = mediaFolderConfig.IsRefreshEventsEnabled;

    // Show the user settings now if it was previously hidden.
    form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").setAttribute("hidden", "");
    form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    Dashboard.hideLoadingMsg();
}

/**
 * 
 * @param {string} username 
 * @param {string} password 
 * @param {boolean?} userKey 
 * @returns {Promise<{ apikey: string; }>}
 */
function getApiKey(username, password, userKey = false) {
    return ApiClient.fetch({
        dataType: "json",
        data: JSON.stringify({
            username,
            password,
            userKey,
        }),
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
        type: "POST",
        url: ApiClient.getUrl("Plugin/Shokofin/Host/GetApiKey"),
    });
}

/**
 * 
 * @returns {Promise<{ IsUsable: boolean; IsActive: boolean; State: "Disconnected" | "Connected" | "Connecting" | "Reconnecting" }>}
 */
function getSignalrStatus() {
    return ApiClient.fetch({
        dataType: "json",
        type: "GET",
        url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Status"),
    });
}

async function signalrConnect() {
    await ApiClient.fetch({
        type: "POST",
        url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Connect"),
    });
    return getSignalrStatus();
}

async function signalrDisconnect() {
    await ApiClient.fetch({
        type: "POST",
        url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Disconnect"),
    });
    return getSignalrStatus();
}

async function defaultSubmit(form) {
    let config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);

    if (config.ApiKey !== "") {
        let publicUrl = form.querySelector("#PublicUrl").value;
        if (publicUrl.endsWith("/")) {
            publicUrl = publicUrl.slice(0, -1);
            form.querySelector("#PublicUrl").value = publicUrl;
        }
        const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);

        // Metadata settings
        // config.TitleMainType = form.querySelector("#TitleMainType").value;
        // config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
        config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
        config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
        config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
        setDescriptionSourcesIntoConfig(form, config);
        config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
        config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;

        // Provider settings
        config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
        config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

        // Library settings
        config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
        config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
        config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
        config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
        config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
        config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

        // Media Folder settings
        let mediaFolderId = form.querySelector("#MediaFolderSelector").value;
        let mediaFolderConfig = mediaFolderId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId) : undefined;
        if (mediaFolderConfig) {
            const filteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
            mediaFolderConfig.IsVirtualFileSystemEnabled = form.querySelector("#MediaFolderVirtualFileSystem").checked;
            mediaFolderConfig.LibraryFilteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
        }
        else {
            config.VirtualFileSystem = form.querySelector("#VirtualFileSystem").checked;
            config.LibraryFilteringMode = form.querySelector("#LibraryFilteringMode").value;
        }

        // SignalR settings
        const reconnectIntervals = filterReconnectIntervals(form.querySelector("#SignalRAutoReconnectIntervals").value);
        config.SignalR_AutoConnectEnabled = form.querySelector("#SignalRAutoConnect").checked;
        config.SignalR_AutoReconnectInSeconds = reconnectIntervals;
        form.querySelector("#SignalRAutoReconnectIntervals").value = reconnectIntervals.join(", ");
        mediaFolderId = form.querySelector("#SignalRMediaFolderSelector").value;
        mediaFolderConfig = mediaFolderId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId) : undefined;
        if (mediaFolderConfig) {
            mediaFolderConfig.IsFileEventsEnabled = form.querySelector("#SignalRFileEvents").checked;
            mediaFolderConfig.IsRefreshEventsEnabled = form.querySelector("#SignalRRefreshEvents").checked;
        }
        else {
            config.SignalR_FileEvents = form.querySelector("#SignalRDefaultFileEvents").checked;
            config.SignalR_RefreshEnabled = form.querySelector("#SignalRDefaultRefreshEvents").checked;
        }

        // Tag settings
        config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
        config.HideArtStyleTags = form.querySelector("#HideArtStyleTags").checked;
        config.HideMiscTags = form.querySelector("#HideMiscTags").checked;
        config.HidePlotTags = form.querySelector("#HidePlotTags").checked;
        config.HideAniDbTags = form.querySelector("#HideAniDbTags").checked;
        config.HideSettingTags = form.querySelector("#HideSettingTags").checked;
        config.HideProgrammingTags = form.querySelector("#HideProgrammingTags").checked;

        // Advanced settings
        config.PublicUrl = publicUrl;
        config.IgnoredFolders = ignoredFolders;
        form.querySelector("#IgnoredFolders").value = ignoredFolders.join();

        // Experimental settings
        config.EXPERIMENTAL_AutoMergeVersions = form.querySelector("#EXPERIMENTAL_AutoMergeVersions").checked;
        config.EXPERIMENTAL_SplitThenMergeMovies = form.querySelector("#EXPERIMENTAL_SplitThenMergeMovies").checked;
        config.EXPERIMENTAL_SplitThenMergeEpisodes = form.querySelector("#EXPERIMENTAL_SplitThenMergeEpisodes").checked;
        config.EXPERIMENTAL_MergeSeasons = form.querySelector("#EXPERIMENTAL_MergeSeasons").checked;

        // User settings
        const userId = form.querySelector("#UserSelector").value;
        if (userId) {
            let userConfig = config.UserList.find((c) => userId === c.UserId);
            if (!userConfig) {
                userConfig = { UserId: userId };
                config.UserList.push(userConfig);
            }

            // The user settings goes below here;
            userConfig.EnableSynchronization = form.querySelector("#UserEnableSynchronization").checked;
            userConfig.SyncUserDataOnImport = form.querySelector("#SyncUserDataOnImport").checked;
            userConfig.SyncUserDataAfterPlayback = form.querySelector("#SyncUserDataAfterPlayback").checked;
            userConfig.SyncUserDataUnderPlayback = form.querySelector("#SyncUserDataUnderPlayback").checked;
            userConfig.SyncUserDataUnderPlaybackLive = form.querySelector("#SyncUserDataUnderPlaybackLive").checked;
            userConfig.SyncUserDataInitialSkipEventCount = form.querySelector("#SyncUserDataInitialSkipEventCount").checked ? 2 : 0;
            userConfig.SyncUserDataUnderPlaybackAtEveryXTicks = 6;
            userConfig.SyncUserDataUnderPlaybackLiveThreshold = 125000000; // 12.5s
            userConfig.SyncRestrictedVideos = form.querySelector("#SyncRestrictedVideos").checked;

            // Only try to save a new token if a token is not already present.
            if (!userConfig.Token) try {
                const username = form.querySelector("#UserUsername").value;
                const password = form.querySelector("#UserPassword").value;
                const response = await getApiKey(username, password, true);
                userConfig.Username = username;
                userConfig.Token = response.apikey;
            }
            catch (err) {
                Dashboard.alert(Messages.InvalidCredentials);
                console.error(err, Messages.InvalidCredentials);
                userConfig.Username = "";
                userConfig.Token = "";
            }
        }

        let result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
        Dashboard.processPluginConfigurationUpdateResult(result);
    }
    else {
        // Connection settings
        let url = form.querySelector("#Url").value;
        if (!url) {
            url = "http://localhost:8111";
        }
        else {
            try {
                let actualUrl = new URL(url);
                url = actualUrl.href;
            }
            catch (err) {
                try {
                    let actualUrl = new URL(`http://${url}:8111`);
                    url = actualUrl.href;
                }
                catch (err2) {
                  throw err;
                }
            }
        }
        if (url.endsWith("/")) {
            url = url.slice(0, -1);
        }

        // Update the url if needed.
        if (config.Url !== url) {
            config.Url = url;
            form.querySelector("#Url").value = url;
            let result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
            Dashboard.processPluginConfigurationUpdateResult(result);
        }

        const username = form.querySelector("#Username").value;
        const password = form.querySelector("#Password").value;
        try {
            const response = await getApiKey(username, password);
            config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            config.Username = username;
            config.ApiKey = response.apikey;

            let result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
            Dashboard.processPluginConfigurationUpdateResult(result);
        }
        catch (err) {
            Dashboard.alert(Messages.InvalidCredentials);
            console.error(err, Messages.InvalidCredentials);
        }
    }

    return config;
}

async function resetConnectionSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    form.querySelector("#Username").value = config.Username;
    form.querySelector("#Password").value = "";

    // Connection settings
    config.ApiKey = "";
    config.ServerVersion = null;

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    let publicUrl = form.querySelector("#PublicUrl").value;
    if (publicUrl.endsWith("/")) {
        publicUrl = publicUrl.slice(0, -1);
        form.querySelector("#PublicUrl").value = publicUrl;
    }
    const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);

    // Metadata settings
    // config.TitleMainType = form.querySelector("#TitleMainType").value;
    // config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
    config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
    config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
    config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
    setDescriptionSourcesIntoConfig(form, config);
    config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
    config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;

    // Provider settings
    config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
    config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

    // Library settings
    config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
    config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
    config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
    config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
    config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
    config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

    // Tag settings
    config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
    config.HideArtStyleTags = form.querySelector("#HideArtStyleTags").checked;
    config.HideMiscTags = form.querySelector("#HideMiscTags").checked;
    config.HidePlotTags = form.querySelector("#HidePlotTags").checked;
    config.HideAniDbTags = form.querySelector("#HideAniDbTags").checked;
    config.HideSettingTags = form.querySelector("#HideSettingTags").checked;
    config.HideProgrammingTags = form.querySelector("#HideProgrammingTags").checked;

    // Advanced settings
    config.PublicUrl = publicUrl;
    config.IgnoredFolders = ignoredFolders;
    form.querySelector("#IgnoredFolders").value = ignoredFolders.join();

    // Experimental settings
    config.EXPERIMENTAL_AutoMergeVersions = form.querySelector("#EXPERIMENTAL_AutoMergeVersions").checked;
    config.EXPERIMENTAL_SplitThenMergeMovies = form.querySelector("#EXPERIMENTAL_SplitThenMergeMovies").checked;
    config.EXPERIMENTAL_SplitThenMergeEpisodes = form.querySelector("#EXPERIMENTAL_SplitThenMergeEpisodes").checked;
    config.EXPERIMENTAL_MergeSeasons = form.querySelector("#EXPERIMENTAL_MergeSeasons").checked;

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function unlinkUser(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const userId = form.querySelector("#UserSelector").value;
    if (!userId) return;

    const index = config.UserList.findIndex(c => userId === c.UserId);
    if (index !== -1) {
        config.UserList.splice(index, 1);
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config)
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncMediaFolderSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const mediaFolderId = form.querySelector("#MediaFolderSelector").value;
    const mediaFolderConfig = mediaFolderId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId) : undefined;
    if (mediaFolderConfig) {
        mediaFolderConfig.IsVirtualFileSystemEnabled = form.querySelector("#MediaFolderVirtualFileSystem").checked;
        mediaFolderConfig.LibraryFilteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
    }
    else {
        config.VirtualFileSystem = form.querySelector("#VirtualFileSystem").checked;
        config.LibraryFilteringMode = form.querySelector("#LibraryFilteringMode").value;
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncSignalrSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const mediaFolderId = form.querySelector("#SignalRMediaFolderSelector").value;
    const reconnectIntervals = filterReconnectIntervals(form.querySelector("#SignalRAutoReconnectIntervals").value);

    config.SignalR_AutoConnectEnabled = form.querySelector("#SignalRAutoConnect").checked;
    config.SignalR_AutoReconnectInSeconds = reconnectIntervals;
    form.querySelector("#SignalRAutoReconnectIntervals").value = reconnectIntervals.join(", ");

    const mediaFolderConfig = mediaFolderId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId) : undefined;
    if (mediaFolderConfig) {
        mediaFolderConfig.IsFileEventsEnabled = form.querySelector("#SignalRFileEvents").checked;
        mediaFolderConfig.IsRefreshEventsEnabled = form.querySelector("#SignalRRefreshEvents").checked;
    }
    else {
        config.SignalR_FileEvents = form.querySelector("#SignalRDefaultFileEvents").checked;
        config.SignalR_RefreshEnabled = form.querySelector("#SignalRDefaultRefreshEvents").checked;
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncUserSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const userId = form.querySelector("#UserSelector").value;
    if (!userId)
        return config;

    let userConfig = config.UserList.find((c) => userId === c.UserId);
    if (!userConfig) {
        userConfig = { UserId: userId };
        config.UserList.push(userConfig);
    }

    // The user settings goes below here;
    userConfig.EnableSynchronization = form.querySelector("#UserEnableSynchronization").checked;
    userConfig.SyncUserDataOnImport = form.querySelector("#SyncUserDataOnImport").checked;
    userConfig.SyncUserDataAfterPlayback = form.querySelector("#SyncUserDataAfterPlayback").checked;
    userConfig.SyncUserDataUnderPlayback = form.querySelector("#SyncUserDataUnderPlayback").checked;
    userConfig.SyncUserDataUnderPlaybackLive = form.querySelector("#SyncUserDataUnderPlaybackLive").checked;
    userConfig.SyncUserDataInitialSkipEventCount = form.querySelector("#SyncUserDataInitialSkipEventCount").checked ? 2 : 0;
    userConfig.SyncUserDataUnderPlaybackAtEveryXTicks = 6;
    userConfig.SyncUserDataUnderPlaybackLiveThreshold = 125000000; // 12.5s
    userConfig.SyncRestrictedVideos = form.querySelector("#SyncRestrictedVideos").checked;

    // Only try to save a new token if a token is not already present.
    const username = form.querySelector("#UserUsername").value;
    const password = form.querySelector("#UserPassword").value;
    if (!userConfig.Token) try {
        const response = await getApiKey(username, password, true);
        userConfig.Username = username;
        userConfig.Token = response.apikey;
    }
    catch (err) {
        Dashboard.alert(Messages.InvalidCredentials);
        console.error(err, Messages.InvalidCredentials);
        userConfig.Username = "";
        userConfig.Token = "";
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config)
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

export default function (page) {
    const form = page.querySelector("#ShokoConfigForm");
    const userSelector = form.querySelector("#UserSelector");
    const mediaFolderSelector = form.querySelector("#MediaFolderSelector");
    const signalrMediaFolderSelector = form.querySelector("#SignalRMediaFolderSelector");

    // Refresh the view after we changed the settings, so the view reflect the new settings.
    const refreshSettings = (config) => {
        if (config.ServerVersion) {
            let version = `Version ${config.ServerVersion.Version}`;
            const extraDetails = [
                config.ServerVersion.ReleaseChannel || "",
                config.ServerVersion.
                Commit ? config.ServerVersion.Commit.slice(0, 7) : "",
            ].filter(s => s).join(", ");
            if (extraDetails)
                version += ` (${extraDetails})`;
            form.querySelector("#ServerVersion").value = version;
        }
        else {
            form.querySelector("#ServerVersion").value = "Version N/A";
        }
        if (!config.CanCreateSymbolicLinks) {
            form.querySelector("#WindowsSymLinkWarning1").removeAttribute("hidden");
            form.querySelector("#WindowsSymLinkWarning2").removeAttribute("hidden");
        }
        if (config.ApiKey) {
            form.querySelector("#Url").setAttribute("disabled", "");
            form.querySelector("#Username").setAttribute("disabled", "");
            form.querySelector("#Password").value = "";
            form.querySelector("#ConnectionSetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionResetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").removeAttribute("hidden");
            form.querySelector("#ProviderSection").removeAttribute("hidden");
            form.querySelector("#LibrarySection").removeAttribute("hidden");
            form.querySelector("#MediaFolderSection").removeAttribute("hidden");
            form.querySelector("#SignalRSection1").removeAttribute("hidden");
            form.querySelector("#SignalRSection2").removeAttribute("hidden");
            form.querySelector("#UserSection").removeAttribute("hidden");
            form.querySelector("#TagSection").removeAttribute("hidden");
            form.querySelector("#AdvancedSection").removeAttribute("hidden");
            form.querySelector("#ExperimentalSection").removeAttribute("hidden");
        }
        else {
            form.querySelector("#Url").removeAttribute("disabled");
            form.querySelector("#Username").removeAttribute("disabled");
            form.querySelector("#ConnectionSetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionResetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").setAttribute("hidden", "");
            form.querySelector("#ProviderSection").setAttribute("hidden", "");
            form.querySelector("#LibrarySection").setAttribute("hidden", "");
            form.querySelector("#MediaFolderSection").setAttribute("hidden", "");
            form.querySelector("#SignalRSection1").setAttribute("hidden", "");
            form.querySelector("#SignalRSection2").setAttribute("hidden", "");
            form.querySelector("#UserSection").setAttribute("hidden", "");
            form.querySelector("#TagSection").setAttribute("hidden", "");
            form.querySelector("#AdvancedSection").setAttribute("hidden", "");
            form.querySelector("#ExperimentalSection").setAttribute("hidden", "");
        }

        loadUserConfig(form, form.querySelector("#UserSelector").value, config);
        loadMediaFolderConfig(form, form.querySelector("#MediaFolderSelector").value, config);
        loadSignalrMediaFolderConfig(form, form.querySelector("#SignalRMediaFolderSelector").value, config);
    };

    /**
     * 
     * @param {{ IsUsable: boolean; IsActive: boolean; State: "Disconnected" | "Connected" | "Connecting" | "Reconnecting" }} status 
     */
    const refreshSignalr = (status) => {
        form.querySelector("#SignalRStatus").value = status.IsActive ? `Enabled, ${status.State}` : status.IsUsable ? "Disabled" : "Unavailable";
        if (status.IsUsable) {
            form.querySelector("#SignalRConnectButton").removeAttribute("disabled");
        }
        else {
            form.querySelector("#SignalRConnectButton").setAttribute("disabled", "");
        }
        if (status.IsActive) {
            form.querySelector("#SignalRConnectContainer").setAttribute("hidden", "");
            form.querySelector("#SignalRDisconnectContainer").removeAttribute("hidden");
        }
        else {
            form.querySelector("#SignalRConnectContainer").removeAttribute("hidden");
            form.querySelector("#SignalRDisconnectContainer").setAttribute("hidden", "");
        }
    };

    const onError = (err) => {
        console.error(err);
        Dashboard.alert(`An error occurred; ${err.message}`);
        Dashboard.hideLoadingMsg();
    };

    userSelector.addEventListener("change", function () {
        loadUserConfig(page, this.value);
    });

    mediaFolderSelector.addEventListener("change", function () {
        loadMediaFolderConfig(page, this.value);
    });

    signalrMediaFolderSelector.addEventListener("change", function () {
        loadSignalrMediaFolderConfig(page, this.value);
    });

    form.querySelector("#UserEnableSynchronization").addEventListener("change", function () {
        const disabled = !this.checked;
        form.querySelector("#SyncUserDataOnImport").disabled = disabled;
        form.querySelector("#SyncUserDataAfterPlayback").disabled = disabled;
        form.querySelector("#SyncUserDataUnderPlayback").disabled = disabled;
        form.querySelector("#SyncUserDataUnderPlaybackLive").disabled = disabled;
        form.querySelector("#SyncUserDataInitialSkipEventCount").disabled = disabled;
    });

    form.querySelector("#UseGroupsForShows").addEventListener("change", function () {
        form.querySelector("#SeasonOrdering").disabled = !this.checked;
        if (this.checked) {
            form.querySelector("#SeasonOrderingContainer").removeAttribute("hidden");
        }
        else {
            form.querySelector("#SeasonOrderingContainer").setAttribute("hidden", "");
        }
    });

    form.querySelector("#descriptionSourceList").addEventListener("click", onSortableContainerClick);

    page.addEventListener("viewshow", async function () {
        Dashboard.showLoadingMsg();
        try {
            const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            const signalrStatus = await getSignalrStatus();
            const users = await ApiClient.getUsers();

            // Connection settings
            form.querySelector("#Url").value = config.Url;
            form.querySelector("#Username").value = config.Username;
            form.querySelector("#Password").value = "";

            // Metadata settings
            // form.querySelector("#TitleMainType").value = config.TitleMainType;
            // form.querySelector("#TitleAlternateType").value = config.TitleAlternateType;
            form.querySelector("#TitleAllowAny").checked = config.TitleAllowAny;
            form.querySelector("#TitleAddForMultipleEpisodes").checked = config.TitleAddForMultipleEpisodes != null ? config.TitleAddForMultipleEpisodes : true;
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;
            await setDescriptionSourcesFromConfig(form, config);
            form.querySelector("#CleanupAniDBDescriptions").checked = config.SynopsisCleanMultiEmptyLines || config.SynopsisCleanLinks;
            form.querySelector("#MinimalAniDBDescriptions").checked = config.SynopsisRemoveSummary || config.SynopsisCleanMiscLines;

            // Provider settings
            form.querySelector("#AddAniDBId").checked = config.AddAniDBId;
            form.querySelector("#AddTMDBId").checked = config.AddTMDBId;

            // Library settings
            form.querySelector("#UseGroupsForShows").checked = config.UseGroupsForShows || false;
            form.querySelector("#SeasonOrdering").value = config.SeasonOrdering;
            form.querySelector("#SeasonOrdering").disabled = !form.querySelector("#UseGroupsForShows").checked;
            if (form.querySelector("#UseGroupsForShows").checked) {
                form.querySelector("#SeasonOrderingContainer").removeAttribute("hidden");
            }
            else {
                form.querySelector("#SeasonOrderingContainer").setAttribute("hidden", "");
            }
            form.querySelector("#CollectionGrouping").value = config.CollectionGrouping || "Default";
            form.querySelector("#SeparateMovies").checked = config.SeparateMovies != null ? config.SeparateMovies : true;
            form.querySelector("#SpecialsPlacement").value = config.SpecialsPlacement === "Default" ? "AfterSeason" : config.SpecialsPlacement;
            form.querySelector("#AddMissingMetadata").checked = config.AddMissingMetadata || false;

            // Media Folder settings
            form.querySelector("#VirtualFileSystem").checked = config.VirtualFileSystem != null ? config.VirtualFileSystem : true;
            form.querySelector("#LibraryFilteringMode").value = config.LibraryFilteringMode;
            mediaFolderSelector.innerHTML += config.MediaFolders.map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId}">${mediaFolder.MediaFolderPath}</option>`).join("");

            // SignalR settings
            form.querySelector("#SignalRAutoConnect").checked = config.SignalR_AutoConnectEnabled;
            form.querySelector("#SignalRAutoReconnectIntervals").value = config.SignalR_AutoReconnectInSeconds.join(", ");
            signalrMediaFolderSelector.innerHTML += config.MediaFolders.map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId}">${mediaFolder.MediaFolderPath}</option>`).join("");
            form.querySelector("#SignalRDefaultFileEvents").checked = config.SignalR_FileEvents;
            form.querySelector("#SignalRDefaultRefreshEvents").checked = config.SignalR_RefreshEnabled;

            // User settings
            userSelector.innerHTML += users.map((user) => `<option value="${user.Id}">${user.Name}</option>`).join("");

            // Tag settings
            form.querySelector("#HideUnverifiedTags").checked = config.HideUnverifiedTags;
            form.querySelector("#HideArtStyleTags").checked = config.HideArtStyleTags;
            form.querySelector("#HideMiscTags").checked = config.HideMiscTags;
            form.querySelector("#HidePlotTags").checked = config.HidePlotTags;
            form.querySelector("#HideAniDbTags").checked = config.HideAniDbTags;
            form.querySelector("#HideSettingTags").checked = config.HideSettingTags;
            form.querySelector("#HideProgrammingTags").checked = config.HideProgrammingTags;

            // Advanced settings
            form.querySelector("#PublicUrl").value = config.PublicUrl;
            form.querySelector("#IgnoredFolders").value = config.IgnoredFolders.join();

            // Experimental settings
            form.querySelector("#EXPERIMENTAL_AutoMergeVersions").checked = config.EXPERIMENTAL_AutoMergeVersions || false;
            form.querySelector("#EXPERIMENTAL_SplitThenMergeMovies").checked = config.EXPERIMENTAL_SplitThenMergeMovies != null ? config.EXPERIMENTAL_SplitThenMergeMovies : true;
            form.querySelector("#EXPERIMENTAL_SplitThenMergeEpisodes").checked = config.EXPERIMENTAL_SplitThenMergeEpisodes || false;
            form.querySelector("#EXPERIMENTAL_MergeSeasons").checked = config.EXPERIMENTAL_MergeSeasons || false;

            if (!config.ApiKey) {
                Dashboard.alert(Messages.ConnectToShoko);
            }

            refreshSettings(config);
            refreshSignalr(signalrStatus);
        }
        catch (err) {
            Dashboard.alert(Messages.UnableToRender);
            console.error(err, Messages.UnableToRender)
            Dashboard.hideLoadingMsg();
        }
    });

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        if (!event.submitter) return;
        switch (event.submitter.name) {
            default:
            case "all-settings":
                Dashboard.showLoadingMsg();
                defaultSubmit(form).then(refreshSettings).catch(onError);
                break;
            case "settings":
                Dashboard.showLoadingMsg();
                syncSettings(form).then(refreshSettings).catch(onError);
                break;
            case "reset-connection":
                Dashboard.showLoadingMsg();
                resetConnectionSettings(form).then(refreshSettings).catch(onError);
                break;
            case "unlink-user":
                unlinkUser(form).then(refreshSettings).catch(onError);
                break;
            case "media-folder-settings":
                Dashboard.showLoadingMsg();
                syncMediaFolderSettings(form).then(refreshSettings).catch(onError);
                break;
            case "signalr-connect":
                signalrConnect().then(refreshSignalr).catch(onError);
                break;
            case "signalr-disconnect":
                signalrDisconnect().then(refreshSignalr).catch(onError);
                break;
            case "signalr-settings":
                syncSignalrSettings(form).then(refreshSettings).catch(onError);
                break;
            case "user-settings":
                Dashboard.showLoadingMsg();
                syncUserSettings(form).then(refreshSettings).catch(onError);
                break;
        }
        return false;
    });
}

function setDescriptionSourcesIntoConfig(form, config) {
    const descriptionElements = form.querySelectorAll(`#descriptionSourceList .chkDescriptionSource`);
    config.DescriptionSourceList = Array.prototype.filter.call(descriptionElements,
        (el) => el.checked)
        .map((el) => el.dataset.descriptionsource);

    config.DescriptionSourceOrder = Array.prototype.map.call(descriptionElements,
        (el) => el.dataset.descriptionsource
    );
}

async function setDescriptionSourcesFromConfig(form, config) {
    const list = form.querySelector("#descriptionSourceList .checkboxList");
    const listItems = list.querySelectorAll('.listItem');

    for (const item of listItems) {
        const source = item.dataset.descriptionsource;
        if (config.DescriptionSourceList.includes(source)) {
            item.querySelector(".chkDescriptionSource").checked = true;
        }
        if (config.DescriptionSourceOrder.includes(source)) {
            list.removeChild(item); // This is safe to be removed as we can re-add it in the next loop
        }
    }

    for (const source of config.DescriptionSourceOrder) {
        const targetElement = Array.prototype.find.call(listItems, (el) => el.dataset.descriptionsource === source);
        if (targetElement) {
            list.append(targetElement);
        }
    }
    for (const option of list.querySelectorAll(".sortableOption")) {
        adjustSortableListElement(option)
    };
}
