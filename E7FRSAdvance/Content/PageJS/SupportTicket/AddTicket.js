$(document).ready(function () {
    $("#RequestDate").datepicker({
        "autoclose": true,
        dateFormat: 'mm/dd/yy'
    }).datepicker('setDate', 'today');

    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop fade show']").remove();
        $("#frmGetTicketList").submit();
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
        var mTicket = {};
        mTicket.SupportTicketImages = [];
        mTicket.Id = $("#hdnId").val();
        mTicket.Title = $("#Title").val();
        mTicket.Description = $("#Description").val();
        mTicket.RequestDate = $("#RequestDate").val();

        $('.hdnImageBase64').each(function () {
            var mSupportTicketImages = {};
            mSupportTicketImages.FileBase64 = $(this).val();
            mSupportTicketImages.FileExtension = $(this).data('fileextension');
            mTicket.SupportTicketImages.push(mSupportTicketImages);
        });

        $.ajax({
            type: "POST",
            url: "/SupportTicket/SaveTicket",
            data: '{mTicket: ' + JSON.stringify(mTicket) + '}',
            dataType: "json",
            contentType: "application/json; charset=utf-8",
            success: function (data) {
                if (data != null) {
                    if (data.type == "success") {
                        CommonNotification("SUCCESS", data.result, "success");
                    }
                    else if (data.type == "error") {
                        CommonNotification("ERROR", data.result, "error");
                    }
                    else if (data.type == "error") {
                        CommonNotification("ERROR", data.result, "error");
                    }

                }
                $("#modal-add-ticket").modal("hide");

            },
            error: function () {
                CommonNotification("Error", data.Message, "error")
            }
        }); return false;
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

function TicketSaved() {
    $("#loader").hide();
    $("#modal-add-ticket").hide();
}

function GetById(id) {
    $("#loader").show();
    $.ajax({
        url: '/SupportTicket/GetById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveTicket").empty().append(data);
            $("#modal-add-ticket").modal('show');
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
            url: '/SupportTicket/Delete',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null) {
                    if (data.type == "success") {
                        CommonNotification("SUCCESS", data.result, "success");
                        $("#frmGetTicketList").submit();
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

function fnUploadImageFile($this) {
    var counter = 0;
    $("#loader").show();
    //var fileUpload = $($this).get(0);
    //var files = fileUpload.files;
    //var fileData = new FormData();
    //fileData.append(files[0].name, files[0]);

    var files = $($this).get(0).files;
    var fileData = new FormData();
    for (var i = 0; i < files.length; i++) {
        fileData.append(files[i].name, files[i]);
    }

    $.ajax({
        url: "/SupportTicket/UploadFile",
        type: "POST",
        contentType: false,
        processData: false,
        data: fileData,
        async: true,
        success: function (data) {
            $("#loader").hide();
            var imageBase64 = '';
            //$('#divImage').empty();
            if (data != null && data != undefined && data != '') {
                $.each(data, function (index, value) {
                    counter++;
                    imageBase64 += "<input type='hidden' id='hdnBas64_" + counter + "' class='hdnImageBase64' value='" + value.FileBase64 + "' data-fileextension='" + value.FileExtension + "' />";
                    imageBase64 += "<img height='100' width='100' id='imgBas64_" + counter + "' src='data:image/png;base64," + value.FileBase64 + "'/>";

                });
            }
            $('#divImage').append(imageBase64);
        },
        error: function (xhr, error, status) {
            $("#loader").hide();
            CommonNotification("ERROR", "Internal server error.", "Error");
        }
    });
}