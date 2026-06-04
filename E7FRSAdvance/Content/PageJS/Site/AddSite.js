$(document).ready(function () {
  //  $("#zoneAdd").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-site'), width: '100%' });
    //$("#drpDivision").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-site'), width: '100%' });

   // $('#MQTTNoOfChannels').mask('00');
   // $('#MQTTActiveChannel').mask('00');

    if ($('#hdnSaveType').val() == 'Success') {

        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val());

        $("[class*='modal-backdrop']").remove();
        $("#frmGetSiteList").submit();
        window.history.pushState('', '', "Index");
    }
    else if ($('#hdnSaveType').val() == 'Error') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val());
        $("[class*='modal-backdrop']").remove();
        window.history.pushState('', '', "Index");
    }

    $("#zoneAdd").change(function () {
        var zoneId = $(this).children("option:selected").val();

        if (zoneId > 0) {
            $("#loader").show();
            $.ajax({
                type: 'POST',
                url: '/Site/GetDivisionByZoneId',
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                data: '{zoneId:' + zoneId + '}',
                contentType: 'application/json',
                success: function (data) {
                    $("#loader").hide();
                    var temp = '';
                    if (data != null) {
                        temp += '<option value="0">Select Division</option>';
                        $.each(data, function (key, value) {
                            temp += '<option value="' + value.Id + '">' + value.Name + '</option>';
                        });
                    }
                    $("#drpDivision").empty().append(temp);
                    if ($("#hdnDivisionId").val() != null && $("#hdnDivisionId").val() != "0") {
                        $("#drpDivision").val($("#hdnDivisionId").val()).trigger('change');
                    } else {
                        $("#drpDivision").val(0).trigger('change');
                    }
                    $("#loader").hide();
                },
                error: function (response) {
                    $("#loader").hide();
                }
            });
        }
    });

});


function fnSave() {
    if (Validate()) {
        $("#frmSaveSite").submit();
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

    if ($.trim($("#MQTTBasePath").val()).length == 0) {
        $("#MQTTBasePath").next("span").html("MQTT Base path is required");
        result = false;
    }
    else {
        $("#MQTTBasePath").next("span").html("");
    }

    if ($.trim($("#MQTTNoOfChannels").val()).length == 0) {
        $("#MQTTNoOfChannels").next("span").html("MQTT number of channels is required");
        result = false;
    } else if ($("#MQTTNoOfChannels").val() <= 0 ) {
        $("#MQTTNoOfChannels").next("span").html("Please enter channels more than 0.");
        result = false;
    }
    else {
        $("#MQTTNoOfChannels").next("span").html("");
    }

    if ($.trim($("#MQTTActiveChannel").val()).length == 0) {
        $("#MQTTActiveChannel").next("span").html("MQTT active channels is required");
        result = false;
    } else if ($("#MQTTActiveChannel").val() <= 0 ) {
        $("#MQTTActiveChannel").next("span").html("Please enter active channels more than 0.");
        result = false;
    }
    else {
        $("#MQTTActiveChannel").next("span").html("");
    }

    if ($("#drpDivision").val() == 0 || $("#drpDivision").val() == undefined) {
        $("#devisionId").html("Division is required");
        result = false;
    }
    else {
        $("#devisionId").html("");
    }

    if ($.trim($("#MACId").val()).length == 0) {
        $("#MACId").next("span").html("Mac Id is required");
        result = false;
    }
    else {
        $("#MACId").next("span").html("");
    }

    return result;
}

function SiteSaved() {
    //CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnSaveMessage").val(), $("#hdnSaveType").val())
    $("#loader").hide();
    //$("#modal-add-Customer").modal("toggle");
    $("#modal-add-site").hide();
}