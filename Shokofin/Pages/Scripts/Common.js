/**
 * Example page showcasing the different view events we can use and their
 * details.
 */

//#region Dashboard

/**
 * @type {DashboardPrototype}
 */
export const Dashboard = globalThis.Dashboard;

/**
 *
 * @callback GenericFunction
 * @returns {void}
 */

/**
 * Prototype for the dashboard.
 *
 * @typedef {Object} DashboardPrototype
 * @property {DashboardConfirm1 | DashboardConfirm2} confirm Show a confirm dialog.
 * @property {DashboardAlert} alert Alert a message.
 * @property {ApiClientGetUrl} getPluginUrl The internal URL of the plugin resource.
 * @property {GenericFunction} showLoadingMsg Show a loading message.
 * @property {GenericFunction} hideLoadingMsg Hide a loading message.
 * @property {GenericFunction} processPluginConfigurationUpdateResult Process a plugin configuration update.
 * @property {DashboardNavigate} navigate Navigate to a route.
 * // TODO: Add the rest here if needed.
 */

/**
 * Show a confirm dialog.
 *
 * @callback DashboardConfirm1
 * @param {string} message The message to show.
 * @param {string} title The title of the confirm dialog.
 * @returns {Promise<void>}
 */
/**
 * Show a confirm dialog.
 *
 * @callback DashboardConfirm2
 * @param {{
 * }} options The message to show.
 * @returns {Promise<void>}
 */

/**
 * Alert message options.
 *
 * @typedef {Object} DashboardAlertOptions
 * @property {string} message The message to show.
 * @property {string} [title] The title of the alert.
 * @property {GenericFunction} [callback] The callback to call when the alert is closed.
 */

/**
 * Show an alert message.
 *
 * @callback DashboardAlert
 * @param {string | DashboardAlertOptions} message The message to show, or an options object for the alert to show.
 * @returns {void}
 */

/**
 * Navigate to a url.
 *
 * @callback DashboardNavigate
 * @param {string} url - The url to navigate to.
 * @param {boolean} [preserveQueryString] - A flag to indicate the current query string should be appended to the new url.
 * @returns {Promise<void>}
 */

//#endregion

//#region API Client

/**
 * @type {ApiClientPrototype}
 */
export const ApiClient = globalThis.ApiClient;

/**
 * @typedef {Object} User
 * @property {string} Id The user id.
 * @property {string} Name The user name.
 */

/**
 * @callback ApiClientGetUsers
 * @returns {Promise<User[]>} The users.
 */

/**
 * @typedef {Object} ApiClientPrototype
 * @property {ApiClientGetPluginConfiguration} getPluginConfiguration Get a plugin configuration.
 * @property {ApiClientUpdatePluginConfiguration} updatePluginConfiguration Update a plugin configuration.
 * @property {ApiClientGetUsers} getUsers Get the current user.
 * @property {ApiClientGetUrl} getUrl Get an API url.
 * @property {ApiClientFetch} fetch Fetch an API call.
 * // TODO: Add the rest here if needed.
 */

/**
 * @typedef {Object} ApiClientGetPluginConfiguration
 * @property {string} id The plugin id.
 * @returns {Promise<T>} The plugin configuration.
 * @template T The type of the plugin configuration.
 */

/**
 * @callback ApiClientUpdatePluginConfiguration
 * @param {string} id The plugin id.
 * @param {T} config The plugin configuration.
 * @returns {Promise<any>} Some sort of result we don't really care about.
 * @template T
 */

/**
 * @callback ApiClientGetUrl
 * @param {string} url The url of the API call.
 * @returns {string} The modified url of the API call.
 */

/**
 * @typedef {Object} ApiClientFetchOptions
 * @property {"json"} dataType The data type of the API call.
 * @property {"GET" | "POST"} [type] The HTTP method of the API call.
 * @property {string | FormData | Blob} [data] The data of the API call.
 * @property {Record<string, string>} [headers] The headers of the API call.
 * @property {string} url The url of the API call.
 */

/**
 * Fetch an API call.
 *
 * @callback ApiClientFetch
 * @param {Object} options The options of the API call.
 * @returns {Promise<T>} The result of the API call.
 * @template T
 */

