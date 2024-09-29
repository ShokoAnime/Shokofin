export default function (view) {
let show = false;
let hide = false;
view.addEventListener("viewshow", () => show = true);
view.addEventListener("viewhide", () => hide = true);

/**
 * @type {import("./Common.js").ApiClientPrototype}
 */
const ApiClient = globalThis.ApiClient;

/**
 * @type {import("./Common.js").DashboardPrototype}
 */
const Dashboard = globalThis.Dashboard;

/**
 * @type {Promise<import("./Common.js")>}
 */
const promise = import(ApiClient.getUrl("/web/" + Dashboard.getPluginUrl("Shoko.Common.js")));
promise.then(({
    ShokoApiClient,
    State,
    createControllerFactory,
    handleError,
    renderCheckboxList,
    renderReadonlyList,
    renderSortableCheckboxList,
    retrieveCheckboxList,
    retrieveSortableCheckboxList,
    updateTabs,
}) => {

//#region Constants

/**
 * @typedef {"Connection" | "Metadata_Title" | "Metadata_Description" | "Metadata_TagGenre" | "Metadata_Misc" | "Metadata_ThirdPartyIntegration" | "Library_Basic" | "Library_Collection" | "Library_New" | "Library_Existing" | "Library_Experimental" | "VFS_Basic" | "VFS_Location" | "User" | "SignalR_Connection" | "SignalR_Basic" | "SignalR_Library_New" | "SignalR_Library_Existing" | "Misc" | "Utilities"} SectionType
 */

const MaxDebugPresses = 7;

/**
 * @type {SectionType[]}
 */
const Sections = [
    "Connection",
    "Metadata_Title",
    "Metadata_Description",
    "Metadata_TagGenre",
    "Metadata_Misc",
    "Metadata_ThirdPartyIntegration",
    "Library_Basic",
    "Library_Collection",
    "Library_New",
    "Library_Existing",
    "Library_Experimental",
    "VFS_Basic",
    "VFS_Location",
    "User",
    "SignalR_Connection",
    "SignalR_Basic",
    "SignalR_Library_New",
    "SignalR_Library_Existing",
    "Misc",
    "Utilities",
];

const Messages = {
    ExpertModeCountdown: "Press <count> more times to <toggle> expert mode.",
    ExpertModeEnabled: "Expert mode enabled.",
    ExpertModeDisabled: "Expert mode disabled.",
    ConnectToShoko: "Please establish a connection to a running instance of Shoko Server before you continue.",
    ConnectedToShoko: "Connection established.",
    DisconnectedToShoko: "Connection has been reset.",
    InvalidCredentials: "An error occurred while trying to authenticating the user using the provided credentials.",
};

//#endregion

//#region Controller Logic

createControllerFactory({
    show,
    hide,
    events: {
        onInit() {
            const view = this;
            const form = view.querySelector("form");

            form.querySelector("#ServerVersion").addEventListener("click", async function () {
                if (++State.expertPresses === MaxDebugPresses) {
                    State.expertPresses = 0;
                    State.expertMode = !State.expertMode;
                    const config = await toggleExpertMode(State.expertMode);
                    await updateView(view, form, config);
                    return;
                }
                if (State.expertPresses >= 3)
                    Dashboard.alert(Messages.ExpertModeCountdown.replace("<count>", MaxDebugPresses - State.expertPresses).replace("<toggle>", State.expertMode ? "disable" : "enable"));
            });

            form.querySelector("#UserSelector").addEventListener("change", function () {
                applyUserConfigToForm(form, this.value);
            });

            form.querySelector("#MediaFolderSelector").addEventListener("change", function () {
                applyLibraryConfigToForm(form, this.value);
            });

            form.querySelector("#SignalRMediaFolderSelector").addEventListener("change", function () {
                applySignalrLibraryConfigToForm(form, this.value);
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
                    form.querySelector("#VFS_CustomLocationContainer").removeAttribute("hidden");
                }
                else {
                    form.querySelector("#VFS_CustomLocationContainer").setAttribute("hidden", "");
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

            form.addEventListener("submit", function (event) {
                event.preventDefault();
                if (!event.submitter) return;
                switch (event.submitter.name) {
                    case "settings":
                        Dashboard.showLoadingMsg();
                        syncSettings(form)
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                    case "remove-library":
                        removeLibraryConfig(form)
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                    case "unlink-user":
                        removeUserConfig(form)
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                    case "signalr-connect":
                        ShokoApiClient.signalrConnect()
                            .then((status) => updateSignalrStatus(form, status))
                            .catch(handleError);
                        break;
                    case "signalr-disconnect":
                        ShokoApiClient.signalrDisconnect()
                            .then((status) => updateSignalrStatus(form, status))
                            .catch(handleError);
                        break;
                    case "reset-connection":
                        Dashboard.showLoadingMsg();
                        resetConnection(form)
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                    default:
                    case "establish-connection":
                        Dashboard.showLoadingMsg();
                        defaultSubmit(form)
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                }
                return false;
            });
        },

        async onShow() {
            const view = this;
            const form = view.querySelector("form");

            // Apply the configuration to the form.
            await applyConfigToForm(form, State.config);

            // Update the view.
            await updateView(view, form, State.config);

            // Show the alert if we're not connected.
            if (!State.connected) {
                Dashboard.alert(Messages.ConnectToShoko);
            }
        },

        onHide() {
            const form = this.querySelector("form");
            applyFormToConfig(form, State.config);
        },
    }
})(view);

/**
 * Update the view to reflect the current state.
 *
 * @param {HTMLDivElement} view - The view element.
 * @param {HTMLFormElement} form - The form element.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns {Promise<void>}
 */
async function updateView(view, form, config) {
    State.config = config;
    State.expertPresses = 0;
    State.expertMode = config.ExpertMode;
    State.connected = Boolean(config.ApiKey);

    if (State.expertMode) {
        form.classList.add("expert-mode");
    }
    else {
        form.classList.remove("expert-mode");
    }

    if (!config.CanCreateSymbolicLinks) {
        form.querySelector("#WindowsSymLinkWarning1").removeAttribute("hidden");
        form.querySelector("#WindowsSymLinkWarning2").removeAttribute("hidden");
    }

    if (State.connected) {
        form.querySelector("#Url").removeAttribute("required");
        form.querySelector("#Username").removeAttribute("required");
    }
    else {
        form.querySelector("#Url").setAttribute("required", "");
        form.querySelector("#Username").setAttribute("required", "");
    }

    /**
     * @type {SectionType[]}
     */
    const activeSections = [];
    switch (State.currentTab) {
        case "connection":
            activeSections.push("Connection");

            if (config.ServerVersion) {
                let version = `Version ${config.ServerVersion.Version}`;
                const extraDetails = [
                    config.ServerVersion.ReleaseChannel || "",
                    config.ServerVersion.Commit ? config.ServerVersion.Commit.slice(0, 7) : "",
                ].filter(s => s).join(", ");
                if (extraDetails)
                    version += ` (${extraDetails})`;
                form.querySelector("#ServerVersion").value = version;
            }
            else {
                form.querySelector("#ServerVersion").value = "Version N/A";
            }

            if (State.connected) {
                form.querySelector("#Url").removeAttribute("required");
                form.querySelector("#Username").removeAttribute("required");
                form.querySelector("#Url").setAttribute("disabled", "");
                form.querySelector("#PublicUrl").setAttribute("disabled", "");
                form.querySelector("#Username").setAttribute("disabled", "");
                form.querySelector("#Password").value = "";
                form.querySelector("#ConnectionSetContainer").setAttribute("hidden", "");
                form.querySelector("#ConnectionResetContainer").removeAttribute("hidden");
            }
            else {
                form.querySelector("#Url").setAttribute("required", "");
                form.querySelector("#Username").setAttribute("required", "");
                form.querySelector("#Url").removeAttribute("disabled");
                form.querySelector("#PublicUrl").removeAttribute("disabled");
                form.querySelector("#Username").removeAttribute("disabled");
                form.querySelector("#ConnectionSetContainer").removeAttribute("hidden");
                form.querySelector("#ConnectionResetContainer").setAttribute("hidden", "");
            }
            break;

        case "metadata":
            activeSections.push("Metadata_Title", "Metadata_Description", "Metadata_TagGenre", "Metadata_Misc", "Metadata_ThirdPartyIntegration");
            break;

        case "library":
            activeSections.push("Library_Basic", "Library_Collection", "Library_New", "Library_Existing", "Library_Experimental");
            break;

        case "vfs":
            activeSections.push("VFS_Basic", "VFS_Location");

            await applyLibraryConfigToForm(form, form.querySelector("#MediaFolderSelector").value, config);
            break;

        case "users":
            activeSections.push("User");

            await applyUserConfigToForm(form, form.querySelector("#UserSelector").value, config);
            break;

        case "signalr":
            activeSections.push("SignalR_Connection", "SignalR_Basic", "SignalR_Library_New", "SignalR_Library_Existing");

            await applySignalrLibraryConfigToForm(form, form.querySelector("#SignalRMediaFolderSelector").value, config);
            break;

        case "misc":
            activeSections.push("Misc");
            break;

        case "utilities":
            activeSections.push("Utilities");
            break;
    }

    for (const sectionName of Sections) {
        const id = `#${sectionName}_Section`;
        const active = activeSections.includes(sectionName);
        if (active) {
            form.querySelector(id).removeAttribute("hidden");
        }
        else {
            form.querySelector(id).setAttribute("hidden", "");
        }
    }

    updateTabs(view);
}

/**
 * Update the SignalR status.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {SignalRStatus} status - The SignalR status.
 */
function updateSignalrStatus(form, status) {
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
}

//#endregion

//#region Form → Configuration

/**
 * Apply a form to a configuration object.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 */
function applyFormToConfig(form, config) {
    switch (State.currentTab) {
        case "metadata": {
            config.TitleMainOverride = form.querySelector("#TitleMainOverride").checked;
            ([config.TitleMainList, config.TitleMainOrder] = retrieveSortableCheckboxList(form, "TitleMainList"));
            config.TitleAlternateOverride = form.querySelector("#TitleAlternateOverride").checked;
            ([config.TitleAlternateList, config.TitleAlternateOrder] = retrieveSortableCheckboxList(form, "TitleAlternateList"));
            config.TitleAllowAny = form.querySelector("#TitleAllowAny").checked;
            config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
            config.DescriptionSourceOverride = form.querySelector("#DescriptionSourceOverride").checked;
            ([config.DescriptionSourceList, config.DescriptionSourceOrder] = retrieveSortableCheckboxList(form, "DescriptionSourceList"));
            config.SynopsisCleanLinks = form.querySelector("#CleanupAniDBDescriptions").checked;
            config.SynopsisCleanMultiEmptyLines = form.querySelector("#CleanupAniDBDescriptions").checked;
            config.SynopsisCleanMiscLines = form.querySelector("#CleanupAniDBDescriptions").checked;
            config.SynopsisRemoveSummary = form.querySelector("#CleanupAniDBDescriptions").checked;
            config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
            config.TagOverride = form.querySelector("#TagOverride").checked;
            config.TagSources = retrieveCheckboxList(form, "TagSources").join(", ");
            config.TagIncludeFilters = retrieveCheckboxList(form, "TagIncludeFilters").join(", ");
            config.TagMinimumWeight = form.querySelector("#TagMinimumWeight").value;
            config.TagMaximumDepth = parseInt(form.querySelector("#TagMaximumDepth").value, 10);
            config.GenreOverride = form.querySelector("#GenreOverride").checked;
            config.GenreSources = retrieveCheckboxList(form, "GenreSources").join(", ");
            config.GenreIncludeFilters = retrieveCheckboxList(form, "GenreIncludeFilters").join(", ");
            config.GenreMinimumWeight = form.querySelector("#GenreMinimumWeight").value;
            config.GenreMaximumDepth = parseInt(form.querySelector("#GenreMaximumDepth").value, 10);
            config.ContentRatingOverride = form.querySelector("#ContentRatingOverride").checked;
            ([config.ContentRatingList, config.ContentRatingOrder] = retrieveSortableCheckboxList(form, "ContentRatingList"));
            config.ProductionLocationOverride = form.querySelector("#ProductionLocationOverride").checked;
            ([config.ProductionLocationList, config.ProductionLocationOrder] = retrieveSortableCheckboxList(form, "ProductionLocationList"));
            config.ThirdPartyIdProviderList = retrieveCheckboxList(form, "ThirdPartyIdProviderList");
            break;
        }

        case "library": {
            const libraryId = form.querySelector("#MediaFolderSelector").value.split(",");
            const mediaFolders = libraryId ? config.MediaFolders.filter((m) => m.LibraryId === libraryId) : undefined;

            config.AutoMergeVersions = form.querySelector("#AutoMergeVersions").checked;
            config.UseGroupsForShows = form.querySelector("#UseGroupsForShows").checked;
            config.SeasonOrdering = form.querySelector("#SeasonOrdering").value;
            config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
            config.FilterMovieLibraries = form.querySelector("#FilterMovieLibraries").checked;
            config.SpecialsPlacement = form.querySelector("#SpecialsPlacement").value;
            config.MovieSpecialsAsExtraFeaturettes = form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked;
            config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

            config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
            config.CollectionMinSizeOfTwo = form.querySelector("#CollectionMinSizeOfTwo").checked;

            config.VFS_Enabled = form.querySelector("#VFS_Enabled").checked;
            config.LibraryFilteringMode = form.querySelector("#LibraryFilteringMode").value;
            if (mediaFolders) {
                for (const c of mediaFolders) {
                    c.IsVirtualFileSystemEnabled = form.querySelector("#MediaFolderVirtualFileSystem").checked;
                    c.LibraryFilteringMode = form.querySelector("#MediaFolderLibraryFilteringMode").value;
                }
            }
            break;
        }

        case "vfs": {
            config.AddTrailers = form.querySelector("#AddTrailers").checked;
            config.AddCreditsAsThemeVideos = form.querySelector("#AddCreditsAsThemeVideos").checked;
            config.AddCreditsAsSpecialFeatures = form.querySelector("#AddCreditsAsSpecialFeatures").checked;
            config.VFS_AddReleaseGroup = form.querySelector("#VFS_AddReleaseGroup").checked;
            config.VFS_AddResolution = form.querySelector("#VFS_AddResolution").checked;

            config.VFS_ResolveLinks = form.querySelector("#VFS_ResolveLinks").checked;
            config.VFS_AttachRoot = form.querySelector("#VFS_AttachRoot").checked;
            config.VFS_Location = form.querySelector("#VFS_Location").value;
            config.VFS_CustomLocation = form.querySelector("#VFS_CustomLocation").value.trim() || null;
            break;
        }

        case "users": {
            const userId = form.querySelector("#UserSelector").value;
            if (userId) {
                let userConfig = config.UserList.find((c) => userId === c.UserId);
                if (!userConfig) {
                    userConfig = { UserId: userId };
                    config.UserList.push(userConfig);
                }

                userConfig.EnableSynchronization = form.querySelector("#UserEnableSynchronization").checked;
                userConfig.SyncUserDataOnImport = form.querySelector("#SyncUserDataOnImport").checked;
                userConfig.SyncUserDataAfterPlayback = form.querySelector("#SyncUserDataAfterPlayback").checked;
                userConfig.SyncUserDataUnderPlayback = form.querySelector("#SyncUserDataUnderPlayback").checked;
                userConfig.SyncUserDataUnderPlaybackLive = form.querySelector("#SyncUserDataUnderPlaybackLive").checked;
                userConfig.SyncUserDataInitialSkipEventCount = form.querySelector("#SyncUserDataInitialSkipEventCount").checked ? 2 : 0;
                userConfig.SyncUserDataUnderPlaybackAtEveryXTicks = 6;
                userConfig.SyncUserDataUnderPlaybackLiveThreshold = 125000000; // 12.5s
                userConfig.SyncRestrictedVideos = form.querySelector("#SyncRestrictedVideos").checked;
                if (!userConfig.Token) {
                    const username = form.querySelector("#UserUsername").value;
                    userConfig.Username = username;
                }
            }
            break;
        }
        case "signalr": {
            const reconnectIntervals = filterReconnectIntervals(form.querySelector("#SignalRAutoReconnectIntervals").value);
            const libraryId = form.querySelector("#SignalRMediaFolderSelector").value;
            const mediaFolders = libraryId ? config.MediaFolders.filter((m) => m.LibraryId === libraryId) : undefined;

            config.SignalR_AutoConnectEnabled = form.querySelector("#SignalRAutoConnect").checked;
            config.SignalR_AutoReconnectInSeconds = reconnectIntervals;
            form.querySelector("#SignalRAutoReconnectIntervals").value = reconnectIntervals.join(", ");
            config.SignalR_EventSources = retrieveCheckboxList(form, "SignalREventSources");

            config.SignalR_FileEvents = form.querySelector("#SignalRDefaultFileEvents").checked;
            config.SignalR_RefreshEnabled = form.querySelector("#SignalRDefaultRefreshEvents").checked;

            if (mediaFolders) {
                for (const c of mediaFolders) {
                    c.IsFileEventsEnabled = form.querySelector("#SignalRFileEvents").checked;
                    c.IsRefreshEventsEnabled = form.querySelector("#SignalRRefreshEvents").checked;
                }
            }
            break;
        }

        case "misc": {
            const ignoredFolders = filterIgnoredFolders(form.querySelector("#IgnoredFolders").value);

            config.Misc_ShowInMenu = form.querySelector("#Misc_ShowInMenu").checked;
            config.IgnoredFolders = ignoredFolders;
            form.querySelector("#IgnoredFolders").value = ignoredFolders.join(", ");

            config.EXPERIMENTAL_MergeSeasons = form.querySelector("#EXPERIMENTAL_MergeSeasons").checked;
            break;
        }
    }
}

//#endregion

//#region Configuration → Form

/**
 * Apply the given configuration to the form.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 */
async function applyConfigToForm(form, config) {
    switch (State.currentTab) {
        case "connection": {
            form.querySelector("#Url").value = config.Url;
            form.querySelector("#PublicUrl").value = config.PublicUrl;
            form.querySelector("#Username").value = config.Username;
            form.querySelector("#Password").value = "";
            break;
        }

        case "metadata": {
            if (form.querySelector("#TitleMainOverride").checked = config.TitleMainOverride) {
                form.querySelector("#TitleMainList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#TitleMainList").setAttribute("hidden", "");
            }
            renderSortableCheckboxList(form, "TitleMainList", config.TitleMainList, config.TitleMainOrder);
            if (form.querySelector("#TitleAlternateOverride").checked = config.TitleAlternateOverride) {
                form.querySelector("#TitleAlternateList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#TitleAlternateList").setAttribute("hidden", "");
            }
            renderSortableCheckboxList(form, "TitleAlternateList", config.TitleAlternateList, config.TitleAlternateOrder);
            form.querySelector("#TitleAllowAny").checked = config.TitleAllowAny;
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;
            if (form.querySelector("#DescriptionSourceOverride").checked = config.DescriptionSourceOverride) {
                form.querySelector("#DescriptionSourceList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#DescriptionSourceList").setAttribute("hidden", "");
            }
            renderSortableCheckboxList(form, "DescriptionSourceList", config.DescriptionSourceList, config.DescriptionSourceOrder);
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
            renderCheckboxList(form, "TagSources", config.TagSources.split(",").map(s => s.trim()).filter(s => s));
            renderCheckboxList(form, "TagIncludeFilters", config.TagIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
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
            renderCheckboxList(form, "GenreSources", config.GenreSources.split(",").map(s => s.trim()).filter(s => s));
            renderCheckboxList(form, "GenreIncludeFilters", config.GenreIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
            form.querySelector("#GenreMinimumWeight").value = config.GenreMinimumWeight;
            form.querySelector("#GenreMaximumDepth").value = config.GenreMaximumDepth.toString();

            if (form.querySelector("#ContentRatingOverride").checked = config.ContentRatingOverride) {
                form.querySelector("#ContentRatingList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#ContentRatingList").setAttribute("hidden", "");
            }
            renderSortableCheckboxList(form, "ContentRatingList", config.ContentRatingList, config.ContentRatingOrder);
            if (form.querySelector("#ProductionLocationOverride").checked = config.ProductionLocationOverride) {
                form.querySelector("#ProductionLocationList").removeAttribute("hidden");
            }
            else {
                form.querySelector("#ProductionLocationList").setAttribute("hidden", "");
            }
            renderSortableCheckboxList(form, "ProductionLocationList", config.ProductionLocationList, config.ProductionLocationOrder);
            renderCheckboxList(form, "ThirdPartyIdProviderList", config.ThirdPartyIdProviderList.map(s => s.trim()).filter(s => s));
            break;
        }

        case "library": {
            const libraries = config.MediaFolders
                .reduce((acc, mediaFolder) => {
                    if (mediaFolder.IsVirtualRoot)
                        return acc;

                    if (acc.find((m) => m.LibraryId === mediaFolder.LibraryId))
                        return acc;

                    acc.push(mediaFolder);
                    return acc;
                }, []);

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
            form.querySelector("#SeparateMovies").checked = config.SeparateMovies;
            form.querySelector("#FilterMovieLibraries").checked = config.FilterMovieLibraries;
            form.querySelector("#SpecialsPlacement").value = config.SpecialsPlacement === "Default" ? "AfterSeason" : config.SpecialsPlacement;
            form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked = config.MovieSpecialsAsExtraFeaturettes;
            form.querySelector("#AddMissingMetadata").checked = config.AddMissingMetadata;

            form.querySelector("#CollectionGrouping").value = config.CollectionGrouping;
            form.querySelector("#CollectionMinSizeOfTwo").checked = config.CollectionMinSizeOfTwo;

            form.querySelector("#VFS_Enabled").checked = config.VFS_Enabled;
            form.querySelector("#LibraryFilteringMode").value = config.LibraryFilteringMode;
            form.querySelector("#MediaFolderSelector").innerHTML = `<option value="">Click here to select a library</option>` + libraries
                .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}${config.ExpertMode ? ` (${library.LibraryId})` : ""}</option>`)
                .join("");
            break;
        }

        case "vfs": {
            form.querySelector("#AddTrailers").checked = config.AddTrailers;
            form.querySelector("#AddCreditsAsThemeVideos").checked = config.AddCreditsAsThemeVideos;
            form.querySelector("#AddCreditsAsSpecialFeatures").checked = config.AddCreditsAsSpecialFeatures;
            form.querySelector("#VFS_AddReleaseGroup").checked = config.VFS_AddReleaseGroup;
            form.querySelector("#VFS_AddResolution").checked = config.VFS_AddResolution;

            form.querySelector("#VFS_ResolveLinks").checked = config.VFS_ResolveLinks;
            form.querySelector("#VFS_AttachRoot").checked = config.VFS_AttachRoot;
            form.querySelector("#VFS_Location").value = config.VFS_Location;
            form.querySelector("#VFS_CustomLocation").value = config.VFS_CustomLocation || "";
            form.querySelector("#VFS_CustomLocation").disabled = config.VFS_Location !== "Custom";
            if (config.VFS_Location === "Custom") {
                form.querySelector("#VFS_CustomLocationContainer").removeAttribute("hidden");
            }
            else {
                form.querySelector("#VFS_CustomLocationContainer").setAttribute("hidden", "");
            }
            break;
        }

        case "signalr": {
            Dashboard.showLoadingMsg();
            const signalrStatus = await ShokoApiClient.getSignalrStatus();
            const libraries = config.MediaFolders
                .reduce((acc, mediaFolder) => {
                    if (mediaFolder.IsVirtualRoot)
                        return acc;

                    if (acc.find((m) => m.LibraryId === mediaFolder.LibraryId))
                        return acc;

                    acc.push(mediaFolder);
                    return acc;
                }, []);

            updateSignalrStatus(form, signalrStatus);

            form.querySelector("#SignalRAutoConnect").checked = config.SignalR_AutoConnectEnabled;
            form.querySelector("#SignalRAutoReconnectIntervals").value = config.SignalR_AutoReconnectInSeconds.join(", ");
            renderCheckboxList(form, "SignalREventSources", config.SignalR_EventSources);

            form.querySelector("#SignalRDefaultFileEvents").checked = config.SignalR_FileEvents;
            form.querySelector("#SignalRDefaultRefreshEvents").checked = config.SignalR_RefreshEnabled;

            form.querySelector("#SignalRMediaFolderSelector").innerHTML = `<option value="">Click here to select a library</option>` + libraries
                .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}${config.ExpertMode ? ` (${library.LibraryId})` : ""}</option>`)
                .join("");
            break;
        }

        case "users": {
            Dashboard.showLoadingMsg();
            const users = await ApiClient.getUsers();
            form.querySelector("#UserSelector").innerHTML = `<option value="">Click here to select a user</option>` + users.map((user) => `<option value="${user.Id}">${user.Name}</option>`).join("");
            break;
        }

        case "misc": {
            form.querySelector("#Misc_ShowInMenu").checked = config.Misc_ShowInMenu;
            form.querySelector("#IgnoredFolders").value = config.IgnoredFolders.join();

            form.querySelector("#EXPERIMENTAL_MergeSeasons").checked = config.EXPERIMENTAL_MergeSeasons || false;
            break;
        }
    }
}

/**
 * Load the user configuration for the given user.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {string} userId - The user ID.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns
 */
async function applyUserConfigToForm(form, userId, config = null) {
    if (!userId) {
        form.querySelector("#UserSettingsContainer").setAttribute("hidden", "");
        form.querySelector("#UserUsername").removeAttribute("required");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        if (State.config) {
            config = State.config;
        }
        else {
            Dashboard.showLoadingMsg();
            config = await ShokoApiClient.getConfiguration();
            shouldHide = true;
        }
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
    form.querySelector("#UserPassword").value = "";

    // Synchronization settings
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

/**
 * Load the VFS library configuration for the given library.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {string} libraryId - The library ID.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns {Promise<void>}
 */
async function applyLibraryConfigToForm(form, libraryId, config = null) {
    if (!libraryId) {
        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        if (State.config) {
            config = State.config;
        }
        else {
            Dashboard.showLoadingMsg();
            config = await ShokoApiClient.getConfiguration();
            shouldHide = true;
        }
    }

    const mediaFolders = State.config.MediaFolders.filter((c) => c.LibraryId === libraryId && !c.IsVirtualRoot);
    if (!mediaFolders.length) {
        renderReadonlyList(form, "MediaFolderImportFolderMapping", []);

        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        if (shouldHide) {
            Dashboard.hideLoadingMsg();
        }
        return;
    }

    renderReadonlyList(form, "MediaFolderImportFolderMapping", mediaFolders.map((c) =>
        c.IsMapped
            ? `${c.MediaFolderPath} | ${c.ImportFolderName} (${c.ImportFolderId}) ${c.ImportFolderRelativePath}`.trimEnd()
            : `${c.MediaFolderPath} | Not Mapped`
    ));

    // Configure the elements within the media folder container
    const libraryConfig = mediaFolders[0];
    form.querySelector("#MediaFolderVirtualFileSystem").checked = libraryConfig.IsVirtualFileSystemEnabled;
    form.querySelector("#MediaFolderLibraryFilteringMode").value = libraryConfig.LibraryFilteringMode;

    // Show the media folder settings now if it was previously hidden.
    form.querySelector("#MediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    if (shouldHide) {
        Dashboard.hideLoadingMsg();
    }
}

/**
 * Load the SignalR library configuration for the given library.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {string} libraryId - The library ID.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns {Promise<void>}
 */
async function applySignalrLibraryConfigToForm(form, libraryId, config = null) {
    if (!libraryId) {
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        return;
    }

    // Get the configuration to use.
    let shouldHide = false;
    if (!config) {
        if (State.config) {
            config = State.config;
        }
        else {
            Dashboard.showLoadingMsg();
            config = await ShokoApiClient.getConfiguration();
            shouldHide = true;
        }
    }

    const libraryConfig = config.MediaFolders.find((c) => c.LibraryId === libraryId && !c.IsVirtualRoot);
    if (!libraryConfig) {
        form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        if (shouldHide) {
            Dashboard.hideLoadingMsg();
        }
        return;
    }

    // Configure the elements within the user container
    form.querySelector("#SignalRFileEvents").checked = libraryConfig.IsFileEventsEnabled;
    form.querySelector("#SignalRRefreshEvents").checked = libraryConfig.IsRefreshEventsEnabled;

    // Show the user settings now if it was previously hidden.
    form.querySelector("#SignalRMediaFolderPerFolderSettingsContainer").removeAttribute("hidden");

    if (shouldHide) {
        Dashboard.hideLoadingMsg();
    }
}

//#endregion

//#region Server Interactions

/**
 * Default submit. Will conditionally sync settings or establish a new
 * connection based on the current state of the local representation of the
 * configuration.
 *
 * @param {HTMLFormElement} form - The form element.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function defaultSubmit(form) {
    let config = State.config || await ShokoApiClient.getConfiguration();
    if (config.ApiKey !== "") {
        return syncSettings(form, config);
    }

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
        await ShokoApiClient.updateConfiguration(config);
    }

    const username = form.querySelector("#Username").value;
    const password = form.querySelector("#Password").value;
    try {
        const response = await ShokoApiClient.getApiKey(username, password);
        config = await ShokoApiClient.getConfiguration();
        config.Username = username;
        config.ApiKey = response.apikey;

        await ShokoApiClient.updateConfiguration(config);

        Dashboard.hideLoadingMsg();
        Dashboard.alert(Messages.ConnectedToShoko);
    }
    catch (err) {
        Dashboard.hideLoadingMsg();
        Dashboard.alert(Messages.InvalidCredentials);
        console.error(err, Messages.InvalidCredentials);
    }

    return config;
}

/**
 * Reset the connection to Shoko.
 *
 * @param {HTMLFormElement} form - The form element.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function resetConnection(form) {
    const config = State.config || await ShokoApiClient.getConfiguration();
    form.querySelector("#Username").value = config.Username;
    form.querySelector("#Password").value = "";

    // Connection settings
    config.ApiKey = "";
    config.ServerVersion = null;

    await ShokoApiClient.updateConfiguration(config);

    Dashboard.hideLoadingMsg();
    Dashboard.alert(Messages.DisconnectedToShoko);

    return config;
}

/**
 * Synchronize the settings with the server.
 *1
 * @param {HTMLFormElement} form - The form element.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function syncSettings(form, config) {
    if (!config) {
        config = State.config || await ShokoApiClient.getConfiguration();
    }

    applyFormToConfig(form, config);

    // User settings
    const userId = form.querySelector("#UserSelector").value;
    if (userId) {
        let userConfig = config.UserList.find((c) => userId === c.UserId);
        if (!userConfig) {
            userConfig = { UserId: userId };
            config.UserList.push(userConfig);
        }

        // Only try to save a new token if a token is not already present.
        if (!userConfig.Token) {
            try {
                const username = form.querySelector("#UserUsername").value;
                const password = form.querySelector("#UserPassword").value;
                const response = await ShokoApiClient.getApiKey(username, password, true);
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
    }

    config.UserList = config.UserList.filter((c) => c.Token);

    await ShokoApiClient.updateConfiguration(config);
    Dashboard.processPluginConfigurationUpdateResult();

    return config;
}

/**
 * Remove a user from the configuration.
 *
 * @param {HTMLFormElement} form - The form element.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function removeUserConfig(form) {
    const config = State.config || await ShokoApiClient.getConfiguration();
    const userId = form.querySelector("#UserSelector").value;
    if (!userId) return config;

    const index = config.UserList.findIndex(c => userId === c.UserId);
    if (index !== -1) {
        config.UserList.splice(index, 1);
    }

    await ShokoApiClient.updateConfiguration(config);
    Dashboard.processPluginConfigurationUpdateResult();

    return config;
}

/**
 * Remove a library from the configuration.
 *
 * @param {HTMLFormElement} form - The form element.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function removeLibraryConfig(form) {
    const config = State.config || await ShokoApiClient.getConfiguration();
    const libraryId = form.querySelector("#MediaFolderSelector").value;
    if (!libraryId) return config;

    let index = config.MediaFolders.findIndex((m) => m.LibraryId === libraryId);
    while (index !== -1) {
        config.MediaFolders.splice(index, 1);
        index = config.MediaFolders.findIndex((m) => m.LibraryId === libraryId);
    }


    const libraries = config.MediaFolders
        .reduce((acc, mediaFolder) => {
            if (mediaFolder.IsVirtualRoot)
                return acc;

            if (acc.find((m) => m.LibraryId === mediaFolder.LibraryId))
                return acc;

            acc.push(mediaFolder);
            return acc;
        }, []);
    form.querySelector("#MediaFolderSelector").value = "";
    form.querySelector("#MediaFolderSelector").innerHTML = `<option value="">Click here to select a library</option>` + libraries
                    .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}</option>`)
                    .join("");
    form.querySelector("#SignalRMediaFolderSelector").innerHTML = `<option value="">Click here to select a library</option>` + libraries
                    .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}</option>`)
                    .join("");

    await ShokoApiClient.updateConfiguration(config);
    Dashboard.processPluginConfigurationUpdateResult();

    return config;
}

/**
 * Toggle expert mode.
 *
 * @param {boolean} value - True to enable expert mode, false to disable it.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function toggleExpertMode(value) {
    const config = State.config || await ShokoApiClient.getConfiguration();

    config.ExpertMode = value;

    await ShokoApiClient.updateConfiguration(config);

    Dashboard.alert(value ? Messages.ExpertModeEnabled : Messages.ExpertModeDisabled);

    return config;
}

//#endregion

//#region Helpers
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

//#endregion

}); }