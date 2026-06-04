$(document).ready(function () {


    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetAIRemarkList").submit();
    });

    $("#btnAddAIRemark").click(function () {
        GetById(0);
    });

    $("#txtSearchName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetAIRemarkList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetAIRemarkList").submit();
            }
        }
    });

    if ($('#hdnCatListType').val() == 'Success') {
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnCatListMessage").val());
    }
    else if ($('#hdnCatListType').val() == 'Error') {
        if ($('#hdnCatListMessage').val() == 'Forbidden!') {
            window.location.href = "/Login/Index";
        } else {
            CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnCatListMessage").val());
        }
    }
});

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetAIRemarkList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetAIRemarkList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetAIRemarkList").submit();
}

function GetById(id) {
    $("#loader").show();
    $.ajax({
        url: '/AIRemark/GetById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAIRemark").empty().append(data);
            $("#modal-add-AIRemark").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/AIRemark/Delete',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null) {
                    if (data.type == "success") {
                        CommonNotification("SUCCESS", data.result, "success");
                        $("#frmGetAIRemarkList").submit();
                    }
                    else if (data.type == "error") {
                        CommonNotification("ERROR", data.result, "error");
                    }
                    else if (data.type == "error") {
                        CommonNotification("ERROR", data.result, "error");
                    }
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Internal server error.", "error");
            }
        });
    }
}