//#endregion

//#region Library Menu

/**
 * @type {LibraryMenuPrototype}
 */
export const LibraryMenu = globalThis.LibraryMenu;

/**
 * @typedef {Object} LibraryMenuPrototype
 * @property {LibraryMenuSetTabs} setTabs Set the tabs.
 */

/**
 * @typedef {Object} LibraryMenuTab
 * @property {string} name The display name of the tab.
 * @property {string} href The url of the tab in the react router.
 */

/**
 * @callback LibraryMenuSetTabsFactory
 * @returns {LibraryMenuTab[]} The tabs.
 */

/**
 * @callback LibraryMenuSetTabs
 * @param {string} tabSetName The name of the tab set.
 * @param {number} index The index of the tab to select.
 * @param {LibraryMenuSetTabsFactory} factory The factory function to create the tabs.
 * @returns {void} Void.
 */

//#endregion

//#region API Client

/**
 * @typedef {{
 *   IsUsable: boolean;
*   IsActive: boolean;
*   State: "Disconnected" | "Connected" | "Connecting" | "Reconnecting";
* }} SignalRStatus
*/

/**
* @typedef {"Shoko" | "AniDB" | "TMDB"} GenericProvider
*/

/**
* @typedef {"Shoko" | "AniDB" | "TvDB" | "TMDB"} DescriptionProvider
*/

/**
* @typedef {"Shoko_Default" | "AniDB_Default" | "AniDB_LibraryLanguage" | "AniDB_CountryOfOrigin" | "TMDB_Default" | "TMDB_LibraryLanguage" | "TMDB_CountryOfOrigin"} TitleProvider
*/

/**
* @typedef {"ContentIndicators" | "Dynamic" | "DynamicCast" | "DynamicEnding" | "Elements" | "ElementsPornographyAndSexualAbuse" | "ElementsTropesAndMotifs" | "Fetishes" | "OriginProduction" | "OriginDevelopment" | "SettingPlace" | "SettingTimePeriod" | "SettingTimeSeason" | "SourceMaterial" | "TargetAudience" | "TechnicalAspects" | "TechnicalAspectsAdaptions" | "TechnicalAspectsAwards" | "TechnicalAspectsMultiAnimeProjects" | "Themes" | "ThemesDeath" | "ThemesTales" | "Ungrouped" | "Unsorted" | "CustomTags"} TagSource
*/

/**
* @typedef {"Parent" | "Child" | "Abstract" | "Weightless" | "Weighted" | "GlobalSpoiler" | "LocalSpoiler"} TagIncludeFilter
*/

/**
* @typedef {"Weightless" | "One" | "Two" | "Three" | "Four" | "Five" | "Six"} TagWeight
*/

/**
* @typedef {0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10} TagDepth
*/

/**
* @typedef {"None" | "Movies" | "Shared"} CollectionCreationType
*/

/**
* @typedef {"Default" | "ReleaseDate" | "Chronological" | "ChronologicalIgnoreIndirect"} SeasonOrderType
*/

/**
* @typedef {"Default" | "Excluded" | "AfterSeason" | "InBetweenSeasonByAirDate" | "InBetweenSeasonByOtherData" | "InBetweenSeasonMixed"} SpecialOrderType
*/

/**
* @typedef {"Default" | "Cache" | "Custom"} VirtualRootLocation
*/

/**
* @typedef {"Auto" | "Strict" | "Lax"} LibraryFilteringMode
*/

/**
* @typedef {{
*   UserId: string;
*   EnableSynchronization: boolean;
*   SyncUserDataAfterPlayback: boolean;
*   SyncUserDataUnderPlayback: boolean;
*   SyncUserDataUnderPlaybackLive: boolean;
*   SyncUserDataInitialSkipEventCount: number;
*   SyncUserDataUnderPlaybackAtEveryXTicks: number;
*   SyncUserDataUnderPlaybackLiveThreshold: number;
*   SyncUserDataOnImport: boolean;
*   SyncRestrictedVideos: boolean;
*   Username: string;
*   Token: string;
* }} UserConfig
*/

