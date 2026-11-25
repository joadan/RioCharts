// Blazor JS initializer: runs once Blazor WebAssembly has started and rendered the app.
// Hides the top progress bar shown over prerendered pages (index.html, .boot-progress).
export function afterWebAssemblyStarted() {
    document.documentElement.classList.add("app-started");
    setTimeout(() => document.querySelector(".boot-progress")?.remove(), 400);
}
