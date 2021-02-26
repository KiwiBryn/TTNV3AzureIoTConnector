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
//	AQIDBA== Base64 encoded payload: [0x01, 0x02, 0x03, 0x04]
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;
	using System.Net.Http;
	using System.Threading;
	using System.Threading.Tasks;

	using Microsoft.Azure.Devices.Client;
	using Microsoft.Extensions.Hosting;
	using Microsoft.Extensions.Logging;
	using Microsoft.Extensions.Options;

	using MQTTnet;
	using MQTTnet.Client;
	using MQTTnet.Client.Options;
	using MQTTnet.Client.Publishing;
	using MQTTnet.Client.Receiving;
	using MQTTnet.Extensions.ManagedClient;

	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	using devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models;
	using devMobile.TheThingsNetwork.API;
	using System.Text;

	public class Worker : BackgroundService
	{
		private static ILogger<Worker> _logger;
		private static ProgramSettings _programSettings;
		private static readonly ConcurrentDictionary<string, DeviceClient> DeviceClients = new ConcurrentDictionary<string, DeviceClient>();
		private static readonly ConcurrentDictionary<string, IManagedMqttClient> MqttClients = new ConcurrentDictionary<string, IManagedMqttClient>();

		public Worker(ILogger<Worker> logger, IOptions<ProgramSettings> programSettings)
		{
			_logger = logger;
			_programSettings = programSettings.Value;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector starting");

			try
			{
				MqttFactory mqttFactory = new MqttFactory();

				foreach (KeyValuePair<string, ApplicationSetting> applicationSetting in _programSettings.Applications)
				{
					_logger.LogInformation("Enabled-ApplicationID:{0}", applicationSetting.Key);

					var mqttClient = mqttFactory.CreateManagedMqttClient();

					var mqttClientoptions = new ManagedMqttClientOptionsBuilder()
									.WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
									.WithClientOptions(new MqttClientOptionsBuilder()
										.WithTcpServer(_programSettings.TheThingsIndustries.MqttServerName)
										.WithCredentials(ApplicationIdGet(applicationSetting.Key), _programSettings.Applications[applicationSetting.Key].MQTTAccessKey)
										.WithClientId(_programSettings.TheThingsIndustries.MqttClientId)
										.WithTls()
										.Build())
									.Build();

					await mqttClient.StartAsync(mqttClientoptions);

					if (!MqttClients.TryAdd(applicationSetting.Key, mqttClient))
					{
						// Need to decide whether device cache add failure aborts startup
						_logger.LogError("Enabled-ApplicationID:{0} cache add failed", applicationSetting.Key);
					}

					using (HttpClient httpClient = new HttpClient())
					{
						// Get ready to enumerate through the Application's devices
						EndDeviceRegistryClient endDeviceRegistryClient = new EndDeviceRegistryClient(_programSettings.TheThingsIndustries.ApiBaseUrl, httpClient)
						{
							ApiKey = _programSettings.TheThingsIndustries.ApiKey
						};

						int devicePage = 1;
						V3EndDevices endDevices = await endDeviceRegistryClient.ListAsync(
							applicationSetting.Key,
							field_mask_paths: Constants.DevicefieldMaskPaths,
							page: devicePage,
							limit: _programSettings.TheThingsIndustries.DevicePageSize,
							cancellationToken: stoppingToken);

						while ((endDevices != null) && (endDevices.End_devices != null)) // If no devices returns null rather than empty list
						{
							foreach (V3EndDevice device in endDevices.End_devices)
							{
								if (DeviceAzureEnabled(device))
								{
									_logger.LogInformation("Enabled-ApplicationID:{0} DeviceID:{1} Device EUI:{2}", device.Ids.Application_ids.Application_id, device.Ids.Device_id, BitConverter.ToString(device.Ids.Dev_eui));

									try
									{
										// This is here in preparation  for DPS which may have different IoT Hub connection strings due to load balancing/region based allocation 
										if (!AzureConnectionStringGet(device.Ids.Application_ids.Application_id, out string connectionString))
										{
											// Need to decide whether device connection string retrive failed aborts startup
											_logger.LogError("Enabled-Application:{0} Device:{1} connection string unknown", device.Ids.Application_ids.Application_id, device.Ids.Device_id);
										}

										DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(connectionString, device.Ids.Device_id, TransportType.Amqp_Tcp_Only);

										await deviceClient.OpenAsync(stoppingToken);

										if (!DeviceClients.TryAdd(device.Ids.Device_id, deviceClient))
										{
											// Need to decide whether device cache add failure aborts startup
											_logger.LogError("Enabled-Device:{0} cache add failed", device.Ids.Device_id);
										}

										AzureIoTHubReceiveMessageHandlerContext context = new AzureIoTHubReceiveMessageHandlerContext()
										{
											TenantId = _programSettings.TheThingsIndustries.Tenant,
											DeviceId = device.Ids.Device_id,
											ApplicationId = device.Ids.Application_ids.Application_id,
										};

										await deviceClient.SetReceiveMessageHandlerAsync(AzureIoTHubClientReceiveMessageHandler, context, stoppingToken);

										await deviceClient.SetMethodDefaultHandlerAsync(AzureIoTHubClientDefaultMethodHandler, context, stoppingToken);
									}
									catch (Exception ex)
									{
										// Need to decide whether device enumeration failure aborts startup
										_logger.LogError(ex, "Enabled-Device:{0} configuration failed", device.Ids.Device_id);
									}
								}
							}

							devicePage += 1;
							endDevices = await endDeviceRegistryClient.ListAsync(
								applicationSetting.Key,
								field_mask_paths: Constants.DevicefieldMaskPaths,
								page: devicePage,
								limit: _programSettings.TheThingsIndustries.DevicePageSize,
								cancellationToken: stoppingToken);
						}

						try
						{
							// At this point all the AzureIoT Hub deviceClients setup and ready to go so can enable MQTT receive
							mqttClient.UseApplicationMessageReceivedHandler(new MqttApplicationMessageReceivedHandlerDelegate(e => MqttClientApplicationMessageReceived(e)));

							// These may shift to individual device subscriptions
							string uplinkTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/up";
							await mqttClient.SubscribeAsync(uplinkTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

							string queuedTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/down/queued";
							await mqttClient.SubscribeAsync(queuedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

							// TODO : Sent topic currently not processed, see https://github.com/TheThingsNetwork/lorawan-stack/issues/76
							//string sentTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/down/sent";
							//await mqttClient.SubscribeAsync(sentTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

							string ackTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/down/ack";
							await mqttClient.SubscribeAsync(ackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

							string nackTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/down/nack";
							await mqttClient.SubscribeAsync(nackTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

							string failedTopic = $"v3/{ApplicationIdGet(applicationSetting.Key)}/devices/+/down/failed";
							await mqttClient.SubscribeAsync(failedTopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);

						}
						catch (Exception ex)
						{
							_logger.LogError(ex, "Enabled-MQTT subscription error");
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Enabled-Application configuration error");

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

			foreach (var deviceClient in DeviceClients)
			{
				_logger.LogInformation("Close-DeviceClient:{0}", deviceClient.Key);
				await deviceClient.Value.CloseAsync();
			}

			foreach (var mqttClient in MqttClients)
			{
				_logger.LogInformation("Close- Application:{0}", mqttClient.Key);
				await mqttClient.Value.StopAsync();
			}
		}

		private async static Task<MethodResponse> AzureIoTHubClientDefaultMethodHandler(MethodRequest methodRequest, object userContext)
		{
			_logger.LogInformation("AzureIoTHubClientDefaultMethodHandler name:{0}", methodRequest.Name);

			return new MethodResponse(200);
		}

		private async Task AzureIoTHubClientReceiveMessageHandler(Message message, object userContext)
		{
			bool confirmed;
			byte port;
			DownlinkPriority priority;
			DownlinkQueue queue;

			try
			{
				AzureIoTHubReceiveMessageHandlerContext receiveMessageHandlerConext = (AzureIoTHubReceiveMessageHandlerContext)userContext;

				if (!DeviceClients.TryGetValue(receiveMessageHandlerConext.DeviceId, out DeviceClient deviceClient))
				{
					_logger.LogWarning("Downlink-DeviceID:{0} unknown", receiveMessageHandlerConext.DeviceId);
					return;
				}

				using (message)
				{
#if DIAGNOSTICS_AZURE_IOT_HUB
					_logger.LogInformation("MessageID: {0}", message.MessageId);
					_logger.LogInformation("DeliveryCount: {0}", message.DeliveryCount);
					_logger.LogInformation("EnqueuedTimeUtc: {0}", message.EnqueuedTimeUtc);
					_logger.LogInformation("SequenceNumber: {0}", message.SequenceNumber);
					_logger.LogInformation("To: {0}", message.To);
					_logger.LogInformation("UserId {0}", message.UserId);
					_logger.LogInformation("ConnectionDeviceId: {0}", message.ConnectionDeviceId);
					_logger.LogInformation("ConnectionModuleId: {0}", message.ConnectionModuleId);
					_logger.LogInformation("ConnectionDeviceId: {0}", message.ConnectionDeviceId);
					_logger.LogInformation("ComponentName: 0}", message.ComponentName);
#endif

#if DOWNLINK_MESSAGE_PROPERTIES_DISPLAY
					foreach (var property in message.Properties)
					{
						_logger.LogInformation("Key:{0} Value:{1}", property.Key, property.Value);
					}
#endif
					// Put the one mandatory message property first, just because
					if (!AzureMessagePortTryGet(message.Properties, out port))
					{
						_logger.LogWarning("Downlink-Port property is invalid");

						await deviceClient.RejectAsync(message);
						return;
					}

					if (!AzureMessageConfirmedTryGet(message.Properties, out confirmed))
					{
						_logger.LogWarning("Downlink-Confirmed flag is invalid");

						await deviceClient.RejectAsync(message);
						return;
					}

					if (!AzureMessagePriorityTryGet(message.Properties, out priority))
					{
						_logger.LogWarning("Downlink-Priority value is invalid");

						await deviceClient.RejectAsync(message);
						return;
					}

					if (!AzureMessageQueueTryGet(message.Properties, out queue))
					{
						_logger.LogWarning("Downlink-Queue value is invalid");

						await deviceClient.RejectAsync(message);
						return;
					}

					_logger.LogInformation("Downlink-DeviceID:{0} MessageID:{2} Port:{3} Confirmed:{4} Priority:{5} Queue:{6}",
						receiveMessageHandlerConext.DeviceId,
						message.MessageId,
						port,
						confirmed,
						priority,
						queue);

					DownlinkPayload Payload = new DownlinkPayload()
					{
						Downlinks = new List<Downlink>()
						{
							new Downlink()
							{
								Confirmed = confirmed,
								PayloadRaw = Encoding.UTF8.GetString(message.GetBytes()),
								Priority = priority,
								Port = port,
								CorrelationIds = AzureLockTokenAdd(message.LockToken)
							}
						}
					};

					if (!MqttClients.TryGetValue(receiveMessageHandlerConext.ApplicationId, out IManagedMqttClient mqttClient))
					{
						_logger.LogWarning("Downlink-ApplicationID:{0} unknown", receiveMessageHandlerConext.ApplicationId);
						return;
					}

					string downlinktopic = $"v3/{receiveMessageHandlerConext.ApplicationId}@{receiveMessageHandlerConext.TenantId}/devices/{receiveMessageHandlerConext.DeviceId}/down/{Enum.GetName(typeof(DownlinkQueue), queue)}".ToLower();

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
				_logger.LogError(ex, "Downlink-Processing failed");
			}
		}

		private bool AzureMessagePortTryGet(IDictionary<string, string> properties, out byte port)
		{
			port = 0;

			if (!properties.ContainsKey("Port"))
			{
				return false;
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

		private bool AzureMessageConfirmedTryGet(IDictionary<string, string> properties, out bool confirmed)
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

		private bool AzureMessagePriorityTryGet(IDictionary<string, string> properties, out DownlinkPriority priority)
		{
			priority = DownlinkPriority.Normal;

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

		private bool AzureMessageQueueTryGet(IDictionary<string, string> properties, out DownlinkQueue queue)
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

		private async void MqttClientApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs e)
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

			_logger.LogWarning("MessageReceived unknown Topic:{0} Payload:{1}", e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
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

#if DIAGNOSTICS_TTN_MQTT
				if (payload.UplinkMessage.RXMetadata != null)
				{
					foreach (RxMetadata rxMetaData in payload.UplinkMessage.RXMetadata)
					{
						_logger.LogInformation("GatewayId {0}" , rxMetaData.GatewayIds.GatewayId);
						_logger.LogInformation("ReceivedAtUTC {0}", rxMetaData.ReceivedAtUtc);
						_logger.LogInformation("RSSI {0}", rxMetaData.Rssi);
						_logger.LogInformation("Snr {0}", rxMetaData.Snr);
					}
				}
#endif
				string applicationId = payload.EndDeviceIds.ApplicationIds.ApplicationId;
				string deviceId = payload.EndDeviceIds.DeviceId;
				int port = payload.UplinkMessage.Port.Value;

				_logger.LogInformation("Uplink-DeviceID:{0} Port:{1} Payload Raw:{2}", deviceId, port, payload.UplinkMessage.PayloadRaw);

				if (!DeviceClients.TryGetValue(deviceId, out DeviceClient deviceClient))
				{
					_logger.LogWarning("Uplink-Unkown DeviceID:{0}", deviceId);
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
				_logger.LogError(ex, "Uplink-Processing failed");
			}
		}

		private async Task DownlinkMessageQueued(MqttApplicationMessageReceivedEventArgs e)
		{
			DownlinkQueuedPayload payload = JsonConvert.DeserializeObject<DownlinkQueuedPayload>(e.ApplicationMessage.ConvertPayloadToString());
			if (payload == null)
			{
				_logger.LogWarning("Queued-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}

			if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
			{
				_logger.LogInformation("Queued-LockToken missing from payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}

			_logger.LogInformation("Queued-Device:{0} LockToken:{1} Confirmed:{2}", payload.EndDeviceIds.DeviceId, lockToken, payload.DownlinkQueued.Confirmed);

			// The confirmation is done in the Ack/Nack/Failed message handler
			if (payload.DownlinkQueued.Confirmed)
			{
				return;
			}

			if (!DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
			{
				_logger.LogWarning("Queued-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
				return;
			}

			try
			{
				await deviceClient.CompleteAsync(lockToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Queued-CompleteAsync");
			}
		}

		private async Task DownlinkMessageAck(MqttApplicationMessageReceivedEventArgs e)
		{
			DownlinkAckPayload payload = JsonConvert.DeserializeObject<DownlinkAckPayload>(e.ApplicationMessage.ConvertPayloadToString());
			if (payload == null)
			{
				_logger.LogWarning("Ack-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}

			if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
			{
				_logger.LogInformation("Ack-LockToken missing from payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}
			_logger.LogInformation("Ack-Device:{0} AzureLockToken:{1}", payload.EndDeviceIds.DeviceId, lockToken);

			if (!DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
			{
				_logger.LogWarning("Ack-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
				return;
			}

			try
			{
				await deviceClient.CompleteAsync(lockToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ack-CompleteAsync");
			}
		}

		private async Task DownlinkMessageNack(MqttApplicationMessageReceivedEventArgs e)
		{
			DownlinkNackPayload payload = JsonConvert.DeserializeObject<DownlinkNackPayload>(e.ApplicationMessage.ConvertPayloadToString());
			if (payload == null)
			{
				_logger.LogWarning("Nack-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}

			if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
			{
				_logger.LogInformation("Nack-LockToken missing from payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}
			_logger.LogInformation("Nack-Device:{0} LockToken:{2}", payload.EndDeviceIds.DeviceId, lockToken);

			if (!DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
			{
				_logger.LogWarning("Nack-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
				return;
			}

			try
			{
				await deviceClient.AbandonAsync(lockToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Nack-AbandonAsync");
			}
		}

		private async Task DownlinkMessageFailed(MqttApplicationMessageReceivedEventArgs e)
		{
			DownlinkFailedPayload payload = JsonConvert.DeserializeObject<DownlinkFailedPayload>(e.ApplicationMessage.ConvertPayloadToString());
			if (payload == null)
			{
				_logger.LogWarning("Failed-Invalid payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}

			if (!AzureLockTokenTryGet(payload.CorrelationIds, out string lockToken))
			{
				_logger.LogInformation("Failed-LockToken missing from payload:{0}", e.ApplicationMessage.ConvertPayloadToString());
				return;
			}
			_logger.LogInformation("Failed-Device:{0} LockToken:{2}", payload.EndDeviceIds.DeviceId, lockToken);

			if (!DeviceClients.TryGetValue(payload.EndDeviceIds.DeviceId, out DeviceClient deviceClient))
			{
				_logger.LogWarning("Failed-DeviceID:{0} unknown", payload.EndDeviceIds.DeviceId);
				return;
			}

			try
			{
				await deviceClient.RejectAsync(lockToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed-RejectAsync");

			}
		}

		private bool DeviceAzureEnabled(V3EndDevice device)
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

		private bool AzureLockTokenTryGet(List<string> correlationIds, out string azureLockToken)
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

		private List<string> AzureLockTokenAdd(string azureLockToken)
		{
			return new List<string>()
			{
				$"{Constants.AzureCorrelationPrefix}{azureLockToken}"
			};
		}

		private bool AzureConnectionStringGet(string applicationId, out string connectionString)
		{
			connectionString = string.Empty;

			if (_programSettings.Applications.ContainsKey(applicationId))
			{
				if (_programSettings.Applications[applicationId].AzureSettings != null)
				{
					if (!string.IsNullOrWhiteSpace(_programSettings.Applications[applicationId].AzureSettings.IoTHubConnectionString))
					{
						connectionString = _programSettings.Applications[applicationId].AzureSettings.IoTHubConnectionString;

						return true;
					}
				}
			}

			if (_programSettings.AzureSettingsDefault != null)
			{
				if (!string.IsNullOrWhiteSpace(_programSettings.AzureSettingsDefault.IoTHubConnectionString))
				{
					connectionString = _programSettings.AzureSettingsDefault.IoTHubConnectionString;

					return true;
				}
			}

			return false;
		}

		private string ApplicationIdGet(string applicationId)
		{
			return $"{applicationId}@{_programSettings.TheThingsIndustries.Tenant}";
		}
	}
}
