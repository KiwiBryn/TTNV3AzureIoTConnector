//---------------------------------------------------------------------------------
// Copyright (c) February 2021, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Base64 encoded payloads
//	AQIDBA== 0x01, 0x02, 0x03, 0x04
// BAMCAQ== 0x04, 0x03, 0x02, 0x01
//
// JSON Payloads
// {"value_0": 0,"value_1": 1,"value_2": 2}
// {"value_9": 9,"value_8": 1,"value_7": 7}
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
   using System;
   using System.Collections.Concurrent;
   using System.Collections.Generic;
   using System.Globalization;
   using System.Net.Http;
   using System.Security.Cryptography;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Azure.Devices.Client;
   using Microsoft.Azure.Devices.Client.Exceptions;
   using Microsoft.Azure.Devices.Provisioning.Client;
   using Microsoft.Azure.Devices.Provisioning.Client.PlugAndPlay;
   using Microsoft.Azure.Devices.Provisioning.Client.Transport;
   using Microsoft.Azure.Devices.Shared;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   using MQTTnet;
   using MQTTnet.Client;
   using MQTTnet.Client.Options;
   using MQTTnet.Client.Receiving;
   using MQTTnet.Extensions.ManagedClient;

   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;

   using devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models;
   using devMobile.TheThingsNetwork.API;

   public class Worker : BackgroundService
   {
      private static ILogger<Worker> _logger;
      private static ProgramSettings _programSettings;
      private static readonly ConcurrentDictionary<string, DeviceClient> _DeviceClients = new ConcurrentDictionary<string, DeviceClient>();
      private static readonly ConcurrentDictionary<string, IManagedMqttClient> _MqttClients = new ConcurrentDictionary<string, IManagedMqttClient>();

      public Worker(ILogger<Worker> logger, IOptions<ProgramSettings> programSettings)
      {
         _logger = logger;
         _programSettings = programSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
         _logger.LogInformation("devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector starting");

         if ((_programSettings.Applications == null) || (_programSettings.Applications.Count == 0))
         {
            _logger.LogError("TTI Applications configuration not found in appsettings file");
            return;
         }

         try
         {
            MqttFactory mqttFactory = new MqttFactory();

            foreach (KeyValuePair<string, ApplicationSetting> applicationSetting in _programSettings.Applications)
            {
               _logger.LogInformation("Config-ApplicationID:{0}", applicationSetting.Key);

               var mqttClient = mqttFactory.CreateManagedMqttClient();

               var mqttClientoptions = new ManagedMqttClientOptionsBuilder()
                              .WithAutoReconnectDelay(_programSettings.TheThingsIndustries.MqttAutoReconnectDelay)
                              .WithClientOptions(new MqttClientOptionsBuilder()
                              .WithTcpServer(_programSettings.TheThingsIndustries.MqttServerName)
                              .WithCredentials(_programSettings.ApplicationIdResolve(applicationSetting.Key), _programSettings.MqttAccessKeyResolve(applicationSetting.Key))
                              .WithClientId(_programSettings.TheThingsIndustries.MqttClientId)
                              .WithTls()
                              .Build())
                              .Build();

               await mqttClient.StartAsync(mqttClientoptions);

               if (!_MqttClients.TryAdd(applicationSetting.Key, mqttClient))
               {
                  // Need to decide whether device cache add failure aborts startup
                  _logger.LogError("Config-ApplicationID:{0} cache add failed", applicationSetting.Key);
                  continue;
               }

               // Add subscriptions before just incase Azure messages queued ready to go...
               mqttClient.UseApplicationMessageReceivedHandler(new MqttApplicationMessageReceivedHandlerDelegate(e => MqttClientApplicationMessageReceived(e)));

               // These may shift to individual device subscriptions
               string uplinkTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/up";
               await mqttClient.SubscribeAsync(uplinkTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               string queuedTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/down/queued";
               await mqttClient.SubscribeAsync(queuedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               // TODO : Sent topic currently not processed, see https://github.com/TheThingsNetwork/lorawan-stack/issues/76
               //string sentTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/down/sent";
               //await mqttClient.SubscribeAsync(sentTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               string ackTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/down/ack";
               await mqttClient.SubscribeAsync(ackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               string nackTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/down/nack";
               await mqttClient.SubscribeAsync(nackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               string failedTopic = $"v3/{_programSettings.ApplicationIdResolve(applicationSetting.Key)}/devices/+/down/failed";
               await mqttClient.SubscribeAsync(failedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

               using (HttpClient httpClient = new HttpClient())
               {
                  // Get ready to enumerate through the Application's devices
                  EndDeviceRegistryClient endDeviceRegistryClient = new EndDeviceRegistryClient(_programSettings.TheThingsIndustries.ApiBaseUrl, httpClient)
                  {
                     ApiKey = _programSettings.TheThingsIndustries.ApiKey
                  };

                  try
                  {
                     int devicePage = 1;
                     V3EndDevices endDevices = await endDeviceRegistryClient.ListAsync(
                        applicationSetting.Key,
                        field_mask_paths: Constants.DevicefieldMaskPaths,
                        page: devicePage,
                        limit: _programSettings.TheThingsIndustries.DevicePageSize,
                        cancellationToken: stoppingToken);

                     while ((endDevices != null) && (endDevices.End_devices != null)) // If no devices returns null rather than empty list
                     {
                        List<Task<bool>> tasks = new List<Task<bool>>();

                        _logger.LogInformation("Config-ApplicationID:{0} start", applicationSetting.Key);

                        foreach (V3EndDevice device in endDevices.End_devices)
                        {
                           if (DeviceAzureEnabled(device))
                           {
                              _logger.LogInformation("Config-ApplicationID:{0} DeviceID:{1} Device EUI:{2}", device.Ids.Application_ids.Application_id, device.Ids.Device_id, device.Ids.Dev_eui);

                              tasks.Add(DeviceRegistration(device.Ids.Application_ids.Application_id,
                                                         device.Ids.Device_id,
                                                         _programSettings.ResolveDeviceModelId(device.Ids.Application_ids.Application_id, device.Attributes),
                                                         stoppingToken));
                           }
                        }

                        _logger.LogInformation("Config-ApplicationID:{0} Page:{1} processing start", applicationSetting.Key, devicePage);

                        Task.WaitAll(tasks.ToArray(), stoppingToken);

                        _logger.LogInformation("Config-ApplicationID:{0} Page:{1} processing finish", applicationSetting.Key, devicePage);

                        endDevices = await endDeviceRegistryClient.ListAsync(
                           applicationSetting.Key,
                           field_mask_paths: Constants.DevicefieldMaskPaths,
                           page: devicePage += 1,
                           limit: _programSettings.TheThingsIndustries.DevicePageSize,
                           cancellationToken: stoppingToken);
                     }
                     _logger.LogInformation("Config-ApplicationID:{0} finish", applicationSetting.Key);
                  }
                  catch (ApiException ex)
                  {
                     _logger.LogError("Config-Application configuration API error:{0}", ex.StatusCode);
                  }
               }
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Config-Application configuration error");

            return;
         }

         try
         {
            await Task.Delay(Timeout.Infinite, stoppingToken);
         }
         catch (TaskCanceledException)
         {
            _logger.LogInformation("devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector stopping");
         }

         foreach (var deviceClient in _DeviceClients)
         {
            try
            {
               _logger.LogInformation("Close-DeviceID:{0}", deviceClient.Key);

               await deviceClient.Value.CloseAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
               _logger.LogError(ex, "devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector Device Client {0} shutdown error", deviceClient.Key);
            }
         }

         foreach (var mqttClient in _MqttClients)
         {
            try
            {
               _logger.LogInformation("Close-ApplicationID:{0}", mqttClient.Key);

               await mqttClient.Value.StopAsync();
            }
            catch (Exception ex)
            {
               _logger.LogError(ex, "devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector MQTT Client {0} shutdown error", mqttClient.Key);
            }
         }
      }

      private async static Task<MethodResponse> AzureIoTHubClientDefaultMethodHandler(MethodRequest methodRequest, object userContext)
      {
         if (methodRequest.DataAsJson != null)
         {
            _logger.LogWarning("AzureIoTHubClientDefaultMethodHandler name:{0} payload:{1)", methodRequest.Name, methodRequest.DataAsJson);
         }
         else
         {
            _logger.LogWarning("AzureIoTHubClientDefaultMethodHandler name:{0} payload:NULL", methodRequest.Name);
         }

         return new MethodResponse(404);
      }

      private static async Task<bool> DeviceRegistration(string applicationId, string deviceId, string modelId, CancellationToken stoppingToken)
      {
         DeviceClient deviceClient = null;
         ITransportSettings[] transportSettings = new ITransportSettings[]
         {
            new AmqpTransportSettings(TransportType.Amqp_Tcp_Only)
            {
               AmqpConnectionPoolSettings = new AmqpConnectionPoolSettings()
               {
                  Pooling = true,
               }
             }
          };

         try
         {
            // See if AzureIoT hub connections string has been configured
            if (_programSettings.ConnectionStringResolve(applicationId, out string connectionString))
            {
               if (!string.IsNullOrEmpty(modelId))
               {
                  ClientOptions clientoptions = new ClientOptions()
                  {
                     ModelId = modelId
                  };
                  deviceClient = DeviceClient.CreateFromConnectionString(connectionString, deviceId, transportSettings, clientoptions);
               }
               else
               {
                  deviceClient = DeviceClient.CreateFromConnectionString(connectionString, deviceId, transportSettings);
               }
            }

            // See if DPS has been configured
            if (_programSettings.DeviceProvisioningServiceSettingsResolve(applicationId, out AzureDeviceProvisiongServiceSettings deviceProvisiongServiceSettings))
            {
               string deviceKey;

               using (var hmac = new HMACSHA256(Convert.FromBase64String(deviceProvisiongServiceSettings.GroupEnrollmentKey)))
               {
                  deviceKey = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(deviceId)));
               }

               using (var securityProvider = new SecurityProviderSymmetricKey(deviceId, deviceKey, null))
               {
                  using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
                  {
                     ProvisioningDeviceClient provClient = ProvisioningDeviceClient.Create(
                        Constants.AzureDpsGlobalDeviceEndpoint,
                        deviceProvisiongServiceSettings.IdScope,
                        securityProvider,
                        transport);

                     DeviceRegistrationResult result;

                     if (!string.IsNullOrEmpty(modelId))
                     {
                        ProvisioningRegistrationAdditionalData provisioningRegistrationAdditionalData = new ProvisioningRegistrationAdditionalData()
                        {
                           JsonData = PnpConvention.CreateDpsPayload(modelId)
                        };

                        result = await provClient.RegisterAsync(provisioningRegistrationAdditionalData, stoppingToken);
                     }
                     else
                     {
                        result = await provClient.RegisterAsync(stoppingToken);
                     }

                     if (result.Status != ProvisioningRegistrationStatusType.Assigned)
                     {
                        _logger.LogError("Config-DeviceID:{0} Status:{1} RegisterAsync failed ", deviceId, result.Status);

                        return false;
                     }

                     IAuthenticationMethod authentication = new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, (securityProvider as SecurityProviderSymmetricKey).GetPrimaryKey());

                     deviceClient = DeviceClient.Create(result.AssignedHub, authentication, transportSettings);
                  }
               }
            }

            if (deviceClient == null)
            {
               _logger.LogError("Config-DeviceID:{0} DeviceClient.Create failed ", deviceId);

               return false;
            }

            await deviceClient.OpenAsync(stoppingToken);

            if (!_DeviceClients.TryAdd(deviceId, deviceClient))
            {
               // Need to decide whether device cache add failure aborts startup
               _logger.LogError("Config-Device:{0} cache add failed", deviceId);

               return false;
            }

            AzureIoTHubReceiveMessageHandlerContext context = new AzureIoTHubReceiveMessageHandlerContext()
            {
               TenantId = _programSettings.TheThingsIndustries.Tenant,
               DeviceId = deviceId,
               ApplicationId = applicationId,
               MethodSettings = _programSettings.Applications[applicationId].MethodSettings,
            };

            await deviceClient.SetReceiveMessageHandlerAsync(AzureIoTHubClientReceiveMessageHandler, context, stoppingToken);

            await deviceClient.SetMethodDefaultHandlerAsync(AzureIoTHubClientDefaultMethodHandler, context, stoppingToken);
         }
         catch (DeviceNotFoundException)
         {
            _logger.LogWarning("Config-Azure Device:{0} device not found connection failed", deviceId);

            return false;
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Config-Azure Device:{0} connection failed", deviceId);

            return false;
         }

         return true;
      }

      private async static Task AzureIoTHubClientReceiveMessageHandler(Message message, object userContext)
      {
         try
         {
            AzureIoTHubReceiveMessageHandlerContext receiveMessageHandlerConext = (AzureIoTHubReceiveMessageHandlerContext)userContext;

            if (!_DeviceClients.TryGetValue(receiveMessageHandlerConext.DeviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Downlink-DeviceID:{0} unknown", receiveMessageHandlerConext.DeviceId);
               return;
            }

            if (!_MqttClients.TryGetValue(receiveMessageHandlerConext.ApplicationId, out IManagedMqttClient mqttClient))
            {
               _logger.LogWarning("Downlink-ApplicationID:{0} unknown", receiveMessageHandlerConext.ApplicationId);
               return;
            }

            using (message)
            {
               DownlinkQueue queue;
               Downlink downlink;

               string payloadText = Encoding.UTF8.GetString(message.GetBytes()).Trim();

               if (message.Properties.ContainsKey("method-name"))
               {
                  // Looks like Azure IoT Central message
                  string methodName = message.Properties["method-name"];
                  if (string.IsNullOrWhiteSpace(methodName))
                  {
                     _logger.LogWarning("Downlink-DeviceID:{0} MessagedID:{1} method-name property empty", receiveMessageHandlerConext.DeviceId, message.MessageId);

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  // Look up the method settings to get confirmed, port, priority, and queue
                  if (!receiveMessageHandlerConext.MethodSettings.TryGetValue(methodName, out MethodSetting methodSetting))
                  {
                     _logger.LogWarning("Downlink-DeviceID:{0} MessagedID:{1} method-name:{2} has no settings", receiveMessageHandlerConext.DeviceId, message.MessageId, methodName);

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  downlink = new Downlink()
                  {
                     Confirmed = methodSetting.Confirmed,
                     Priority = methodSetting.Priority,
                     Port = methodSetting.Port,
                     CorrelationIds = AzureLockToken.Add(message.LockToken),
                  };

                  queue = methodSetting.Queue;

                  // Check to see if special case for Azure IoT central command with no request payload
                  if (payloadText.CompareTo("@") != 0)
                  {
                     try
                     {
                        // Split over multiple lines to improve readability
                        if (!(payloadText.StartsWith("{") && payloadText.EndsWith("}"))
                                             &&
                           (!(payloadText.StartsWith("[") && payloadText.EndsWith("]"))))
                        {
                           throw new JsonReaderException();
                        }

                        downlink.PayloadDecoded = JToken.Parse(payloadText);
                     }
                     catch (JsonReaderException)
                     {
                        try
                        {
                           JToken value = JToken.Parse(payloadText);

                           downlink.PayloadDecoded = new JObject(new JProperty(methodName, value));
                        }
                        catch (JsonReaderException)
                        {
                           downlink.PayloadDecoded = new JObject(new JProperty(methodName, payloadText));
                        }
                     }
                  }
                  else
                  {
                     downlink.PayloadRaw = "";
                  }

                  _logger.LogInformation("Downlink-IoT Central DeviceID:{0} MessageID:{2} LockToken:{3} Port:{4} Confirmed:{5} Priority:{6} Queue:{7}",
                        receiveMessageHandlerConext.DeviceId,
                        message.MessageId,
                        message.LockToken,
                        downlink.Port,
                        downlink.Confirmed,
                        downlink.Priority,
                        queue);
               }
               else
               {
                  // Looks like it's Azure IoT hub message, Put the one mandatory message property first, just because
                  if (!AzureDownlinkMessage.PortTryGet(message.Properties, out byte port))
                  {
                     _logger.LogWarning("Downlink-Port property is invalid");

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  if (!AzureDownlinkMessage.ConfirmedTryGet(message.Properties, out bool confirmed))
                  {
                     _logger.LogWarning("Downlink-Confirmed flag is invalid");

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  if (!AzureDownlinkMessage.PriorityTryGet(message.Properties, out DownlinkPriority priority))
                  {
                     _logger.LogWarning("Downlink-Priority value is invalid");

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  if (!AzureDownlinkMessage.QueueTryGet(message.Properties, out queue))
                  {
                     _logger.LogWarning("Downlink-Queue value is invalid");

                     await deviceClient.RejectAsync(message);
                     return;
                  }

                  downlink = new Downlink()
                  {
                     Confirmed = confirmed,
                     Priority = priority,
                     Port = port,
                     CorrelationIds = AzureLockToken.Add(message.LockToken),
                  };

                  // Split over multiple lines in an attempt to improve readability. In this scenario a valid JSON string should start/end with {/} for an object or [/] for an array
                  if ((payloadText.StartsWith("{") && payloadText.EndsWith("}"))
                                                ||
                     ((payloadText.StartsWith("[") && payloadText.EndsWith("]"))))
                  {
                     try
                     {
                        downlink.PayloadDecoded = JToken.Parse(payloadText);
                     }
                     catch (JsonReaderException)
                     {
                        downlink.PayloadRaw = payloadText;
                     }
                  }
                  else
                  {
                     downlink.PayloadRaw = payloadText;
                  }

                  _logger.LogInformation("Downlink-IoT Hub DeviceID:{0} MessageID:{2} LockToken:{3} Port:{4} Confirmed:{5} Priority:{6} Queue:{7}",
                     receiveMessageHandlerConext.DeviceId,
                     message.MessageId,
                     message.LockToken,
                     downlink.Port,
                     downlink.Confirmed,
                     downlink.Priority,
                     queue);
               }

               DownlinkPayload Payload = new DownlinkPayload()
               {
                  Downlinks = new List<Downlink>()
                  {
                     downlink
                  }
               };

               string downlinktopic = $"v3/{receiveMessageHandlerConext.ApplicationId}@{receiveMessageHandlerConext.TenantId}/devices/{receiveMessageHandlerConext.DeviceId}/down/{JsonConvert.SerializeObject(queue).Trim('"')}";

               var mqttMessage = new MqttApplicationMessageBuilder()
                                    .WithTopic(downlinktopic)
                                    .WithPayload(JsonConvert.SerializeObject(Payload))
                                    .WithAtLeastOnceQoS()
                                    .Build();

               await mqttClient.PublishAsync(mqttMessage);
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Downlink-ReceiveMessge processing failed");
         }
      }

      private async void MqttClientApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs e)
      {
         string topic = e.ApplicationMessage.Topic.Substring(e.ApplicationMessage.Topic.LastIndexOf("/") + 1);

         switch (topic)
         {
            case "up":
               await UplinkMessageReceived(e);
               break;
            case "queued":
               await DownlinkMessageQueued(e);
               break;
            case "ack":
               await DownlinkMessageAck(e);
               break;
            case "nack":
               await DownlinkMessageNack(e);
               break;
            case "failed":
               await DownlinkMessageFailed(e);
               break;
            default:
               _logger.LogWarning("MessageReceived unknown Topic:{0} Payload:{1}", e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
               break;
         }
      }

      private async Task UplinkMessageReceived(MqttApplicationMessageReceivedEventArgs e)
      {
         try
         {
            PayloadUplink payload = JsonConvert.DeserializeObject<PayloadUplink>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               _logger.LogWarning("Uplink-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            if (!payload.UplinkMessage.Port.HasValue)
            {
               _logger.LogInformation("Uplink-Control message");
               return;
            }

            string applicationId = payload.EndDeviceIds.ApplicationIds.ApplicationId;
            string deviceId = payload.EndDeviceIds.DeviceId;
            int port = payload.UplinkMessage.Port.Value;

            _logger.LogInformation("Uplink-DeviceID:{0} Port:{1} Payload Raw:{2}", deviceId, port, payload.UplinkMessage.PayloadRaw);

            if (!_DeviceClients.TryGetValue(deviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Uplink-Unkown DeviceID:{0}", deviceId);
               return;
            }

            JObject telemetryEvent = new JObject
            {
               { "ApplicationID", applicationId },
               { "DeviceID", deviceId },
               { "Port", port },
               { "Simulated", payload.Simulated },
               { "ReceivedAtUtc", payload.UplinkMessage.ReceivedAtUtc.ToString("s", CultureInfo.InvariantCulture) },
               { "PayloadRaw", payload.UplinkMessage.PayloadRaw }
            };

            // If the payload has been unpacked in TTN backend add fields to telemetry event payload
            if (payload.UplinkMessage.PayloadDecoded != null)
            {
               EnumerateChildren(telemetryEvent, payload.UplinkMessage.PayloadDecoded);
            }

            // Send the message to Azure IoT Hub/Azure IoT Central
            using (Message ioTHubmessage = new Message(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(telemetryEvent))))
            {
               // Ensure the displayed time is the acquired time rather than the uploaded time. 
               ioTHubmessage.Properties.Add("iothub-creation-time-utc", payload.UplinkMessage.ReceivedAtUtc.ToString("s", CultureInfo.InvariantCulture));
               ioTHubmessage.Properties.Add("ApplicationId", applicationId);
               ioTHubmessage.Properties.Add("DeviceId", deviceId);
               ioTHubmessage.Properties.Add("port", port.ToString());
               ioTHubmessage.Properties.Add("Simulated", payload.Simulated.ToString());

               await deviceClient.SendEventAsync(ioTHubmessage);
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Uplink-Processing failed");
         }
      }

      private static async Task DownlinkMessageQueued(MqttApplicationMessageReceivedEventArgs e)
      {
         try
         {
            DownlinkQueuedPayload payload = JsonConvert.DeserializeObject<DownlinkQueuedPayload>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               _logger.LogError("Queued-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            if (!_DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Queued-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
               return;
            }

            if (!AzureLockToken.TryGet(payload.CorrelationIds, out string lockToken))
            {
               _logger.LogWarning("Queued-DeviceID:{0} LockToken missing from payload:{1}", payload.EndDeviceIds.DeviceId, e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            // The confirmation is done in the Ack/Nack/Failed message handler
            if (payload.DownlinkQueued.Confirmed)
            {
               _logger.LogInformation("Queued-DeviceID:{0} confirmed LockToken:{1} ", payload.EndDeviceIds.DeviceId, lockToken);
               return;
            }

            try
            {
               await deviceClient.CompleteAsync(lockToken);
            }
            catch (DeviceMessageLockLostException)
            {
               _logger.LogWarning("Queued-CompleteAsync DeviceID:{0} LockToken:{1} timeout", payload.EndDeviceIds.DeviceId, lockToken);
               return;
            }

            _logger.LogInformation("Queued-DeviceID:{0} LockToken:{1} success", payload.EndDeviceIds.DeviceId, lockToken);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Queued-Processing error");
         }
      }

      private static async Task DownlinkMessageAck(MqttApplicationMessageReceivedEventArgs e)
      {
         try
         {
            DownlinkAckPayload payload = JsonConvert.DeserializeObject<DownlinkAckPayload>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               _logger.LogError("Ack-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            if (!_DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Ack-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
               return;
            }

            if (!AzureLockToken.TryGet(payload.CorrelationIds, out string lockToken))
            {
               _logger.LogWarning("Ack-DeviceID:{0} LockToken missing from payload:{1}", payload.EndDeviceIds.DeviceId, e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            try
            {
               await deviceClient.CompleteAsync(lockToken);
            }
            catch (DeviceMessageLockLostException)
            {
               _logger.LogWarning("Ack-CompleteAsync Device:{0} LockToken:{1} timeout", payload.EndDeviceIds.DeviceId, lockToken);
               return;
            }

            _logger.LogInformation("Ack-Device:{0} LockToken:{1} success", payload.EndDeviceIds.DeviceId, lockToken);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Ack-Processing error");
         }
      }

      private static async Task DownlinkMessageNack(MqttApplicationMessageReceivedEventArgs e)
      {
         try
         {
            DownlinkNackPayload payload = JsonConvert.DeserializeObject<DownlinkNackPayload>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               _logger.LogError("Nack-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            if (!_DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Nack-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
               return;
            }

            if (!AzureLockToken.TryGet(payload.CorrelationIds, out string lockToken))
            {
               _logger.LogWarning("Nack-DeviceID:{0} LockToken missing from payload:{1}", payload.EndDeviceIds.DeviceId, e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            try
            {
               await deviceClient.AbandonAsync(lockToken);
            }
            catch (DeviceMessageLockLostException)
            {
               _logger.LogWarning("Nack-AbandonAsync DeviceID:{0} LockToken:{1} timeout", payload.EndDeviceIds.DeviceId, lockToken);

               return;
            }

            _logger.LogInformation("Nack-DeviceID:{0} LockToken:{1} success", payload.EndDeviceIds.DeviceId, lockToken);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Nack-Processing error");
         }
      }

      private static async Task DownlinkMessageFailed(MqttApplicationMessageReceivedEventArgs e)
      {
         try
         {
            DownlinkFailedPayload payload = JsonConvert.DeserializeObject<DownlinkFailedPayload>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               _logger.LogError("Failed-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            if (!_DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
            {
               _logger.LogWarning("Failed-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
               return;
            }

            if (!AzureLockToken.TryGet(payload.CorrelationIds, out string lockToken))
            {
               _logger.LogWarning("Failed-DeviceID:{0} LockToken missing from payload:{1}", payload.EndDeviceIds.DeviceId, e.ApplicationMessage.ConvertPayloadToString());
               return;
            }

            try
            {
               await deviceClient.RejectAsync(lockToken);
            }
            catch (DeviceMessageLockLostException)
            {
               _logger.LogWarning("Failed-RejectAsync DeviceID:{0} LockToken:{1} timeout", payload.EndDeviceIds.DeviceId, lockToken);
               return;
            }

            _logger.LogInformation("Failed-Device{0} LockToken:{1} success", payload.EndDeviceIds.DeviceId, lockToken);
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Failed-Processing error");
         }
      }

      private static bool DeviceAzureEnabled(V3EndDevice device)
      {
         bool integrated = _programSettings.TheThingsIndustries.DeviceIntegrationDefault;

         if (device.Attributes != null)
         {
            // Using application device integration property
            if (device.Attributes.ContainsKey(Constants.DeviceAzureIntegrationProperty))
            {
               if (bool.TryParse(device.Attributes[Constants.DeviceAzureIntegrationProperty], out integrated))
               {
                  return integrated;
               }

               _logger.LogWarning("Device:{0} Azure Integration property:{1} value:{2} invalid", device.Ids.Device_id, Constants.DeviceAzureIntegrationProperty, device.Attributes[Constants.DeviceAzureIntegrationProperty]);
            }
         }

         if (_programSettings.Applications[device.Ids.Application_ids.Application_id].DeviceIntegrationDefault.HasValue)
         {
            // Using application default from appsettings.json
            return _programSettings.Applications[device.Ids.Application_ids.Application_id].DeviceIntegrationDefault.Value;
         }

         return integrated;
      }

      private void EnumerateChildren(JObject jobject, JToken token)
      {
         if (token is JProperty property)
         {
            if (token.First is JValue)
            {
               // Temporary dirty hack for Azure IoT Central compatibility
               if (token.Parent is JObject possibleGpsProperty)
               {
                  if (possibleGpsProperty.Path.StartsWith("GPS_", StringComparison.OrdinalIgnoreCase))
                  {
                     if (string.Compare(property.Name, "Latitude", true) == 0)
                     {
                        jobject.Add("lat", property.Value);
                     }
                     if (string.Compare(property.Name, "Longitude", true) == 0)
                     {
                        jobject.Add("lon", property.Value);
                     }
                     if (string.Compare(property.Name, "Altitude", true) == 0)
                     {
                        jobject.Add("alt", property.Value);
                     }
                  }
               }
               jobject.Add(property.Name, property.Value);
            }
            else
            {
               JObject parentObject = new JObject();
               foreach (JToken token2 in token.Children())
               {
                  EnumerateChildren(parentObject, token2);
                  jobject.Add(property.Name, parentObject);
               }
            }
         }
         else
         {
            foreach (JToken token2 in token.Children())
            {
               EnumerateChildren(jobject, token2);
            }
         }
      }
   }
}
