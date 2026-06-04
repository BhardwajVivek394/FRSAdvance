$(document).ready(function () {
    if ($('#hdnSaveType').val() == 'Success') {
        
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop fade show']").remove();
        $("#frmGetZoneList").submit();
        window.history.pushState('', '', "Index");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop fade show']").remove();
        window.history.pushState('', '', "Index");
    }
});

function fnSave() {
    if (Validate()) {
        $("#frmSaveZone").submit();
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

function ZoneSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    //$("#modal-add-Customer").modal("toggle");
    $("#modal-add-zone").hide();
}