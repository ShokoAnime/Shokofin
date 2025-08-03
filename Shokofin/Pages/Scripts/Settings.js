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
    overrideSortableCheckboxList,
    renderCheckboxList,
    renderReadonlyList,
    renderSortableCheckboxList,
    retrieveCheckboxList,
    retrieveSortableCheckboxList,
    updateTabs,
}) => {

//#region Constants

/**
 * @typedef {"Connection" | "Metadata_Title" | "Metadata_Description" | "Metadata_TagGenre" | "Metadata_Image" | "Metadata_Misc" | "Metadata_ThirdPartyIntegration" | "Library_Basic" | "Library_Collection" | "Library_MultipleVersions" | "Library_MediaFolder" | "Library_SeasonMerging" | "VFS_Basic" | "VFS_Location" | "User" | "Series" | "SignalR_Connection" | "SignalR_Basic" | "SignalR_Library_New" | "SignalR_Library_Existing" | "Misc" | "Debug" | "Utilities"} SectionType
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
    "Metadata_Image",
    "Metadata_Misc",
    "Metadata_ThirdPartyIntegration",
    "Library_Basic",
    "Library_Collection",
    "Library_MultipleVersions",
    "Library_MediaFolder",
    "Library_SeasonMerging",
    "VFS_Basic",
    "VFS_Location",
    "User",
    "Series",
    "SignalR_Connection",
    "SignalR_Basic",
    "SignalR_Library_New",
    "SignalR_Library_Existing",
    "Misc",
    "Debug",
    "Utilities",
];

const Messages = {
    ViewModeCountdown: "Press <count> more times to <toggle> view mode.",
    ExpertModeEnabled: "Advanced mode enabled.",
    ExpertModeDisabled: "Advanced mode disabled.",
    DebugModeEnabled: "Debug mode enabled.",
    DebugModeDisabled: "Debug mode disabled.",
    ConnectToShoko: "Please establish a connection to a running instance of Shoko Server before you continue.",
    ConnectedToShoko: "Connection established.",
    DisconnectedToShoko: "Connection has been reset.",
    InvalidCredentials: "An error occurred while trying to authenticating the user using the provided credentials.",
};

let alternateTitleListTemplate = "";

//#endregion

//#region Controller Logic

createControllerFactory({
    show,
    hide,
    events: {
        onInit() {
            const view = this;
            const form = view.querySelector("form");

            if (alternateTitleListTemplate === "") {
                alternateTitleListTemplate = form.querySelector("#TitleAlternateListContainer").innerHTML;
                form.querySelector("#TitleAlternateListContainer").innerHTML = "";
            }

            form.querySelector("#ServerVersion").addEventListener("click", async function onVersionClick() {
                if (++State.clickCounter === MaxDebugPresses) {
                    State.clickCounter = 0;
                    State.advancedMode = !State.advancedMode;
                    State.debugMode = false;
                    const config = await toggleExpertMode(State.advancedMode, State.debugMode);
                    await updateView(view, form, config);
                    return;
                }
                if (State.clickCounter >= 3)
                    Dashboard.alert(Messages.ViewModeCountdown.replace("<count>", MaxDebugPresses - State.clickCounter).replace("<toggle>", State.advancedMode ? "disable" : "enable"));
            });

            form.querySelector(".sectionTitleContainer > a").addEventListener("click", async function(event) {
                if ((State.clickCounter + 1) === MaxDebugPresses) {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    State.clickCounter = 0;
                    State.advancedMode = !State.advancedMode;
                    State.debugMode = State.advancedMode;
                    const config = await toggleExpertMode(State.advancedMode, State.debugMode);
                    await updateView(view, form, config);
                    return;
                }
            });

            form.querySelector("#UserSelector").addEventListener("change", function () {
                applyUserConfigToForm(form, this.value);
            });

            form.querySelector("#SeriesSearch").addEventListener("input", function () {
                const value = this.value.trim();
                if (State.seriesQuery === value && State.seriesTimeout) {
                    return;
                }

                if (State.seriesTimeout) {
                    clearTimeout(State.seriesTimeout);
                }

                const timeout = State.seriesTimeout = setTimeout(async () => {
                    if (State.seriesTimeout !== timeout) {
                        return;
                    }

                    console.log("Series Search: " + value);
                    State.seriesQuery = value;
                    applySeriesConfigToForm(form, "");
                    form.querySelector("#SeriesSelector").setAttribute("disabled", "");
                    try {
                        State.seriesList = await ShokoApiClient.getSeriesList(value);
                    }
                    catch (error) {
                        console.log(error, "Got an error attempting to search for a series.");
                        form.querySelector("#SeriesSelector").value = "";
                        form.querySelector("#SeriesSelector").innerHTML = `<option value="">Failed to load series!</option>`;
                        form.querySelector("#SeriesSelector").removeAttribute("disabled");
                        return;
                    }

                    if (State.seriesTimeout !== timeout) {
                        console.log("Returned too late for series search: " + value);
                        return;
                    }

                    const series = State.seriesList;
                    const seriesId = value && series.length > 0 ? series[0].Id.toString() : "";
                    State.seriesTimeout = null;
                    form.querySelector("#SeriesSelector").innerHTML =
                        `<option value="">Click here to select a series</option>` +
                        series.map((s) => `<option value="${s.Id}">${s.Title.length >= 50 ? `${s.Title.substring(0, 47)}...` : s.Title} (a${s.AnidbId})</option>`).join("");
                    form.querySelector("#SeriesSelector").removeAttribute("disabled");
                    form.querySelector("#SeriesSelector").value = seriesId;
                    applySeriesConfigToForm(form, seriesId);
                }, 250);
            });

            form.querySelector("#SeriesSelector").addEventListener("change", function () {
                applySeriesConfigToForm(form, this.value);
            });

            form.querySelectorAll("#SeriesSeasonMergingBehavior input").forEach(input => input.addEventListener("change", onSeasonMergingBehaviorChange));

            function onSeasonMergingBehaviorChange() {
                const option = this.getAttribute("data-option");
                const value = this.checked;
                if (option === "NoMerge") {
                    if (value) {
                        form.querySelectorAll("#SeriesSeasonMergingBehavior input").forEach((input) => {
                            if (input !== this) {
                                input.checked = false;
                            }
                        });
                    }
                    return;
                }

                if (value) {
                    const input = form.querySelector("#SeriesSeasonMergingBehavior input[data-option='NoMerge']");
                    if (input.getAttribute("data-option") === "NoMerge" && input.checked) {
                        input.checked = false;
                    }
                }

                if (option.startsWith("MergeGroup")) {
                    const reverse = option.slice(0, 11) + (option.slice(11) === "Target" ? "Source" : "Target");
                    const reversedInput = form.querySelector(`#SeriesSeasonMergingBehavior input[data-option="${reverse}"]`);
                    if (value) {
                        reversedInput.checked = false;
                    }
                }
            }

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
                    case "remove-alternate-title":
                        removeAlternateTitle(form, parseInt(event.submitter.dataset.index, 10))
                            .then((config) => updateView(view, form, config))
                            .catch(handleError);
                        break;
                    case "add-alternate-title":
                        addAlternateTitle(form)
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
    State.clickCounter = 0;
    State.advancedMode = config.AdvancedMode;
    State.debugMode = config.Debug.ShowInUI;
    State.connected = Boolean(config.ApiKey);

    if (State.advancedMode) {
        form.classList.add("advanced-mode");
    }
    else {
        form.classList.remove("advanced-mode");
    }
    if (State.debugMode) {
        form.classList.add("debug-mode");
    }
    else {
        form.classList.remove("debug-mode");
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
            activeSections.push("Metadata_Title", "Metadata_Description", "Metadata_TagGenre", "Metadata_Image", "Metadata_Misc", "Metadata_ThirdPartyIntegration");
            if (form.querySelectorAll("#TitleAlternateListContainer > fieldset").length >= 5) {
                form.querySelector("button[name=\"add-alternate-title\"]").setAttribute("disabled", "");
            }
            else {
                form.querySelector("button[name=\"add-alternate-title\"]").removeAttribute("disabled");
            }
            break;

        case "library":
            activeSections.push("Library_Basic", "Library_Collection", "Library_MultipleVersions", "Library_MediaFolder", "Library_SeasonMerging");

            await applyLibraryConfigToForm(form, form.querySelector("#MediaFolderSelector").value, config);
            break;

        case "vfs":
            activeSections.push("VFS_Basic", "VFS_Location");
            break;

        case "users":
            activeSections.push("User");

            await applyUserConfigToForm(form, form.querySelector("#UserSelector").value, config);
            break;

        case "series":
            activeSections.push("Series");

            await applySeriesConfigToForm(form, form.querySelector("#SeriesSelector").value, config);
            break;

        case "signalr":
            activeSections.push("SignalR_Connection", "SignalR_Basic", "SignalR_Library_New", "SignalR_Library_Existing");

            await applySignalrLibraryConfigToForm(form, form.querySelector("#SignalRMediaFolderSelector").value, config);
            break;

        case "misc":
            activeSections.push("Misc", "Debug");
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
            config.MarkSpecialsWhenGrouped = form.querySelector("#MarkSpecialsWhenGrouped").checked;
            config.Title.Default.RemoveDuplicates = form.querySelector("#RemoveDuplicateTitles").checked;
            ([config.Title.Default.MainTitle.List, config.Title.Default.MainTitle.Order] = retrieveSortableCheckboxList(form, "TitleMainList"));
            config.Title.Default.MainTitle.AllowAny = form.querySelector("#TitleMainAllowAny").checked;

            const alternateTitles = form.querySelectorAll("#TitleAlternateListContainer > fieldset");
            config.Title.Default.AlternateTitles = [];
            for (let i = 1; i <= alternateTitles.length; i++) {
                const [list, order] = retrieveSortableCheckboxList(form, `TitleAlternateList_${i}`);
                config.Title.Default.AlternateTitles.push({
                    List: list,
                    Order: order,
                    AllowAny: form.querySelector(`#TitleAlternateAllowAny_${i}`).checked,
                });
            }

            const tagExcludeList = filterTags(form.querySelector("#TagExcludeList").value);
            const genreExcludeList = filterTags(form.querySelector("#GenreExcludeList").value);
            ([config.Description.Default.List, config.Description.Default.Order] = retrieveSortableCheckboxList(form, "DescriptionSourceList"));
            config.DescriptionConversionMode = form.querySelector("#DescriptionConversionMode").value;

            config.HideUnverifiedTags = form.querySelector("#HideUnverifiedTags").checked;
            config.TagSources = retrieveCheckboxList(form, "TagSources").join(", ");
            config.TagIncludeFilters = retrieveCheckboxList(form, "TagIncludeFilters").join(", ");
            config.TagMinimumWeight = form.querySelector("#TagMinimumWeight").value;
            config.TagMaximumDepth = parseInt(form.querySelector("#TagMaximumDepth").value, 10);
            config.TagExcludeList = tagExcludeList;
            form.querySelector("#TagExcludeList").value = tagExcludeList.join(", ");

            config.GenreSources = retrieveCheckboxList(form, "GenreSources").join(", ");
            config.GenreIncludeFilters = retrieveCheckboxList(form, "GenreIncludeFilters").join(", ");
            config.GenreMinimumWeight = form.querySelector("#GenreMinimumWeight").value;
            config.GenreMaximumDepth = parseInt(form.querySelector("#GenreMaximumDepth").value, 10);
            config.GenreExcludeList = genreExcludeList;
            form.querySelector("#GenreExcludeList").value = genreExcludeList.join(", ");

            config.Image.Default.UsePreferred = form.querySelector("#Image_UsePreferred").checked;
            config.Image.Default.UseCommunityRating = form.querySelector("#Image_UseCommunityRating").checked;
            ([config.Image.Default.PosterList, config.Image.Default.PosterOrder] = retrieveSortableCheckboxList(form, "Image_PosterList"));
            ([config.Image.Default.LogoList, config.Image.Default.LogoOrder] = retrieveSortableCheckboxList(form, "Image_LogoList"));
            ([config.Image.Default.BackdropList, config.Image.Default.BackdropOrder] = retrieveSortableCheckboxList(form, "Image_BackdropList"));
            config.Image.DebugMode = form.querySelector("#Image_DebugMode").checked;

            config.Metadata_StudioOnlyAnimationWorks = form.querySelector("#Metadata_StudioOnlyAnimationWorks").checked;
            ([config.ContentRatingList, config.ContentRatingOrder] = retrieveSortableCheckboxList(form, "ContentRatingList"));
            ([config.ProductionLocationList, config.ProductionLocationOrder] = retrieveSortableCheckboxList(form, "ProductionLocationList"));

            config.ThirdPartyIdProviderList = retrieveCheckboxList(form, "ThirdPartyIdProviderList");
            break;
        }

        case "library": {
            const libraryId = form.querySelector("#MediaFolderSelector").value;
            const mediaFolders = libraryId ? config.MediaFolders.filter((m) => m.LibraryId === libraryId) : undefined;
            const seasonMergeWindow = sanitizeNumber(form.querySelector("#SeasonMerging_MergeWindowInDays").value);

            config.DefaultLibraryStructure = form.querySelector("#DefaultLibraryStructure").value;
            config.DefaultSeasonOrdering = form.querySelector("#DefaultSeasonOrdering").value;
            config.SeparateMovies = form.querySelector("#SeparateMovies").checked;
            config.FilterMovieLibraries = !form.querySelector("#DisableFilterMovieLibraries").checked;
            config.DefaultSpecialsPlacement = form.querySelector("#DefaultSpecialsPlacement").value;
            config.MovieSpecialsAsExtraFeaturettes = form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked;
            config.AddMissingMetadata = form.querySelector("#AddMissingMetadata").checked;

            config.CollectionGrouping = form.querySelector("#CollectionGrouping").value;
            config.CollectionMinSizeOfTwo = form.querySelector("#CollectionMinSizeOfTwo").checked;

            config.AutoMergeVersions = form.querySelector("#AutoMergeVersions").checked;
            ([config.MergeVersionSortSelectorList, config.MergeVersionSortSelectorOrder] = retrieveSortableCheckboxList(form, "MergeVersionSortSelectorList"));

            config.DefaultLibraryOperationMode = form.querySelector("#DefaultLibraryOperationMode").value;
            if (mediaFolders) {
                for (const c of mediaFolders) {
                    c.LibraryOperationMode = form.querySelector("#MediaFolderLibraryOperationMode").value;
                }
            }

            config.SeasonMerging_Enabled = form.querySelector("#SeasonMerging_Enabled").checked;
            config.SeasonMerging_DefaultBehavior = form.querySelector("#SeasonMerging_AutoMerge").checked ? "None" : "NoMerge";
            config.SeasonMerging_SeriesTypes = retrieveCheckboxList(form, "SeasonMerging_SeriesTypes");
            config.SeasonMerging_MergeWindowInDays = seasonMergeWindow;
            form.querySelector("#SeasonMerging_MergeWindowInDays").value = seasonMergeWindow;
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
            config.VFS_IterativeFileChecks = form.querySelector("#VFS_IterativeFileChecks").checked;
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
            const stallTime = sanitizeNumber(form.querySelector("#Debug_UsageTrackerStalledTimeInSeconds").value, 1, 10800);
            const maxRequests = sanitizeNumber(form.querySelector("#Debug_MaxInFlightRequests").value, 1, 100);
            const expirationScanFrequency = sanitizeNumber(form.querySelector("#Debug_ExpirationScanFrequencyInMinutes").value, 1, 180);
            const slidingExpiration = sanitizeNumber(form.querySelector("#Debug_SlidingExpirationInMinutes").value, 1, 180);
            const absoluteExpiration = sanitizeNumber(form.querySelector("#Debug_AbsoluteExpirationRelativeToNowInMinutes").value, 1, 1440);

            config.Misc_ShowInMenu = form.querySelector("#Misc_ShowInMenu").checked;
            config.IgnoredFolders = ignoredFolders;
            form.querySelector("#IgnoredFolders").value = ignoredFolders.join(", ");

            config.Debug.UsageTrackerStalledTimeInSeconds = stallTime;
            form.querySelector("#Debug_UsageTrackerStalledTimeInSeconds").value = config.Debug.UsageTrackerStalledTimeInSeconds;
            config.Debug.MaxInFlightRequests = maxRequests;
            form.querySelector("#Debug_MaxInFlightRequests").value = config.Debug.MaxInFlightRequests;
            config.Debug.AutoClearClientCache = form.querySelector("#Debug_AutoClearClientCache").checked;
            config.Debug.AutoClearManagerCache = form.querySelector("#Debug_AutoClearManagerCache").checked;
            config.Debug.AutoClearVfsCache = form.querySelector("#Debug_AutoClearVfsCache").checked;
            config.Debug.ExpirationScanFrequencyInMinutes = expirationScanFrequency;
            form.querySelector("#Debug_ExpirationScanFrequencyInMinutes").value = config.Debug.ExpirationScanFrequencyInMinutes;
            config.Debug.SlidingExpirationInMinutes = slidingExpiration;
            form.querySelector("#Debug_SlidingExpirationInMinutes").value = config.Debug.SlidingExpirationInMinutes;
            config.Debug.AbsoluteExpirationRelativeToNowInMinutes = absoluteExpiration;
            form.querySelector("#Debug_AbsoluteExpirationRelativeToNowInMinutes").value = config.Debug.AbsoluteExpirationRelativeToNowInMinutes;
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
            form.querySelector("#MarkSpecialsWhenGrouped").checked = config.MarkSpecialsWhenGrouped;
            form.querySelector("#RemoveDuplicateTitles").checked = config.Title.Default.RemoveDuplicates;
            renderSortableCheckboxList(form, "TitleMainList", config.Title.Default.MainTitle.List, config.Title.Default.MainTitle.Order);
            form.querySelector("#TitleMainAllowAny").checked = config.Title.Default.MainTitle.AllowAny;

            const configAlternateTitles = [...config.Title.Default.AlternateTitles];
            if (configAlternateTitles.length === 0) {
                configAlternateTitles.push({ List: [], Order: [], AllowAny: false });
            }

            renderAlternateTitles(form, configAlternateTitles);

            renderSortableCheckboxList(form, "DescriptionSourceList", config.Description.Default.List, config.Description.Default.Order);
            form.querySelector("#DescriptionConversionMode").value = config.DescriptionConversionMode;

            form.querySelector("#HideUnverifiedTags").checked = config.HideUnverifiedTags;
            renderCheckboxList(form, "TagSources", config.TagSources.split(",").map(s => s.trim()).filter(s => s));
            renderCheckboxList(form, "TagIncludeFilters", config.TagIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
            form.querySelector("#TagMinimumWeight").value = config.TagMinimumWeight;
            form.querySelector("#TagMaximumDepth").value = config.TagMaximumDepth.toString();
            form.querySelector("#TagExcludeList").value = config.TagExcludeList.join(", ");

            renderCheckboxList(form, "GenreSources", config.GenreSources.split(",").map(s => s.trim()).filter(s => s));
            renderCheckboxList(form, "GenreIncludeFilters", config.GenreIncludeFilters.split(",").map(s => s.trim()).filter(s => s));
            form.querySelector("#GenreMinimumWeight").value = config.GenreMinimumWeight;
            form.querySelector("#GenreMaximumDepth").value = config.GenreMaximumDepth.toString();
            form.querySelector("#GenreExcludeList").value = config.GenreExcludeList.join(", ");

            form.querySelector("#Image_UsePreferred").checked = config.Image.Default.UsePreferred;
            form.querySelector("#Image_UseCommunityRating").checked = config.Image.Default.UseCommunityRating;
            renderSortableCheckboxList(form, "Image_PosterList", config.Image.Default.PosterList, config.Image.Default.PosterOrder);
            renderSortableCheckboxList(form, "Image_LogoList", config.Image.Default.LogoList, config.Image.Default.LogoOrder);
            renderSortableCheckboxList(form, "Image_BackdropList", config.Image.Default.BackdropList, config.Image.Default.BackdropOrder);
            form.querySelector("#Image_DebugMode").checked = config.Image.DebugMode;

            form.querySelector("#Metadata_StudioOnlyAnimationWorks").checked = config.Metadata_StudioOnlyAnimationWorks;
            renderSortableCheckboxList(form, "ContentRatingList", config.ContentRatingList, config.ContentRatingOrder);
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

            form.querySelector("#DefaultLibraryStructure").value = config.DefaultLibraryStructure;
            form.querySelector("#DefaultSeasonOrdering").value = config.DefaultSeasonOrdering;
            form.querySelector("#SeparateMovies").checked = config.SeparateMovies;
            form.querySelector("#DisableFilterMovieLibraries").checked = !config.FilterMovieLibraries;
            form.querySelector("#DefaultSpecialsPlacement").value = config.DefaultSpecialsPlacement === "Default" ? "Excluded" : config.DefaultSpecialsPlacement;
            form.querySelector("#MovieSpecialsAsExtraFeaturettes").checked = config.MovieSpecialsAsExtraFeaturettes;
            form.querySelector("#AddMissingMetadata").checked = config.AddMissingMetadata;

            form.querySelector("#CollectionGrouping").value = config.CollectionGrouping;
            form.querySelector("#CollectionMinSizeOfTwo").checked = config.CollectionMinSizeOfTwo;

            form.querySelector("#AutoMergeVersions").checked = config.AutoMergeVersions || false;
            renderSortableCheckboxList(form, "MergeVersionSortSelectorList", config.MergeVersionSortSelectorList, config.MergeVersionSortSelectorOrder);

            form.querySelector("#DefaultLibraryOperationMode").value = config.DefaultLibraryOperationMode;
            form.querySelector("#MediaFolderSelector").innerHTML = `<option value="">Click here to select a library</option>` + libraries
                .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}${State.advancedMode ? ` (${library.LibraryId})` : ""}</option>`)
                .join("");

            form.querySelector("#SeasonMerging_Enabled").checked = config.SeasonMerging_Enabled;
            form.querySelector("#SeasonMerging_AutoMerge").checked = config.SeasonMerging_DefaultBehavior === "None";
            renderCheckboxList(form, "SeasonMerging_SeriesTypes", config.SeasonMerging_SeriesTypes);
            form.querySelector("#SeasonMerging_MergeWindowInDays").value = config.SeasonMerging_MergeWindowInDays;
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
            form.querySelector("#VFS_IterativeFileChecks").checked = config.VFS_IterativeFileChecks;
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
                .map((library) => `<option value="${library.LibraryId}">${library.LibraryName}${State.advancedMode ? ` (${library.LibraryId})` : ""}</option>`)
                .join("");
            break;
        }

        case "users": {
            Dashboard.showLoadingMsg();
            const users = await ApiClient.getUsers();
            form.querySelector("#UserSelector").innerHTML =
                `<option value="">Click here to select a user</option>` +
                users.map((user) => `<option value="${user.Id}">${user.Name}</option>`).join("");
            break;
        }

        case "series": {
            const series = State.seriesList || [];
            form.querySelector("#SeriesSelector").innerHTML =
                `<option value="">${State.seriesList ? "Click here to select a series" : "Loading series list"}</option>` +
                series.map((s) => `<option value="${s.Id}">${s.Title.length >= 50 ? `${s.Title.substring(0, 47)}...` : s.Title} (a${s.AnidbId})</option>`).join("");
            form.querySelector("#SeriesSearch").value = State.seriesQuery;
            if (State.seriesList && !State.seriesTimeout) {
                form.querySelector("#SeriesSelector").disabled = false;
                if (State.seriesId) {
                    form.querySelector("#SeriesSelector").value = State.seriesId;
                    form.querySelector("#SeriesSelector").dispatchEvent(new Event("change"));
                }
            }
            else {
                form.querySelector("#SeriesSearch").dispatchEvent(new Event("input"));
            }
            break;
        }

        case "misc": {
            form.querySelector("#Misc_ShowInMenu").checked = config.Misc_ShowInMenu;
            form.querySelector("#IgnoredFolders").value = config.IgnoredFolders.join();

            form.querySelector("#Debug_UsageTrackerStalledTimeInSeconds").value = config.Debug.UsageTrackerStalledTimeInSeconds;
            form.querySelector("#Debug_MaxInFlightRequests").value = config.Debug.MaxInFlightRequests;
            form.querySelector("#Debug_AutoClearClientCache").checked = config.Debug.AutoClearClientCache;
            form.querySelector("#Debug_AutoClearManagerCache").checked = config.Debug.AutoClearManagerCache;
            form.querySelector("#Debug_AutoClearVfsCache").checked = config.Debug.AutoClearVfsCache;
            form.querySelector("#Debug_ExpirationScanFrequencyInMinutes").value = config.Debug.ExpirationScanFrequencyInMinutes;
            form.querySelector("#Debug_SlidingExpirationInMinutes").value = config.Debug.SlidingExpirationInMinutes;
            form.querySelector("#Debug_AbsoluteExpirationRelativeToNowInMinutes").value = config.Debug.AbsoluteExpirationRelativeToNowInMinutes;
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
 * Load the series configuration for the given series.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {string} seriesId - The series ID.
 * @param {import("./Common.js").PluginConfiguration} config - The plugin configuration.
 * @returns {Promise<void>}
 */
async function applySeriesConfigToForm(form, seriesId, config = null) {
    State.seriesId = seriesId;
    if (!seriesId) {
        form.querySelector("#SeriesSettingsContainer").setAttribute("hidden", "");
        return;
    }

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

    const seriesConfig = await ShokoApiClient.getSeriesConfiguration(seriesId);
    form.querySelector("#SeriesType").value = seriesConfig.Type;
    form.querySelector("#SeriesLibraryStructure").value = seriesConfig.StructureType;
    form.querySelector("#SeriesSeasonOrdering").value = seriesConfig.SeasonOrdering;
    form.querySelector("#SeriesSpecialsPlacement").value = seriesConfig.SpecialsPlacement;
    renderCheckboxList(form, "SeriesSeasonMergingBehavior", seriesConfig.SeasonMergingBehavior.split(",").map(s => s.trim()).filter(s => s && s !== "None"));
    form.querySelector("#SeriesEpisodeConversion").value = seriesConfig.EpisodeConversion;
    form.querySelector("#SeriesOrderByAirdate").checked = seriesConfig.OrderByAirdate;

    form.querySelector("#SeriesSettingsContainer").removeAttribute("hidden");

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
        renderReadonlyList(form, "MediaFolderManagedFolderMapping", []);

        form.querySelector("#MediaFolderPerFolderSettingsContainer").setAttribute("hidden", "");
        if (shouldHide) {
            Dashboard.hideLoadingMsg();
        }
        return;
    }

    renderReadonlyList(form, "MediaFolderManagedFolderMapping", mediaFolders.map((c) =>
        c.IsMapped
            ? `${c.MediaFolderPath} | ${c.ManagedFolderName} (${c.ManagedFolderId}) ${c.ManagedFolderRelativePath}`.trimEnd()
            : `${c.MediaFolderPath} | Not Mapped`
    ));

    // Configure the elements within the media folder container
    const libraryConfig = mediaFolders[0];
    form.querySelector("#MediaFolderLibraryOperationMode").value = libraryConfig.LibraryOperationMode;

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

    const seriesId = form.querySelector("#SeriesSelector").value;
    if (seriesId) {
        let seriesConfig = await ShokoApiClient.getSeriesConfiguration(seriesId);
        if (seriesConfig) {
            seriesConfig.SeriesType = form.querySelector("#SeriesType").value;
            seriesConfig.StructureType = form.querySelector("#SeriesLibraryStructure").value;
            seriesConfig.SeasonOrdering = form.querySelector("#SeriesSeasonOrdering").value;
            seriesConfig.SpecialsPlacement = form.querySelector("#SeriesSpecialsPlacement").value;
            seriesConfig.SeasonMergingBehavior = retrieveCheckboxList(form, "SeriesSeasonMergingBehavior").join(",") || "None";
            seriesConfig.EpisodeConversion = form.querySelector("#SeriesEpisodeConversion").value;
            seriesConfig.OrderByAirdate = form.querySelector("#SeriesOrderByAirdate").checked;

            await ShokoApiClient.updateSeriesConfiguration(seriesId, seriesConfig);
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

    form.querySelector("#UserSelector").value = "";

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
 * Remove an alternate/original title from the view.
 *
 * @param {HTMLFormElement} form - The form element.
 * @param {number} index - The index of the alternate title to remove.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function removeAlternateTitle(form, index) {
    const config = State.config || await ShokoApiClient.getConfiguration();

    const alternateTitles = form.querySelectorAll("#TitleAlternateListContainer > fieldset");
    const configAlternateTitles = [];
    let j = 0;
    for (let i = 1; i <= alternateTitles.length; i++) {
        const [list, order] = retrieveSortableCheckboxList(form, `TitleAlternateList_${i}`);
        if (i !== index) {
            configAlternateTitles[j] = { List: list, Order: order };
            configAlternateTitles[j].AllowAny = form.querySelector(`#TitleAlternateAllowAny_${i}`).checked;
            j++;
        }
    }

    if (configAlternateTitles.length === 0) {
        configAlternateTitles.push({ List: [], Order: [], AllowAny: false });
    }

    renderAlternateTitles(form, configAlternateTitles);

    return config;
}

/**
 * Add a new alternate/original title to the view.
 *
 * @param {HTMLFormElement} form - The form element.
 */
async function addAlternateTitle(form) {
    const config = State.config || await ShokoApiClient.getConfiguration();

    const alternateTitles = form.querySelectorAll("#TitleAlternateListContainer > fieldset");
    if (alternateTitles.length >= 5) {
        return config;
    }

    const configAlternateTitles = [];
    let j = 0;
    for (let i = 1; i <= alternateTitles.length; i++) {
        const [list, order] = retrieveSortableCheckboxList(form, `TitleAlternateList_${i}`);
        configAlternateTitles[j] = { List: list, Order: order };
        configAlternateTitles[j].AllowAny = form.querySelector(`#TitleAlternateAllowAny_${i}`).checked;
        j++;
    }

    configAlternateTitles.push({ List: [], Order: [], AllowAny: false });
    renderAlternateTitles(form, configAlternateTitles);

    return config;
}

/**
 * Render the alternate titles.
 *
 * @param {HTMLFormElement} form
 * @param {TitleConfiguration[]} configAlternateTitles
 * @returns {void}
 */
function renderAlternateTitles(form, configAlternateTitles) {
    if (form.querySelectorAll("#TitleAlternateListContainer > fieldset").length !== configAlternateTitles.length) {
        const container = form.querySelector("#TitleAlternateListContainer");
        container.innerHTML = "";
        const remaining = Math.max(0, 5 - configAlternateTitles.length);
        for (let i = 1; i <= configAlternateTitles.length; i++) {
            const html = alternateTitleListTemplate
                .replace(/%number%/g, i)
                .replace(/%number_formatted%/g, configAlternateTitles.length === 1 ? "" : i === 1 ? "1st " : i === 2 ? "2nd " : i === 3 ? "3rd " : `${i}th `)
                .replace(/%remaining%/g, remaining);
            container.insertAdjacentHTML("beforeend", html);
            if (i === 1) {
                container.querySelector(`#TitleAlternateRemoveButton_${i}`).setAttribute("hidden", "");
                if (remaining === 0) {
                    container.querySelector(`#TitleAlternateAddButton_${i}>button`).className = "raised button-alt block emby-button";
                    container.querySelector(`#TitleAlternateAddButton_${i}>button`).setAttribute("disabled", "");
                }
            }
            else {
                container.querySelector(`#TitleAlternateAddButton_${i}`).setAttribute("hidden", "");
            }
            overrideSortableCheckboxList(container.querySelector(`#TitleAlternateList_${i}`));
        }
    }
    for (let i = 1; i <= configAlternateTitles.length; i++) {
        const j = i - 1;
        renderSortableCheckboxList(form, `TitleAlternateList_${i}`, configAlternateTitles[j].List, configAlternateTitles[j].Order);
        form.querySelector(`#TitleAlternateAllowAny_${i}`).checked = configAlternateTitles[j].AllowAny;
    }
}

/**
 * Toggle expert mode.
 *
 * @param {boolean} expertMode - True to enable expert mode, false to disable it.
 * @param {boolean} debugMode - True to enable debug mode, false to disable it.
 * @returns {Promise<PluginConfiguration>} The updated plugin configuration.
 */
async function toggleExpertMode(expertMode = false, debugMode = false) {
    const config = State.config || await ShokoApiClient.getConfiguration();
    const debugChanged = config.Debug.ShowInUI !== debugMode;
    const expertChanged = config.AdvancedMode !== expertMode;
    if (!expertChanged && !debugChanged) return config;

    config.AdvancedMode = expertMode;
    config.Debug.ShowInUI = debugMode;

    await ShokoApiClient.updateConfiguration(config);

    if (debugChanged) {
        Dashboard.alert(debugMode ? Messages.DebugModeEnabled : Messages.DebugModeDisabled);
    }
    else if (expertChanged) {
        Dashboard.alert(expertMode ? Messages.ExpertModeEnabled : Messages.ExpertModeDisabled);
    }

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
 * Filter out non-integer values and sanitize list.
 * @param {string} value - Stringified list of values to filter.
 * @returns {number[]} An array of sanitized and filtered values.
 */
function filterReconnectIntervals(value) {
    return value
        .split(",")
        .map(str => parseInt(str.trim().toLowerCase(), 10))
        .filter(int => !Number.isNaN(int) || !Number.isInteger(int));
}

/**
 * Filter out illegal values and convert from string to number.
 * @param {string} raw  - Stringified value to filter.
 * @returns {number} A sanitized and filtered value.
 */
function sanitizeNumber(raw, min = 0, max = Number.MAX_SAFE_INTEGER) {
    const value = parseInt(raw.trim().toLowerCase(), 10);
    if (Number.isNaN(value) || value < min)
        return min;
    if (value > max)
        return max;
    return value;
}

/**
 * Filter out duplicates and sanitize list.
 * @param {string} value - Stringified list of values to filter.
 * @returns {string[]} An array of sanitized and filtered values.
 */
function filterTags(value) {
    return Array.from(
        new Set(
            value
            .split(",")
            .map(str => str.trim().toLowerCase())
            .filter(str => str)
        )
    );
}


//#endregion

}); }