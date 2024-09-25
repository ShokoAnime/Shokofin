const PluginConfig = {
    pluginId: "5216ccbf-d24a-4eb3-8a7e-7da4230b7052"
};

const Messages = {
    ExpertModeCountdown: "Press <count> more times to <toggle> expert mode.",
    ExpertModeEnabled: "Expert mode enabled.",
    ExpertModeDisabled: "Expert mode disabled.",
    ConnectToShoko: "Please establish a connection to a running instance of Shoko Server before you continue.",
    ConnectedToShoko: "Connection established.",
    DisconnectedToShoko: "Connection reset.",
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

/**
 * 
 * @param {HTMLElement} element 
 * @param {number} index 
 */
function adjustSortableListElement(element, index) {
    const button = element.querySelector(".btnSortable");
    const icon = button.querySelector(".material-icons");
    if (index > 0) {
        button.title = "Up";
        button.classList.add("btnSortableMoveUp");
        button.classList.remove("btnSortableMoveDown");
        icon.classList.add("keyboard_arrow_up");
        icon.classList.remove("keyboard_arrow_down");
    }
    else {
        button.title = "Down";
        button.classList.add("btnSortableMoveDown");
        button.classList.remove("btnSortableMoveUp");
        icon.classList.add("keyboard_arrow_down");
        icon.classList.remove("keyboard_arrow_up");
    }
}

/**
 * @param {PointerEvent} event
 **/
function onSortableContainerClick(event) {
    const parentWithClass = (element, className) => 
        (element.parentElement.classList.contains(className)) ? element.parentElement : null;
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
        let i = 0;
        for (const option of list.querySelectorAll(".sortableOption")) {
            adjustSortableListElement(option, i++);
        }
    }
}

async function loadUserConfig(form, userId, config) {
    if (!userId) {
        form.querySelector("#UserSettingsContainer").setAttribute("hidden", "");
        form.querySelector("#UserUsername").removeAttribute("required");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        Dashboard.showLoadingMsg();
        config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
        shouldHide = true;
    }

    // Configure the elements within the user container
    const userConfig = config.UserList.find((c) => userId === c.UserId) || { UserId: userId };
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

    if (shouldHide) {
        Dashboard.hideLoadingMsg();
    }
}

