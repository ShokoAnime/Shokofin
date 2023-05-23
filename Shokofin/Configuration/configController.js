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
 function filterIgnoredExtensions(value) {
    // We convert to a set to filter out duplicate values.
    const filteredSet = new Set(
        value
            // Split the values at every space, tab, comma.
            .split(/[\s,]+/g)
            // Sanitize inputs.
            .map(str =>  {
                // Trim the start and end and convert to lower-case.
                str = str.trim().toLowerCase();

                // Add a dot if it's missing.
                if (str[0] !== ".")
                    str = "." + str;

                return str;
            }),
        );

    // Filter out empty values.
    if (filteredSet.has(""))
        filteredSet.delete("");

    // Convert it back into an array.
    return Array.from(filteredSet);
}

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
        url: ApiClient.getUrl("Plugin/Shokofin/GetApiKey"),
    });
}

async function defaultSubmit(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);

    if (config.ApiKey !== "") {
        let publicHost = form.querySelector("#PublicHost").value;
        if (publicHost.endsWith("/")) {
            publicHost = publicHost.slice(0, -1);
            form.querySelector("#PublicHost").value = publicHost;
        }
        const ignoredFileExtensions = filterIgnoredExtensions(form.querySelector("#IgnoredFileExtensions").value);
        const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);
        const filteringModeRaw = form.querySelector("#LibraryFilteringMode").value;
        const filteringMode = filteringModeRaw === "true" ? true : filteringModeRaw === "false" ? false : null;

        // Metadata settings
        config.TitleMainType = form.querySelector("#TitleMainType").value;
        config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
        config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
        config.DescriptionSource = form.querySelector("#DescriptionSource").value;
        config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
        config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;
    
        // Provider settings
        config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
        config.AddTvDBId = form.querySelector("#AddTvDBId").checked;
        config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

        // Library settings
        config.LibraryFilteringMode = filteringMode;
        config.SeriesGrouping = form.querySelector("#SeriesGrouping").value;
        config.BoxSetGrouping = form.querySelector("#BoxSetGrouping").value;
        config.FilterOnLibraryTypes = form.querySelector("#FilterOnLibraryTypes").checked;
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
        config.SentryEnabled = form.querySelector("#SentryEnabled").checked;
        config.PublicHost = publicHost;
        config.IgnoredFileExtensions = ignoredFileExtensions;
        form.querySelector("#IgnoredFileExtensions").value = ignoredFileExtensions.join(" ");
        config.IgnoredFolders = ignoredFolders;
        form.querySelector("#IgnoredFolders").value = ignoredFolders.join();

        // Experimental settings
        config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
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
        let host = form.querySelector("#Host").value;
        if (!host) {
            host = "http://localhost:8111";
        }
        else {
            try {
                let url = new URL(host);
                host = url.href;
            }
            catch (err) {
                try {
                    let url = new URL(`http://${host}:8111`);
                    host = url.href;
                }
                catch (err2) {
                  throw err;
                }
            }
        }
        if (host.endsWith("/")) {
            host = host.slice(0, -1);
        }

        // Update the host if needed.
        if (config.Host !== host) {
            config.Host = host;
            form.querySelector("#Host").value = host;
            let result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
            Dashboard.processPluginConfigurationUpdateResult(result);
        }

        const username = form.querySelector("#Username").value;
        const password = form.querySelector("#Password").value;
        try {
            const response = await getApiKey(username, password);
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

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function disableSentry(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    form.querySelector("#SentryEnabled").checked = false;

    // Connection settings
    config.SentryEnabled = false;

    const result = await ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config);
    Dashboard.processPluginConfigurationUpdateResult(result);

    return config;
}

