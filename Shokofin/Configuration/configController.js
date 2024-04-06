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
            .map(str =>  {
                // Trim the start and end and convert to lower-case.
                str = str.trim().toLowerCase();
                return str;
            }),
    );

    // Filter out empty values.
    if (filteredSet.has(""))
        filteredSet.delete("");

    // Convert it back into an array.
    return Array.from(filteredSet);
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

async function defaultSubmit(form) {
    let config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);

    if (config.ApiKey !== "") {
        let publicUrl = form.querySelector("#PublicUrl").value;
        if (publicUrl.endsWith("/")) {
            publicUrl = publicUrl.slice(0, -1);
            form.querySelector("#PublicUrl").value = publicUrl;
        }
        const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);
        const filteringModeRaw = form.querySelector("#LibraryFilteringMode").value;
        const filteringMode = filteringModeRaw === "true" ? true : filteringModeRaw === "false" ? false : null;

        // Metadata settings
        config.TitleMainType = form.querySelector("#TitleMainType").value;
        config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
        config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
        config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
        config.DescriptionSource = form.querySelector("#DescriptionSource").value;
        config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
        config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;
    
        // Provider settings
        config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
        config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

        // Library settings
        config.VirtualFileSystem = form.querySelector("#VirtualFileSystem").checked;
        config.LibraryFilteringMode = filteringMode;
        config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
        config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
        config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
        config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
        config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
        config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
    
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
                let url = new URL(url);
                url = url.href;
            }
            catch (err) {
                try {
                    let url = new URL(`http://${url}:8111`);
                    url = url.href;
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
    const filteringModeRaw = form.querySelector("#LibraryFilteringMode").value;
    const filteringMode = filteringModeRaw === "true" ? true : filteringModeRaw === "false" ? false : null;

    // Metadata settings
    config.TitleMainType = form.querySelector("#TitleMainType").value;
    config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
    config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
    config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
    config.DescriptionSource = form.querySelector("#DescriptionSource").value;
    config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
    config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;

    // Provider settings
    config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
    config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

    // Library settings
    config.VirtualFileSystem = form.querySelector("#VirtualFileSystem").checked;
    config.LibraryFilteringMode = filteringMode;
    config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
    config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
    config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
    config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
    config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
    config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;

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
            form.querySelector("#UserSection").setAttribute("hidden", "");
            form.querySelector("#TagSection").setAttribute("hidden", "");
            form.querySelector("#AdvancedSection").setAttribute("hidden", "");
            form.querySelector("#ExperimentalSection").setAttribute("hidden", "");
        }

        const userId = form.querySelector("#UserSelector").value;
        loadUserConfig(form, userId, config);
    };

    const onError = (err) => {
        console.error(err);
        Dashboard.alert(`An error occurred; ${err.message}`);
        Dashboard.hideLoadingMsg();
    };

    userSelector.addEventListener("change", function () {
        loadUserConfig(page, this.value);
    });

    form.querySelector("#UserEnableSynchronization").addEventListener("change", function () {
        const disabled = !this.checked;
        form.querySelector("#SyncUserDataOnImport").disabled = disabled;
        form.querySelector("#SyncUserDataAfterPlayback").disabled = disabled;
        form.querySelector("#SyncUserDataUnderPlayback").disabled = disabled;
        form.querySelector("#SyncUserDataUnderPlaybackLive").disabled = disabled;
        form.querySelector("#SyncUserDataInitialSkipEventCount").disabled = disabled;
    });

    form.querySelector("#VirtualFileSystem").addEventListener("change", function () {
        form.querySelector("#LibraryFilteringMode").disabled = this.checked;
        if (this.checked) {
            form.querySelector("#LibraryFilteringModeContainer").setAttribute("hidden", "");
        }
        else {
            form.querySelector("#LibraryFilteringModeContainer").removeAttribute("hidden");
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

    page.addEventListener("viewshow", async function () {
        Dashboard.showLoadingMsg();
        try {
            const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            const users = await ApiClient.getUsers();

            // Connection settings
            form.querySelector("#Url").value = config.Url;
            form.querySelector("#Username").value = config.Username;
            form.querySelector("#Password").value = "";

            // Metadata settings
            form.querySelector("#TitleMainType").value = config.TitleMainType;
            form.querySelector("#TitleAlternateType").value = config.TitleAlternateType;
            form.querySelector("#TitleAllowAny").checked = config.TitleAllowAny;
            form.querySelector("#TitleAddForMultipleEpisodes").checked = config.TitleAddForMultipleEpisodes != null ? config.TitleAddForMultipleEpisodes : true;
            form.querySelector("#DescriptionSource").value = config.DescriptionSource;
            form.querySelector("#CleanupAniDBDescriptions").checked = config.SynopsisCleanMultiEmptyLines || config.SynopsisCleanLinks;
            form.querySelector("#MinimalAniDBDescriptions").checked = config.SynopsisRemoveSummary || config.SynopsisCleanMiscLines;

            // Provider settings
            form.querySelector("#AddAniDBId").checked = config.AddAniDBId;
            form.querySelector("#AddTMDBId").checked = config.AddTMDBId;

            // Library settings
            form.querySelector("#VirtualFileSystem").checked = config.VirtualFileSystem != null ? config.VirtualFileSystem : true;
            form.querySelector("#LibraryFilteringMode").value = `${config.LibraryFilteringMode != null ? config.LibraryFilteringMode : null}`;
            form.querySelector("#LibraryFilteringMode").disabled = form.querySelector("#VirtualFileSystem").checked;
            if (form.querySelector("#VirtualFileSystem").checked) {
                form.querySelector("#LibraryFilteringModeContainer").setAttribute("hidden", "");
            }
            else {
                form.querySelector("#LibraryFilteringModeContainer").removeAttribute("hidden");
            }
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
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;

            // User settings
            userSelector.innerHTML += users.map((user) => `<option value="${user.Id}">${user.Name}</option>`);

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
            case "user-settings":
                Dashboard.showLoadingMsg();
                syncUserSettings(form).then(refreshSettings).catch(onError);
                break;
        }
        return false;
    });
}