async function loadMediaFolderConfig(form, selectedValue, config) {
    const [mediaFolderId, libraryId] = selectedValue.split(",");
    if (!mediaFolderId || !libraryId) {
        form.querySelector("#MediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        Dashboard.showLoadingMsg();
        config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
        shouldHide = true;
    }

    const mediaFolderConfig = config.MediaFolders.find((c) => mediaFolderId === c.MediaFolderId && libraryId === c.LibraryId);
    if (!mediaFolderConfig) {
        form.querySelector("#MediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        if (shouldHide) {
            Dashboard.hideLoadingMsg();
        }
        return;
    }

    form.querySelector("#MediaFolderImportFolderName").value = mediaFolderConfig.IsMapped ? `${mediaFolderConfig.ImportFolderName} (${mediaFolderConfig.ImportFolderId}) ${mediaFolderConfig.ImportFolderRelativePath}` : "Not Mapped";

    // Configure the elements within the user container
    form.querySelector("#MediaFolderVirtualFileSystem").checked = mediaFolderConfig.IsVirtualFileSystemEnabled;
    form.querySelector("#MediaFolderLibraryFilteringMode").value = mediaFolderConfig.LibraryFilteringMode;

    // Show the user settings now if it was previously hidden.
    form.querySelector("#MediaFolderDefaultSettingsContainer").setAttribute("hidden", "");
    form.querySelector("#MediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    if (shouldHide) {
        Dashboard.hideLoadingMsg();
    }
}

async function loadSignalrMediaFolderConfig(form, selectedValue, config) {
    const [mediaFolderId, libraryId] = selectedValue.split(",");
    if (!mediaFolderId || !libraryId) {
        form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        Dashboard.showLoadingMsg();
        config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
        shouldHide = true;
    }

    const mediaFolderConfig = config.MediaFolders.find((c) => mediaFolderId === c.MediaFolderId && libraryId === c.LibraryId);
    if (!mediaFolderConfig) {
        form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").removeAttribute("hidden");
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        if (shouldHide) {
            Dashboard.hideLoadingMsg();
        }
        return;
    }

    form.querySelector("#SignalRMediaFolderImportFolderName").value = mediaFolderConfig.IsMapped ? `${mediaFolderConfig.ImportFolderName} (${mediaFolderConfig.ImportFolderId}) ${mediaFolderConfig.ImportFolderRelativePath}` : "Not Mapped";

    // Configure the elements within the user container
    form.querySelector("#SignalRFileEvents").checked = mediaFolderConfig.IsFileEventsEnabled;
    form.querySelector("#SignalRRefreshEvents").checked = mediaFolderConfig.IsRefreshEventsEnabled;

    // Show the user settings now if it was previously hidden.
    form.querySelector("#SignalRMediaFolderDefaultSettingsContainer").setAttribute("hidden", "");
    form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    if (shouldHide) {
        Dashboard.hideLoadingMsg();
    }
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
        const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);

        // Metadata settings
        config.TitleMainOverride = form.querySelector("#TitleMainOverride").checked;
        ([config.TitleMainList, config.TitleMainOrder] = retrieveSortableList(form, "TitleMainList"));
        config.TitleAlternateOverride = form.querySelector("#TitleAlternateOverride").checked;
        ([config.TitleAlternateList, config.TitleAlternateOrder] = retrieveSortableList(form, "TitleAlternateList"));
        config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
        config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
        config.DescriptionSourceOverride = form.querySelector("#DescriptionSourceOverride").checked;
        ([config.DescriptionSourceList, config.DescriptionSourceOrder] = retrieveSortableList(form, "DescriptionSourceList"));
        config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMiscLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisRemoveSummary = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
        config.TagOverride = form.querySelector("#TagOverride").checked;
        config.TagSources = retrieveSimpleList(form, "TagSources").join(", ");
        config.TagIncludeFilters = retrieveSimpleList(form, "TagIncludeFilters").join(", ");
        config.TagMinimumWeight = form.querySelector("#TagMinimumWeight").value;
        config.TagMaximumDepth = parseInt(form.querySelector("#TagMaximumDepth").value, 10);
        config.GenreOverride = form.querySelector("#GenreOverride").checked;
        config.GenreSources = retrieveSimpleList(form, "GenreSources").join(", ");
        config.GenreIncludeFilters = retrieveSimpleList(form, "GenreIncludeFilters").join(", ");
        config.GenreMinimumWeight = form.querySelector("#GenreMinimumWeight").value;
        config.GenreMaximumDepth = parseInt(form.querySelector("#GenreMaximumDepth").value, 10);
        config.ContentRatingOverride = form.querySelector("#ContentRatingOverride").checked;
        ([config.ContentRatingList, config.ContentRatingOrder] = retrieveSortableList(form, "ContentRatingList"));
        config.ProductionLocationOverride = form.querySelector("#ProductionLocationOverride").checked;
        ([config.ProductionLocationList, config.ProductionLocationOrder] = retrieveSortableList(form, "ProductionLocationList"));

        // Provider settings
        config.ThirdPartyIdProviderList = retrieveSimpleList(form, "ThirdPartyIdProviderList");

        // Library settings
        config.AutoMergeVersions = form.querySelector("#AutoMergeVersions").checked;
        config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
        config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
        config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
        config.CollectionMinSizeOfTwo = form.querySelector("#CollectionMinSizeOfTwo").checked;
        config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
        config.FilterMovieLibraries = form.querySelector("#FilterMovieLibraries").checked;
        config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
        config.MovieSpecialsAsExtraFeaturettes = form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked;
        config.AddTrailers = form.querySelector("#AddTrailers").checked;
        config.AddCreditsAsThemeVideos = form.querySelector("#AddCreditsAsThemeVideos").checked;
        config.AddCreditsAsSpecialFeatures = form.querySelector("#AddCreditsAsSpecialFeatures").checked;
        config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

        // Media Folder settings
        let [mediaFolderId, libraryId] = form.querySelector("#MediaFolderSelector").value.split(",");
        let mediaFolderConfig = mediaFolderId && libraryId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId && m.LibraryId === libraryId) : undefined;
        config.IgnoredFolders = ignoredFolders;
        form.querySelector("#IgnoredFolders").value = ignoredFolders.join();
        config.VFS_AddReleaseGroup = form.querySelector("#VFS_AddReleaseGroup").checked;
        config.VFS_AddResolution = form.querySelector("#VFS_AddResolution").checked;
        config.VFS_AttachRoot = form.querySelector("#VFS_AttachRoot").checked;
        config.VFS_Location = form.querySelector("#VFS_Location").value;
        config.VFS_CustomLocation = form.querySelector("#VFS_CustomLocation").value.trim() || null;
        if (mediaFolderConfig) {
            const libraryId = mediaFolderConfig.LibraryId;
            for (const c of config.MediaFolders.filter(m => m.LibraryId === libraryId)) {
                c.IsVirtualFileSystemEnabled = form.querySelector("#MediaFolderVirtualFileSystem").checked;
                c.LibraryFilteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
            }
        }
        else {
            config.VFS_Enabled = form.querySelector("#VFS_Enabled").checked;
            config.LibraryFilteringMode = form.querySelector("#LibraryFilteringMode").value;
        }

        // SignalR settings
        const reconnectIntervals = filterReconnectIntervals(form.querySelector("#SignalRAutoReconnectIntervals").value);
        config.SignalR_AutoConnectEnabled = form.querySelector("#SignalRAutoConnect").checked;
        config.SignalR_AutoReconnectInSeconds = reconnectIntervals;
        form.querySelector("#SignalRAutoReconnectIntervals").value = reconnectIntervals.join(", ");
        config.SignalR_EventSources = retrieveSimpleList(form, "SignalREventSources");
        ([mediaFolderId, libraryId] = form.querySelector("#SignalRMediaFolderSelector").value.split(","));
        mediaFolderConfig = mediaFolderId && libraryId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId && m.LibraryId === libraryId) : undefined;
        if (mediaFolderConfig) {
            const libraryId = mediaFolderConfig.LibraryId;
            for (const c of config.MediaFolders.filter(m => m.LibraryId === libraryId)) {
                c.IsFileEventsEnabled = form.querySelector("#SignalRFileEvents").checked;
                c.IsRefreshEventsEnabled = form.querySelector("#SignalRRefreshEvents").checked;
            }
        }
        else {
            config.SignalR_FileEvents = form.querySelector("#SignalRDefaultFileEvents").checked;
            config.SignalR_RefreshEnabled = form.querySelector("#SignalRDefaultRefreshEvents").checked;
        }


        // Experimental settings
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
        let publicUrl = form.querySelector("#PublicUrl").value;
        if (publicUrl.endsWith("/")) {
            publicUrl = publicUrl.slice(0, -1);
        }

        // Update the url if needed.
        if (config.Url !== url || config.PublicUrl !== publicUrl) {
            config.Url = url;
            config.PublicUrl = publicUrl;
            form.querySelector("#Url").value = url;
            form.querySelector("#PublicUrl").value = publicUrl;
            await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
        }

        const username = form.querySelector("#Username").value;
        const password = form.querySelector("#Password").value;
        try {
            const response = await getApiKey(username, password);
            config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            config.Username = username;
            config.ApiKey = response.apikey;

            await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);

            Dashboard.hideLoadingMsg();
            Dashboard.alert(Messages.ConnectedToShoko);
        }
        catch (err) {
            Dashboard.hideLoadingMsg();
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

    await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);

    Dashboard.hideLoadingMsg();
    Dashboard.alert(Messages.DisconnectedToShoko);

    return config;
}

