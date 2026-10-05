// User details page: removing a user from a group.
// Browsers can't send DELETE from a plain <form>, so the Remove button calls
// DELETE /Admin/Users/{userId}/Groups/{groupId} with fetch, then reloads so the
// list and the "assign" dropdown both reflect the change. Assigning is a normal
// form POST and needs no script.
(function () {
    "use strict";

    function getAntiForgeryToken() {
        var input = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    document.querySelectorAll(".js-unassign-group").forEach(function (button) {
        button.addEventListener("click", function () {
            var name = button.getAttribute("data-group-name") || "this group";
            if (!window.confirm("Remove this user from \"" + name + "\"?")) {
                return;
            }

            button.disabled = true;

            fetch(button.getAttribute("data-unassign-url"), {
                method: "DELETE",
                headers: {
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getAntiForgeryToken()
                }
            })
                .then(function (response) {
                    // 404 means it was already removed (e.g. in another tab): reload shows the truth.
                    if (!response.ok && response.status !== 404) {
                        throw new Error("Unassign failed with status " + response.status);
                    }
                    window.location.reload();
                })
                .catch(function () {
                    button.disabled = false;
                    alert("Couldn't remove the user from this group. Please try again.");
                });
        });
    });
})();