/**
* @typedef {{
*   LibraryId: string;
*   LibraryName: string | null;
*   MediaFolderId: string;
*   MediaFolderPath: string;
*   ImportFolderId: number;
*   ImportFolderName: string | null;
*   ImportFolderRelativePath: string;
*   IsVirtualRoot: boolean;
*   IsMapped: boolean;
*   IsFileEventsEnabled: boolean;
*   IsRefreshEventsEnabled: boolean;
*   IsVirtualFileSystemEnabled: boolean;
*   LibraryFilteringMode: LibraryFilteringMode;
* }} MediaFolderConfig
*/

/**
* @typedef {{
*   Version: string;
*   Commit: string | null;
*   ReleaseChannel: "Stable" | "Dev" | "Debug" | null;
*   ReleaseDate: string | null;
* }} ServerInformation
*/

/**
* @typedef {{
*   CanCreateSymbolicLinks: boolean;
*   Url: string;
*   PublicUrl: string;
*   ServerVersion: ServerInformation | null;
*   Username: string;
*   ApiKey: string;
*   ThirdPartyIdProviderList: Except<DescriptionProvider, "Shoko">[];
*   TitleMainOverride: boolean;
*   TitleMainList: TitleProvider[];
*   TitleMainOrder: TitleProvider[];
*   TitleAlternateOverride: boolean;
*   TitleAlternateList: TitleProvider[];
*   TitleAlternateOrder: TitleProvider[];
*   TitleAllowAny: boolean;
*   MarkSpecialsWhenGrouped: boolean;
*   DescriptionSourceOverride: boolean;
*   DescriptionSourceList: DescriptionProvider[];
*   DescriptionSourceOrder: DescriptionProvider[];
*   SynopsisCleanLinks: boolean;
*   SynopsisCleanMiscLines: boolean;
*   SynopsisRemoveSummary: boolean;
*   SynopsisCleanMultiEmptyLines: boolean;
*   TagOverride: boolean;
*   TagSources: TagSource[];
*   TagIncludeFilters: TagIncludeFilter[];
*   TagMinimumWeight: TagWeight;
*   TagMaximumDepth: TagDepth;
*   GenreOverride: boolean;
*   GenreSources: TagSource[];
*   GenreIncludeFilters: TagIncludeFilter[];
*   GenreMinimumWeight: TagWeight;
*   GenreMaximumDepth: TagDepth;
*   HideUnverifiedTags: boolean;
*   ContentRatingOverride: boolean;
*   ContentRatingList: GenericProvider[];
*   ContentRatingOrder: GenericProvider[];
*   ProductionLocationOverride: boolean;
*   ProductionLocationList: GenericProvider[];
*   ProductionLocationOrder: GenericProvider[];
*   UserList: UserConfig[];
*   AutoMergeVersions: boolean;
*   UseGroupsForShows: boolean;
*   SeparateMovies: boolean;
*   FilterMovieLibraries: boolean;
*   MovieSpecialsAsExtraFeaturettes: boolean;
*   AddTrailers: boolean;
*   AddCreditsAsThemeVideos: boolean;
*   AddCreditsAsSpecialFeatures: boolean;
*   CollectionGrouping: CollectionCreationType;
*   SeasonOrdering: SeasonOrderType;
*   SpecialsPlacement: SpecialOrderType;
*   AddMissingMetadata: boolean;
*   IgnoredFolders: string[];
*   VFS_Enabled: boolean;
*   VFS_Threads: number;
*   VFS_AddReleaseGroup: boolean;
*   VFS_AddResolution: boolean;
*   VFS_AttachRoot: boolean;
*   VFS_Location: VirtualRootLocation;
*   VFS_CustomLocation: string;
*   LibraryFilteringMode: LibraryFilteringMode;
*   MediaFolders: MediaFolderConfig[];
*   SignalR_AutoConnectEnabled: boolean;
*   SignalR_AutoReconnectInSeconds: number[];
*   SignalR_RefreshEnabled: boolean;
*   SignalR_FileEvents: boolean;
*   SignalR_EventSources: GenericProvider[];
*   Misc_ShowInMenu: boolean;
*   EXPERIMENTAL_MergeSeasons: boolean;
*   ExpertMode: boolean;
* }} PluginConfiguration
*/