async function syncSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);

    // Metadata settings
    config.TitleMainOverride = form.querySelector("#TitleMainOverride").checked;
    ([config.TitleMainList, config.TitleMainOrder] = retrieveSortableList(form, "TitleMainList"));
    config.TitleAlternateOverride = form.querySelector("#TitleAlternateOverride").checked;
    ([config.TitleAlternateList, config.TitleAlternateOrder] = retrieveSortableList(form, "TitleAlternateList"));
    config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
    config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
    config.DescriptionSourceOverride = form.querySelector("#DescriptionSourceOverride").checked;
    ([config.DescriptionSourceList, config.DescriptionSourceOrder] = retrieveSortableList(form, "DescriptionSourceList"));
    config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMiscLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisRemoveSummary = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
    config.TagOverride = form.querySelector("#TagOverride").checked;
    config.TagSources = retrieveSimpleList(form, "TagSources").join(", ");
    config.TagIncludeFilters = retrieveSimpleList(form, "TagIncludeFilters").join(", ");
    config.TagMinimumWeight = form.querySelector("#TagMinimumWeight").value;
    config.TagMaximumDepth = parseInt(form.querySelector("#TagMaximumDepth").value, 10);
    config.GenreOverride = form.querySelector("#GenreOverride").checked;
    config.GenreSources = retrieveSimpleList(form, "GenreSources").join(", ");
    config.GenreIncludeFilters = retrieveSimpleList(form, "GenreIncludeFilters").join(", ");
    config.GenreMinimumWeight = form.querySelector("#GenreMinimumWeight").value;
    config.GenreMaximumDepth = parseInt(form.querySelector("#GenreMaximumDepth").value, 10);
    config.ContentRatingOverride = form.querySelector("#ContentRatingOverride").checked;
    ([config.ContentRatingList, config.ContentRatingOrder] = retrieveSortableList(form, "ContentRatingList"));
    config.ProductionLocationOverride = form.querySelector("#ProductionLocationOverride").checked;
    ([config.ProductionLocationList, config.ProductionLocationOrder] = retrieveSortableList(form, "ProductionLocationList"));

    // Provider settings
    config.ThirdPartyIdProviderList = retrieveSimpleList(form, "ThirdPartyIdProviderList");

    // Library settings
    config.AutoMergeVersions = form.querySelector("#AutoMergeVersions").checked;
    config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
    config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
    config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
    config.FilterMovieLibraries = form.querySelector("#FilterMovieLibraries").checked;
    config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
    config.CollectionMinSizeOfTwo = form.querySelector("#CollectionMinSizeOfTwo").checked;
    config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
    config.MovieSpecialsAsExtraFeaturettes = form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked;
    config.AddTrailers = form.querySelector("#AddTrailers").checked;
    config.AddCreditsAsThemeVideos = form.querySelector("#AddCreditsAsThemeVideos").checked;
    config.AddCreditsAsSpecialFeatures = form.querySelector("#AddCreditsAsSpecialFeatures").checked;
    config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

    // Experimental settings
    config.VFS_AttachRoot = form.querySelector("#VFS_AttachRoot").checked;
    config.VFS_Location = form.querySelector("#VFS_Location").value;
    config.VFS_CustomLocation = form.querySelector("#VFS_CustomLocation").value.trim() || null;
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

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function removeMediaFolder(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const [mediaFolderId, libraryId] = form.querySelector("#MediaFolderSelector").value.value.split(",");
    if (!mediaFolderId || !libraryId) return;

    const index = config.MediaFolders.findIndex((m) => m.MediaFolderId === mediaFolderId && m.LibraryId === libraryId);
    if (index !== -1) {
        config.MediaFolders.splice(index, 1);
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    form.querySelector("#MediaFolderSelector").value = "";
    form.querySelector("#MediaFolderSelector").innerHTML = `<option value="">Default settings for new media folders</option>` + config.MediaFolders
        .filter((mediaFolder) => !mediaFolder.IsVirtualRoot)
        .map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId},${mediaFolder.LibraryId}">${mediaFolder.LibraryName} (${mediaFolder.MediaFolderPath})</option>`)
        .join("");
    form.querySelector("#SignalRMediaFolderSelector").innerHTML = `<option value="">Default settings for new media folders</option>` + config.MediaFolders
        .filter((mediaFolder) => !mediaFolder.IsVirtualRoot)
        .map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId},${mediaFolder.LibraryId}">${mediaFolder.LibraryName} (${mediaFolder.MediaFolderPath})</option>`)
        .join("");

    Dashboard.processPluginConfigurationUpdateResult(result);
    return config;
}

