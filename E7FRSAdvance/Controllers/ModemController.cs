using System;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Mvc;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Client.Subscribing;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Controllers
{
    /// <summary>
    /// ModemController handles communication with modems via TCP and MQTT protocols.
    /// 
    /// Protocol Usage by Modem Version:
    /// ================================
    /// V1 Modem: TCP only (Signal Strength command only: ZL+CSQ?)
    /// V2 Modem: TCP + MQTT (user selects protocol via dropdown)
    /// A10 under V1: No commands supported (Command tab hidden)
    /// A10 under V2: MQTT commands only (JSON format)
    /// 
    /// A10 Commands Supported:
    /// =======================
    /// Time Commands:
    ///   - time_get: {"id":1,"time_get":1} => {"id":1,"ts":1744954496,"response":"time_get_ok"}
    ///   - time_set: {"id":1,"time_set":1744954451} => {"id":1,"ts":1744954451,"response":"time_set_ok"}
    ///   - fw_get: {"id":1,"fw_get":1} => {"id":1,"ts":1744954291,"response":"fw_get_ok","ver":"324"}
    /// 
    /// Event Commands:
    ///   - event_get: {"id":1,"event_get":1} => Event data with values array
    ///   - event_clear: {"id":1,"event_clear":1} => {"id":1,"response":"event_clear_ok"}
    /// 
    /// Channel Commands:
    ///   - channel_get: {"id":1,"channel_get":1} => {"id":1,"response":"channel_get_ok","mode":4,...}
    ///   - channel_get_all: {"id":1,"channel_get_all":1} => {"id":1,"response":"channel_get_ok","channels":[...]}
    ///   - channel_set: {"id":1,"channel_set":1,"mode":4,...} => {"id":1,"response":"channel_set_ok"}
    /// 
    /// Update Command:
    ///   - update: {"id":1,"update":1} => Device sends all updates
    /// 
    /// TCP Connection:
    /// ===============
    /// Host: proxy.energy7.org
    /// Port: Calculated from ClusterName pattern
    ///       Formula: 1400 + ClusterNumber + LcOffset
    /// 
    /// Endpoints:
    /// ==========
    /// POST /Modem/SendTcpCommand  - TCP communication (V1 and V2 modems)
    /// POST /Modem/SendCommand     - MQTT communication (V2 modems and A10 devices)
    /// </summary>
    public class ModemController : Controller
    {
        //private readonly string _connectionString = ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString;

        // TCP Server Host
        private static readonly string TCP_SERVER_HOST = ConfigurationManager.AppSettings["TcpServerHost"] ?? "proxy.energy7.org";

        // Base port for TCP connections
        private const int TCP_BASE_PORT = 1400;

        // Default Lc offset when no /LcXb pattern found
        private const int DEFAULT_LC_OFFSET = 30;

        /// <summary>
        /// Calculate TCP port based on ClusterName pattern.
        /// </summary>
        private int CalculateTcpPort(string clusterName)
        {
            int clusterNumber = 0;
            int lcOffset = DEFAULT_LC_OFFSET;

            if (string.IsNullOrEmpty(clusterName))
            {
                return TCP_BASE_PORT + DEFAULT_LC_OFFSET;
            }

            string lowerName = clusterName.ToLower();

            int underscoreIndex = lowerName.IndexOf("cluster_");
            if (underscoreIndex >= 0)
            {
                int startIndex = underscoreIndex + 8;
                StringBuilder digits = new StringBuilder();

                for (int i = startIndex; i < lowerName.Length; i++)
                {
                    char c = lowerName[i];
                    if (char.IsDigit(c))
                    {
                        digits.Append(c);
                    }
                    else
                    {
                        break;
                    }
                }

                if (digits.Length > 0)
                {
                    clusterNumber = int.Parse(digits.ToString());
                }
            }
            else
            {
                StringBuilder digits = new StringBuilder();
                foreach (char c in clusterName)
                {
                    if (char.IsDigit(c))
                    {
                        digits.Append(c);
                    }
                    else if (digits.Length > 0)
                    {
                        break;
                    }
                }

                if (digits.Length > 0)
                {
                    clusterNumber = int.Parse(digits.ToString());
                }
            }

            int lcIndex = lowerName.IndexOf("/lc");
            if (lcIndex >= 0)
            {
                int digitIndex = lcIndex + 3;

                if (digitIndex < lowerName.Length)
                {
                    char lcDigit = lowerName[digitIndex];
                    if (char.IsDigit(lcDigit))
                    {
                        lcOffset = (lcDigit - '0') * 10;
                    }
                }
            }

            int calculatedPort = TCP_BASE_PORT + clusterNumber + lcOffset;

            return calculatedPort;
        }

        #region TCP Communication (For V1 and V2 Modems)

        /// <summary>
        /// Extract clean JSON response from raw TCP data.
        /// </summary>
        private string ExtractCleanJsonResponse(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse))
            {
                return null;
            }

            int jsonStart = -1;
            int jsonEnd = -1;
            int braceCount = 0;
            bool inJson = false;

            for (int i = 0; i < rawResponse.Length; i++)
            {
                char c = rawResponse[i];

                if (c == '{')
                {
                    if (!inJson)
                    {
                        jsonStart = i;
                        inJson = true;
                    }
                    braceCount++;
                }
                else if (c == '}' && inJson)
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        jsonEnd = i;

                        string potentialJson = rawResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);

                        if (potentialJson.Contains("CSQ") || potentialJson.Contains("ID"))
                        {
                            try
                            {
                                JObject json = JObject.Parse(potentialJson);

                                if (json["CSQ"] != null)
                                {
                                    return json.ToString(Newtonsoft.Json.Formatting.None);
                                }
                            }
                            catch
                            {
                            }
                        }

                        inJson = false;
                        braceCount = 0;
                    }
                }
            }

            int arrayStart = rawResponse.IndexOf("[\"CSQ\"");
            if (arrayStart >= 0)
            {
                int arrayEnd = rawResponse.IndexOf("]", arrayStart);
                if (arrayEnd > arrayStart)
                {
                    string arrayJson = rawResponse.Substring(arrayStart, arrayEnd - arrayStart + 1);

                    try
                    {
                        if (arrayJson.Contains("CSQ"))
                        {
                            int csqIndex = arrayJson.IndexOf("\"CSQ\"");
                            if (csqIndex >= 0)
                            {
                                int valueStart = arrayJson.IndexOf("\"", csqIndex + 5);
                                if (valueStart >= 0)
                                {
                                    int valueEnd = arrayJson.IndexOf("\"", valueStart + 1);
                                    if (valueEnd > valueStart)
                                    {
                                        string csqValue = arrayJson.Substring(valueStart + 1, valueEnd - valueStart - 1);

                                        string idValue = "";
                                        int idIndex = arrayJson.IndexOf("\"ID\"");
                                        if (idIndex < 0)
                                        {
                                            idIndex = arrayJson.IndexOf("ID\":");
                                        }
                                        if (idIndex >= 0)
                                        {
                                            int idValueStart = arrayJson.IndexOf("\"", idIndex + 3);
                                            if (idValueStart >= 0)
                                            {
                                                int idValueEnd = arrayJson.IndexOf("\"", idValueStart + 1);
                                                if (idValueEnd > idValueStart)
                                                {
                                                    idValue = arrayJson.Substring(idValueStart + 1, idValueEnd - idValueStart - 1);
                                                }
                                            }
                                        }

                                        JObject result = new JObject();
                                        result["CSQ"] = csqValue;
                                        if (!string.IsNullOrEmpty(idValue))
                                        {
                                            result["ID"] = idValue;
                                        }
                                        return result.ToString(Newtonsoft.Json.Formatting.None);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            int csqPlainIndex = rawResponse.IndexOf("CSQ");
            if (csqPlainIndex >= 0)
            {
                int colonIndex = rawResponse.IndexOf(":", csqPlainIndex);
                if (colonIndex < 0)
                {
                    colonIndex = rawResponse.IndexOf(",", csqPlainIndex);
                }

                if (colonIndex >= 0 && colonIndex < rawResponse.Length - 1)
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    bool foundDigit = false;

                    for (int i = colonIndex + 1; i < rawResponse.Length && i < colonIndex + 10; i++)
                    {
                        char c = rawResponse[i];
                        if (char.IsDigit(c) || c == ',')
                        {
                            valueBuilder.Append(c);
                            foundDigit = true;
                        }
                        else if (foundDigit && !char.IsDigit(c) && c != ',')
                        {
                            break;
                        }
                    }

                    if (valueBuilder.Length > 0)
                    {
                        JObject result = new JObject();
                        result["CSQ"] = valueBuilder.ToString().Trim(',');
                        return result.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Send command via TCP socket connection.
        /// </summary>
        [HttpPost]
        public JsonResult SendTcpCommand()
        {
            try
            {
                // Read JSON from request body
                Request.InputStream.Position = 0;
                string jsonBody;
                using (var reader = new System.IO.StreamReader(Request.InputStream))
                {
                    jsonBody = reader.ReadToEnd();
                }

                // Deserialize JSON
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var request = serializer.Deserialize<SendTcpCommandRequest>(jsonBody);

                if (request == null)
                {
                    return Json(new { success = false, error = "Invalid request" });
                }
                if (request.TcpInfo == null)
                {
                    return Json(new { success = false, error = "Modem not found: " + request.ModemId });
                }
                if (string.IsNullOrEmpty(request.TcpInfo.ClusterName))
                {
                    return Json(new { success = false, error = "Cluster not found for modem: " + request.ModemId });
                }

                //int tcpPort = CalculateTcpPort(request.TcpInfo.ClusterName);
                string value = request.TcpInfo.TcpSendPort;
                int tcpPort = int.Parse(value.Substring(value.LastIndexOf(':') + 1));
                string rawResponse = SendTcpCommandInternal(
                    TCP_SERVER_HOST,
                    tcpPort,
                    request.Command,
                    TimeSpan.FromSeconds(10)
                );
                string cleanResponse = ExtractCleanJsonResponse(rawResponse);

                return Json(new
                {
                    success = cleanResponse != null,
                    data = cleanResponse,
                    rawData = rawResponse,
                    protocol = "TCP",
                    host = TCP_SERVER_HOST,
                    port = tcpPort,
                    //siteId = tcpInfo.SiteId,
                    siteName = request.TcpInfo.StationName,
                    clusterName = request.TcpInfo.ClusterName,
                    commandType = "modem_at_tcp"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "TCP Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Internal method to send TCP command and receive response.
        /// </summary>
        private string SendTcpCommandInternal(string host, int port, string command, TimeSpan timeout)
        {
            string response = null;
            TcpClient client = null;
            NetworkStream stream = null;

            try
            {
                client = new TcpClient();

                IAsyncResult connectResult = client.BeginConnect(host, port, null, null);
                bool connected = connectResult.AsyncWaitHandle.WaitOne(timeout);

                if (!connected)
                {
                    throw new TimeoutException("TCP connection timeout to " + host + ":" + port.ToString());
                }

                client.EndConnect(connectResult);

                stream = client.GetStream();
                stream.ReadTimeout = (int)timeout.TotalMilliseconds;
                stream.WriteTimeout = (int)timeout.TotalMilliseconds;

                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\r\n");
                stream.Write(commandBytes, 0, commandBytes.Length);
                stream.Flush();

                Thread.Sleep(200);

                byte[] buffer = new byte[4096];
                StringBuilder responseBuilder = new StringBuilder();

                try
                {
                    DateTime startTime = DateTime.Now;
                    while ((DateTime.Now - startTime).TotalMilliseconds < timeout.TotalMilliseconds)
                    {
                        if (stream.DataAvailable)
                        {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                                string currentResponse = responseBuilder.ToString();
                                if (currentResponse.Contains("\n") ||
                                    currentResponse.Contains("OK") ||
                                    currentResponse.Contains("ERROR") ||
                                    currentResponse.Contains("+CSQ") ||
                                    (currentResponse.Contains("[") && currentResponse.Contains("]")) ||
                                    (currentResponse.Contains("{") && currentResponse.Contains("}")))
                                {
                                    Thread.Sleep(100);

                                    if (stream.DataAvailable)
                                    {
                                        int extraBytes = stream.Read(buffer, 0, buffer.Length);
                                        if (extraBytes > 0)
                                        {
                                            responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, extraBytes));
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }
                    }

                    response = responseBuilder.ToString().Trim();

                    if (string.IsNullOrEmpty(response))
                    {
                        response = null;
                    }
                }
                catch (IOException)
                {
                    response = null;
                }
            }
            catch (SocketException ex)
            {
                throw new Exception("TCP Socket Error: " + ex.Message);
            }
            finally
            {
                if (stream != null)
                {
                    try { stream.Close(); } catch { }
                }
                if (client != null)
                {
                    try { client.Close(); } catch { }
                }
            }

            return response;
        }

        #endregion

        #region MQTT Communication (For V2 Modems and A10 Devices)

        /// <summary>
        /// Send command via MQTT protocol.
        /// </summary>
        //[HttpPost]
        //public JsonResult SendCommand(string modemId, string command, string clusterName, string mqttBasePath)
        //{
        //    try
        //    {
        //        if (string.IsNullOrEmpty(mqttBasePath))
        //        {
        //            return Json(new { success = false, error = "MQTT path not found for modem: " + modemId });
        //        }

        //        if (string.IsNullOrEmpty(clusterName))
        //        {
        //            return Json(new { success = false, error = "Cluster not found for modem: " + modemId });
        //        }

        //        CommandInfo cmdInfo = ParseCommand(command);
        //        string response;

        //        if (cmdInfo != null)
        //        {
        //            response = SendMqttCommandWithValidation(
        //                mqttBasePath,
        //                clusterName.Replace(" ", ""),
        //                command,
        //                cmdInfo.Id,
        //                cmdInfo.CommandType
        //            );

        //            string topic = mqttBasePath.TrimEnd('/') + "/" + clusterName;

        //            return Json(new
        //            {
        //                success = response != null,
        //                data = response,
        //                protocol = "MQTT",
        //                cluster = clusterName,
        //                topic = topic,
        //                commandId = cmdInfo.Id,
        //                commandType = cmdInfo.CommandType
        //            });
        //        }
        //        else
        //        {
        //            response = SendMqttCommandSimple(
        //                mqttBasePath,
        //                clusterName.Replace(" ", ""),
        //                command
        //            );

        //            string topic = mqttBasePath.TrimEnd('/') + "/" + clusterName;

        //            return Json(new
        //            {
        //                success = response != null,
        //                data = response,
        //                protocol = "MQTT",
        //                cluster = clusterName,
        //                topic = topic,
        //                commandType = "modem_at"
        //            });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { success = false, error = ex.Message });
        //    }
        //}

        /// <summary>
        /// Parse JSON command for A10 devices.
        /// Supports: time_get, time_set, fw_get, event_get, event_clear, 
        ///           channel_get, channel_get_all, channel_set, update
        /// </summary>
        private CommandInfo ParseCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return null;
            }

            string trimmed = command.TrimStart();
            if (!trimmed.StartsWith("{"))
            {
                return null;
            }

            try
            {
                JObject json = JObject.Parse(command);
                CommandInfo info = new CommandInfo();

                if (json["id"] != null)
                {
                    info.Id = json["id"].Value<int>();
                }
                else
                {
                    return null;
                }

                // Time Commands
                if (json["time_get"] != null)
                {
                    info.CommandType = "time_get";
                    info.ExpectedResponse = "time_get_ok";
                }
                else if (json["time_set"] != null)
                {
                    info.CommandType = "time_set";
                    info.ExpectedResponse = "time_set_ok";
                }
                else if (json["fw_get"] != null)
                {
                    info.CommandType = "fw_get";
                    info.ExpectedResponse = "fw_get_ok";
                }
                // Event Commands
                else if (json["event_get"] != null)
                {
                    info.CommandType = "event_get";
                    info.ExpectedResponse = "event";
                }
                else if (json["event_clear"] != null)
                {
                    info.CommandType = "event_clear";
                    info.ExpectedResponse = "event_clear_ok";
                }
                // Channel Commands
                else if (json["channel_get"] != null)
                {
                    info.CommandType = "channel_get";
                    info.ExpectedResponse = "channel_get_ok";
                }
                else if (json["channel_get_all"] != null)
                {
                    info.CommandType = "channel_get_all";
                    info.ExpectedResponse = "channel_get_ok";
                }
                else if (json["channel_set"] != null)
                {
                    info.CommandType = "channel_set";
                    info.ExpectedResponse = "channel_set_ok";
                }
                // Update Command
                else if (json["update"] != null)
                {
                    info.CommandType = "update";
                    info.ExpectedResponse = "update";
                }
                else
                {
                    info.CommandType = "unknown";
                    info.ExpectedResponse = null;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Send plain text AT command to modem via MQTT - returns first response.
        /// </summary>
        private string SendMqttCommandSimple(string mqttBasePath, string clusterName, string command)
        {
            string response = null;
            ManualResetEventSlim responseReceived = new ManualResetEventSlim(false);

            MqttFactory factory = new MqttFactory();
            IMqttClient client = factory.CreateMqttClient();

            string clientId = "Web_" + Guid.NewGuid().ToString("N");

            IMqttClientOptions options = new MqttClientOptionsBuilder()
                .WithTcpServer("energy7.in", 1883)
                .WithCredentials("energy7.in", "k3806557")
                .WithClientId(clientId)
                .WithCleanSession()
                .WithCommunicationTimeout(TimeSpan.FromSeconds(30))
                .Build();

            client.UseApplicationMessageReceivedHandler(e =>
            {
                if (e.ApplicationMessage.Payload != null && e.ApplicationMessage.Payload.Length > 0)
                {
                    if (response == null)
                    {
                        response = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        responseReceived.Set();
                    }
                }
            });

            try
            {
                client.ConnectAsync(options).GetAwaiter().GetResult();

                string basePath = mqttBasePath.TrimEnd('/');
                string cleanClusterName = clusterName.Replace(" ", "");

                string responseTopic = basePath + "/" + cleanClusterName + "/P";
                string commandTopic = basePath + "/" + cleanClusterName + "/S";

                client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(responseTopic)
                    .Build()).GetAwaiter().GetResult();

                client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(commandTopic)
                    .WithPayload(command)
                    .WithAtLeastOnceQoS()
                    .Build()).GetAwaiter().GetResult();

                responseReceived.Wait(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (client.IsConnected)
                {
                    client.DisconnectAsync().GetAwaiter().GetResult();
                }
                client.Dispose();
                responseReceived.Dispose();
            }

            return response;
        }

        /// <summary>
        /// Send JSON command to A10 device via MQTT - validates response by ID.
        /// </summary>
        //private string SendMqttCommandWithValidation(string mqttBasePath, string clusterName, string command, int expectedId, string commandType)
        //{
        //    string validResponse = null;
        //    ManualResetEventSlim responseReceived = new ManualResetEventSlim(false);

        //    MqttFactory factory = new MqttFactory();
        //    IMqttClient client = factory.CreateMqttClient();

        //    string clientId = "Web_" + Guid.NewGuid().ToString("N");

        //    IMqttClientOptions options = new MqttClientOptionsBuilder()
        //        .WithTcpServer("energy7.in", 1883)
        //        .WithCredentials("energy7.in", "k3806557")
        //        .WithClientId(clientId)
        //        .WithCleanSession()
        //        .WithCommunicationTimeout(TimeSpan.FromSeconds(30))
        //        .Build();

        //    client.UseApplicationMessageReceivedHandler(e =>
        //    {
        //        if (e.ApplicationMessage.Payload != null && e.ApplicationMessage.Payload.Length > 0)
        //        {
        //            string responseStr = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        //            if (IsValidResponse(responseStr, expectedId, commandType))
        //            {
        //                validResponse = responseStr;
        //                responseReceived.Set();
        //            }
        //        }
        //    });

        //    try
        //    {
        //        client.ConnectAsync(options).GetAwaiter().GetResult();

        //        string basePath = mqttBasePath.TrimEnd('/');
        //        string cleanClusterName = clusterName.Replace(" ", "");

        //        string responseTopic = basePath + "/" + cleanClusterName + "/P";
        //        string commandTopic = basePath + "/" + cleanClusterName + "/S";

        //        client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
        //            .WithTopicFilter(responseTopic)
        //            .Build()).GetAwaiter().GetResult();

        //        client.PublishAsync(new MqttApplicationMessageBuilder()
        //            .WithTopic(commandTopic)
        //            .WithPayload(command)
        //            .WithAtLeastOnceQoS()
        //            .Build()).GetAwaiter().GetResult();

        //        responseReceived.Wait(TimeSpan.FromSeconds(10));
        //    }
        //    finally
        //    {
        //        if (client.IsConnected)
        //        {
        //            client.DisconnectAsync().GetAwaiter().GetResult();
        //        }
        //        client.Dispose();
        //        responseReceived.Dispose();
        //    }

        //    return validResponse;
        //}

        /// <summary>
        /// Validate if MQTT response matches expected command ID and type.
        /// Supports all A10 command types including time_get, time_set, fw_get.
        /// </summary>
        //private bool IsValidResponse(string responseJson, int expectedId, string commandType)
        //{
        //    try
        //    {
        //        JObject json = JObject.Parse(responseJson);

        //        // Check if response has ID
        //        if (json["id"] == null)
        //        {
        //            return false;
        //        }

        //        int responseId = json["id"].Value<int>();
        //        if (responseId != expectedId)
        //        {
        //            return false;
        //        }

        //        // Get response field if exists
        //        string responseField = null;
        //        JToken responseToken = json["response"];
        //        if (responseToken != null)
        //        {
        //            responseField = responseToken.Value<string>();
        //        }

        //        switch (commandType)
        //        {
        //            // ============================================
        //            // TIME COMMANDS
        //            // ============================================
        //            case "time_get":
        //                // Response: {"id":1,"ts":1744954496,"response":"time_get_ok"}
        //                if (responseField == "time_get_ok")
        //                {
        //                    return true;
        //                }
        //                // Also accept if ts field exists
        //                return json["ts"] != null;

        //            case "time_set":
        //                // Response: {"id":1,"ts":1744954451,"response":"time_set_ok"}
        //                if (responseField == "time_set_ok")
        //                {
        //                    return true;
        //                }
        //                return false;

        //            case "fw_get":
        //                // Response: {"id":1,"ts":1744954291,"response":"fw_get_ok","ver":"324"}
        //                if (responseField == "fw_get_ok")
        //                {
        //                    return true;
        //                }
        //                // Also accept if ver field exists
        //                return json["ver"] != null;

        //            // ============================================
        //            // EVENT COMMANDS
        //            // ============================================
        //            case "event_get":
        //                // Response: {"id":1,"channel":1,"ts":1744954496,"type":"event","run_ms":1500,"seg":1,"values":[...]}
        //                return json["values"] != null || json["event"] != null || json["ts"] != null || json["channel"] != null;

        //            case "event_clear":
        //                // Response: {"id":1,"response":"event_clear_ok"}
        //                if (responseField == "event_clear_ok")
        //                {
        //                    return true;
        //                }
        //                return json["event_clear_ok"] != null;

        //            // ============================================
        //            // CHANNEL COMMANDS
        //            // ============================================
        //            case "channel_get":
        //                // Response: {"id":1,"ts":1744954563,"response":"channel_get_ok","mode":4,"offset":0,"div":100,"mul":328,"threshold":60}
        //                if (responseField == "channel_get_ok")
        //                {
        //                    return true;
        //                }
        //                return json["mode"] != null;

        //            case "channel_get_all":
        //                // Response: {"id":1,"ts":1744954563,"response":"channel_get_ok","channels":[{...}]}
        //                if (responseField == "channel_get_ok")
        //                {
        //                    return true;
        //                }
        //                return json["channels"] != null;

        //            case "channel_set":
        //                // Response: {"id":1,"ts":1744954563,"response":"channel_set_ok"}
        //                if (responseField == "channel_set_ok")
        //                {
        //                    return true;
        //                }
        //                return false;

        //            // ============================================
        //            // UPDATE COMMAND
        //            // ============================================
        //            case "update":
        //                return json["ts"] != null || json["update_ok"] != null || responseField != null || json["type"] != null;

        //            default:
        //                return true;
        //        }
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Holds MQTT connection information for a modem
    /// </summary>
    public class ModemInfo
    {
        public string ClusterName { get; set; }
        public string MqttBasePath { get; set; }
    }
    public class SendTcpCommandRequest
    {
        public string ModemId { get; set; }
        public string Command { get; set; }
        public ModemTcpInfo TcpInfo { get; set; }
    }
    /// <summary>
    /// Holds TCP connection information for a modem.
    /// </summary>
    public class ModemTcpInfo
    {
        //public int SiteId { get; set; }
        public string ClusterId { get; set; }
        public string ClusterName { get; set; }
        public int ClusterNo { get; set; }
        public string ModemId { get; set; }
        public string ModemName { get; set; }
        public string MqttBase { get; set; }
        public bool MqttStatus { get; set; }
        public string PublishTopic { get; set; }
        public string Signal { get; set; }
        public string SimNumber { get; set; }
        public string StationName { get; set; }
        public string SubscribeTopic { get; set; }
        public string TcpRecvPort { get; set; }
        public string TcpSendPort { get; set; }
        public string TcpStatus { get; set; }
        public string Version { get; set; }
        public string ZoneName { get; set; }
    }

    /// <summary>
    /// Holds parsed command information for A10 JSON commands
    /// </summary>
    public class CommandInfo
    {
        public int Id { get; set; }
        public string CommandType { get; set; }
        public string ExpectedResponse { get; set; }
    }

    #endregion
}