async function syncSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    let publicHost = form.querySelector("#PublicHost").value;
    if (publicHost.endsWith("/")) {
        publicHost = publicHost.slice(0, -1);
        form.querySelector("#PublicHost").value = publicHost;
    }
    const ignoredFileExtensions = filterIgnoredExtensions(form.querySelector("#IgnoredFileExtensions").value);
    const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);
    const filteringModeRaw = form.querySelector("#LibraryFilteringMode").value;
    const filteringMode = filteringModeRaw === "true" ? true : filteringModeRaw === "false" ? false : null;

    // Metadata settings
    config.TitleMainType = form.querySelector("#TitleMainType").value;
    config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
    config.TitleAddForMultipleEpisodes = form.querySelector("#TitleAddForMultipleEpisodes").checked;
    config.DescriptionSource = form.querySelector("#DescriptionSource").value;
    config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
    config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;

    // Provider settings
    config.AddAniDBId = form.querySelector("#AddAniDBId").checked;
    config.AddTvDBId = form.querySelector("#AddTvDBId").checked;
    config.AddTMDBId = form.querySelector("#AddTMDBId").checked;

    // Library settings
    config.LibraryFilteringMode = filteringMode;
    config.SeriesGrouping = form.querySelector("#SeriesGrouping").value;
    config.BoxSetGrouping = form.querySelector("#BoxSetGrouping").value;
    config.FilterOnLibraryTypes = form.querySelector("#FilterOnLibraryTypes").checked;
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
    config.SentryEnabled = form.querySelector("#SentryEnabled").checked;
    config.PublicHost = publicHost;
    config.IgnoredFileExtensions = ignoredFileExtensions;
    form.querySelector("#IgnoredFileExtensions").value = ignoredFileExtensions.join(" ");
    config.IgnoredFolders = ignoredFolders;
    form.querySelector("#IgnoredFolders").value = ignoredFolders.join();

    // Experimental settings
    config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
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
        if (config.SentryEnabled == null) {
            form.querySelector("#Host").removeAttribute("disabled");
            form.querySelector("#Username").removeAttribute("disabled");
            form.querySelector("#ConsentSection").removeAttribute("hidden");
            form.querySelector("#ConnectionSetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionResetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionSection").setAttribute("hidden", "");
            form.querySelector("#MetadataSection").setAttribute("hidden", "");
            form.querySelector("#ProviderSection").setAttribute("hidden", "");
            form.querySelector("#LibrarySection").setAttribute("hidden", "");
            form.querySelector("#UserSection").setAttribute("hidden", "");
            form.querySelector("#TagSection").setAttribute("hidden", "");
            form.querySelector("#AdvancedSection").setAttribute("hidden", "");
            form.querySelector("#ExperimentalSection").setAttribute("hidden", "");
        }
        else if (config.ApiKey) {
            form.querySelector("#Host").setAttribute("disabled", "");
            form.querySelector("#Username").setAttribute("disabled", "");
            form.querySelector("#Password").value = "";
            form.querySelector("#ConsentSection").setAttribute("hidden", "");
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
            form.querySelector("#Host").removeAttribute("disabled");
            form.querySelector("#Username").removeAttribute("disabled");
            form.querySelector("#ConsentSection").setAttribute("hidden", "");
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
    });

    page.addEventListener("viewshow", async function () {
        Dashboard.showLoadingMsg();
        try {
            const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
            const users = await ApiClient.getUsers();

            // Connection settings
            form.querySelector("#Host").value = config.Host;
            form.querySelector("#Username").value = config.Username;
            form.querySelector("#Password").value = "";

            // Metadata settings
            form.querySelector("#TitleMainType").value = config.TitleMainType;
            form.querySelector("#TitleAlternateType").value = config.TitleAlternateType;
            form.querySelector("#TitleAddForMultipleEpisodes").checked = config.TitleAddForMultipleEpisodes || true;
            form.querySelector("#DescriptionSource").value = config.DescriptionSource;
            form.querySelector("#CleanupAniDBDescriptions").checked = config.SynopsisCleanMultiEmptyLines || config.SynopsisCleanLinks;
            form.querySelector("#MinimalAniDBDescriptions").checked = config.SynopsisRemoveSummary || config.SynopsisCleanMiscLines;

            // Provider settings
            form.querySelector("#AddAniDBId").checked = config.AddAniDBId;
            form.querySelector("#AddTvDBId").checked = config.AddTvDBId;
            form.querySelector("#AddTMDBId").checked = config.AddTMDBId;

            // Library settings
            form.querySelector("#LibraryFilteringMode").value = `${config.LibraryFilteringMode != null ? config.LibraryFilteringMode : null}`;
            form.querySelector("#SeriesGrouping").value = config.SeriesGrouping;
            form.querySelector("#BoxSetGrouping").value = config.BoxSetGrouping;
            form.querySelector("#FilterOnLibraryTypes").checked = config.FilterOnLibraryTypes;
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
            form.querySelector("#SentryEnabled").checked = config.SentryEnabled == null ? true : config.SentryEnabled;
            form.querySelector("#PublicHost").value = config.PublicHost;
            form.querySelector("#IgnoredFileExtensions").value = config.IgnoredFileExtensions.join(" ");
            form.querySelector("#IgnoredFolders").value = config.IgnoredFolders.join();

            // Experimental settings
            form.querySelector("#SeasonOrdering").value = config.SeasonOrdering;
            form.querySelector("#EXPERIMENTAL_AutoMergeVersions").checked = config.EXPERIMENTAL_AutoMergeVersions || false;
            form.querySelector("#EXPERIMENTAL_SplitThenMergeMovies").checked = config.EXPERIMENTAL_SplitThenMergeMovies || true;
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
            case "disable-sentry":
                Dashboard.showLoadingMsg();
                disableSentry(form).then(refreshSettings).catch(onError);
        }
        return false;
    });
}
