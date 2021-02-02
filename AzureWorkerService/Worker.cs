//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
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
namespace devMobile.TheThingsNetwork.WorkerService
{
   using System;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Extensions.Configuration;
   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   public class Worker : BackgroundService
   {
      private readonly ILogger<Worker> _logger;
      private readonly IConfiguration _configuration;
      private readonly ApplicationSettings _applicationSettings;

      public Worker(ILogger<Worker> logger, IConfiguration configuration, IOptions<ApplicationSettings> applicationSettings)
      {
         _logger = logger;
         _configuration = configuration;
         _applicationSettings = applicationSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
         while (!stoppingToken.IsCancellationRequested)
         {
            _logger.LogDebug("Debug GetSection: {0}", _configuration.GetSection("ApplicationSettings").GetValue<string>("SecretKey1"));
            _logger.LogDebug("Debug: {0}", _applicationSettings.SecretKey2);
            _logger.LogInformation("Info worker running at: {time}", DateTimeOffset.Now);
            _logger.LogWarning("Warning worker running at: {time}", DateTimeOffset.Now);
            _logger.LogError("Error running at: {time}", DateTimeOffset.Now);

            await Task.Delay(300000, stoppingToken);
         }
      }
   }
}
