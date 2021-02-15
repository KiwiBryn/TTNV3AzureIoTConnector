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
               int page = 1;

               V3Applications applications = await applicationRegistryClient.ListAsync(
                  _programSettings.TheThingsIndustries.Collaborator,
                  field_mask_paths: Constants.ApplicationFieldMaskPaths,
                  limit: _programSettings.TheThingsIndustries.ApplicationPageSize,
                  page: page,
                  cancellationToken: stoppingToken);
               while ((applications != null) && (applications.Applications != null))
               {
                  _logger.LogInformation("Applications:{0} Page:{1} Page size:{2}", applications.Applications.Count, page, _programSettings.TheThingsIndustries.ApplicationPageSize);
                  foreach (V3Application application in applications.Applications)
                  {
                     using (_logger.BeginScope("Application:{0} configuration", application.Ids.Application_id))
                     {
#if APPLICATION_FIELDS_MINIMUM
                        _logger.LogInformation("ID:{0}", application.Ids.Application_id);
#else
                        _logger.LogInformation("ID:{0} Name:{1} Description:{2} CreatedAt:{3} UpdatedAt:{4}", application.Ids.Application_id, application.Name, application.Description, application.Created_at, application.Updated_at);
#endif
                        if (application.Attributes != null)
                        {
                           using (_logger.BeginScope("Application {0} property configuration", application.Ids.Application_id))
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

                     }

                     page += 1;
                     applications = await applicationRegistryClient.ListAsync(
                        _programSettings.TheThingsIndustries.Collaborator,
                        field_mask_paths: Constants.ApplicationFieldMaskPaths,
                        limit: _programSettings.TheThingsIndustries.ApplicationPageSize,
                        page: page,
                        cancellationToken: stoppingToken);
                  }
               }
            }
            catch (Exception ex)
            {
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
   }
}


