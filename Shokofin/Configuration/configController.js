const PluginConfig = {
    pluginId: "5216ccbf-d24a-4eb3-8a7e-7da4230b7052"
};

function loadUserConfig(page, userId) {
    Dashboard.showLoadingMsg();

    ApiClient.getPluginConfiguration(PluginConfig.pluginId).then(function (config) {
        const userConfig = config.Options.filter(function (c) {
            return userId === c.UserId;
        })[0] || {};

        page.querySelector('#chkEnablePushbullet').checked = userConfig.Enabled || false;
        if (userConfig.ApiKey) {
            page.querySelector("#UserUsername").setAttribute("disabled", "");
            page.querySelector("#UserUsername").removeAttribute("required");
        }
        else {
            page.querySelector("#UserUsername").removeAttribute("disabled");
            page.querySelector("#UserUsername").setAttribute("required", "");
            page.querySelector("#UserPassword").removeAttribute("disabled");
        }
        page.querySelector("#UserUsername").value = userConfig.Username || '';
        page.querySelector("#UserPassword").value = userConfig.Password || '';

        Dashboard.hideLoadingMsg();
    });
}

export default function (view) {

    // view.querySelector('#testNotification').addEventListener('click', function () {
    //     Dashboard.showLoadingMsg();

    //     ApiClient.getPluginConfiguration(PushbulletPluginConfig.uniquePluginId).then(function (config) {
    //         if (!config.Options.length) {
    //             Dashboard.hideLoadingMsg();
    //             Dashboard.alert('Please configure and save at least one notification account.');
    //         }

    //         config.Options.map(function (c) {
    //             ApiClient.ajax({
    //                 type: 'POST',
    //                 url: ApiClient.getUrl('Notification/Pushbullet/Test/' + c.UserId)
    //             }).then(function () {
    //                 Dashboard.hideLoadingMsg();
    //             }, onError);
    //         });
    //     });
    // });

    // view.querySelector('.PushbulletConfigurationForm').addEventListener('submit', function (e) {
    //     Dashboard.showLoadingMsg();
    //     const form = this;
    //     ApiClient.getPluginConfiguration(PushbulletPluginConfig.uniquePluginId).then(function (config) {
    //         const userId = form.querySelector('#UserSelector').value;
    //         let PushbulletConfig = config.Options.filter(function (c) {
    //             return userId === c.UserId;
    //         })[0];

    //         if (!PushbulletConfig) {
    //             PushbulletConfig = {};
    //             config.Options.push(PushbulletConfig);
    //         }

    //         PushbulletConfig.UserId = userId;
    //         PushbulletConfig.Enabled = form.querySelector('#chkEnablePushbullet').checked;
    //         PushbulletConfig.Channel = form.querySelector('#txtPushbulletChannel').value;
    //         PushbulletConfig.Token = form.querySelector('#txtPushbulletAuthKey').value;

    //         ApiClient.updatePluginConfiguration(PushbulletPluginConfig.uniquePluginId, config).then(function (result) {
    //             Dashboard.processPluginConfigurationUpdateResult(result);
    //         });
    //     });
    //     e.preventDefault();
    //     return false;
    // });

    view.querySelector('#UserSelector').addEventListener('change', function () {
        loadUserConfig(view, this.value);
    });

    view.querySelector('.shokoConfigPage')
        .addEventListener('viewshow', function () {
            Dashboard.showLoadingMsg();
            const page = this;
            const onError = () => {
                Dashboard.alert('There was an error loading the page, please refresh once to see if that will fix it.');
                Dashboard.hideLoadingMsg();
            };

            Promise.all([
                ApiClient.getPluginConfiguration(PluginConfig.pluginId),
                ApiClient.getUsers(),
            ]).then((config, users) => {

                // Connection settings
                page.querySelector('#Host').value = config.Host;
                page.querySelector('#Username').value = config.Username;
                page.querySelector('#Password').value = config.Password;

                // Metadata settings
                page.querySelector('#TitleMainType').value = config.TitleMainType;
                page.querySelector('#TitleAlternateType').value = config.TitleAlternateType;
                page.querySelector('#DescriptionSource').value = config.DescriptionSource;
                page.querySelector('#CleanupAniDBDescriptions').checked = config.SynopsisCleanMultiEmptyLines || config.SynopsisCleanLinks;
                page.querySelector('#MinimalAniDBDescriptions').checked = config.SynopsisRemoveSummary || config.SynopsisCleanMiscLines;

                // Library settings
                page.querySelector('#SeriesGrouping').value = config.SeriesGrouping;
                page.querySelector('#BoxSetGrouping').value = config.BoxSetGrouping;
                page.querySelector('#FilterOnLibraryTypes').checked = config.FilterOnLibraryTypes;
                page.querySelector('#SpecialsPlacement').value = config.SpecialsPlacement;
                page.querySelector('#MarkSpecialsWhenGrouped').checked = config.MarkSpecialsWhenGrouped;

                // Synchronization settings
                page.querySelector('#UpdateWatchedStatus').checked = config.UpdateWatchedStatus;
                page.querySelector('#UserSelector').innerHTML = users.map((user) => `<option value="${user.Id}">${user.Name}</option>`);

                // Tag settings
                page.querySelector('#HideArtStyleTags').checked = config.HideArtStyleTags;
                page.querySelector('#HideSourceTags').checked = config.HideSourceTags;
                page.querySelector('#HideMiscTags').checked = config.HideMiscTags;
                page.querySelector('#HidePlotTags').checked = config.HidePlotTags;
                page.querySelector('#HideAniDbTags').checked = config.HideAniDbTags;

                // Advanced settings
                page.querySelector('#PublicHost').value = config.PublicHost;
                page.querySelector('#PreferAniDbPoster').checked = config.PreferAniDbPoster;
                page.querySelector('#AddAniDBId').checked = config.AddAniDBId;

                if (!config.ApiKey.length) {
                    Dashboard.alert('Please make sure that the connection settings are correct before you continue.');
                }

                selector.dispatchEvent(new Event('change', {
                    bubbles: true,
                    cancelable: false
                }));
            }, onError);
        });

    view.querySelector('.shokoConfigForm')
        .addEventListener('submit', function (e) {
            Dashboard.showLoadingMsg();
            const page = this;

            ApiClient.getPluginConfiguration(PluginConfig.pluginId).then((config) => {
                const userId = form.querySelector('#UserSelector').value;
                const userName = page.querySelector('#UserUsername').value;
                const userPass = page.querySelector('#UserPassword').value;
                
                let host = page.querySelector('#Host').value;
                if (host.endsWith("/")) {
                    host = host.slice(0, -1);
                    page.querySelector('#Host').value = host;
                }
                
                let publicHost = page.querySelector('#PublicHost').value;
                if (publicHost.endsWith("/")) {
                    publicHost = publicHost.slice(0, -1);
                    page.querySelector('#PublicHost').value = publicHost;
                }
                
                let currentUserConfig = config.Options.find((c) => userId === c.UserId);
                if (!currentUserConfig) {
                    currentUserConfig = {};
                    config.Options.push(currentUserConfig);
                }

                const username = page.querySelector('#Username').value;
                const password = page.querySelector('#Password').value;
                // Reset the api-key if the username and/or password have changed.
                if (config.Username != username || config.Password !== password)
                    config.ApiKey = "";

                // Connection settings
                config.Host = host;
                config.Username = username;
                config.Password = password;

                // Metadata settings
                config.TitleMainType = page.querySelector('#TitleMainType').value;
                config.TitleAlternateType = page.querySelector('#TitleAlternateType').value;
                config.DescriptionSource = page.querySelector('#DescriptionSource').value;
                config.SynopsisCleanLinks = page.querySelector('#CleanupAniDBDescriptions').checked;
                config.SynopsisCleanMultiEmptyLines = page.querySelector('#CleanupAniDBDescriptions').checked;
                config.SynopsisCleanMiscLines = page.querySelector('#MinimalAniDBDescriptions').checked;
                config.SynopsisRemoveSummary = page.querySelector('#MinimalAniDBDescriptions').checked;

                // Library settings
                config.SeriesGrouping = page.querySelector('#SeriesGrouping').value;
                config.BoxSetGrouping = page.querySelector('#BoxSetGrouping').value;
                config.FilterOnLibraryTypes = page.querySelector('#FilterOnLibraryTypes').checked;
                config.SpecialsPlacement = page.querySelector('#SpecialsPlacement').value;
                config.MarkSpecialsWhenGrouped = page.querySelector('#MarkSpecialsWhenGrouped').checked;

                // Synchronization settings
                config.UpdateWatchedStatus = page.querySelector('#UpdateWatchedStatus').checked;
                currentUserConfig.UserId = userId;
                currentUserConfig.Enabled = page.querySelector('#UserEnabled').checked
                if (currentUserConfig.ApiKey ? (currentUserConfig.Username != userName || currentUserConfig.Password != userPass) : true)
                    currentUserConfig.ApiKey = "";
                currentUserConfig.Username = userName;
                currentUserConfig.Password = userPass;

                // Tag settings
                config.HideArtStyleTags = page.querySelector('#HideArtStyleTags').checked;
                config.HideSourceTags = page.querySelector('#HideSourceTags').checked;
                config.HideMiscTags = page.querySelector('#HideMiscTags').checked;
                config.HidePlotTags = page.querySelector('#HidePlotTags').checked;
                config.HideAniDbTags = page.querySelector('#HideAniDbTags').checked;

                // Advanced settings
                config.PublicHost = publicHost;
                config.PreferAniDbPoster = page.querySelector('#PreferAniDbPoster').checked;
                config.AddAniDBId = page.querySelector('#AddAniDBId').checked;

                ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
                ApiClient.updatePluginConfiguration(PluginConfig.pluginId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            });
            e.preventDefault();
            return false;
        });
}
