$(document).ready(function () {
    //$("#RequestDate").datepicker({
    //    "autoclose": true,
    //    dateFormat: 'mm/dd/yy'
    //}).datepicker('setDate', 'today');

    //if ($('#hdnSaveType').val() == 'Success') {
    //    CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());

    //    $("#frmTestimonialList").submit();
    //}
    //else if ($('#hdnSaveType').val() == 'Error') {
    //    CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnSaveMessage").val());
    //}

    if ($('#drpAssetType').val() != null && $('#drpAssetType').val() != undefined && $('#drpAssetType').val() != '' && $('#drpAssetType').val() != '0') {
        BindAsset($('#drpAssetType').val());
    }

    if ($('#drpDivision').val() != null && $('#drpDivision').val() != undefined && $('#drpDivision').val() != '' && $('#drpDivision').val() != '0') {
        BindSite($('#drpDivision').val())
    }

    $('#drpDivision').change(function () {
        BindSite($(this).val());
    });

    $('#drpAssetType').change(function () {
        var assetTypeId = $(this).val();
        //Bind Asset
        BindAsset(assetTypeId);     

    });


    GetDivisionByZone($("#zoneAdd1").val());

    $("#zoneAdd1").change(function () {
        GetDivisionByZone($(this).val());

        $("#frmGetSiteList").submit();
    });

});

