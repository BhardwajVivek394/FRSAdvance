$(document).ready(function () {
    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop']").remove();
        $("#frmGetUserClassList").submit();
        window.history.pushState('', '', "Index");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop']").remove();
        window.history.pushState('', '', "Index");
    }
});

function fnSave() {
    if (Validate()) {
        $("#frmSaveUserClass").submit();
    }
}

function Validate() {
    var result = true;

    if ($.trim($("#ClassName").val()).length == 0) {
        $("#ClassName").next("span").html("Class name is required");
        result = false;
    }
    else {
        $("#ClassName").next("span").html("");
    }

    if ($.trim($("#DelayInMinutes").val()).length == 0) {
        $("#DelayInMinutes").next("span").html("Delay In Minutes is required");
        result = false;
    }
    else {
        $("#DelayInMinutes").next("span").html("");
    }

    return result;
}

function UserClassSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    //$("#modal-add-Customer").modal("toggle");
    $("#modal-add-userClass").hide();
}