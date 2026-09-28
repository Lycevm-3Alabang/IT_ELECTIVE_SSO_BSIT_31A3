// Issue 14: Tenant App Management UI. Requires jQuery, jquery-validate,
// jquery-validate-unobtrusive and the Bootstrap 5 bundle to be loaded first.
(function ($) {
    "use strict";

    // Client side of [HttpUrl].
    $.validator.addMethod("httpurl", function (value, element) {
        return this.optional(element) || /^https?:\/\/[^\s\/$.?#][^\s]*$/i.test(value);
    });
    $.validator.unobtrusive.adapters.addBool("httpurl");

    var modalEl = document.getElementById("appModal");
    var content = document.getElementById("appModalContent");
    var modal = bootstrap.Modal.getOrCreateInstance(modalEl);

    function render(html) {
        content.innerHTML = html;
        var $form = $(content).find("form");
        // Re-parse so unobtrusive validation binds to the injected form.
        $form.removeData("validator").removeData("unobtrusiveValidation");
        $.validator.unobtrusive.parse($form);
    }

    function fail() {
        alert("Something went wrong. Please try again.");
    }

    // --- Create / Edit: load form into modal ---------------------------------
    $(document).on("click", ".js-app-form-trigger", function () {
        fetch(this.getAttribute("data-url"), { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.text(); })
            .then(function (html) { render(html); modal.show(); })
            .catch(fail);
    });

    // --- Create / Edit: submit via AJAX --------------------------------------
    $(content).on("submit", "form", function (e) {
        e.preventDefault();
        var form = this;
        if (!$(form).valid()) { return; }

        var submit = form.querySelector('[type="submit"]');
        submit.disabled = true;

        fetch(form.action, {
            method: "POST",
            body: new FormData(form), // includes the anti-forgery token
            headers: { "X-Requested-With": "XMLHttpRequest" }
        })
            .then(function (r) {
                if (!r.ok) { throw new Error(r.status); }
                var isJson = (r.headers.get("content-type") || "").indexOf("json") !== -1;
                return isJson
                    ? r.json().then(function () { window.location.reload(); }) // success: TempData message shows after reload
                    : r.text().then(render);                                   // server-side validation errors
            })
            .catch(function () { submit.disabled = false; fail(); });
    });

    // --- Delete confirmation modal -------------------------------------------
    $(document).on("click", ".js-delete-trigger", function () {
        var groups = parseInt(this.getAttribute("data-group-count"), 10) || 0;
        $("#deleteAppForm").attr("action", this.getAttribute("data-delete-url"));
        $("#deleteAppName").text(this.getAttribute("data-app-name"));
        $("#deleteAppGroupCount").text(groups);
        $("#deleteAppGroupWarning").toggleClass("d-none", groups === 0);
        $("#deleteAppNoGroups").toggleClass("d-none", groups !== 0);
    });
})(jQuery);