/**
* Shoko API client.
*/
export const ShokoApiClient = {
   /**
    * The plugin ID.
    *
    * @private
    */
   pluginId: "5216ccbf-d24a-4eb3-8a7e-7da4230b7052",

   /**
    * Get the plugin configuration.
    *
    * @public
    * @returns {Promise<PluginConfiguration>} The plugin configuration.
    */
   getConfiguration() {
       return ApiClient.getPluginConfiguration(ShokoApiClient.pluginId);
   },

   /**
    * Update the plugin configuration.
    *
    * @public
    * @param {PluginConfiguration} config - The plugin configuration to update.
    * @returns {Promise<any>} Some sort of result we don't really care about.
    */
   updateConfiguration(config) {
       return ApiClient.updatePluginConfiguration(ShokoApiClient.pluginId, config);
   },

   /**
    * Get an API key for the username and password combo. Optionally get an
    * user key instead of a plugin key.
    *
    * @public
    * @param {string} username - The username.
    * @param {string} password - The password.
    * @param {boolean?} userKey - Optional. Whether to get a user key or a plugin key.
    * @returns {Promise<{ apikey: string; }>} The API key.
    */
   getApiKey(username, password, userKey = false) {
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
   },

   /**
    * Check the status of the SignalR connection.
    *
    * @private
    * @returns {Promise<SignalRStatus>} The SignalR status.
    */
   getSignalrStatus() {
       return ApiClient.fetch({
           dataType: "json",
           type: "GET",
           url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Status"),
       });
   },

   /**
    * Connects to the SignalR stream on the server.
    *
    * @public
    * @returns {Promise<SignalRStatus>} The SignalR status.
    */
   async signalrConnect() {
       await ApiClient.fetch({
           type: "POST",
           url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Connect"),
       });
       return ShokoApiClient.getSignalrStatus();
   },

   /**
    * Disconnects from the SignalR stream on the server.
    *
    * @public
    * @returns {Promise<SignalRStatus>} The SignalR status.
    */
   async signalrDisconnect() {
       await ApiClient.fetch({
           type: "POST",
           url: ApiClient.getUrl("Plugin/Shokofin/SignalR/Disconnect"),
       });
       return ShokoApiClient.getSignalrStatus();
   },
};

//#endregion

//#region State

/**
 * @type {{
*   config: PluginConfiguration | null;
*   currentTab: TabType;
*   expertPresses: number;
*   expertMode: boolean;
*   connected: boolean;
*   timeout: number | null;
* }}
*/
export const State = window["SHOKO_STATE_OBJECT"] || (window["SHOKO_STATE_OBJECT"] = {
   config: null,
   currentTab: "connection",
   expertPresses: 0,
   expertMode: false,
   connected: false,
   timeout: null,
});

//#endregion

//#region Tabs

/**
 * @typedef {"connection" | "metadata" | "library" | "vfs" | "users" | "signalr" | "misc" | "utilities"} TabType
 */

/**
 * @typedef {Object} ShokoTab
 * @property {TabType} id The tab id.
 * @property {string} href The tab href.
 * @property {string} helpHref The tab help href.
 * @property {string} name The tab name.
 * @property {boolean?} connected Optional. Whether the tab is only rendered when or when not connected.
 * @property {boolean?} expertMode Optional. Whether the tab is only rendered when in or not in expert mode.
 */

const DefaultHelpLink = "https://docs.shokoanime.com/jellyfin/configuring-shokofin/";

/**
 * @type {readonly ShokoTab[]}
 */
