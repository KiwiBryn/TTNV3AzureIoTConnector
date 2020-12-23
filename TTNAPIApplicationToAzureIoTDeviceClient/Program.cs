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
//#define DIAGNOSTICS
//#define DIAGNOSTICS_AZURE_IOT_HUB
//#define DIAGNOSTICS_MQTT
//#define DEVICE_FIELDS_MINIMUM
//#define DEVICE_ATTRIBUTES_DISPLAY
//#define DOWNLINK_MESSAGE_PROPERTIES_DISPLAY
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsNetwork.TTNAPIApplicationToAzureIoTDeviceClient
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
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
		private static readonly CacheItemPolicy cacheItemPolicy = new CacheItemPolicy(); // This will be revisited 

#if DEVICE_FIELDS_MINIMUM
		private static readonly string[] DevicefieldMaskPaths = { "attributes" };
#else
		private static readonly string[] DevicefieldMaskPaths = { "name", "description", "attributes" };
#endif

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
			MqttFactory factory = new MqttFactory();
			mqttClient = factory.CreateMqttClient();

#if DIAGNOSTICS
			Console.WriteLine($"baseURL: {options.ApiBaseUrl}");
			Console.WriteLine($"APIKey: {options.ApiKey}");
			Console.WriteLine($"ApplicationID: {options.ApiApplicationID}");
			Console.WriteLine($"AazureIoTHubconnectionString: {options.AzureIoTHubconnectionString}");
			Console.WriteLine();
#endif

			try
			{
				// First configure MQTT, open connection and wire up disconnection handler. 
				// Can't wire up MQTT received handler as at this stage AzureIoTHub devices not connected.
				mqttOptions = new MqttClientOptionsBuilder()
					.WithTcpServer(options.MqttServerName)
					.WithCredentials(options.MqttApplicationID, options.MqttAccessKey)
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
						field_mask_paths: DevicefieldMaskPaths, 
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

								await deviceClient.SetReceiveMessageHandlerAsync(
									AzureIoTHubClientReceiveMessageHandler,
									new AzureIoTHubReceiveMessageHandlerContext()
									{
										DeviceId = endDevice.Ids.Device_id,
										ApplicationId = endDevice.Ids.Application_ids.Application_id,
									});

								DeviceClients.Add(endDevice.Ids.Device_id, deviceClient, cacheItemPolicy);
							}
							catch( Exception ex)
                     {
								Console.WriteLine($"Azure IoT Hub OpenAsync failed {ex.Message}");
							}
						}
					}
				}

				string uplinktopic = $"v3/{options.MqttApplicationID}/devices/+/up";

				// At this point all the AzureIoT Hub deviceClients setup and ready to go so can enable MQTT receive
				mqttClient.UseApplicationMessageReceivedHandler(new MqttApplicationMessageReceivedHandlerDelegate(e => MqttClientApplicationMessageReceived(e)));

				await mqttClient.SubscribeAsync(uplinktopic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
			}
			catch(Exception ex)
			{
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

			// Consider ways to mop up connections

			Console.WriteLine("Press any key to exit");
			Console.ReadLine();
		}

		private static void MqttClientApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs e)
		{
			if (e.ApplicationMessage.Topic.EndsWith("/up"))
			{
				PayloadUplinkV3 payload = JsonConvert.DeserializeObject<PayloadUplinkV3>(e.ApplicationMessage.ConvertPayloadToString());

				Console.WriteLine();
				Console.WriteLine();
				Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} TTN Uplink message");
#if DIAGNOSTICS_MQTT
				Console.WriteLine($" ClientId:{e.ClientId} Topic:{e.ApplicationMessage.Topic}");
				Console.WriteLine($" Cached: {DeviceClients.Contains(payload.EndDeviceIds.DeviceId)}");
#endif
				Console.WriteLine($" ApplicationID: {payload.EndDeviceIds.ApplicationIds.ApplicationId}");
				Console.WriteLine($" DeviceID: {payload.EndDeviceIds.DeviceId}");
				Console.WriteLine($" Port: {payload.UplinkMessage.Port} ");
				Console.WriteLine($" Payload raw: {payload.UplinkMessage.PayloadRaw}");

				if (payload.UplinkMessage.PayloadDecoded != null)
				{
					Console.WriteLine($" Payload decoded: {payload.UplinkMessage.PayloadRaw}");
					EnumerateChildren(1, payload.UplinkMessage.PayloadDecoded);
				}

				Console.WriteLine();
			}
			else
			{
				Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} ClientId: {e.ClientId} Topic: {e.ApplicationMessage.Topic}");
			}
		}


		static void EnumerateChildren(int indent, JToken token)
		{
			string prepend = string.Empty.PadLeft(indent);

			if (token is JProperty)
			{
				if (token.First is JValue)
				{
					JProperty property = (JProperty)token;
					Console.WriteLine($"{prepend} Name:{property.Name} Value:{property.Value}");
				}
				else
				{
					JProperty property = (JProperty)token;
					Console.WriteLine($"{prepend} Name:{property.Name}");
					indent += 1;
				}
			}
			foreach (JToken token2 in token.Children())
				{
					EnumerateChildren(indent, token2);
				}
		}

		private async static Task AzureIoTHubClientReceiveMessageHandler(Message message, object userContext)
      {
			AzureIoTHubReceiveMessageHandlerContext receiveMessageHandlerConext = (AzureIoTHubReceiveMessageHandlerContext)userContext;

		   DeviceClient deviceClient = (DeviceClient)DeviceClients.Get(receiveMessageHandlerConext.DeviceId);

			using (message)
			{
				Console.WriteLine();
				Console.WriteLine();
				Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} Azure IoT Hub downlink message");
				Console.WriteLine($" ApplicationID: {receiveMessageHandlerConext.ApplicationId}");
				Console.WriteLine($" DeviceID: {receiveMessageHandlerConext.DeviceId}");
#if DIAGNOSTICS_AZURE_IOT_HUB
				Console.WriteLine($" Cached: {DeviceClients.Contains(receiveMessageHandlerConext.DeviceId)}");
				Console.WriteLine($" MessageID: {message.MessageId}");
				Console.WriteLine($" DeliveryCount: {message.DeliveryCount}");
				Console.WriteLine($" EnqueuedTimeUtc: {message.EnqueuedTimeUtc}");
				Console.WriteLine($" SequenceNumber: {message.SequenceNumber}");
				Console.WriteLine($" To: {message.To}");
#endif
				string messageBody = Encoding.UTF8.GetString(message.GetBytes());
				Console.WriteLine($" Body: {messageBody}");
#if DOWNLINK_MESSAGE_PROPERTIES_DISPLAY
				foreach (var property in message.Properties)
				{
					Console.WriteLine($"   Key:{property.Key} Value:{property.Value}");
				}
#endif

				await deviceClient.CompleteAsync(message);

				Console.WriteLine();
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
	}
}
