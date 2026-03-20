// FAIT Cowork — client-side helpers

// Trigger a click on a DOM element by ID.
// Used by DesignWorkspace.razor to open the hidden file input for reference image upload.
// Usage in Blazor: await JS.InvokeVoidAsync("triggerElementClick", "design-ref-input");
window.triggerElementClick = function (id) {
    var el = document.getElementById(id);
    if (el) el.click();
};
