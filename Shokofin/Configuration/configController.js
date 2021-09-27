const PluginConfig = {
    pluginId: "5216ccbf-d24a-4eb3-8a7e-7da4230b7052"
};

const Messages = {
    ConnectToShoko: "Please establish a connection to a running instance of Shoko Server before you continue.",
    InvalidCredentials: "An error occured while trying to authenticating the user using the provided credentials.",
    UnableToRender: "There was an error loading the page, please refresh once to see if that will fix it.",
};

async function loadUserConfig(page, userId, config) {
    if (!userId) {
        page.querySelector("#UserSettingsContainer").setAttribute("hidden", "");
        page.querySelector("#UserUsername").removeAttribute("required");
        Dashboard.hideLoadingMsg();
        return;
    }
    
    Dashboard.showLoadingMsg();

    // Get the configuration to use.
    if (!config) config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId)
    const userConfig = config.UserList.find((c) => userId === c.UserId) || { UserId: userId };

    // Configure the elements within the user container
    page.querySelector("#UserEnableSynchronization").checked = userConfig.EnableSynchronization || false;
    page.querySelector("#UserUsername").value = userConfig.Username || "";
    page.querySelector("#UserPassword").value = "";
    if (userConfig.Token) {
        page.querySelector("#UserDeleteContainer").removeAttribute("hidden");
        page.querySelector("#UserUsername").setAttribute("disabled", "");
        page.querySelector("#UserPasswordContainer").setAttribute("hidden", "");
        page.querySelector("#UserUsername").removeAttribute("required");
    }
    else {
        page.querySelector("#UserDeleteContainer").setAttribute("hidden", "");
        page.querySelector("#UserUsername").removeAttribute("disabled");
        page.querySelector("#UserPasswordContainer").removeAttribute("hidden");
        page.querySelector("#UserUsername").setAttribute("required", "");
    }

    // Show the user settings now if it was previously hidden.
    page.querySelector("#UserSettingsContainer").removeAttribute("hidden");

    Dashboard.hideLoadingMsg();
}