function GetDivisionByZone(zoneId) {
    if (zoneId != null && zoneId != undefined && zoneId != '') {
        $("#loader").show();
        $.ajax({
            url: '/Testimonial/GetDivisionByZoneId',
            type: 'POST',
            data: '{zoneId:' + zoneId + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divisions").empty();
                $("#divisions").append($("<option></option>").val('0').html('Select Divison'));
                $.each(data, function (key, value) {
                    if ($('#hdnSearchDivisionId').val() != '0' && $('#hdnSearchDivisionId').val() == value.Id) {
                        $("#divisions").append($("<option selected></option>").val(value.Id).html(value.Name));
                    } else {
                        $("#divisions").append($("<option></option>").val(value.Id).html(value.Name));
                    }
                });
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }

}


function fnSave() {
    if (Validate()) {
        var mTestimonial = {};
        mTestimonial.TestimonialImages = [];
        mTestimonial.Id = $("#hdnId").val();
        mTestimonial.DivisionId = $("#drpDivision").val();
        mTestimonial.SiteId = $("#drpSite").val();
        mTestimonial.AssetTypeId = $("#drpAssetType").val();
        mTestimonial.AssetId = $("#drpAssetId").val();
        mTestimonial.Remark = $("#Remark").val();

        $('.hdnImageBase64').each(function () {

            var mTestimonialImages = {};
            mTestimonialImages.FileBase64 = $(this).val();
            mTestimonialImages.FileExtension = $(this).data('fileextension');
            mTestimonial.TestimonialImages.push(mTestimonialImages);
        });

        $.ajax({
            type: "POST",
            url: "/Testimonial/Create",
            data: '{mTestimonial: ' + JSON.stringify(mTestimonial) + '}',
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

                $("#modal-add-Testimonial").modal("hide");
                $("#frmTestimonialList").submit();


            },
            error: function () {
                CommonNotification("Error", data.Message, "error")
            }
        }); return false;
    }
}

function Validate() {
    var result = true;
    if ($.trim($("#drpDivision").val()).length == 0 || $("#drpDivision").val() == "0") {
        $("#drpDivision").next("span").html("Division is required");
        result = false;
    }
    else {
        $("#drpDivision").next("span").html("");
    }

    if ($.trim($("#drpSite").val()).length == 0 || $("#drpSite").val() == "0") {
        $("#drpSite").next("span").html("Site is required");
        result = false;
    }
    else {
        $("#drpSite").next("span").html("");
    }

    if ($.trim($("#drpAssetType").val()).length == 0 || $("#drpAssetType").val() == "0") {
        $("#drpAssetType").next("span").html("Asset Type is required");
        result = false;
    }
    else {
        $("#drpAssetType").next("span").html("");
    }

    if ($.trim($("#drpAssetId").val()).length == 0 || $("#drpAssetId").val() == "0") {
        $("#drpAssetId").next("span").html("Asset is required");
        result = false;
    }
    else {
        $("#drpAssetId").next("span").html("");
    }

    if ($.trim($("#Remark").val()).length == 0) {
        $("#Remark").next("span").html("Remark is required");
        result = false;
    }
    else {
        $("#Remark").next("span").html("");
    }
    return result;
}

function ValidateRemark() {
    var result = true;
  
    if ($.trim($("#testimonialRemark").val()).length == 0) {
        $("#testimonialRemark").next("span").html("Remark is required");
        result = false;
    }
    else {
        $("#testimonialRemark").next("span").html("");
    }
    return result;
}

function ValidateRailwayRemark() {
    var result = true;

    if ($.trim($("#RailwayRemark").val()).length == 0) {
        $("#RailwayRemark").next("span").html("Remark is required");
        result = false;
    }
    else {
        $("#RailwayRemark").next("span").html("");
    }
    return result;
}

function GetById(id) {
    $("#loader").show();
    $.ajax({
        url: '/Testimonial/_Add',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divTestimonialCreate").empty().append(data);
            $("#modal-add-Testimonial").modal('show');
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
            url: '/Testimonial/Delete',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null) {
                    if (data.type == "success") {
                        CommonNotification("SUCCESS", data.result, "success");
                        $("#frmTestimonialList").submit();
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


function BindAsset(assetTypeId) {
    if (assetTypeId != null && assetTypeId != undefined && assetTypeId != '') {
        $("#loader").show();
        $.ajax({
            url: '/YardConfig/GetAllAssest',
            type: 'Post',
            data: '{siteId:' + $("#drpSite").val() + ',assetTypeId:' + assetTypeId + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null && data.length > 0) {
                    var html = "";
                    html += "<option>Select</option>";
                    $.each(data, function (id, val) {
                        if ($('#hdnAssetId').val() != null && $('#hdnAssetId').val() != undefined && $('#hdnAssetId').val() != '0' && $('#hdnAssetId').val() == val.Id) {
                            html += "<option value=" + val.Id + " selected>" + val.Name + "</option>";

                        } else {
                            html += "<option value=" + val.Id + ">" + val.Name + "</option>";

                        }

                    });
                    $("#drpAssetId").empty().append(html);
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
}

function BindSite(divisionId) {
    if (divisionId != null && divisionId != undefined && divisionId != '') {
        $("#loader").show();
        $.ajax({
            url: '/Maintenace/GetSiteByDivisionId',
            type: 'POST',
            data: '{divisionId:' + divisionId + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#drpSite").empty();
                $("#drpSite").append($("<option></option>").val('0').html('Select'));
                $.each(data, function (key, value) {
                    if ($('#hdnSiteId').val() != null && $('#hdnSiteId').val() != undefined && $('#hdnSiteId').val() != '0' && $('#hdnSiteId').val() == value.Id) {
                        $("#drpSite").append($("<option selected></option>").val(value.Id).html(value.Name));

                    } else {
                        $("#drpSite").append($("<option></option>").val(value.Id).html(value.Name));

                    }

                  
                });
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
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
        url: "/Testimonial/UploadFile",
        type: "POST",
        contentType: false,
        processData: false,
        data: fileData,
        async: true,
        success: function (data) {
            $("#loader").hide();
            var imageBase64 = '';
            $('#divImage').empty();
            if (data != null && data != undefined && data != '') {
                $.each(data, function (index, value) {
                    counter++;
                    imageBase64 += "<div class='col-sm-12'>";
                    imageBase64 += "<input type='hidden' id='hdnBas64_" + counter + "' class='hdnImageBase64' value='" + value.FileBase64 + "' data-fileextension='" + value.FileExtension + "' />";
                    imageBase64 += "<img style='height:100%;width:100%' id='imgBas64_" + counter + "' src='data:image/png;base64," + value.FileBase64 + "'/>";
                    imageBase64 += "</div>";

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


function fnShowRemark(id) {
    $("#loader").show();
    $.ajax({
        url: '/Testimonial/GetShowRemark',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divTestimonialRemark").empty().append(data);
            $("#modal-add-testimonialRemark").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnUploadRemarkImageFile($this) {
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
        url: "/Testimonial/UploadFile",
        type: "POST",
        contentType: false,
        processData: false,
        data: fileData,
        async: true,
        success: function (data) {
            $("#loader").hide();
            var imageBase64 = '';
            $('#divRemarkImage').empty();
            if (data != null && data != undefined && data != '') {
                $.each(data, function (index, value) {
                    counter++;
                    imageBase64 += "<div class='col-sm-12'>";
                    imageBase64 += "<input type='hidden' id='hdnRemarkBas64_" + counter + "' class='hdnRemarkImageBase64' value='" + value.FileBase64 + "' data-fileextension='" + value.FileExtension + "' />";
                    imageBase64 += "<img style='height:100%;width:100%' id='imgBas64_" + counter + "' src='data:image/png;base64," + value.FileBase64 + "'/>";
                    imageBase64 += "</div>";

                });
            }

            $('#divRemarkImage').append(imageBase64);
        },
        error: function (xhr, error, status) {
            $("#loader").hide();
            CommonNotification("ERROR", "Internal server error.", "Error");
        }
    });
}

function fnSaveTestimonialRemark() {
    if (ValidateRemark()) {
        var mTestimonial = {};
        mTestimonial.TestimonialImages = [];
        mTestimonial.Id = $("#hdnRemarkId").val();
        mTestimonial.Remark = $("#testimonialRemark").val();

        $('.hdnRemarkImageBase64').each(function () {

            var mTestimonialImages = {};
            mTestimonialImages.FileBase64 = $(this).val();
            mTestimonialImages.FileExtension = $(this).data('fileextension');
            mTestimonialImages.IsRailway = false;
            mTestimonial.TestimonialImages.push(mTestimonialImages);
        });
        $("#loader").show();
        $.ajax({
            type: "POST",
            url: "/Testimonial/Update",
            data: '{mTestimonial: ' + JSON.stringify(mTestimonial) + '}',
            dataType: "json",
            contentType: "application/json; charset=utf-8",
            success: function (data) {
                $("#loader").hide();
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

                $("#modal-add-testimonialRemark").modal("hide");
                $("#frmTestimonialList").submit();


            },
            error: function () {
                $("#loader").hide();
                CommonNotification("Error", data.Message, "error")
            }
        }); return false;
    }
}




function fnShowRailwayRemark(id) {
    $("#loader").show();
    $.ajax({
        url: '/Testimonial/GetShowRailwayRemark',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divTestimonialRailwayRemark").empty().append(data);
            $("#modal-add-testimonialRailwayRemark").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnUploadRailwayRemarkImageFile($this) {
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
        url: "/Testimonial/UploadFile",
        type: "POST",
        contentType: false,
        processData: false,
        data: fileData,
        async: true,
        success: function (data) {
            $("#loader").hide();
            var imageBase64 = '';
            $('#divRailwayRemarkImage').empty();
            if (data != null && data != undefined && data != '') {
                $.each(data, function (index, value) {
                    counter++;
                    imageBase64 += "<div class='col-sm-12'>";
                    imageBase64 += "<input type='hidden' id='hdnRailwayRemarkBas64_" + counter + "' class='hdnRailwayRemarkImageBase64' value='" + value.FileBase64 + "' data-fileextension='" + value.FileExtension + "' />";
                    imageBase64 += "<img style='height:100%;width:100%' id='imgBas64_" + counter + "' src='data:image/png;base64," + value.FileBase64 + "'/>";
                    imageBase64 += "</div>";

                });
            }

            $('#divRailwayRemarkImage').append(imageBase64);
        },
        error: function (xhr, error, status) {
            $("#loader").hide();
            CommonNotification("ERROR", "Internal server error.", "Error");
        }
    });
}

function fnSaveTestimonialRailwayRemark() {
    if (ValidateRailwayRemark()) {
        var mTestimonial = {};
        mTestimonial.TestimonialImages = [];
        mTestimonial.Id = $("#hdnRailwayRemarkId").val();
        mTestimonial.RailwayRemark = $("#RailwayRemark").val();

        $('.hdnRailwayRemarkImageBase64').each(function () {

            var mTestimonialImages = {};
            mTestimonialImages.FileBase64 = $(this).val();
            mTestimonialImages.FileExtension = $(this).data('fileextension');
            mTestimonialImages.IsRailway = true;
            mTestimonial.TestimonialImages.push(mTestimonialImages);
        });
        $("#loader").show();
        $.ajax({
            type: "POST",
            url: "/Testimonial/Update",
            data: '{mTestimonial: ' + JSON.stringify(mTestimonial) + '}',
            dataType: "json",
            contentType: "application/json; charset=utf-8",
            success: function (data) {
                $("#loader").hide();
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

                $("#modal-add-testimonialRailwayRemark").modal("hide");
                $("#frmTestimonialList").submit();


            },
            error: function () {
                $("#loader").hide();
                CommonNotification("Error", data.Message, "error")
            }
        }); return false;
    }
}



function fnViewRemark(id, isRailway) {
    $("#loader").show();
    $.ajax({
        url: '/Testimonial/GetViewRemark',
        type: 'Post',
        data: '{id:' + id + ',isRailway:' + isRailway +'}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divTestimonialViewRemark").empty().append(data);
            $("#modal-add-testimonialViewRemark").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}