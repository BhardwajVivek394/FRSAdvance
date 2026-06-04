$(document).ready(function () {
    if ($('#hdnSaveType').val() == 'Success') {
        
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop fade show']").remove();
        $("#frmGetLearningList").submit();
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
        $("#frmSaveLearning").submit();
    }
}

function Validate() {
    var result = true;
    if ($.trim($("#Subject").val()).length == 0) {
        $("#Subject").next("span").html("Subject is required");
        result = false;
    }
    else {
        $("#Subject").next("span").html("");
    }

    if ($.trim($("#VideoURL").val()).length == 0) {
        $("#VideoURL").next("span").html("Video URL is required");
        result = false;
    }
    else {
        $("#VideoURL").next("span").html("");
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

function LearningSaved() {
    $("#loader").hide();
    $("#modal-add-learning").hide();
}