function getApiKey(username, password) {
    return ApiClient.fetch({
        dataType: "json",
        data: JSON.stringify({
            username,
            password,
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

        // Metadata settings
        config.TitleMainType = form.querySelector("#TitleMainType").value;
        config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
        config.DescriptionSource = form.querySelector("#DescriptionSource").value;
        config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
        config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
        config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;
    
        // Library settings
        config.SeriesGrouping = form.querySelector("#SeriesGrouping").value;
        config.BoxSetGrouping = form.querySelector("#BoxSetGrouping").value;
        config.FilterOnLibraryTypes = form.querySelector("#FilterOnLibraryTypes").checked;
        config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
        config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
    
        // Tag settings
        config.HideArtStyleTags = form.querySelector("#HideArtStyleTags").checked;
        config.HideSourceTags = form.querySelector("#HideSourceTags").checked;
        config.HideMiscTags = form.querySelector("#HideMiscTags").checked;
        config.HidePlotTags = form.querySelector("#HidePlotTags").checked;
        config.HideAniDbTags = form.querySelector("#HideAniDbTags").checked;
    
        // Advanced settings
        config.PublicHost = publicHost;
        config.PreferAniDbPoster = form.querySelector("#PreferAniDbPoster").checked;
        config.AddAniDBId = form.querySelector("#AddAniDBId").checked;

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
            
            // Only try to save a new token if a token is not already present.
            const username = form.querySelector("#UserUsername").value;
            const password = form.querySelector("#UserPassword").value;
            if (!userConfig.Token) try {
                const response = await getApiKey(username, password);
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
        if (host.endsWith("/")) {
            host = host.slice(0, -1);
            form.querySelector("#Host").value = host;
        }

        // Update the host if needed.
        if (config.Host !== host) {
            config.Host = host;
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

async function syncSettings(form) {
    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    let publicHost = form.querySelector("#PublicHost").value;
    if (publicHost.endsWith("/")) {
        publicHost = publicHost.slice(0, -1);
        form.querySelector("#PublicHost").value = publicHost;
    }

    // Metadata settings
    config.TitleMainType = form.querySelector("#TitleMainType").value;
    config.TitleAlternateType = form.querySelector("#TitleAlternateType").value;
    config.DescriptionSource = form.querySelector("#DescriptionSource").value;
    config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
    config.SynopsisCleanMiscLines = form.querySelector("#MinimalAniDBDescriptions").checked;
    config.SynopsisRemoveSummary = form.querySelector("#MinimalAniDBDescriptions").checked;

    // Library settings
    config.SeriesGrouping = form.querySelector("#SeriesGrouping").value;
    config.BoxSetGrouping = form.querySelector("#BoxSetGrouping").value;
    config.FilterOnLibraryTypes = form.querySelector("#FilterOnLibraryTypes").checked;
    config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
    config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;

    // Tag settings
    config.HideArtStyleTags = form.querySelector("#HideArtStyleTags").checked;
    config.HideSourceTags = form.querySelector("#HideSourceTags").checked;
    config.HideMiscTags = form.querySelector("#HideMiscTags").checked;
    config.HidePlotTags = form.querySelector("#HidePlotTags").checked;
    config.HideAniDbTags = form.querySelector("#HideAniDbTags").checked;

    // Advanced settings
    config.PublicHost = publicHost;
    config.PreferAniDbPoster = form.querySelector("#PreferAniDbPoster").checked;
    config.AddAniDBId = form.querySelector("#AddAniDBId").checked;

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
    const userId = form.querySelector("#UserSelector").value;
    if (!userId) return;

    const config = await ApiClient.getPluginConfiguration(PluginConfig.pluginId);
    let userConfig = config.UserList.find((c) => userId === c.UserId);
    if (!userConfig) {
        userConfig = { UserId: userId };
        config.UserList.push(userConfig);
    }
    
    // The user settings goes below here;
    userConfig.EnableSynchronization = form.querySelector("#UserEnableSynchronization").checked;
    
    // Only try to save a new token if a token is not already present.
    const username = form.querySelector("#UserUsername").value;
    const password = form.querySelector("#UserPassword").value;
    if (!userConfig.Token) try {
        const response = await getApiKey(username, password);
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
    const refershSettings = (config) => {
        if (config.ApiKey) {
            form.querySelector("#Host").setAttribute("disabled", "");
            form.querySelector("#Username").setAttribute("disabled", "");
            form.querySelector("#Password").value = "";
            form.querySelector("#ConnectionSetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionResetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").removeAttribute("hidden");
            form.querySelector("#LibrarySection").removeAttribute("hidden");
            form.querySelector("#UserSection").removeAttribute("hidden");
            form.querySelector("#TagSection").removeAttribute("hidden");
            form.querySelector("#AdvancedSection").removeAttribute("hidden");
        }
        else {
            form.querySelector("#Host").removeAttribute("disabled");
            form.querySelector("#Username").removeAttribute("disabled");
            form.querySelector("#ConnectionSetContainer").removeAttribute("hidden");
            form.querySelector("#ConnectionResetContainer").setAttribute("hidden", "");
            form.querySelector("#ConnectionSection").removeAttribute("hidden");
            form.querySelector("#MetadataSection").setAttribute("hidden", "");
            form.querySelector("#MetadataSection").setAttribute("hidden", "");
            form.querySelector("#LibrarySection").setAttribute("hidden", "");
            form.querySelector("#UserSection").setAttribute("hidden", "");
            form.querySelector("#TagSection").setAttribute("hidden", "");
            form.querySelector("#AdvancedSection").setAttribute("hidden", "");
        }

        const userId = form.querySelector("#UserSelector").value;
        loadUserConfig(page, userId, config);
    };

    const onError = (err) => {
        console.error(err);
        Dashboard.alert(`An error occurred; ${err.message}`);
        Dashboard.hideLoadingMsg();
    };

    userSelector.addEventListener("change", function () {
        loadUserConfig(page, this.value);
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
            form.querySelector("#DescriptionSource").value = config.DescriptionSource;
            form.querySelector("#CleanupAniDBDescriptions").checked = config.SynopsisCleanMultiEmptyLines || config.SynopsisCleanLinks;
            form.querySelector("#MinimalAniDBDescriptions").checked = config.SynopsisRemoveSummary || config.SynopsisCleanMiscLines;

            // Library settings
            form.querySelector("#SeriesGrouping").value = config.SeriesGrouping;
            form.querySelector("#BoxSetGrouping").value = config.BoxSetGrouping;
            form.querySelector("#FilterOnLibraryTypes").checked = config.FilterOnLibraryTypes;
            form.querySelector("#SpecialsPlacement").value = config.SpecialsPlacement;
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;

            // User settings
            userSelector.innerHTML += users.map((user) => `<option value="${user.Id}">${user.Name}</option>`);

            // Tag settings
            form.querySelector("#HideArtStyleTags").checked = config.HideArtStyleTags;
            form.querySelector("#HideSourceTags").checked = config.HideSourceTags;
            form.querySelector("#HideMiscTags").checked = config.HideMiscTags;
            form.querySelector("#HidePlotTags").checked = config.HidePlotTags;
            form.querySelector("#HideAniDbTags").checked = config.HideAniDbTags;

            // Advanced settings
            form.querySelector("#PublicHost").value = config.PublicHost;
            form.querySelector("#PreferAniDbPoster").checked = config.PreferAniDbPoster;
            form.querySelector("#AddAniDBId").checked = config.AddAniDBId;

            if (!config.ApiKey) {
                Dashboard.alert(Messages.ConnectToShoko);
            }

            refershSettings(config);
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
                defaultSubmit(form).then(refershSettings).catch(onError);
                break;
            case "settings":
                Dashboard.showLoadingMsg();
                syncSettings(form).then(refershSettings).catch(onError);
                break;
            case "reset-connection":
                Dashboard.showLoadingMsg();
                resetConnectionSettings(form).then(refershSettings).catch(onError);
                break;
            case "unlink-user":
                unlinkUser(form).then(refershSettings).catch(onError);
                break;
            case "user-settings":
                Dashboard.showLoadingMsg();
                syncUserSettings(form).then(refershSettings).catch(onError);
                break;
        }
        return false;
    });
}