const Tabs = [
    {
        id: "connection",
        href: getConfigurationPageUrl("Shoko.Settings", "connection"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#connecting-to-shoko-server",
        name: "Connection",
    },
    {
        id: "metadata",
        href: getConfigurationPageUrl("Shoko.Settings", "metadata"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#metadata",
        name: "Metadata",
        connected: true,
    },
    {
        id: "library",
        href: getConfigurationPageUrl("Shoko.Settings", "library"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#library",
        name: "Library",
        connected: true,
    },
    {
        id: "vfs",
        href: getConfigurationPageUrl("Shoko.Settings", "vfs"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#vfs",
        name: "VFS",
        connected: true,
    },
    {
        id: "users",
        href: getConfigurationPageUrl("Shoko.Settings", "users"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#users",
        name: "Users",
        connected: true,
    },
    {
        id: "signalr",
        href: getConfigurationPageUrl("Shoko.Settings", "signalr"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#signalr",
        name: "SignalR",
        connected: true,
    },
    {
        id: "misc",
        href: getConfigurationPageUrl("Shoko.Settings", "misc"),
        helpHref: "https://docs.shokoanime.com/jellyfin/configuring-shokofin/#misc",
        name: "Misc",
        connected: true,
        expertMode: true,
    },
    {
        id: "utilities",
        href: getConfigurationPageUrl("Shoko.Settings", "utilities"),
        helpHref: "https://docs.shokoanime.com/jellyfin/utilities",
        name: "Utilities",
    },
];

/**
 * Responsible for updating the tabs at the top of the page.
 *
 * @param {HTMLElement} view - The view element.
 * @param {TabType} [tabName] - Optional. Change the current tab.
 */
export function updateTabs(view, tabName) {
    if (tabName) {
        State.currentTab = tabName;
    }

    const tabs = Tabs.filter(tab => tab.id === State.currentTab || (tab.connected === undefined || tab.connected === State.connected) && (tab.expertMode === undefined || tab.expertMode === State.expertMode));
    let index = tabs.findIndex((tab => tab.id === State.currentTab));
    if (index === -1) {
        index = 0;
    }

    LibraryMenu.setTabs("shoko", index, () => tabs);

    const helpLink = view.querySelector(".sectionTitleContainer > a.headerHelpButton");
    if (helpLink) {
        const currentTab = Tabs.find(tab => tab.id === State.currentTab);
        if (currentTab) {
            helpLink.setAttribute("href", currentTab.helpHref);
        }
        else {
            helpLink.setAttribute("href", DefaultHelpLink);
        }
    }
}

//#endregion

//#region Constants


const Messages = {
    UnableToRender: "There was an error loading the page, please refresh once to see if that will fix it, and if it doesn't, then reach out to support or debug it yourself. Your call.",
};


//#endregion

//#region Event Lifecycle

/**
 * Possible properties.
 *
 * @typedef {"fullscreen"} Property
 */

/**
 * View extra options.
 *
 * @typedef {Object} ViewExtraOptions
 * @property {boolean} supportsThemeMedia Supports theme media.
 * @property {boolean} enableMediaControls Enables media controls.
 */

/**
 * Minimal event details.
 *
 * @typedef {Object} MinimalDetails
 * @property {string} type The request route type.
 * @property {Property[]} properties The properties that are available in the event.
 */

/**
 * Full event details.
 *
 * @typedef {Object} FullDetails
 * @property {string?} type The request route type.
 * @property {Property[]} properties The properties that are available in the event.
 * @property {Record<string, string>} params The search query parameters of the current view, from the React's router's POV.
 * @property {boolean} [isRestored] Whether the current view is restored from a previous hidden state or a brand new view.
 * @property {any?} state The state of the current view, from the React's router's POV.
 * @property {ViewExtraOptions} options - The options of the current view.
 */

/**
 * First event that's triggered when the page is initialized.
 *
 * @callback onViewInit
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<{}>} event - The event with the minimal details.
 * @returns {void} Void.
 */

/**
 * Triggered after the init event and when the page is restored from a previous
 * hidden state, but right before the view is shown.
 *
 * @callback onViewBeforeShow
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<FullDetails>} event - The event with the full details.
 * @returns {void} Void.
 */
/**
 * Triggered after the init event and when the page is restored from a previous
 * hidden state, when the view is shown.
 *
 * @callback onViewShow
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<FullDetails>} event - The event with the full details.
 * @returns {void} Void.
 */

/**
 * Triggered right before the view is hidden. Can be used to cancel the
 * hiding process by calling {@link Event.preventDefault event.preventDefault()}.
 *
 * @callback onViewBeforeHide
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<MinimalDetails>} event - The event with the minimal details.
 * @returns {void} Void.
 */

/**
 * Triggered right after the view is hidden. Can be used for clearing up state
 * before the view is shown again or before it's destroyed.
 *
 * @callback onViewHide
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<MinimalDetails>} event - The event with the minimal details.
 * @returns {void} Void.
 */

/**
 * Triggered right before the view is destroyed. This means the view will not
 * be shown again. If you navigate to and from the page it will instead
 * re-initialise a new instance of the view if it has already destroyed the
 * previous instance by the time it should show the view.
 *
 * @callback onViewDestroy
 * @this {HTMLDivElement} - The view element.
 * @param {CustomEvent<{}>} event - The event with the no details.
 * @returns {void} Void.
 */

/**
 * View lifecycle events in all their glory.
 *
 * @typedef {Object} ViewLifecycleEvents
 * @property {onViewInit} onInit
 *
 * First event that's triggered when the page is initialized.
 *
 * @property {onViewBeforeShow} onBeforeShow
 *
 * Triggered after the init event and when the page is restored from a previous
 * hidden state, but right before the view is shown.
 *
 * @property {onViewShow} onShow
 *
 * Triggered after the init event and when the page is restored from a previous
 * hidden state, when the view is shown.
 *
 * @property {onViewBeforeHide} onBeforeHide
 *
 * Triggered right before the view is hidden. Can be used to cancel the
 * hiding process by calling {@link Event.preventDefault event.preventDefault()}.
 *
 * @property {onViewHide} onHide
 *
 * Triggered right after the view is hidden. Can be used for clearing up state
 * before the view is shown again or before it's destroyed.
 *
 * @property {onViewDestroy} onDestroy
 *
 * Triggered right before the view is destroyed. This means the view will not
 * be shown again. If you navigate to and from the page it will instead
 * re-initialise a new instance of the view if it has already destroyed the
 * previous instance by the time it should show the view.
 */

/**
 * @param {HTMLDivElement} view - The view element.
 * @param {ViewLifecycleEvents} events - The events.
 * @param {TabType} [initialTab] - The initial tab.
 * @param {boolean} [hide] - Whether to hide the view immediately.
 * @param {boolean} [show] - Whether to show the view immediately.
 * @returns {void} Void.
 */
export function setupEvents(view, events, initialTab = "connection", hide = false, show = false) {
    if (events.onBeforeShow) {
        view.addEventListener("viewbeforeshow", events.onBeforeShow.bind(view));
    }

    if (events.onShow) {
        view.addEventListener("viewshow", async (event) => {
            try {
                // Clear the current timeout if there is one.
                if (State.timeout) {
                    clearTimeout(State.timeout);
                    State.timeout = null;
                }

                // Set the current tab if the current view supports tabs.
                if (view.classList.contains("withTabs")) {
                    State.currentTab = new URLSearchParams(window.location.href.split("#").slice(1).join("#").split("?").slice(1).join("?")).get("tab") || initialTab;

                    // And update the tabs if the state is already initialised.
                    if (State.config) {
                        updateTabs(view);
                    }
                }

                // Initialise the state now if it's not yet initialised.
                if (!State.config) {
                    Dashboard.showLoadingMsg();
                    State.config = await ShokoApiClient.getConfiguration();
                    State.expertPresses = 0;
                    State.expertMode = State.config.ExpertMode;
                    State.connected = Boolean(State.config.ApiKey);
                }

                // Show the view.
                await events.onShow.call(view, event);

                if (view.classList.contains("withTabs")) {
                    updateTabs(view);
                }
            }
            catch (err) {
                // Show an error message if we failed to render the view.
                Dashboard.alert(Messages.UnableToRender);
                console.error(Messages.UnableToRender, err);
            }
            finally {
                // Hide the loading message if there is one.
                Dashboard.hideLoadingMsg();
            }
        });
    }

    if (events.onBeforeHide) {
        view.addEventListener("viewbeforehide", events.onBeforeHide.bind(view));
    }

    if (events.onHide) {
        view.addEventListener("viewhide", (event) => {
            // Clear the current timeout if there is one.
            if (State.timeout) {
                clearTimeout(State.timeout);
                State.timeout = null;
            }

            // Hide the view.
            events.onHide.call(view, event);

            // Reset the state after the view is hidden if we're not switching
            // to another view.
            State.timeout = setTimeout(() => {
                State.config = null;
                State.currentTab = initialTab;
                State.expertPresses = 0;
                State.expertMode = false;
                State.connected = false;
                State.timeout = null;
            }, 100);
        });
    }

    if (events.onDestroy) {
        view.addEventListener("viewdestroy", events.onDestroy.bind(view));
    }

    // Override any links with link redirection set.
    view.querySelectorAll("a.link-redirection").forEach(overrideLink);

    view.querySelectorAll("div[is=\"sortable-checkbox-list\"]").forEach(overrideSortableCheckboxList);

    // The view event is only send if a controller factory is not provided…
    // which is not the case here, since we're running in the controller factory
    // right now. So just send the init event now.
    if (events.onInit) {
        const initEvent = new CustomEvent("viewinit", { detail: {}, bubbles: true, cancelable: false });

        events.onInit.call(view, initEvent);

        // Do nothing if both show and hide are requested.
        if (hide && show) return;

        // Show the view if requested.
        if (show) {
            const eventDetails = {
                /** @type {FullDetails} */
                detail: {
                    type: view.getAttribute("data-type") || null,
                    params: Object.fromEntries(new URLSearchParams(window.location.hash.split("#").slice(1).join("#").split("?").slice(1).join("?"))),
                    properties: (view.getAttribute("data-properties") || "").split(","),
                    isRestored: undefined,
                    state: null,
                    options: {
                        supportsThemeMedia: false,
                        enableMediaControls: true,
                    },
                },
                bubbles: true,
                cancelable: false,
            }
            view.dispatchEvent(new CustomEvent("viewbeforeshow", eventDetails));
            view.dispatchEvent(new CustomEvent("viewshow", eventDetails));
        }

        // Hide the view if requested.
        if (hide) {
            const eventDetails = {
                /** @type {MinimalDetails} */
                detail: {
                    type: view.getAttribute("data-type") || null,
                    properties: (view.getAttribute("data-properties") || "").split(","),
                },
                bubbles: true,
                cancelable: false,
            };
            view.dispatchEvent(new CustomEvent("viewbeforehide", { ...eventDetails, cancelable: true }));
            view.dispatchEvent(new CustomEvent("viewhide", eventDetails));
        }
    }
}

//#endregion

//#region Controller Factory

/**
 * A factory responsible for creating a new view and setting up its events as
 * needed.
 *
 * @callback controllerFactoryFn
 * @param {HTMLDivElement} view - The view element.
 * @returns {void} Void.
 */

/**
 * Controller factory options.
 *
 * @typedef {Object} controllerFactoryOptions
 * @property {ViewLifecycleEvents} events  The lifecycle events for the view.
 * @property {TabType} [initialTab] - The initial tab.
 * @property {boolean} [show] - Whether to show the view immediately.
 * @property {boolean} [hide] - Whether to hide the view immediately.
 */

/**
 * Create a new view and set up its events as needed.
 *
 * @param {controllerFactoryOptions} options - The controller factory options.
 * @returns {controllerFactoryFn} The controller factory.
 */
export function createControllerFactory(options) {
    const { events, initialTab, hide, show } = options;
    return function(view) {
        setupEvents(view, events, initialTab, hide, show);
    }
}

//#endregion

//#region Helpers

//#region Helpers - Handle Error

/**
 * Handle an error during a configuration update.
 *
 * @param {any} err - The error.
 */
export function handleError(err) {
    console.error(err);
    Dashboard.alert(`An error occurred; ${err.message}`);
    Dashboard.hideLoadingMsg();
}

//#endregion

//#region Helpers - Override Link

/**
 * Construct the URL for a tab on the configuration page.
 *
 * @param {string} page
 * @param {string} [tab]
 * @returns {string}
 */
function getConfigurationPageUrl(page, tab = "") {
    const urlSearch = new URLSearchParams();
    urlSearch.set("name", page);
    if (tab) {
        urlSearch.set("tab", tab);
    }
    return "configurationpage?" + urlSearch.toString();
}

/**
 * Redirect a link to the configuration page through React instead of the
 * browser.
 *
 * @param {HTMLAnchorElement} event
 */
function onLinkRedirectClick(event) {
    event.preventDefault();
    Dashboard.navigate(getConfigurationPageUrl(event.target.dataset.href));
}

/**
 * Override links to the configuration page in the DOM.
 *
 * @param {HTMLAnchorElement} target - The link to override.
 * @returns {void} Void.
 */
function overrideLink(target) {
    const page = target.dataset.page;
    target.href = location.href.split("#")[0] + "#" + getConfigurationPageUrl(page);
    target.addEventListener("click", onLinkRedirectClick);
}

//#endregion

//#region Helpers - Readonly List

/**
 * Initialize a readonly list.
 *
 * @param {HTMLFormElement} form
 * @param {string} name
 * @param {string[]} entries
 * @returns {void}
 */
export function renderReadonlyList(form, name, entries) {
    const list = form.querySelector(`#${name} .checkboxList`);
    const listItems = entries.map((entry) =>
        `<div class="listItem"><div class="listItemBody"><h3 class="listItemBodyText">${entry}</h3></div></div>`
    );
    list.innerHTML = listItems.join("");
}

//#endregion

//#region Helpers - Checkbox List

/**
 * @param {HTMLFormElement} form
 * @param {string} name
 * @param {string[]} enabled
 * @returns {void}
 **/
export function renderCheckboxList(form, name, enabled) {
    for (const item of Array.from(form.querySelectorAll(`#${name}[is=\"checkbox-list\"] .listItem input[data-option]`))) {
        if (enabled.includes(item.dataset.option))
            item.checked = true;
    }
}

/**
 * Retrieve the enabled state from a simple list.
 *
 * @param {HTMLFormElement} form
 * @param {string} name - Name of the selector list to retrieve.
 * @returns {string[]}
 **/
export function retrieveCheckboxList(form, name) {
    return Array.from(form.querySelectorAll(`#${name}[is=\"checkbox-list\"] .listItem input[data-option]`))
        .filter(item => item.checked)
        .map(item => item.dataset.option)
        .sort();
}

//#endregion

//#region Helpers - Sortable Checkbox List

/**
 * Handle the click event on the buttons within a sortable list.
 *
 * @param {PointerEvent} event - The click event.
 **/
function onSortableContainerClick(event) {
    const btnSortable = getParentWithClass(event.target, "btnSortable");
    if (!btnSortable) return;

    const listItem = getParentWithClass(btnSortable, "sortableOption");
    if (!listItem) return;

    const list = getParentWithClass(listItem, "paperList");
    if (!list) return;

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

    let index = 0;
    for (const option of list.querySelectorAll(".sortableOption")) {
        adjustSortableListElement(option, index++);
    }
}

/**
 * Override the click event on the buttons within a sortable list.
 *
 * @param {HTMLDivElement} element
 */
function overrideSortableCheckboxList(element) {
    element.addEventListener("click", onSortableContainerClick);
}

/**
 * Adjust the sortable list element.
 *
 * @param {HTMLElement} element - The element.
 * @param {number} index - The index of the element.
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
 * Get the parent element with the given class, or null if not found.
 *
 * @param {HTMLElement} element - The element.
 * @param {string} className - The class name.
 * @returns {HTMLElement | null} The parent element with the given class, or
 * null if not found.
 */
function getParentWithClass(element, className) {
    return element.parentElement.classList.contains(className) ? element.parentElement : null;
}

/**
 * Render a sortable checkbox list.
 *
 * @param {HTMLFormElement} form
 * @param {string} name
 * @param {string[]} enabled
 * @param {string[]} order
 * @returns {void}
 */
export function renderSortableCheckboxList(form, name, enabled, order) {
    let index = 0;
    const list = form.querySelector(`#${name}[is=\"sortable-checkbox-list\"] .checkboxList`);
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
 * Retrieve the enabled state and order list from a sortable list.
 *
 * @param {HTMLElement} view - The view element.
 * @param {string} name - The name of the sortable checkbox list to retrieve.
 * @returns {[string[], string[]]}
 */
export function retrieveSortableCheckboxList(view, name) {
    const titleElements = Array.from(view.querySelectorAll(`#${name}[is=\"sortable-checkbox-list\"] .listItem input[data-option]`));
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

//#endregion

//#endregion