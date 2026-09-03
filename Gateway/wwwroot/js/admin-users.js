// Issue 13: User Management UI - client-side wiring for the active toggle
// (AJAX, Issue 5) and the delete confirmation modal (Issue 13).
(function () {
    "use strict";

    function getAntiForgeryToken() {
        var input = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    // --- Active toggle (AJAX) ---------------------------------------------
    document.querySelectorAll(".js-toggle-active").forEach(function (toggle) {
        toggle.addEventListener("change", function () {
            var url = toggle.getAttribute("data-toggle-url");
            var token = getAntiForgeryToken();
            var previousChecked = !toggle.checked; // state before this change

            toggle.disabled = true;

            fetch(url, {
                method: "POST",
                headers: {
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": token || "",
                },
            })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("Toggle request failed with status " + response.status);
                    }
                    return response.json();
                })
                .then(function (data) {
                    toggle.checked = data.isActive;

                    // Update the status badge in the same table row, if present.
                    var row = toggle.closest("tr[data-user-row]");
                    if (row) {
                        var badge = row.querySelector("[data-status-badge]");
                        if (badge) {
                            badge.textContent = data.isActive ? "Active" : "Inactive";
                            badge.classList.toggle("bg-success", data.isActive);
                            badge.classList.toggle("bg-secondary", !data.isActive);
                        }
                    }

                    // Update the label next to the switch on the Details page, if present.
                    var label = document.querySelector('label[for="' + toggle.id + '"]');
                    if (label) {
                        label.textContent = data.isActive ? "Active" : "Inactive";
                    }
                })
                .catch(function () {
                    // Revert the visual state on failure so the UI doesn't lie
                    // about what actually happened server-side.
                    toggle.checked = previousChecked;
                    alert("Couldn't update this user's status. Please try again.");
                })
                .finally(function () {
                    toggle.disabled = false;
                });
        });
    });

    // --- Delete confirmation modal -----------------------------------------
    var deleteForm = document.getElementById("deleteUserForm");
    var deleteEmailLabel = document.getElementById("deleteUserModalEmail");

    document.querySelectorAll(".js-delete-trigger").forEach(function (button) {
        button.addEventListener("click", function () {
            var deleteUrl = button.getAttribute("data-delete-url");
            var email = button.getAttribute("data-user-email");

            if (deleteForm) {
                deleteForm.setAttribute("action", deleteUrl);
            }
            if (deleteEmailLabel) {
                deleteEmailLabel.textContent = email || "this user";
            }
        });
    });
})();
