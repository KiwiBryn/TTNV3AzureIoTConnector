//---------------------------------------------------------------------------------
// Copyright (c) December 2020, devMobile Software
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
//	AQIDBA== Base64 encoded payload: [0x01, 0x02, 0x03, 0x04]
//
//#define DIAGNOSTICS
//#define DIAGNOSTICS_AZURE_IOT_HUB
//#define DIAGNOSTICS_TTN_MQTT
//#define DEVICE_FIELDS_MINIMUM
//#define DEVICE_ATTRIBUTES_DISPLAY
//#define DOWNLINK_MESSAGE_PROPERTIES_DISPLAY
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsNetwork.TTNAPIApplicationToAzureIoTDeviceClient
{
   using System;
   using System.Collections.Generic;
   using System.Diagnostics;
   using System.Globalization;
   using System.Linq;
   using System.Net.Http;
   using System.Runtime.Caching;
   using System.Text;
   using System.Threading.Tasks;

   using Microsoft.Azure.Devices.Client;

   using CommandLine;
   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;

   using MQTTnet;
   using MQTTnet.Client;
   using MQTTnet.Client.Disconnecting;
   using MQTTnet.Client.Options;
   using MQTTnet.Client.Publishing;
   using MQTTnet.Client.Receiving;

   using devMobile.TheThingsNetwork.API;
   using devMobile.TheThingsNetwork.Models;

   public static class Program
   {
      private static IMqttClient mqttClient = null;
      private static IMqttClientOptions mqttOptions = null;
      private static readonly ObjectCache DeviceClients = MemoryCache.Default;

      public static async Task Main(string[] args)
      {
         Console.WriteLine("TheThingsNetwork.TTNAPIApplicationToAzureIoTDeviceClient starting");
         Console.WriteLine();

         await Parser.Default.ParseArguments<CommandLineOptions>(args)
            .WithNotParsed(HandleParseError)
            .WithParsedAsync(ApplicationCore);
      }

      private static void HandleParseError(IEnumerable<Error> errors)
      {
         if (errors.IsVersion())
         {
            Console.WriteLine("Version Request");
            return;
         }

         if (errors.IsHelp())
         {
            Console.WriteLine("Help Request");
            return;
         }
         Console.WriteLine("Parser Fail");
      }

      private static async Task ApplicationCore(CommandLineOptions options)
      {
         CacheItemPolicy cacheItemPolicy = new CacheItemPolicy();
         MqttFactory factory = new MqttFactory();
         mqttClient = factory.CreateMqttClient();

#if DIAGNOSTICS
			Console.WriteLine($"Tennant: {options.Tenant}");
			Console.WriteLine($"baseURL: {options.ApiBaseUrl}");
			Console.WriteLine($"APIKey: {options.ApiKey}");
			Console.WriteLine($"ApplicationID: {options.ApiApplicationID}");
			Console.WriteLine($"AazureIoTHubconnectionString: {options.AzureIoTHubconnectionString}");
			Console.WriteLine();
#endif

         try
         {
            // First configure MQTT, open connection and wire up disconnection handler. 
            mqttOptions = new MqttClientOptionsBuilder()
               .WithTcpServer(options.MqttServerName)
               .WithCredentials($"{options.ApiApplicationID}@{options.Tenant}", options.MqttAccessKey)
               .WithClientId(options.MqttClientID)
               .WithTls()
               .Build();

            mqttClient.UseDisconnectedHandler(new MqttClientDisconnectedHandlerDelegate(e => MqttClientDisconnected(e)));

            await mqttClient.ConnectAsync(mqttOptions);

            // Prepare the HTTP client to be used in the TTN device enumeration
            using (HttpClient httpClient = new HttpClient())
            {
               EndDeviceRegistryClient endDeviceRegistryClient = new EndDeviceRegistryClient(options.ApiBaseUrl, httpClient)
               {
                  ApiKey = options.ApiKey
               };

               // Retrieve list of devices page by page
               V3EndDevices endDevices = await endDeviceRegistryClient.ListAsync(
                  options.ApiApplicationID,
                  field_mask_paths: Constants.DevicefieldMaskPaths,
                  limit: options.DevicePageSize);
               if ((endDevices != null) && (endDevices.End_devices != null)) // If no devices returns null rather than empty list
               {
                  foreach (V3EndDevice endDevice in endDevices.End_devices)
                  {
                     // Display the device info+attributes then connect device to Azure IoT Hub
#if DEVICE_FIELDS_MINIMUM
                     Console.WriteLine($"EndDevice ID: {endDevice.Ids.Device_id}");
#else
							Console.WriteLine($"Device ID: {endDevice.Ids.Device_id} Name: {endDevice.Name} Description: {endDevice.Description}");
							Console.WriteLine($"  CreatedAt: {endDevice.Created_at:dd-MM-yy HH:mm:ss} UpdatedAt: {endDevice.Updated_at:dd-MM-yy HH:mm:ss}");
#endif

#if DEVICE_ATTRIBUTES_DISPLAY
							if (endDevice.Attributes != null)
							{
								Console.WriteLine("  EndDevice attributes");

								foreach (KeyValuePair<string, string> attribute in endDevice.Attributes)
								{
									Console.WriteLine($"    Key: {attribute.Key} Value: {attribute.Value}");
								}
							}
#endif
                     try
                     {
                        DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(
                           options.AzureIoTHubconnectionString,
                           endDevice.Ids.Device_id,
                           TransportType.Amqp_Tcp_Only);

                        await deviceClient.OpenAsync();

                        DeviceClients.Add(endDevice.Ids.Device_id, deviceClient, cacheItemPolicy);

                        AzureIoTHubReceiveMessageHandlerContext context = new AzureIoTHubReceiveMessageHandlerContext()
                        {
                           TenantId = options.Tenant,
                           DeviceId = endDevice.Ids.Device_id,
                           ApplicationId = options.ApiApplicationID,
                        };

                        await deviceClient.SetReceiveMessageHandlerAsync(AzureIoTHubClientReceiveMessageHandler, context);

                        await deviceClient.SetMethodDefaultHandlerAsync(AzureIoTHubClientDefaultMethodHandler, context);
                     }
                     catch (Exception ex)
                     {
                        Console.WriteLine();
                        Console.WriteLine($"Azure IoT Hub OpenAsync failed {ex.Message}");
                     }
                  }
               }
            }

            // At this point all the AzureIoT Hub deviceClients setup and ready to go so can enable MQTT receive
            mqttClient.UseApplicationMessageReceivedHandler(new MqttApplicationMessageReceivedHandlerDelegate(e => MqttClientApplicationMessageReceived(e)));

            // These may shift to individual device subscriptions
            string uplinkTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/up";
            await mqttClient.SubscribeAsync(uplinkTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

            string queuedTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/down/queued";
            await mqttClient.SubscribeAsync(queuedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

            // TODO : Sent topic currently not processed, see https://github.com/TheThingsNetwork/lorawan-stack/issues/76
            //string sentTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/down/sent";
            //await mqttClient.SubscribeAsync(sentTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

            string ackTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/down/ack";
            await mqttClient.SubscribeAsync(ackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

            string nackTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/down/nack";
            await mqttClient.SubscribeAsync(nackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

            string failedTopic = $"v3/{options.ApiApplicationID}@{options.Tenant}/devices/+/down/failed";
            await mqttClient.SubscribeAsync(failedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine($"Main {ex.Message}");
            Console.WriteLine("Press any key to exit");
            Console.ReadLine();
            return;
         }

         while (!Console.KeyAvailable)
         {
            Console.Write(".");
            await Task.Delay(1000);
         }

         // Mop up Azure IoT connections and MQTT COnnection for applications
         foreach (KeyValuePair<string, object> device in DeviceClients)
         {
            DeviceClient deviceClient = (DeviceClient)device.Value;

            await deviceClient.CloseAsync();

            deviceClient.Dispose();
         }

         await mqttClient.DisconnectAsync();

         Console.WriteLine("Press <enter> key to exit");
         Console.ReadLine();
      }

      private static async void MqttClientApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs e)
      {
         if (e.ApplicationMessage.Topic.EndsWith("/up", StringComparison.InvariantCultureIgnoreCase))
         {
            await UplinkMessageReceived(e);
            return;
         }

         // Something other than an uplink message
         if (e.ApplicationMessage.Topic.EndsWith("/queued", StringComparison.InvariantCultureIgnoreCase))
         {
            await DownlinkMessageQueued(e);
            return;
         }

         if (e.ApplicationMessage.Topic.EndsWith("/ack", StringComparison.InvariantCultureIgnoreCase))
         {
            await DownlinkMessageAck(e);
            return;
         }

         if (e.ApplicationMessage.Topic.EndsWith("/nack", StringComparison.InvariantCultureIgnoreCase))
         {
            await DownlinkMessageNack(e);
            return;
         }

         if (e.ApplicationMessage.Topic.EndsWith("/failed", StringComparison.InvariantCultureIgnoreCase))
         {
            await DownlinkMessageFailed(e);
            return;
         }

         Console.WriteLine($"Unknown {e.ApplicationMessage.Topic}");
         Console.WriteLine($" payload: {e.ApplicationMessage.ConvertPayloadToString()}");
      }

      static async Task UplinkMessageReceived(MqttApplicationMessageReceivedEventArgs e)
      {
         Console.WriteLine();
         Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Uplink: {e.ApplicationMessage.Topic}");
         Console.WriteLine($" Payload: {e.ApplicationMessage.ConvertPayloadToString()}");

         try
         {
            PayloadUplink payload = JsonConvert.DeserializeObject<PayloadUplink>(e.ApplicationMessage.ConvertPayloadToString());
            if (payload == null)
            {
               Console.WriteLine($" Uplink: Payload invalid");
               return;
            }

            if (!payload.UplinkMessage.Port.HasValue)
            {
               Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} TTN Control message");
               return;
            }

#if DIAGNOSTICS_TTN_MQTT
				if (payload.UplinkMessage.RXMetadata != null)
				{
					foreach (RxMetadata rxMetaData in payload.UplinkMessage.RXMetadata)
					{
						Console.WriteLine($"  GatewayId", rxMetaData.GatewayIds.GatewayId);
						Console.WriteLine($"  ReceivedAtUTC", rxMetaData.ReceivedAtUtc);
						Console.WriteLine($"  RSSI", rxMetaData.Rssi);
						Console.WriteLine($"  Snr", rxMetaData.Snr);
					}
				}
#endif
            string applicationId = payload.EndDeviceIds.ApplicationIds.ApplicationId;
            string deviceId = payload.EndDeviceIds.DeviceId;
            int port = payload.UplinkMessage.Port.Value;

            Console.WriteLine($" ApplicationID: {applicationId}");
            Console.WriteLine($" DeviceID: {deviceId}");
            Console.WriteLine($" Port: {port}");

            DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(deviceId);
            if (deviceClient == null)
            {
               Console.WriteLine($" UplinkMessageReceived unknown DeviceID: {deviceId}");
               return;
            }

            JObject telemetryEvent = new JObject();

            telemetryEvent.Add("ApplicationID", applicationId);
            telemetryEvent.Add("DeviceID", deviceId);
            telemetryEvent.Add("Port", port);
            telemetryEvent.Add("PayloadRaw", payload.UplinkMessage.PayloadRaw);

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

               await deviceClient.SendEventAsync(ioTHubmessage);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine("Uplink failed: {0}", ex.Message);
         }
      }

      static async Task DownlinkMessageQueued(MqttApplicationMessageReceivedEventArgs e)
      {
         Console.WriteLine();
         Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Queued: {e.ApplicationMessage.Topic}");
         Console.WriteLine($" payload: {e.ApplicationMessage.ConvertPayloadToString()}");

         DownlinkQueuedPayload payload = JsonConvert.DeserializeObject<DownlinkQueuedPayload>(e.ApplicationMessage.ConvertPayloadToString());
         if (payload == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Queued: Payload invalid");
            return;
         }

         // The confirmation is done in the Ack/Nack/Failed message handler
         if (payload.DownlinkQueued.Confirmed)
         {
            Console.WriteLine();
            Console.WriteLine($" Queued: Confirmed delivery");
            return;
         }

         Console.WriteLine($" Queued: Unconfirmed delivery");

         if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
         {
            Console.WriteLine();
            Console.WriteLine($" Queued: Azure IoT Hub message correlationID {Constants.AzureCorrelationPrefix} not found");
            return;
         }

         DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(payload.EndDeviceIds.DeviceId);
         if (deviceClient == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Queued: Unknown DeviceID: {payload.EndDeviceIds.DeviceId}");
            return;
         }

         try
         {
            await deviceClient.CompleteAsync(lockToken);
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine( $" Queued CompleteAsync failed: {ex.Message}");
         }
      }

      static async Task DownlinkMessageAck(MqttApplicationMessageReceivedEventArgs e)
      {
         Console.WriteLine();
         Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Ack: {e.ApplicationMessage.Topic}");
         Console.WriteLine($" payload: {e.ApplicationMessage.ConvertPayloadToString()}");

         DownlinkAckPayload payload = JsonConvert.DeserializeObject<DownlinkAckPayload>(e.ApplicationMessage.ConvertPayloadToString());
         if (payload == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Ack: Payload invalid");
            return;
         }

         if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
         {
            Console.WriteLine();
            Console.WriteLine($" Ack: Azure IoT Hub message correlationID {Constants.AzureCorrelationPrefix} not found");
            return;
         }

         DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(payload.EndDeviceIds.DeviceId);
         if (deviceClient == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Ack: Unknown DeviceID: {payload.EndDeviceIds.DeviceId}");
            return;
         }

         try
         {
            await deviceClient.CompleteAsync(lockToken);
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine($" Ack CompleteAsync failed: {ex.Message}");
         }
      }

      static async Task DownlinkMessageNack(MqttApplicationMessageReceivedEventArgs e)
      {
         Console.WriteLine();
         Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Nack: {e.ApplicationMessage.Topic}");
         Console.WriteLine($" payload: {e.ApplicationMessage.ConvertPayloadToString()}");

         DownlinkNackPayload payload = JsonConvert.DeserializeObject<DownlinkNackPayload>(e.ApplicationMessage.ConvertPayloadToString());
         if (payload == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Nack: Payload invalid");
            return;
         }

         if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
         {
            Console.WriteLine();
            Console.WriteLine($" Nack: Azure IoT Hub message correlationID {Constants.AzureCorrelationPrefix} not found");
            return;
         }

         DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(payload.EndDeviceIds.DeviceId);
         if (deviceClient == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Nak : Unknown DeviceID: {payload.EndDeviceIds.DeviceId}");
            return;
         }

         try
         {
            await deviceClient.AbandonAsync(lockToken);
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine($" Nack AbandonAsync failed: {ex.Message}");
         }
      }

      static async Task DownlinkMessageFailed(MqttApplicationMessageReceivedEventArgs e)
      {
         Console.WriteLine();
         Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Failed: {e.ApplicationMessage.Topic}");
         Console.WriteLine($" payload: {e.ApplicationMessage.ConvertPayloadToString()}");

         DownlinkFailedPayload payload = JsonConvert.DeserializeObject<DownlinkFailedPayload>(e.ApplicationMessage.ConvertPayloadToString());
         if (payload == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Failed: Payload invalid");
            return;
         }

         if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
         {
            Console.WriteLine();
            Console.WriteLine($" Failed: Azure IoT Hub message correlationID {Constants.AzureCorrelationPrefix} not found");
            return;
         }

         DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(payload.EndDeviceIds.DeviceId);
         if (deviceClient == null)
         {
            Console.WriteLine();
            Console.WriteLine($" Failed: Unknown DeviceID: {payload.EndDeviceIds.DeviceId}");
            return;
         }

         try
         {
            await deviceClient.RejectAsync(lockToken);
         }
         catch (Exception ex)
         {
            Console.WriteLine();
            Console.WriteLine($" Failed Reject: {ex.Message}");
         }
      }

      private static void EnumerateChildren(JObject jobject, JToken token)
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

      private async static Task<MethodResponse> AzureIoTHubClientDefaultMethodHandler(MethodRequest methodRequest, object userContext)
      {
         Console.WriteLine($"AzureIoTHubClientDefaultMethodHandler name: {methodRequest.Name}");
         return new MethodResponse(200);
      }

      private async static Task AzureIoTHubClientReceiveMessageHandler(Message message, object userContext)
      {
         bool confirmed;
         byte port;
         DownlinkPriority priority;
         DownlinkQueue queue;

         Console.WriteLine();

         try
         {
            AzureIoTHubReceiveMessageHandlerContext receiveMessageHandlerConext = (AzureIoTHubReceiveMessageHandlerContext)userContext;

            DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(receiveMessageHandlerConext.DeviceId);
            if (deviceClient == null)
            {
               Console.WriteLine($" UplinkMessageReceived unknown DeviceID: {receiveMessageHandlerConext.DeviceId}");
               return;
            }

            using (message)
            {
#if DIAGNOSTICS_AZURE_IOT_HUB
					Console.WriteLine($" MessageID: {message.MessageId}");
					Console.WriteLine($" DeliveryCount: {message.DeliveryCount}");
					Console.WriteLine($" EnqueuedTimeUtc: {message.EnqueuedTimeUtc}");
					Console.WriteLine($" SequenceNumber: {message.SequenceNumber}");
					Console.WriteLine($" To: {message.To}");
               Console.WriteLine($" UserId: {message.UserId}");
               Console.WriteLine($" ConnectionDeviceId: {message.CorrelationId}");
               Console.WriteLine($" ConnectionModuleId: {message.ConnectionModuleId}");
               Console.WriteLine($" ConnectionDeviceId: {message.ConnectionDeviceId}");
               Console.WriteLine($" ComponentName: {message.ComponentName}");
#endif
               string messageBody = Encoding.UTF8.GetString(message.GetBytes());
               Console.WriteLine($" Body: {messageBody}");
#if DOWNLINK_MESSAGE_PROPERTIES_DISPLAY
					foreach (var property in message.Properties)
					{
						Console.WriteLine($"   Key:{property.Key} Value:{property.Value}");
					}
#endif
               // Put the one mandatory message property first, just because
               if (!AzureMessagePortTryGet(message.Properties, out port))
               {
                  Console.WriteLine();

                  Console.WriteLine($" UplinkMessageReceived Port property is missing, invalid, and value must be between {Constants.PortNumberMinimum} and {Constants.PortNumberMaximum}");

                  await deviceClient.RejectAsync(message);
                  return;
               }

               if (!AzureMessageConfirmedTryGet(message.Properties, out confirmed))
               {
                  Console.WriteLine(" UplinkMessageReceived confirmed flag is invalid");

                  await deviceClient.RejectAsync(message);
                  return;
               }

               if (!AzureMessagePriorityTryGet(message.Properties, out priority))
               {
                  Console.WriteLine(" UplinkMessageReceived Priority value is invalid");

                  await deviceClient.RejectAsync(message);
                  return;
               }

               if (!AzureMessageQueueTryGet(message.Properties, out queue))
               {
                  Console.WriteLine(" UplinkMessageReceived Queue value is invalid");

                  await deviceClient.RejectAsync(message);
                  return;
               }

               Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Azure IoT Hub downlink message");
               Console.WriteLine($" ApplicationID: {receiveMessageHandlerConext.ApplicationId}");
               Console.WriteLine($" DeviceID: {receiveMessageHandlerConext.DeviceId}");
               Console.WriteLine($" LockToken: {message.LockToken}");
               Console.WriteLine($" Port: {port}");
               Console.WriteLine($" Confirmed: {confirmed}");
               Console.WriteLine($" Priority: {priority}");
               Console.WriteLine($" queue: {queue}");

               DownlinkPayload Payload = new DownlinkPayload()
               {
                  Downlinks = new List<Downlink>()
                  {
                     new Downlink()
                     {
                        Confirmed = confirmed,
                        PayloadRaw = messageBody,
                        Priority = priority,
                        Port = port,
                        CorrelationIds = new List<string>()
                        {
                           $"{Constants.AzureCorrelationPrefix}{message.LockToken}"
                        }
                     }
                  }
               };

               string downlinktopic = $"v3/{receiveMessageHandlerConext.ApplicationId}@{receiveMessageHandlerConext.TenantId}/devices/{receiveMessageHandlerConext.DeviceId}/down/{Enum.GetName( typeof(DownlinkQueue), queue)}".ToLower();

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
            Debug.WriteLine("UplinkMessageReceived failed: {0}", ex.Message);
         }
      }

      private static async void MqttClientDisconnected(MqttClientDisconnectedEventArgs e)
      {
         Debug.WriteLine($"Disconnected: {e.ReasonCode}");
         await Task.Delay(TimeSpan.FromSeconds(5));

         try
         {
            await mqttClient.ConnectAsync(mqttOptions);
         }
         catch (Exception ex)
         {
            Debug.WriteLine("Reconnect failed: {0}", ex.Message);
         }
      }

      private static bool AzureLockTokenTryGet(List<string> correlationIds, out string azureLockToken)
      {
         azureLockToken = string.Empty;

         // if AzureCorrelationPrefix prefix not found bug out
         if (!correlationIds.Any(o => o.StartsWith(Constants.AzureCorrelationPrefix)))
         {
            return false;
         }

         azureLockToken = correlationIds.Single(o => o.StartsWith(Constants.AzureCorrelationPrefix));

         azureLockToken = azureLockToken.Remove(0, Constants.AzureCorrelationPrefix.Length);

         return true;
      }

      private static bool AzureMessagePortTryGet( IDictionary<string, string>properties, out byte port)
      {
         port = 0;

         if (!properties.ContainsKey("Port"))
         {
            return false ;
         }

         if (!byte.TryParse(properties["Port"], out port))
         {
            return false;
         }

         if ((port < Constants.PortNumberMinimum) || port > (Constants.PortNumberMaximum))
         {
            return false;
         }

         return true;
      }


      private static bool AzureMessageConfirmedTryGet(IDictionary<string, string> properties, out bool confirmed)
      {
         confirmed = false;

         if (!properties.ContainsKey("Confirmed"))
         {
            return true;
         }

         if (!bool.TryParse(properties["Confirmed"], out confirmed))
         {
            return false;
         }

         return true;
      }

      private static bool AzureMessagePriorityTryGet(IDictionary<string, string> properties, out DownlinkPriority priority)
      {
         priority =  DownlinkPriority.Normal;

         if (!properties.ContainsKey("Priority"))
         {
            return true;
         }

         if (!Enum.TryParse(properties["Priority"], true, out priority) || !Enum.IsDefined(typeof(DownlinkPriority), priority))
         {
            return false;
         }

         return true;
      }

      private static bool AzureMessageQueueTryGet(IDictionary<string, string> properties, out DownlinkQueue queue)
      {
         queue = DownlinkQueue.Push;

         if (!properties.ContainsKey("Queue"))
         {
            return true;
         }

         if (!Enum.TryParse(properties["Queue"], true, out queue) || !Enum.IsDefined(typeof(DownlinkQueue), queue))
         {
            return false;
         }

         return true;
      }
   }
}
