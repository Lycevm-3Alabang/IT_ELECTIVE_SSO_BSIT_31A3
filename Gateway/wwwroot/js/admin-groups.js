// Group management UI: live "Saved as: [AppName]-[GroupName]" preview on the
// Create/Edit forms, and a confirmation prompt before deleting a group.
// The server does the real prefixing and validation; this is only a preview.
(function () {
    "use strict";

    // --- Name preview ------------------------------------------------------
    var form = document.getElementById("groupForm");
    var preview = document.getElementById("groupNamePreview");
    var nameInput = document.getElementById("GroupName");
    var appSelect = document.getElementById("TenantAppId");

    function currentAppName() {
        if (appSelect && appSelect.tagName === "SELECT") {
            var option = appSelect.options[appSelect.selectedIndex];
            return option && option.value ? option.getAttribute("data-name") || "" : "";
        }
        return form ? form.getAttribute("data-app-name") || "" : "";
    }

    function updatePreview() {
        var app = currentAppName();
        if (!app) {
            preview.textContent = "Select an app to see the final name";
            return;
        }

        var prefix = app + "-";
        var name = nameInput.value.trim();
        // Same rule as the server: a name typed with the prefix isn't prefixed twice.
        if (name.toLowerCase().indexOf(prefix.toLowerCase()) === 0) {
            name = name.substring(prefix.length).trim();
        }
        preview.textContent = prefix + (name || "…");
    }

    if (form && preview && nameInput) {
        nameInput.addEventListener("input", updatePreview);
        if (appSelect) {
            appSelect.addEventListener("change", updatePreview);
        }
        updatePreview();
    }

    // --- Delete confirmation ----------------------------------------------
    document.querySelectorAll("form.js-delete-group").forEach(function (deleteForm) {
        deleteForm.addEventListener("submit", function (event) {
            var name = deleteForm.getAttribute("data-group-name") || "this group";
            var members = parseInt(deleteForm.getAttribute("data-member-count"), 10) || 0;
            var message = "Delete group \"" + name + "\"?";
            if (members > 0) {
                message += "\n\n" + members + " user" + (members === 1 ? " is" : "s are") +
                    " in this group and will be removed from it.";
            }
            if (!window.confirm(message)) {
                event.preventDefault();
            }
        });
    });
})();
