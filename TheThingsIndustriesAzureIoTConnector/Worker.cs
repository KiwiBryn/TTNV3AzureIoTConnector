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
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
   using System;
   using System.Collections.Generic;
   using System.Net.Http;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   using devMobile.TheThingsNetwork.API;

   public class Worker : BackgroundService
   {
      private readonly ILogger<Worker> _logger;
      private readonly ProgramSettings _programSettings;

      public Worker(ILogger<Worker> logger, IOptions<ProgramSettings> programSettings)
      {
         _logger = logger;
         _programSettings = programSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
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

               // Retrive a list of applications page by page
               while ((applications != null) && (applications.Applications != null))
               {
                  _logger.LogInformation("Applications:{0} Page:{1} Page size:{2}", applications.Applications.Count, applicationPage, _programSettings.TheThingsIndustries.ApplicationPageSize);
                  foreach (V3Application application in applications.Applications)
                  {
                     using (_logger.BeginScope("Application:{0} configuration", application.Ids.Application_id))
                     {
#if APPLICATION_FIELDS_MINIMUM
                        _logger.LogInformation("ID:{0}", application.Ids.Application_id);
#else
                        _logger.LogInformation("ID:{0} Name:{1} Description:{2} CreatedAt:{3} UpdatedAt:{4}", application.Ids.Application_id, application.Name, application.Description, application.Created_at, application.Updated_at);
#endif
                        if (_logger.IsEnabled(LogLevel.Information) && (application.Attributes != null))
                        {
                           using (_logger.BeginScope("Application {0} attribute configuration", application.Ids.Application_id))
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
                        _logger.LogInformation("Application:{0} Integrated", application.Ids.Application_id);

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
                              _logger.LogInformation("Application:{0} Devices:{1} Page:{2} Page size:{3}", application.Ids.Application_id, endDevices.End_devices.Count, devicePage, _programSettings.TheThingsIndustries.DevicePageSize);

                              foreach (V3EndDevice endDevice in endDevices.End_devices)
                              {
                                 using (_logger.BeginScope("Application:{0} Device:{1} configuration", application.Ids.Application_id, endDevice.Ids.Device_id))
                                 {
                                    _logger.LogInformation("ID:{0} EUI:{1} Name:{2} Description:{3} CreatedAt:{4} UpdatedAt:{5}", endDevice.Ids.Device_id, BitConverter.ToString(endDevice.Ids.Dev_eui), endDevice.Name, endDevice.Description, endDevice.Created_at, endDevice.Updated_at);

                                    if (_logger.IsEnabled(LogLevel.Information) && (endDevice.Attributes != null))
                                    {
                                       using (_logger.BeginScope("Enddevice {0} attribute configuration", application.Ids.Application_id))
                                       {
                                          foreach (KeyValuePair<string, string> attribute in endDevice.Attributes)
                                          {
                                             _logger.LogInformation("Key:{0} Value:{1}", attribute.Key, attribute.Value);
                                          }
                                       }
                                    }

                                    if (DeviceAzureEnabled(application, endDevice))
                                    {
                                       _logger.LogInformation("Application:{0} Device:{1} Integrated", application.Ids.Application_id, endDevice.Ids.Device_id);

                                       /*
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
                                       */
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

         await Task.Delay(Timeout.Infinite, stoppingToken);
      }

      bool ApplicationAzureEnabled(V3Application application)
      {
         bool integrated = _programSettings.TheThingsIndustries.ApplicationIntegrationDefault;

         if (application.Attributes == null)
         {
            _logger.LogInformation("Application:{0} has no attributes", application.Ids.Application_id);

            return integrated;
         }

         if (!application.Attributes.ContainsKey(Constants.ApplicationAzureIntegrationProperty))
         {
            _logger.LogInformation("Application:{0} Attribute:{1} missing", application.Ids.Application_id, Constants.ApplicationAzureIntegrationProperty);

            return integrated;
         }

         if (!bool.TryParse(application.Attributes["azureintegration"], out integrated))
         {
            _logger.LogWarning("Application:{0} Azure Integration property:{1} value:{2} invalid", application.Ids.Application_id, Constants.ApplicationAzureIntegrationProperty, application.Attributes["azureintegration"]);

            return integrated;
         }

         _logger.LogInformation("Application:{0} is Azure Integrated:{1} ", application.Ids.Application_id, integrated);

         return integrated;
      }

      bool DeviceAzureEnabled(V3Application application, V3EndDevice device)
      {
         bool integrated = _programSettings.TheThingsIndustries.DeviceIntegrationDefault;

         if (device.Attributes != null)
         {
            _logger.LogInformation("Application:{0} has attributes", application.Ids.Application_id);

            if (application.Attributes.ContainsKey(Constants.DeviceAzureIntegrationProperty))
            {
               if (bool.TryParse(device.Attributes[Constants.DeviceAzureIntegrationProperty], out integrated))
               {
                  _logger.LogInformation("Application:{0} Device:{1} is Azure Integrated:{1} device", application.Ids.Application_id, device.Ids.Device_id, integrated);

                  return integrated;
               }

               _logger.LogWarning("Application:{0} Device {1} Azure Integration property:{2} value:{3} invalid", application.Ids.Application_id, device.Ids.Device_id, Constants.ApplicationAzureIntegrationProperty, application.Attributes[Constants.DeviceAzureIntegrationProperty]);
            }
         }

         if (application.Attributes != null)
         {
            _logger.LogInformation("Application:{0} Device{1} has attributes", application.Ids.Application_id, device.Ids.Device_id);

            if (application.Attributes.ContainsKey(Constants.ApplicationDeviceAzureIntegrationProperty))
            {
               if (bool.TryParse(application.Attributes[Constants.ApplicationDeviceAzureIntegrationProperty], out integrated))
               {
                  _logger.LogInformation("Application:{0} Device:{1} is Azure Integrated:{1} application", application.Ids.Application_id, device.Ids.Device_id, integrated);

                  return integrated;
               }

               _logger.LogWarning("Application:{0} Device Azure Integration default property:{1} value:{2} invalid", application.Ids.Application_id, Constants.ApplicationAzureIntegrationProperty, application.Attributes[Constants.ApplicationDeviceAzureIntegrationProperty]);
            }
         }

         _logger.LogInformation("Application:{0} Device:{1} is Azure Integrated:{1} default", application.Ids.Application_id, device.Ids.Device_id, integrated);

         return integrated;
      }
   }
}
