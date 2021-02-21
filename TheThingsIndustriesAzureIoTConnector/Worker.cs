//---------------------------------------------------------------------------------
// Copyright (c) February 2020, devMobile Software
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
   using System.Collections.Generic;
   using System.Collections.Concurrent;
   using System.Net.Http;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Azure.Devices.Client;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   using devMobile.TheThingsNetwork.API;
   using devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models;

   public class Worker : BackgroundService
   {
      private readonly ILogger<Worker> _logger;
      private readonly ProgramSettings _programSettings;
      private static readonly ConcurrentDictionary<string, DeviceClient> _deviceClients = new ConcurrentDictionary<string, DeviceClient>();

      public Worker(ILogger<Worker> logger, IOptions<ProgramSettings> programSettings)
      {
         _logger = logger;
         _programSettings = programSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
         _logger.LogInformation("devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector starting");

         using (HttpClient httpClient = new HttpClient())
         {
            ApplicationRegistryClient applicationRegistryClient = new ApplicationRegistryClient(_programSettings.TheThingsIndustries.ApiBaseUrl, httpClient)
            {
               ApiKey = _programSettings.TheThingsIndustries.ApiKey
            };

            try
            {
               int applicationPage = 1;

               V3Applications applications = await applicationRegistryClient.ListAsync(
                  _programSettings.TheThingsIndustries.Collaborator,
                  field_mask_paths: Constants.ApplicationFieldMaskPaths,
                  limit: _programSettings.TheThingsIndustries.ApplicationPageSize,
                  page: applicationPage,
                  cancellationToken: stoppingToken);

               // Retrieve a list of applications page by page
               while ((applications != null) && (applications.Applications != null))
               {
                  foreach (V3Application application in applications.Applications)
                  {
                     _logger.LogInformation("Application:{0}", application.Ids.Application_id);

                     using (_logger.BeginScope("Application:{0} configuration", application.Ids.Application_id))
                     {
                        if (application.Attributes != null)
                        {
                           using (_logger.BeginScope("Application:{0} attribute configuration", application.Ids.Application_id))
                           {
                              foreach (KeyValuePair<string, string> attribute in application.Attributes)
                              {
                                 _logger.LogInformation("Key:{0} Value:{1}", attribute.Key, attribute.Value);
                              }
                           }
                        }
                     }

                     if (ApplicationAzureEnabled(application))
                     {
                        _logger.LogInformation("Application:{0} Azure integrated", application.Ids.Application_id);

                        EndDeviceRegistryClient endDeviceRegistryClient = new EndDeviceRegistryClient(_programSettings.TheThingsIndustries.ApiBaseUrl, httpClient)
                        {
                           ApiKey = _programSettings.TheThingsIndustries.ApiKey
                        };

                        try
                        {
                           // Retrieve list of devices page by page
                           int devicePage = 1;
                           V3EndDevices endDevices = await endDeviceRegistryClient.ListAsync(
                              application.Ids.Application_id,
                              field_mask_paths: Constants.DevicefieldMaskPaths,
                              page: devicePage,
                              limit: _programSettings.TheThingsIndustries.DevicePageSize,
                              cancellationToken: stoppingToken);
                           while ((endDevices != null) && (endDevices.End_devices != null)) // If no devices returns null rather than empty list
                           {
                              foreach (V3EndDevice device in endDevices.End_devices)
                              {
                                 _logger.LogInformation("Application:{0} Device:{1}", device.Ids.Application_ids.Application_id, device.Ids.Device_id);

                                 using (_logger.BeginScope("Application:{0} Device:{1} configuration", device.Ids.Application_ids.Application_id, device.Ids.Device_id))
                                 {
                                    _logger.LogInformation("Device EUI:{0} Join EUI:{1}", BitConverter.ToString(device.Ids.Dev_eui), BitConverter.ToString(device.Ids.Join_eui));

                                    if (device.Attributes != null)
                                    {
                                       using (_logger.BeginScope("Device:{0} attribute configuration", application.Ids.Application_id))
                                       {
                                          foreach (KeyValuePair<string, string> attribute in device.Attributes)
                                          {
                                             _logger.LogInformation("Key:{0} Value:{1}", attribute.Key, attribute.Value);
                                          }
                                       }
                                    }

                                    if (DeviceAzureEnabled(application, device))
                                    {
                                       _logger.LogInformation("Application:{0} Device:{1} Integrated", device.Ids.Application_ids.Application_id, device.Ids.Device_id);

                                       try
                                       {
                                          DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(
                                             _programSettings.AzureSettingsDefault.IoTHubConnectionString,
                                             device.Ids.Device_id,
                                             TransportType.Amqp_Tcp_Only);

                                          await deviceClient.OpenAsync();

                                          if (!_deviceClients.TryAdd(device.Ids.Device_id, deviceClient))
                                          {
                                             // Need to decide whether device cache add failure aborts startup
                                             _logger.LogError("DeviceClient cache Device:{0} add failed", device.Ids.Device_id);
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
                                          _logger.LogError(ex, "DeviceClient connection failed");
                                       }
                                    }
                                 }
                              }

                              devicePage += 1;
                              endDevices = await endDeviceRegistryClient.ListAsync(
                                 application.Ids.Application_id,
                                 field_mask_paths: Constants.DevicefieldMaskPaths,
                                 page: devicePage,
                                 limit: _programSettings.TheThingsIndustries.DevicePageSize,
                                 cancellationToken: stoppingToken);
                           }
                        }
                        catch (Exception ex)
                        {
                           // Need to decide whether device enumeration failure aborts startup
                           _logger.LogError(ex, "Device enumeration failed");
                        }
                     }
                  }
                     
                  applicationPage += 1;
                  applications = await applicationRegistryClient.ListAsync(
                     _programSettings.TheThingsIndustries.Collaborator,
                     field_mask_paths: Constants.ApplicationFieldMaskPaths,
                     limit: _programSettings.TheThingsIndustries.ApplicationPageSize,
                     page: applicationPage,
                     cancellationToken: stoppingToken);
               }
            }
            catch (Exception ex)
            {
               // Need to decide whether application enumeration failure aborts startup
               _logger.LogError(ex, "Application enumeration failed");
            }
         }

         try
         {
            await Task.Delay(Timeout.Infinite, stoppingToken);
         }
         catch(TaskCanceledException )
         {
            _logger.LogInformation("devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector stopping");
         }

         foreach( var deviceClient in _deviceClients)
         {
            _logger.LogInformation("DeviceClient device{0} closing", deviceClient.Key);
            await deviceClient.Value.CloseAsync();
         }

   }
}
