$(document).ready(function () {

    IsUserNameExisting($("#EmailAddress").val());

  //  $("#drpBranch").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-user'), width: '100%' });
  //  $("#roleAdd").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-user'), width: '100%' });
  //  $("#levelAdd").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-user'), width: '100%' });
    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        $("#frmGetUserList").submit();
        window.history.pushState('', '', "Index");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val());

        $("[class*='modal-backdrop']").remove();
        window.history.pushState('', '', "Index");
    }

    $("#EmailAddress").blur(function () {
        IsUserNameExisting($(this).val());
    });

    $("#EmailAddress").keyup(function () {
        IsUserNameExisting($(this).val());
    });

    $("#EmailAddress").focus(function () {
        IsUserNameExisting($(this).val());
    });

});

function fnSave() {
    if (Validate()) {
        $("#frmSaveUser").submit();
    }
}

function Validate() {
    var result = true;
    var email = /^[A-Z0-9._%+-]+@([A-Z0-9-]+\.)+[A-Z]{2,4}$/i;
    if ($.trim($("#FirstName").val()).length == 0) {
        $("#FirstName").next("span").html("First Name is required");
        result = false;
    }
    else {
        $("#FirstName").next("span").html("");
    }
    if ($.trim($("#LastName").val()).length == 0) {
        $("#LastName").next("span").html("Last Name is required");
        result = false;
    }
    else {
        $("#LastName").next("span").html("");
    }

    if ($.trim($("#PhoneNumber").val()).length == 0) {
        $("#PhoneNumber").next("span").html("Phone number is required");
        result = false;
    }
    else {
        $("#PhoneNumber").next("span").html("");
    }

    if ($.trim($("#EmailAddress").val()).length == 0) {
        $("#EmailAddress").next("span").html("Email address is required");
        result = false;
    }
    else {
        var emailValue = $("#EmailAddress").val()
            .trim()
            .replace(/[\u200E\u200F\u202A-\u202E]/g, "");

        var email = email.test(emailValue)
        if (!email) {
            $("#EmailAddress").next("span").html("Please enter valid email address");
            result = false;
        }
        else if ($.trim($("#EmailAddress").val()).length > 50) {
            $("#EmailAddress").next("span").html("Only 50 characters allow");
            result = false;
        }
        else {
            $("#EmailAddress").next("span").html("");
        }
    }


    if ($.trim($("#Password").val()).length == 0) {
        $("#Password").next("span").html("Password is required");
        result = false;
    }
    else {
        $("#Password").next("span").html("");
    }

    if ($.trim($("#UserClassId").val()).length == 0) {
        $("#UserClassId").next("span").html("UserClass is required");
        result = false;
    }
    else {
        $("#UserClassId").next("span").html("");
    }

    if ($("#CalibrationPercentage").val() != null && $("#CalibrationPercentage").val() != undefined && $("#CalibrationPercentage").val() != '') {
        if (parseFloat($("#CalibrationPercentage").val()) <= 0 || parseFloat($("#CalibrationPercentage").val()) > 100) {

            $("#CalibrationPercentage").next("span").html("Invalid Percentage!");
            result = false;
        }

    }
  
    return result;
}

function UserSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    //$("#modal-add-Customer").modal("toggle");
    $("#modal-add-user").hide();
}


function IsUserNameExisting(userName) {
    $("#loader").show();
    if (userName !== "") {
        $.ajax({
            url: '/User/IsUserNameExisting',
            type: 'Post',
            data: '{UserName:"' + userName + '",userId:' + $("#hdnUserId").val() + '}',
            contentType: 'application/json',
            success: function (data) {
                if (data === "true") {
                    $("#EmailAddress").next("span").html("Email address already exist");
                    $("#btnSave").attr("disabled", true);
                }
                else if (data == "false") {
                    $("#EmailAddress").next("span").html(" ");
                    $("#btnSave").attr("disabled", false);
                }
                $("#loader").hide();
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
    $("#loader").hide();
}