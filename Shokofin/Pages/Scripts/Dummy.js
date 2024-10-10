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
promise.then(({ State, createControllerFactory }) => {

createControllerFactory({
    show,
    hide,
    initialTab: "utilities",
    events: {
        onShow(event) {
            const content = this.querySelector(".content-primary");
            const { isRestored = false } = event.detail;
            if (isRestored) {
                State.timeout = setTimeout(() => {
                    content.innerHTML = "Baka baka!";
                }, 2000);
            }
            else {
                State.timeout = setTimeout(() => {
                    content.innerHTML = "Baka!";
                }, 2000);
            }
        },
        onHide() {
            const content = this.querySelector(".content-primary");
            content.innerHTML = "Dummy.";
        },
    },
})(view);

}); }
