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

    // --- Reset password (confirmation -> AJAX -> copyable result) ----------
    // Issues 117-122.
    var resetModalEl = document.getElementById("resetPasswordModal");
    var resetEmailLabel = document.getElementById("resetPasswordModalEmail");
    var resetResultEmailLabel = document.getElementById("resetPasswordResultEmail");
    var resetConfirmStep = document.getElementById("resetPasswordConfirmStep");
    var resetResultStep = document.getElementById("resetPasswordResultStep");
    var resetConfirmBtn = document.getElementById("resetPasswordConfirmBtn");
    var resetCancelBtn = document.getElementById("resetPasswordCancelBtn");
    var resetDoneBtn = document.getElementById("resetPasswordDoneBtn");
    var temporaryPasswordField = document.getElementById("temporaryPasswordField");
    var copyBtn = document.getElementById("copyTemporaryPasswordBtn");
    var copyFeedback = document.getElementById("copyPasswordFeedback");
    var currentResetUrl = null;

    function showSuccessAlert(message) {
        var container = document.getElementById("userAlerts");
        if (!container) {
            return;
        }
        var alert = document.createElement("div");
        alert.className = "alert alert-success alert-dismissible fade show";
        alert.setAttribute("role", "alert");
        alert.textContent = message;

        var closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.className = "btn-close";
        closeBtn.setAttribute("data-bs-dismiss", "alert");
        closeBtn.setAttribute("aria-label", "Close");
        alert.appendChild(closeBtn);

        container.appendChild(alert);
    }

    function addAuditLogRow(actionText) {
        var list = document.getElementById("auditLogList");
        if (!list) {
            return;
        }
        // Remove the "No activity recorded yet." placeholder, if present.
        var placeholder = list.querySelector("li.text-muted");
        if (placeholder) {
            placeholder.remove();
        }
        var item = document.createElement("li");
        item.className = "list-group-item d-flex justify-content-between align-items-center small";

        var actionSpan = document.createElement("span");
        actionSpan.textContent = actionText;

        var timeSpan = document.createElement("span");
        timeSpan.className = "text-muted";
        timeSpan.textContent = new Date().toLocaleString();

        item.appendChild(actionSpan);
        item.appendChild(timeSpan);
        list.insertBefore(item, list.firstChild);
    }

    function resetModalToConfirmStep() {
        if (resetConfirmStep) resetConfirmStep.classList.remove("d-none");
        if (resetResultStep) resetResultStep.classList.add("d-none");
        if (resetConfirmBtn) resetConfirmBtn.classList.remove("d-none");
        if (resetCancelBtn) resetCancelBtn.classList.remove("d-none");
        if (resetDoneBtn) resetDoneBtn.classList.add("d-none");
        if (copyFeedback) copyFeedback.classList.add("d-none");
        if (temporaryPasswordField) temporaryPasswordField.value = "";
    }

    document.querySelectorAll(".js-reset-password-trigger").forEach(function (button) {
        button.addEventListener("click", function () {
            currentResetUrl = button.getAttribute("data-reset-url");
            var email = button.getAttribute("data-user-email") || "this user";
            if (resetEmailLabel) resetEmailLabel.textContent = email;
            resetModalToConfirmStep();
        });
    });

    if (resetConfirmBtn) {
        resetConfirmBtn.addEventListener("click", function () {
            if (!currentResetUrl) {
                return;
            }
            var token = getAntiForgeryToken();
            resetConfirmBtn.disabled = true;

            fetch(currentResetUrl, {
                method: "POST",
                headers: {
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": token || "",
                },
            })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("Reset request failed with status " + response.status);
                    }
                    return response.json();
                })
                .then(function (data) {
                    // Step 2: show the copyable temporary password.
                    if (resetResultEmailLabel) resetResultEmailLabel.textContent = data.email;
                    if (temporaryPasswordField) temporaryPasswordField.value = data.temporaryPassword;
                    if (resetConfirmStep) resetConfirmStep.classList.add("d-none");
                    if (resetResultStep) resetResultStep.classList.remove("d-none");
                    if (resetConfirmBtn) resetConfirmBtn.classList.add("d-none");
                    if (resetCancelBtn) resetCancelBtn.classList.add("d-none");
                    if (resetDoneBtn) resetDoneBtn.classList.remove("d-none");

                    addAuditLogRow("Password reset");
                    showSuccessAlert("Password for " + data.email + " was reset successfully.");
                })
                .catch(function () {
                    alert("Couldn't reset this user's password. Please try again.");
                })
                .finally(function () {
                    resetConfirmBtn.disabled = false;
                });
        });
    }

    if (copyBtn && temporaryPasswordField) {
        copyBtn.addEventListener("click", function () {
            temporaryPasswordField.select();
            temporaryPasswordField.setSelectionRange(0, 9999);

            var copyPromise = navigator.clipboard && navigator.clipboard.writeText
                ? navigator.clipboard.writeText(temporaryPasswordField.value)
                : Promise.resolve().then(function () { document.execCommand("copy"); });

            copyPromise
                .then(function () {
                    if (copyFeedback) {
                        copyFeedback.classList.remove("d-none");
                    }
                })
                .catch(function () {
                    alert("Couldn't copy automatically. Please copy the password manually.");
                });
        });
    }

    if (resetModalEl) {
        resetModalEl.addEventListener("hidden.bs.modal", resetModalToConfirmStep);
    }

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
