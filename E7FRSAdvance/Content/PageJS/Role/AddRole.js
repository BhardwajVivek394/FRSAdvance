$(document).ready(function () {
 
    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        $("#frmGetRoleList").submit();
        window.history.pushState('', '', "Role");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        window.history.pushState('', '', "Role");
    }

});

function fnSave() {
    if (Validate()) {
        $("#frmSaveRole").submit();
    }
}

function Validate() {

    var result = true;

    if ($.trim($("#Title").val()).length == 0) {
        $("#Title").next("span").html("Title is required");
        result = false;
    }
    else {
        $("#Title").next("span").html("");
    }

    if ($.trim($("#Description").val()).length == 0) {
        $("#Description").next("span").html("Description is required");
        result = false;
    }
    else {
        $("#Description").next("span").html("");
    }


    return result;
}

function RoleSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    $("#modal-add-role").hide();
}