async function syncMediaFolderSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const [mediaFolderId, libraryId] = form.querySelector("#MediaFolderSelector").value.split(",");
    const mediaFolderConfig = mediaFolderId && libraryId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId && m.LibraryId === libraryId) : undefined;
    const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);

    config.IgnoredFolders = ignoredFolders;
    form.querySelector("#IgnoredFolders").value = ignoredFolders.join();
    config.VFS_AddReleaseGroup = form.querySelector("#VFS_AddReleaseGroup").checked;
    config.VFS_AddResolution = form.querySelector("#VFS_AddResolution").checked;
    config.VFS_AttachRoot = form.querySelector("#VFS_AttachRoot").checked;
    config.VFS_Location = form.querySelector("#VFS_Location").value;
    config.VFS_CustomLocation = form.querySelector("#VFS_CustomLocation").value.trim() || null;
    if (mediaFolderConfig) {
        for (const c of config.MediaFolders.filter(m => m.LibraryId === libraryId)) {
            c.IsVirtualFileSystemEnabled = form.querySelector("#MediaFolderVirtualFileSystem").checked;
            c.LibraryFilteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
        }
    }
    else {
        config.VFS_Enabled = form.querySelector("#VFS_Enabled").checked;
        config.LibraryFilteringMode = form.querySelector("#LibraryFilteringMode").value;
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncSignalrSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    const [mediaFolderId, libraryId] = form.querySelector("#SignalRMediaFolderSelector").value.split(",");
    const reconnectIntervals = filterReconnectIntervals(form.querySelector("#SignalRAutoReconnectIntervals").value);

    config.SignalR_AutoConnectEnabled = form.querySelector("#SignalRAutoConnect").checked;
    config.SignalR_AutoReconnectInSeconds = reconnectIntervals;
    config.SignalR_EventSources = retrieveSimpleList(form, "SignalREventSources");
    form.querySelector("#SignalRAutoReconnectIntervals").value = reconnectIntervals.join(", ");

    const mediaFolderConfig = mediaFolderId && libraryId ? config.MediaFolders.find((m) => m.MediaFolderId === mediaFolderId && m.LibraryId === libraryId) : undefined;
    if (mediaFolderConfig) {
        for (const c of config.MediaFolders.filter(m => m.LibraryId === libraryId)) {
            c.IsFileEventsEnabled = form.querySelector("#SignalRFileEvents").checked;
            c.IsRefreshEventsEnabled = form.querySelector("#SignalRRefreshEvents").checked;
        }
    }
    else {
        config.SignalR_FileEvents = form.querySelector("#SignalRDefaultFileEvents").checked;
        config.SignalR_RefreshEnabled = form.querySelector("#SignalRDefaultRefreshEvents").checked;
    }

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function toggleExpertMode(value) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);

    config.ExpertMode = value;

    await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);

    Dashboard.alert(value ? Messages.ExpertModeEnabled : Messages.ExpertModeDisabled);

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
    const MaxDebugPresses = 7;
    let expertPresses = 0;
    let expertMode = false;
    /** @type {HTMLFormElement} */
    const form = page.querySelector("#ShokoConfigForm");
    const serverVersion = form.querySelector("#ServerVersion");
    const userSelector = form.querySelector("#UserSelector");
    const mediaFolderSelector = form.querySelector("#MediaFolderSelector");
    const signalrMediaFolderSelector = form.querySelector("#SignalRMediaFolderSelector");

    // Refresh the view after we changed the settings, so the view reflect the new settings.
    const refreshSettings = async (config) => {
        if (config.ExpertMode) {
            form.classList.add("expert-mode");
        }
        else {
            form.classList.remove("expert-mode");
        }
        if (config.ServerVersion) {
            let version = `Version ${config.ServerVersion.Version}`;
            const extraDetails = [
                config.ServerVersion.ReleaseChannel || "",
                config.ServerVersion.
                Commit ? config.ServerVersion.Commit.slice(0, 7) : "",
            ].filter(s => s).join(", ");
            if (extraDetails)
                version += ` (${extraDetails})`;
            serverVersion.value = version;
        }
        else {
            serverVersion.value = "Version N/A";
        }
        if (!config.CanCreateSymbolicLinks) {
            form.querySelector("#WindowsSymLinkWarning1").removeAttribute("hidden");
            form.querySelector("#WindowsSymLinkWarning2").removeAttribute("hidden");
        }
        if (config.ApiKey) {
            form.querySelector("#Url").setAttribute("disabled", "");
            form.querySelector("#PublicUrl").setAttribute("disabled", "");
            form.querySelector("#Username").setAttribute("disabled", "");
            form.querySelector("#Password").value = "";
            form.querySelector("#ConnectionSetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionResetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").removeAttribute("hidden");
            form.querySelector("#LibrarySection").removeAttribute("hidden");
            form.querySelector("#MediaFolderSection").removeAttribute("hidden");
            form.querySelector("#SignalRSection1").removeAttribute("hidden");
            form.querySelector("#SignalRSection2").removeAttribute("hidden");
            form.querySelector("#UserSection").removeAttribute("hidden");
            form.querySelector("#ExperimentalSection").removeAttribute("hidden");
        }
        else {
            form.querySelector("#Url").removeAttribute("disabled");
            form.querySelector("#PublicUrl").removeAttribute("disabled");
            form.querySelector("#Username").removeAttribute("disabled");
            form.querySelector("#ConnectionSetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionResetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").setAttribute("hidden", "");
            form.querySelector("#LibrarySection").setAttribute("hidden", "");
            form.querySelector("#MediaFolderSection").setAttribute("hidden", "");
            form.querySelector("#SignalRSection1").setAttribute("hidden", "");
            form.querySelector("#SignalRSection2").setAttribute("hidden", "");
            form.querySelector("#UserSection").setAttribute("hidden", "");
            form.querySelector("#ExperimentalSection").setAttribute("hidden", "");
        }

        await loadUserConfig(form, form.querySelector("#UserSelector").value, config);
        await loadMediaFolderConfig(form, form.querySelector("#MediaFolderSelector").value, config);
        await loadSignalrMediaFolderConfig(form, form.querySelector("#SignalRMediaFolderSelector").value, config);

        Dashboard.hideLoadingMsg();
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

    serverVersion.addEventListener("click", async function () {
        if (++expertPresses === MaxDebugPresses) {
            expertPresses = 0;
            expertMode = !expertMode;
            const config = await toggleExpertMode(expertMode);
            refreshSettings(config);
            return;
        }
        if (expertPresses >= 3)
            Dashboard.alert(Messages.ExpertModeCountdown.replace("<count>", MaxDebugPresses - expertPresses).replace("<toggle>", expertMode ? "disable" : "enable"));
    });

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

    form.querySelector("#VFS_Location").addEventListener("change", function () {
        form.querySelector("#VFS_CustomLocation").disabled = this.value !== "Custom";
        if (this.value === "Custom") {
            form.querySelector("#VFS_CustomLocation").removeAttribute("hidden");
        }
        else {
            form.querySelector("#VFS_CustomLocation").setAttribute("hidden", "");
        }
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

    form.querySelector("#TitleMainList").addEventListener("click", onSortableContainerClick);
    form.querySelector("#TitleAlternateList").addEventListener("click", onSortableContainerClick);
    form.querySelector("#DescriptionSourceList").addEventListener("click", onSortableContainerClick);
    form.querySelector("#ContentRatingList").addEventListener("click", onSortableContainerClick);
    form.querySelector("#ProductionLocationList").addEventListener("click", onSortableContainerClick);

    form.querySelector("#TitleMainOverride").addEventListener("change", function () {
        const list = form.querySelector(`#TitleMainList`);
        this.checked ? list.removeAttribute("hidden") : list.setAttribute("hidden", "");
    });

    form.querySelector("#TitleAlternateOverride").addEventListener("change", function () {
        const list = form.querySelector(`#TitleAlternateList`);
        this.checked ? list.removeAttribute("hidden") : list.setAttribute("hidden", "");
    });

    form.querySelector("#DescriptionSourceOverride").addEventListener("change", function () {
        const list = form.querySelector("#DescriptionSourceList");
        this.checked ? list.removeAttribute("hidden") : list.setAttribute("hidden", "");
    });

    form.querySelector("#TagOverride").addEventListener("change", function () {
        if (this.checked) {
            form.querySelector("#TagSources").removeAttribute("hidden");
            form.querySelector("#TagIncludeFilters").removeAttribute("hidden");
            form.querySelector("#TagMinimumWeightContainer").removeAttribute("hidden");
            form.querySelector("#TagMinimumWeightContainer").disabled = false;
            form.querySelector("#TagMaximumDepthContainer").removeAttribute("hidden");
            form.querySelector("#TagMaximumDepthContainer").disabled = false;
        }
        else {
            form.querySelector("#TagSources").setAttribute("hidden", "");
            form.querySelector("#TagIncludeFilters").setAttribute("hidden", "");
            form.querySelector("#TagMinimumWeightContainer").setAttribute("hidden", "");
            form.querySelector("#TagMinimumWeightContainer").disabled = true;
            form.querySelector("#TagMaximumDepthContainer").setAttribute("hidden", "");
            form.querySelector("#TagMaximumDepthContainer").disabled = true;
        }
    });

    form.querySelector("#GenreOverride").addEventListener("change", function () {
        if (this.checked) {
            form.querySelector("#GenreSources").removeAttribute("hidden");
            form.querySelector("#GenreIncludeFilters").removeAttribute("hidden");
            form.querySelector("#GenreMinimumWeightContainer").removeAttribute("hidden");
            form.querySelector("#GenreMinimumWeightContainer").disabled = false;
            form.querySelector("#GenreMaximumDepthContainer").removeAttribute("hidden");
            form.querySelector("#GenreMaximumDepthContainer").disabled = false;
        }
        else {
            form.querySelector("#GenreSources").setAttribute("hidden", "");
            form.querySelector("#GenreIncludeFilters").setAttribute("hidden", "");
            form.querySelector("#GenreMinimumWeightContainer").setAttribute("hidden", "");
            form.querySelector("#GenreMinimumWeightContainer").disabled = true;
            form.querySelector("#GenreMaximumDepthContainer").setAttribute("hidden", "");
            form.querySelector("#GenreMaximumDepthContainer").disabled = true;
        }
    });

    form.querySelector("#ContentRatingOverride").addEventListener("change", function () {
        const list = form.querySelector("#ContentRatingList");
        this.checked ? list.removeAttribute("hidden") : list.setAttribute("hidden", "");
    });

    form.querySelector("#ProductionLocationOverride").addEventListener("change", function () {
        const list = form.querySelector("#ProductionLocationList");
        this.checked ? list.removeAttribute("hidden") : list.setAttribute("hidden", "");
    });

    page.addEventListener("viewshow", async function () {
        Dashboard.showLoadingMsg();
        try {
            const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            const signalrStatus = await getSignalrStatus();
            const users = await ApiClient.getUsers();

            expertPresses = 0;
            expertMode = config.ExpertMode;

            // Connection settings
            form.querySelector("#Url").value = config.Url;
            form.querySelector("#PublicUrl").value = config.PublicUrl;
            form.querySelector("#Username").value = config.Username;
            form.querySelector("#Password").value = "";

            // Metadata settings
            if (form.querySelector("#TitleMainOverride").checked = config.TitleMainOverride) {
                form.querySelector("#TitleMainList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#TitleMainList").setAttribute("hidden", "");
            }
            initSortableList(form, "TitleMainList", config.TitleMainList, config.TitleMainOrder);
            if (form.querySelector("#TitleAlternateOverride").checked = config.TitleAlternateOverride) {
                form.querySelector("#TitleAlternateList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#TitleAlternateList").setAttribute("hidden", "");
            }
            initSortableList(form, "TitleAlternateList", config.TitleAlternateList, config.TitleAlternateOrder);
            form.querySelector("#TitleAllowAny").checked = config.TitleAllowAny;
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;
            if (form.querySelector("#DescriptionSourceOverride").checked = config.DescriptionSourceOverride) {
                form.querySelector("#DescriptionSourceList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#DescriptionSourceList").setAttribute("hidden", "");
            }
            initSortableList(form, "DescriptionSourceList", config.DescriptionSourceList, config.DescriptionSourceOrder);
            form.querySelector("#CleanupAniDBDescriptions").checked = (
                config.SynopsisCleanMultiEmptyLines ||
                config.SynopsisCleanLinks ||
                config.SynopsisRemoveSummary ||
                config.SynopsisCleanMiscLines
            );
            form.querySelector("#HideUnverifiedTags").checked = config.HideUnverifiedTags;
            if (form.querySelector("#TagOverride").checked = config.TagOverride) {
                form.querySelector("#TagSources").removeAttribute("hidden");
                form.querySelector("#TagIncludeFilters").removeAttribute("hidden");
                form.querySelector("#TagMinimumWeightContainer").removeAttribute("hidden");
                form.querySelector("#TagMinimumWeightContainer").disabled = false;
                form.querySelector("#TagMaximumDepthContainer").removeAttribute("hidden");
                form.querySelector("#TagMaximumDepthContainer").disabled = false;
            }
            else {
                form.querySelector("#TagSources").setAttribute("hidden", "");
                form.querySelector("#TagIncludeFilters").setAttribute("hidden", "");
                form.querySelector("#TagMinimumWeightContainer").setAttribute("hidden", "");
                form.querySelector("#TagMinimumWeightContainer").disabled = true;
                form.querySelector("#TagMaximumDepthContainer").setAttribute("hidden", "");
                form.querySelector("#TagMaximumDepthContainer").disabled = true;
            }
            initSimpleList(form, "TagSources", config.TagSources.split(",").map(s => s.trim()).filter(s => s));
            initSimpleList(form, "TagIncludeFilters", config.TagIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
            form.querySelector("#TagMinimumWeight").value = config.TagMinimumWeight;
            form.querySelector("#TagMaximumDepth").value = config.TagMaximumDepth.toString();
            if (form.querySelector("#GenreOverride").checked = config.GenreOverride) {
                form.querySelector("#GenreSources").removeAttribute("hidden");
                form.querySelector("#GenreIncludeFilters").removeAttribute("hidden");
                form.querySelector("#GenreMinimumWeightContainer").removeAttribute("hidden");
                form.querySelector("#GenreMinimumWeightContainer").disabled = false;
                form.querySelector("#GenreMaximumDepthContainer").removeAttribute("hidden");
                form.querySelector("#GenreMaximumDepthContainer").disabled = false;
            }
            else {
                form.querySelector("#GenreSources").setAttribute("hidden", "");
                form.querySelector("#GenreIncludeFilters").setAttribute("hidden", "");
                form.querySelector("#GenreMinimumWeightContainer").setAttribute("hidden", "");
                form.querySelector("#GenreMinimumWeightContainer").disabled = true;
                form.querySelector("#GenreMaximumDepthContainer").setAttribute("hidden", "");
                form.querySelector("#GenreMaximumDepthContainer").disabled = true;
            }
            initSimpleList(form, "GenreSources", config.GenreSources.split(",").map(s => s.trim()).filter(s => s));
            initSimpleList(form, "GenreIncludeFilters", config.GenreIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
            form.querySelector("#GenreMinimumWeight").value = config.GenreMinimumWeight;
            form.querySelector("#GenreMaximumDepth").value = config.GenreMaximumDepth.toString();
            
            if (form.querySelector("#ContentRatingOverride").checked = config.ContentRatingOverride) {
                form.querySelector("#ContentRatingList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#ContentRatingList").setAttribute("hidden", "");
            }
            initSortableList(form, "ContentRatingList", config.ContentRatingList, config.ContentRatingOrder);
            if (form.querySelector("#ProductionLocationOverride").checked = config.ProductionLocationOverride) {
                form.querySelector("#ProductionLocationList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#ProductionLocationList").setAttribute("hidden", "");
            }
            initSortableList(form, "ProductionLocationList", config.ProductionLocationList, config.ProductionLocationOrder);

            // Provider settings
            initSimpleList(form, "ThirdPartyIdProviderList", config.ThirdPartyIdProviderList.map(s => s.trim()).filter(s => s));

            // Library settings
            form.querySelector("#AutoMergeVersions").checked = config.AutoMergeVersions || false;
            if (form.querySelector("#UseGroupsForShows").checked = config.UseGroupsForShows) {
                form.querySelector("#SeasonOrderingContainer").removeAttribute("hidden");
                form.querySelector("#SeasonOrdering").disabled = false;
            }
            else {
                form.querySelector("#SeasonOrderingContainer").setAttribute("hidden", "");
                form.querySelector("#SeasonOrdering").disabled = true;
            }
            form.querySelector("#SeasonOrdering").value = config.SeasonOrdering;
            form.querySelector("#CollectionGrouping").value = config.CollectionGrouping;
            form.querySelector("#CollectionMinSizeOfTwo").checked = config.CollectionMinSizeOfTwo;
            form.querySelector("#SeparateMovies").checked = config.SeparateMovies;
            form.querySelector("#FilterMovieLibraries").checked = config.FilterMovieLibraries;
            form.querySelector("#SpecialsPlacement").value = config.SpecialsPlacement === "Default" ? "AfterSeason" : config.SpecialsPlacement;
            form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked = config.MovieSpecialsAsExtraFeaturettes;
            form.querySelector("#AddTrailers").checked = config.AddTrailers;
            form.querySelector("#AddCreditsAsThemeVideos").checked = config.AddCreditsAsThemeVideos;
            form.querySelector("#AddCreditsAsSpecialFeatures").checked = config.AddCreditsAsSpecialFeatures;
            form.querySelector("#AddMissingMetadata").checked = config.AddMissingMetadata;

            // Media Folder settings

            form.querySelector("#IgnoredFolders").value = config.IgnoredFolders.join();
            form.querySelector("#VFS_AddReleaseGroup").checked = config.VFS_AddReleaseGroup;
            form.querySelector("#VFS_AddResolution").checked = config.VFS_AddResolution;
            form.querySelector("#VFS_AttachRoot").checked = config.VFS_AttachRoot;
            form.querySelector("#VFS_Location").value = config.VFS_Location;
            form.querySelector("#VFS_CustomLocation").value = config.VFS_CustomLocation || "";
            form.querySelector("#VFS_CustomLocation").disabled = config.VFS_Location !== "Custom";
            if (config.VFS_Location === "Custom") {
                form.querySelector("#VFS_CustomLocation").removeAttribute("hidden");
            }
            else {
                form.querySelector("#VFS_CustomLocation").setAttribute("hidden", "");
            }
            form.querySelector("#VFS_Enabled").checked = config.VFS_Enabled;
            form.querySelector("#LibraryFilteringMode").value = config.LibraryFilteringMode;
            mediaFolderSelector.innerHTML = `<option value="">Default settings for new media folders</option>` + config.MediaFolders
                .filter((mediaFolder) => !mediaFolder.IsVirtualRoot)
                .map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId},${mediaFolder.LibraryId}">${mediaFolder.LibraryName} (${mediaFolder.MediaFolderPath})</option>`)
                .join("");

            // SignalR settings
            form.querySelector("#SignalRAutoConnect").checked = config.SignalR_AutoConnectEnabled;
            form.querySelector("#SignalRAutoReconnectIntervals").value = config.SignalR_AutoReconnectInSeconds.join(", ");
            initSimpleList(form, "SignalREventSources", config.SignalR_EventSources);
            signalrMediaFolderSelector.innerHTML = `<option value="">Default settings for new media folders</option>` + config.MediaFolders
                .filter((mediaFolder) => !mediaFolder.IsVirtualRoot)
                .map((mediaFolder) => `<option value="${mediaFolder.MediaFolderId},${mediaFolder.LibraryId}">${mediaFolder.LibraryName} (${mediaFolder.MediaFolderPath})</option>`)
                .join("");
            form.querySelector("#SignalRDefaultFileEvents").checked = config.SignalR_FileEvents;
            form.querySelector("#SignalRDefaultRefreshEvents").checked = config.SignalR_RefreshEnabled;

            // User settings
            userSelector.innerHTML = `<option value="">Click here to select a user</option>` + users.map((user) => `<option value="${user.Id}">${user.Name}</option>`).join("");

            // Experimental settings
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
            case "establish-connection":
                Dashboard.showLoadingMsg();
                defaultSubmit(form)
                    .then(refreshSettings)
                    .then(getSignalrStatus)
                    .then(refreshSignalr)
                    .catch(onError);
                break;
            case "reset-connection":
                Dashboard.showLoadingMsg();
                resetConnectionSettings(form)
                    .then(refreshSettings)
                    .then(getSignalrStatus)
                    .then(refreshSignalr)
                    .catch(onError);
                break;
            case "remove-media-folder":
                removeMediaFolder(form).then(refreshSettings).catch(onError);
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

/**
 * Initialize a selectable list.
 * 
 * @param {HTMLFormElement} form
 * @param {string} name
 * @param {string[]} enabled
 * @param {string[]} order
 * @returns {void}
 */
function initSortableList(form, name, enabled, order) {
    let index = 0;
    const list = form.querySelector(`#${name} .checkboxList`);
    const listItems = Array.from(list.querySelectorAll(".listItem"))
        .map((item) => ({
            item,
            checkbox: item.querySelector("input[data-option]"),
            isSortable: item.className.includes("sortableOption"),
        }))
        .map(({ item, checkbox, isSortable }) => ({
            item,
            checkbox,
            isSortable,
            option: checkbox.dataset.option,
        }));
    list.innerHTML = "";
    for (const option of order) {
        const { item, checkbox, isSortable } = listItems.find((item) => item.option === option) || {};
        if (!item)
            continue;
        list.append(item);
        if (enabled.includes(option))
            checkbox.checked = true;
        if (isSortable)
            adjustSortableListElement(item, index++);
    }
}

/**
 * @param {HTMLFormElement} form
 * @param {string} name
 * @param {string[]} enabled
 * @returns {void}
 **/
function initSimpleList(form, name, enabled) {
    for (const item of Array.from(form.querySelectorAll(`#${name} .listItem input[data-option]`))) {
        if (enabled.includes(item.dataset.option))
            item.checked = true;
    }
}

/**
 * Retrieve the enabled state and order list from a sortable list.
 *
 * @param {HTMLFormElement} form
 * @param {string} name
 * @returns {[boolean, string[], string[]]}
 */
function retrieveSortableList(form, name) {
    const titleElements = Array.from(form.querySelectorAll(`#${name} .listItem input[data-option]`));
    const getValue = (el) => el.dataset.option;
    return [
        titleElements
            .filter((el) => el.checked)
            .map(getValue)
            .sort(),
        titleElements
            .map(getValue),
    ];
}

/**
 * Retrieve the enabled state from a simple list.
 *
 * @param {HTMLFormElement} form
 * @param {string} name - Name of the selector list to retrieve.
 * @returns {string[]}
 **/
function retrieveSimpleList(form, name) {
    return Array.from(form.querySelectorAll(`#${name} .listItem input[data-option]`))
        .filter(item => item.checked)
        .map(item => item.dataset.option)
        .sort();
}