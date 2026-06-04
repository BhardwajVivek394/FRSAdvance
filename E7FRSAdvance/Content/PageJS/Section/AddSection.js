$(document).ready(function () {
    //$("#zoneAdd").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-division'), width: '100%' });

    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        $("#frmGetSectionList").submit();
        window.history.pushState('', '', "Index");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        window.history.pushState('', '', "Index");
    }
});

function fnSave() {
    if (Validate()) {
        debugger
        $("#frmSaveSection").submit();
    }
}

function Validate() {
    var result = true;

    if ($.trim($("#Name").val()).length == 0) {
        $("#Name").next("span").html("Name is required");
        result = false;
    }
    else {
        $("#Name").next("span").html("");
    }

    return result;
}

function SectionSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    //$("#modal-add-Customer").modal("toggle");
    $("#modal-add-section").hide();
}