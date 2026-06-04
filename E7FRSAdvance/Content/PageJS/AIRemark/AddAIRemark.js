$(document).ready(function () {
    if ($('#hdnSaveType').val() == 'Success') {
        
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop fade show']").remove();
        $("#frmGetAIRemarkList").submit();
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
        $("#frmSaveAIRemark").submit();
    }
}

function Validate() {
    var result = true;
    
    if ($.trim($("#Remark").val()).length == 0) {
        $("#Remark").next("span").html("Remark is required");
        result = false;
    }
    else {
        $("#Remark").next("span").html("");
    }

    if ($.trim($("#AssetTypeId").val()).length == 0) {
        $("#AssetTypeId").next("span").html("Asset Type is required");
        result = false;
    }
    else {
        $("#AssetTypeId").next("span").html("");
    }

    return result;
}
